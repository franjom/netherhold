// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;

namespace DK2.Simulation.Systems;

/// <summary>
/// Gives workers something to do: find the nearest tile their keeper has marked,
/// walk to it, and work on it until it is gone.
///
/// <para>This is the producer <see cref="TileWorkSystem"/> was written to wait
/// for. Everything about <i>what</i> work does to a tile lives in
/// <see cref="TileWorkRules"/> and is data; everything about <i>who</i> does it
/// and <i>when</i> lives here, and much of that is not.</para>
///
/// <para><b>The order an imp prefers its jobs in is CONFIRMED</b> (owner
/// observation of the original, 2026-08-29) and is the whole of
/// <see cref="JobPriority"/>:</para>
/// <list type="number">
/// <item><b>Marked excavation</b> — whatever the keeper has tagged, rock or gold
/// alike.</item>
/// <item><b>Claiming floor.</b></item>
/// <item><b>Reinforcing walls.</b></item>
/// </list>
/// <para>The order is strict, not weighted: <i>any</i> reachable marked tile
/// beats <i>every</i> claimable one however far away, and so on down. That is
/// what "first do what is designated for excavation, after that comes claiming,
/// after that reinforcing" says, and it is why this is three searches rather
/// than one scored one.</para>
///
/// <para><b>What an imp does when all three come up empty is NOT implemented.</b>
/// In the original it wanders the dungeon and interacts with the environment and
/// with creatures; here it stands still and looks again after
/// <c>ImpIdleDelayBeforeReevaluationSeconds</c>. Wandering is visible behaviour
/// with no simulation consequence we model yet, and inventing a walk would put
/// imps in places DK2 would not (Q41).</para>
///
/// <para><b>Carrying mined gold is here</b>, and deliberately not in that list.
/// An imp mines into its own hands and, when the next swing would not fit, runs
/// to the nearest place its keeper can keep gold and puts the load down
/// (CONFIRMED, Q31). That makes it the tail of a mining job rather than a job an
/// imp chooses, which is why <see cref="JobPriority"/> has no entry for it
/// (ADR-0018).</para>
///
/// <para>An imp with nowhere to unload <b>keeps mining</b>, which is also what
/// the original does — but the gold it can no longer hold is reported as
/// uncollected and dropped, where DK2 would grow a lump on the floor in front of
/// it that somebody can come back for. A lump is an OBJECT and the OBJECTS record
/// is not parsed until M6. So the <i>decision</i> is DK2's and the <i>fallback
/// store</i> is missing, which is the honest way round: a keeper with no treasury
/// still mines here, and simply loses what a keeper in the original would keep.
/// See Q31.</para>
///
/// <para><b>Still absent:</b> dragging the unconscious to a prison, fleeing a
/// fight, and picking up gold somebody else dropped.
/// See docs/research/imp-tasks.md.</para>
///
/// <para>What is inferred rather than known, with nothing hidden:</para>
/// <list type="bullet">
/// <item><b>Nearest first.</b> Within one priority, an imp takes the tile fewest
/// steps away, and unreachable ones are not considered at all — which falls out
/// of searching outwards from the imp rather than being a rule applied
/// afterwards. HYPOTHESIS: it matches how the original's imps visibly behave,
/// but "nearest" could as easily mean straight-line distance, or the order the
/// tiles were marked in (Q25).</item>
/// <item><b>Claiming and reinforcing spread from ground the keeper already
/// holds.</b> An imp will only take one of those jobs where the tile touches its
/// keeper's territory, which is what makes a dungeon grow outwards rather than
/// appear in islands across the map. CONFIRMED (Q42); the alternative — any
/// reachable floor, anywhere — is refuted. Marked work carries no such
/// condition: a keeper may tag a wall anywhere.</item>
/// <item><b>Three workers to a face of a wall, and exactly one to a floor tile
/// somebody is standing on to claim it</b> — the surplus goes and finds other
/// work rather than queueing (Q43, CONFIRMED both ways). The cap is per job
/// rather than per tile, which is observed and not derivable; see
/// <see cref="ImpCrowding"/>.</item>
/// <item><b>A wall is worked from beside it and floor is worked from on top of
/// it.</b> CONFIRMED both ways: a gold block takes three imps a side and twelve
/// in all, which is four sides of three (Q27), and an imp claims a floor tile by
/// jumping onto it (Q44). Which stance a job uses is derived rather than listed —
/// see <see cref="WorkStance"/>.</item>
/// <item><b>One application per work period.</b> Work lands every
/// <see cref="SimulationTiming.WorkApplicationPeriodTicks"/> ticks, starting one
/// period after the imp arrives — the period being the Q19 reading that the
/// health deltas are per second. Whether a single imp contributes the whole
/// delta is the other half of Q20 and is UNKNOWN; this contributes all of
/// it.</item>
/// <item><b>Idle re-evaluation.</b> An imp that looks for work and finds none
/// waits <c>ImpIdleDelayBeforeReevaluationSeconds</c> before looking again. The
/// variable's name says what it is for, which makes the delay CONFIRMED. An imp
/// whose wall has just fallen is not idle and does not wait — in the original it
/// moves straight to the next marked tile — and a freshly placed one looks
/// immediately; both of those are ours.</item>
/// </list>
///
/// <para>Applies to any creature flagged <see cref="CreatureFlags.IsWorker"/>,
/// which in the shipped data is the Imp and the Dwarf — never to creature id 1,
/// for the same reason tile rules never name terrain id 2.</para>
/// </summary>
[Dk2Behavior(Confidence.Hypothesis, "docs/research/imp-tasks.md")]
public sealed class ImpTaskSystem : ISimulationSystem
{
    private const string SystemName = "ImpTask";

    /// <summary>
    /// What an imp looks for, in the order it looks. CONFIRMED — see the type
    /// comment. Strictly ordered: nothing at a later priority is considered
    /// while anything at an earlier one is reachable.
    /// </summary>
    private enum JobPriority
    {
        MarkedExcavation = 0,
        Claim = 1,
        Reinforce = 2,
    }

    private static readonly JobPriority[] Priorities =
        [JobPriority.MarkedExcavation, JobPriority.Claim, JobPriority.Reinforce];

    /// <summary>
    /// <b>Gold on the floor comes before all three of them</b>, and it is not in
    /// <see cref="JobPriority"/> because it is not a tile-work job — there is no
    /// <see cref="TileWorkKind"/> for "pick that up", nothing is damaged and
    /// nothing converts. It is searched for first and the enum's order follows.
    ///
    /// <para><b>CONFIRMED by the owner</b> (2026-09-05, Q141): <i>"if there is
    /// gold on the ground an imp will first take from that gold pile and take it
    /// to a treasury, until all of it is gone"</i> — reported from a dungeon
    /// where a marked gem tile sat right beside the gold and <i>nobody was
    /// mining it</i>. That is the strong form: not "an imp will get round to
    /// it", but fetching <b>preempting</b> the job that had been top of the
    /// order since ADR-0018.</para>
    ///
    /// <para>Above claiming and reinforcing by transitivity rather than by a
    /// second observation: excavation already outranks both (Q24, CONFIRMED),
    /// and this outranks excavation.</para>
    /// </summary>
    private const string FetchPriority = "FetchFloorGold";

    /// <summary>
    /// The neighbours a tile can be worked from, in the order they are tried.
    /// Matches the pathfinder's expansion order so that the tile an imp takes is
    /// the tile the search stopped for.
    /// </summary>
    private static readonly int[] NeighbourDx = [0, -1, 1, 0];
    private static readonly int[] NeighbourDy = [-1, 0, 0, 1];

    private readonly IPathfinder _pathfinder;
    private readonly List<TilePos> _route = [];

    public ImpTaskSystem() : this(new GridPathfinder()) { }

    /// <summary>Takes the pathfinder explicitly so a replacement for Q5 can be dropped in.</summary>
    public ImpTaskSystem(IPathfinder pathfinder) =>
        _pathfinder = pathfinder ?? throw new ArgumentNullException(nameof(pathfinder));

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        IReadOnlyList<CreatureState> creatures = context.World.Creatures;

        // Spawn order, deliberately: when two imps could take the same job, the
        // older one gets it, and that has to be reproducible.
        for (int i = 0; i < creatures.Count; i++)
        {
            CreatureState creature = creatures[i];
            CreatureDef definition = context.Database.CreatureById(creature.CreatureTypeId);
            if (!definition.Flags.HasFlag(CreatureFlags.IsWorker)) continue;

            // A collapsed imp does not work (ADR-0051). Its job was cleared when
            // it dropped, so this only stops it picking a new one — but without
            // the guard a dying imp would keep mining, which is a strange thing
            // for something lying on the floor to do.
            if (CreatureDeathRules.IsDown(creature)) continue;

            Update(context, creature, definition);
        }
    }

    private void Update(SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        // One grid for the whole of this creature's tick. Every question below
        // that depends on what this worker can stand on — where it may route,
        // which stance a job is done from, whether it has arrived — has to be
        // answered against the same world, or a worker can be routed by one
        // answer and judged by another.
        var grid = new WorldPathGrid(context.Database, context.World, definition);

        if (creature.Activity == CreatureActivity.DeliveringGold)
        {
            UpdateDelivering(context, creature, grid);
            return;
        }

        if (creature.Activity == CreatureActivity.FetchingGold)
        {
            UpdateFetching(context, creature, definition, grid);
            return;
        }

        if (creature.TargetTileIndex >= 0 && !JobStillStands(context, creature))
        {
            context.Emit(SystemName, "job_abandoned",
                $"imp={creature.Id} {Describe(context.World, creature.TargetTileIndex)} " +
                $"{creature.TargetWork} reason=gone");
            GoIdle(creature);
            return;
        }

        // An imp with gold in its hands and no job is not weighing what to do
        // next: it is running to put the load down. This is the other half of
        // the mining job — the seam ran out mid-load — and it comes before job
        // selection rather than inside it, which is why carrying is absent from
        // JobPriority (Q31, ADR-0018). The timer gate is what stops an imp with
        // nowhere to unload from searching the whole dungeon every tick.
        if (creature.GoldHeld > 0 && creature.TargetTileIndex < 0 && creature.TimerTicks <= 0
            && TryRouteToSomewhereItFits(context, creature, grid))
        {
            BeginDelivering(creature);
            return;
        }

        switch (creature.Activity)
        {
            case CreatureActivity.Idle:
                UpdateIdle(context, creature, definition, grid);
                break;

            case CreatureActivity.MovingToJob:
                UpdateMoving(context, creature, grid);
                break;

            case CreatureActivity.Working:
                UpdateWorking(context, creature, definition, grid);
                break;

            default:
                GoIdle(creature);
                break;
        }
    }

    private void UpdateIdle(
        SimulationContext context, CreatureState creature, CreatureDef definition, WorldPathGrid grid)
    {
        creature.TimerTicks--;
        if (creature.TimerTicks > 0) return;

        if (!TryTakeJob(context, creature, definition, grid))
            creature.TimerTicks = IdleDelayTicks(context.Database);
    }

    private void UpdateMoving(SimulationContext context, CreatureState creature, WorldPathGrid grid)
    {
        if (IsInWorkingPosition(context, creature, grid))
        {
            creature.Path.Clear();
            creature.PathIndex = 0;
            creature.Activity = CreatureActivity.Working;
            creature.TimerTicks = SimulationTiming.WorkApplicationPeriodTicks;
            return;
        }

        if (creature.PathIndex < creature.Path.Count) return;

        // Walked the whole route and the target is still not next to it: the
        // world changed under the route. Drop the job rather than re-planning
        // from here, so an imp cannot get stuck chasing a moving answer.
        context.Emit(SystemName, "job_abandoned",
            $"imp={creature.Id} {Describe(context.World, creature.TargetTileIndex)} " +
            $"{creature.TargetWork} reason=out_of_reach");
        GoIdle(creature);
    }

    private void UpdateWorking(
        SimulationContext context, CreatureState creature, CreatureDef definition, WorldPathGrid grid)
    {
        if (!IsInWorkingPosition(context, creature, grid))
        {
            GoIdle(creature);
            return;
        }

        // Decrement first, then act on zero: setting the timer to N and spending
        // one tick each time makes applications exactly N ticks apart, which is
        // what "one application per work period" has to mean.
        creature.TimerTicks--;
        if (creature.TimerTicks > 0) return;

        // Stop before the swing that would not fit — but only if there is
        // somewhere to take the load. An imp mines into its own hands up to
        // maxGoldHeld and then runs to the drop spot (CONFIRMED, Q31); an imp
        // with nowhere to run to keeps mining instead, which is also what the
        // original does. See WouldOverfillItsHands.
        if (WouldOverfillItsHands(context, creature, definition)
            && TryRouteToSomewhereItFits(context, creature, grid))
        {
            BeginDelivering(creature);
            return;
        }

        context.World.QueueWork(new TileWorkOrder(
            creature.TargetTileIndex, creature.TargetWork, creature.OwnerPlayerId, creature.Id));
        creature.TimerTicks = SimulationTiming.WorkApplicationPeriodTicks;
    }

    /// <summary>
    /// Whether one more swing would free more gold than this worker can hold.
    ///
    /// This is where an imp decides to break off and carry, and the rule is the
    /// only stopping point that neither wastes gold nor overfills a pair of
    /// hands. The obvious alternative — swing until the hands are exactly full —
    /// has to throw the overflow away, and with the shipped numbers that is most
    /// of a seam: <c>MineGoldHealth</c> is −300 against a seam's 660 health and
    /// 3000 gold, so one swing frees 1363 where the Imp's <c>maxGoldHeld</c> is
    /// 1400.
    ///
    /// <para>That near-miss is worth noticing rather than smoothing over. 1400 is
    /// almost exactly <i>two</i> swings' worth under the Q47 reading that an
    /// application lands twice a second and applies half the tabled delta
    /// (2 × 681 = 1362) — independent support for that reading, from a number
    /// nothing else in the tile mechanics touches. Nothing here depends on it:
    /// at the whole-delta granularity we currently run, an imp makes one trip per
    /// swing instead of one per two.</para>
    ///
    /// <para>Says nothing about whether there is anywhere to take the load. That
    /// is the caller's second question, and the answer decides whether the imp
    /// leaves or keeps mining.</para>
    /// </summary>
    private static bool WouldOverfillItsHands(
        SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        if (creature.GoldHeld <= 0) return false;

        ref TileState tile = ref context.World.TileAtIndex(creature.TargetTileIndex);
        int yield = TileWorkRules.GoldYield(
            context.Database, tile, creature.TargetWork, creature.OwnerPlayerId);

        return yield > 0 && creature.GoldHeld + yield > definition.MaxGoldHeld;
    }

    /// <summary>
    /// Commits a worker to the road, once a route to somewhere its load fits has
    /// already been planned by <see cref="TryRouteToSomewhereItFits"/>.
    ///
    /// Sets the fields by hand rather than calling
    /// <see cref="CreatureState.ClearJob"/>, which would throw away the route
    /// that was just planned.
    /// </summary>
    /// <summary>
    /// Looks for the nearest reachable tile with gold lying on it and sets off,
    /// if this worker has a hand free.
    ///
    /// <para>Same search as every other job, for the same three reasons: it
    /// gives the nearest pile in steps, the route to it, and rules out every
    /// unreachable pile without ever building a list. <b>The third is what
    /// reproduces the owner's own correction</b> — see
    /// <see cref="UpdateFetching"/>.</para>
    ///
    /// <para><b>One imp to a pile tile</b>, the same cap a floor job gets
    /// (<see cref="ImpCrowding"/>: one for a tile a worker stands on). Gold is on
    /// the floor and an imp stands on it to pick it up, so the stance decides the
    /// number and nothing new is invented. Whether the original crowds a pile
    /// differently is UNKNOWN and nobody has watched it.</para>
    ///
    /// <para><b>Only with room in its hands.</b> A worker already carrying its
    /// limit is going to a treasury, not to another pile — and the case of a
    /// part-loaded imp topping up from a pile it passes is Q142, deliberately
    /// not decided here: an imp of ours with gold in its hands is already in
    /// <see cref="CreatureActivity.DeliveringGold"/> before job selection is
    /// reached, so the question does not arise until something changes that.</para>
    /// </summary>
    private bool TryFetchGold(
        SimulationContext context, CreatureState creature, CreatureDef definition, WorldPathGrid grid)
    {
        if (creature.GoldHeld >= definition.MaxGoldHeld) return false;

        WorldState world = context.World;

        if (!_pathfinder.TryFindNearest(
                grid, creature.Tile, at => CanFetchFrom(context, at, creature.Id), _route))
        {
            return false;
        }

        TilePos standing = _route.Count > 0 ? _route[_route.Count - 1] : creature.Tile;
        int tileIndex = (standing.Y * world.Width) + standing.X;

        creature.TargetTileIndex = tileIndex;
        creature.TargetWork = default;
        creature.Path.Clear();
        creature.Path.AddRange(_route);
        creature.PathIndex = 0;
        creature.Activity = CreatureActivity.FetchingGold;
        creature.TimerTicks = 0;

        context.Emit(SystemName, "fetch_taken",
            $"imp={creature.Id} player={creature.OwnerPlayerId} " +
            $"{Describe(world, tileIndex)} " +
            $"gold={FloorGold.OnTile(world, context.Database, tileIndex)} " +
            $"priority={FetchPriority} steps={_route.Count}");
        return true;
    }

    /// <summary>
    /// Whether a worker standing here would be standing on gold somebody could
    /// pick up. The pile is worked from the tile itself, so this tests the tile
    /// underfoot and no neighbours — unlike a wall, which is worked from beside
    /// it.
    /// </summary>
    private static bool CanFetchFrom(SimulationContext context, TilePos at, int askingCreatureId)
    {
        WorldState world = context.World;
        if (!world.Contains(at)) return false;

        int index = (at.Y * world.Width) + at.X;
        if (FloorGold.OnTile(world, context.Database, index) <= 0) return false;

        return !ImpCrowding.FaceIsFull(
            world, context.Database, index, at, askingCreatureId, workedFromTheTile: true);
    }

    /// <summary>
    /// One tick of going to get gold: arrive, fill your hands, become a carrier.
    ///
    /// <para><b>The pick-up is instant on arrival</b> rather than costing a work
    /// period. Picking gold up is not work — nothing is damaged, no health delta
    /// applies, and the balance table has no number for it — so charging it a
    /// swing would be inventing a duration. If the original animates a pause
    /// here it is Q143, and that is worth a stopwatch rather than a guess.</para>
    ///
    /// <para><b>This is also where the owner's correction lands</b>
    /// (2026-09-05): imps put onto a marked gem tile that was <i>not</i>
    /// connected to the keeper's dungeon mined it happily and <b>never carried
    /// any gold home</b>. Nothing here codes that. It falls out of two searches
    /// that both walk — an imp cannot reach a pile it cannot walk to, and
    /// <see cref="TryRouteToSomewhereItFits"/> cannot find a store across water —
    /// so a stranded imp picks the gold up, finds nowhere to put it, and stands
    /// there while the pile grows around it. <b>Whether the original's rule is
    /// really reachability</b> or whether it is about the ground being claimed
    /// is Q144: both fit, because the tile in question was neither.</para>
    /// </summary>
    private void UpdateFetching(
        SimulationContext context, CreatureState creature, CreatureDef definition, WorldPathGrid grid)
    {
        WorldState world = context.World;

        // Somebody else emptied it, or it was never there.
        if (creature.TargetTileIndex < 0
            || FloorGold.OnTile(world, context.Database, creature.TargetTileIndex) <= 0)
        {
            context.Emit(SystemName, "fetch_abandoned",
                $"imp={creature.Id} {Describe(world, Math.Max(0, creature.TargetTileIndex))} " +
                "reason=gone");
            GoIdle(creature);
            return;
        }

        // Still on the road.
        int here = (creature.Tile.Y * world.Width) + creature.Tile.X;
        if (here != creature.TargetTileIndex)
        {
            if (creature.PathIndex < creature.Path.Count) return;

            context.Emit(SystemName, "fetch_abandoned",
                $"imp={creature.Id} {Describe(world, creature.TargetTileIndex)} " +
                "reason=out_of_reach");
            GoIdle(creature);
            return;
        }

        int room = definition.MaxGoldHeld - creature.GoldHeld;
        int taken = TakeFromPilesOn(context, creature.TargetTileIndex, room);
        creature.GoldHeld += taken;

        context.Emit(SystemName, "gold_picked_up",
            $"imp={creature.Id} player={creature.OwnerPlayerId} amount={taken} " +
            $"{Describe(world, creature.TargetTileIndex)} " +
            $"left={FloorGold.OnTile(world, context.Database, creature.TargetTileIndex)} " +
            $"carrying={creature.GoldHeld}");

        if (creature.GoldHeld <= 0)
        {
            GoIdle(creature);
            return;
        }

        // Straight into the carrying half, which is the same mechanic whatever
        // put the gold in its hands.
        if (TryRouteToSomewhereItFits(context, creature, grid))
        {
            BeginDelivering(creature);
            return;
        }

        context.Emit(SystemName, "delivery_blocked",
            $"imp={creature.Id} player={creature.OwnerPlayerId} gold={creature.GoldHeld} " +
            "reason=no_room after=fetch (Q144: the owner has watched exactly this)");
        GoIdle(creature);
        creature.TimerTicks = IdleDelayTicks(context.Database);
    }

    /// <summary>
    /// Takes up to <paramref name="room"/> off the piles on one tile, oldest
    /// first, and returns how much came off.
    ///
    /// <para>Oldest first — spawn order — for the reason everything else in this
    /// project iterates in spawn order: it is reproducible, and a rule that
    /// preferred the biggest or the nearest pile would be a rule nobody has
    /// watched. It matters because a creature's death leaves up to three piles
    /// on one tile (ADR-0055), so which one an imp empties first is a real
    /// question, and this is an answer of ours.</para>
    /// </summary>
    private static int TakeFromPilesOn(SimulationContext context, int tileIndex, int room)
    {
        if (room <= 0) return 0;

        var piles = new List<ObjectState>();
        FloorGold.PilesOn(context.World, context.Database, tileIndex, piles);

        int taken = 0;
        foreach (ObjectState pile in piles)
        {
            if (taken >= room) break;
            taken += FloorGold.Take(context.World, context.Database, pile, room - taken);
        }

        return taken;
    }

    private static void BeginDelivering(CreatureState creature)
    {
        creature.TargetTileIndex = -1;
        creature.TargetWork = default;
        creature.Activity = CreatureActivity.DeliveringGold;
        creature.TimerTicks = 0;
    }

    /// <summary>
    /// One tick of carrying: put the load down if this is somewhere it can go,
    /// otherwise keep walking, otherwise look for somewhere else.
    ///
    /// <para>The put-down test runs every tick rather than only on arrival, so an
    /// imp that walks over a treasury tile with room in it uses that one. Ours:
    /// what the original does when a nearer store appears mid-journey is
    /// UNKNOWN, and the alternative — march to whatever was nearest when the
    /// journey started — would have an imp walk past an empty treasury.</para>
    ///
    /// <para>Re-routing on arrival is what makes a load spill from one treasury
    /// tile into the next: the imp fills the tile it is standing on, finds it
    /// still has gold, and looks for the nearest tile with room left. That is
    /// Q35's spillover, arrived at rather than coded.</para>
    ///
    /// <para>A worker whose destination filled up while it walked, and which can
    /// then find nowhere else, gives up and goes back to looking for work still
    /// holding its load. It does not stand there: "goes and finds other work" is
    /// what the original's imps do when they cannot get at what they wanted
    /// (Q43), and the gold stays in its hands until somewhere opens up.</para>
    /// </summary>
    private void UpdateDelivering(
        SimulationContext context, CreatureState creature, WorldPathGrid grid)
    {
        if (TryPutDown(context, creature) && creature.GoldHeld == 0)
        {
            GoIdle(creature);
            return;
        }

        // Still on the road.
        if (creature.PathIndex < creature.Path.Count) return;

        if (TryRouteToSomewhereItFits(context, creature, grid)) return;

        context.Emit(SystemName, "delivery_blocked",
            $"imp={creature.Id} player={creature.OwnerPlayerId} gold={creature.GoldHeld} " +
            "reason=no_room (Q140: the original may put the load down on the floor here)");
        GoIdle(creature);
        creature.TimerTicks = IdleDelayTicks(context.Database);
    }

    /// <summary>
    /// Puts as much of the load as will fit into the tile the worker is standing
    /// on, and says whether any of it went in.
    ///
    /// Deposits into <i>this</i> tile rather than through
    /// <see cref="GoldStore.Deposit"/>, because a carrier's gold lands where the
    /// carrier is. Which tile of a multi-tile treasury that turns out to be is
    /// therefore decided by the walk, which is the closest thing to Q35's answer
    /// that does not require guessing.
    /// </summary>
    private static bool TryPutDown(SimulationContext context, CreatureState creature)
    {
        WorldState world = context.World;
        TilePos here = creature.Tile;
        if (!world.Contains(here)) return false;

        int index = (here.Y * world.Width) + here.X;
        ref TileState tile = ref world.TileAtIndex(index);
        if (tile.OwnerPlayerId != creature.OwnerPlayerId) return false;

        int put = GoldStore.DepositInto(world, context.Database, index, creature.GoldHeld);
        if (put <= 0) return false;

        creature.GoldHeld -= put;
        context.Emit(SystemName, "gold_delivered",
            $"imp={creature.Id} player={creature.OwnerPlayerId} amount={put} " +
            $"at=({here.X},{here.Y}) tile_total={tile.GoldHeld} carrying={creature.GoldHeld}");
        return true;
    }

    /// <summary>
    /// Routes the worker to the nearest tile of its keeper's that has room for
    /// gold — a treasury tile or a dungeon-heart tile, since those are the only
    /// two the data gives a capacity.
    ///
    /// Same search as job selection, for the same reason: "nearest" has to mean
    /// nearest by walking, and unreachable stores must not be considered at all.
    /// </summary>
    private bool TryRouteToSomewhereItFits(
        SimulationContext context, CreatureState creature, WorldPathGrid grid)
    {
        byte player = creature.OwnerPlayerId;

        if (!_pathfinder.TryFindNearest(grid, creature.Tile, at => HasRoomForGold(context, at, player), _route))
            return false;

        creature.Path.Clear();
        creature.Path.AddRange(_route);
        creature.PathIndex = 0;

        TilePos drop = _route.Count > 0 ? _route[_route.Count - 1] : creature.Tile;
        context.Emit(SystemName, "delivering",
            $"imp={creature.Id} player={player} gold={creature.GoldHeld} " +
            $"to=({drop.X},{drop.Y}) steps={_route.Count}");
        return true;
    }

    private static bool HasRoomForGold(SimulationContext context, TilePos at, byte playerId)
    {
        WorldState world = context.World;
        if (!world.Contains(at)) return false;

        int index = (at.Y * world.Width) + at.X;
        ref TileState tile = ref world.TileAtIndex(index);

        // Room-aware: the nine tiles of a dungeon heart's raised platform hold
        // nothing (Q52), so an imp must not set off for one.
        return tile.OwnerPlayerId == playerId
               && GoldStore.SpaceIn(world, context.Database, index) > 0;
    }

    /// <summary>
    /// Looks for the nearest tile this keeper has marked that this creature can
    /// work, and takes it.
    ///
    /// One search answers both halves of the question. Walking outwards from the
    /// creature and stopping at the first tile a job can be worked from gives
    /// the nearest job measured in steps, and gives the route to it, and rules
    /// out everything unreachable, all without ever building a list of
    /// candidates to sort.
    /// </summary>
    private bool TryTakeJob(
        SimulationContext context, CreatureState creature, CreatureDef definition, WorldPathGrid grid)
    {
        // Gold on the floor first, above every tile job -- see FetchPriority.
        if (TryFetchGold(context, creature, definition, grid)) return true;

        // Strict priority: the whole map is searched for excavation before a
        // single claimable tile is considered, and so on. Three searches, not
        // one scored search over a mixed candidate list — because the order is
        // an ordering, not a weighting.
        foreach (JobPriority priority in Priorities)
        {
            if (TryTakeJobAt(context, creature, grid, priority)) return true;
        }

        return false;
    }

    private bool TryTakeJobAt(
        SimulationContext context, CreatureState creature, WorldPathGrid grid, JobPriority priority)
    {
        WorldState world = context.World;
        byte player = creature.OwnerPlayerId;

        bool found = _pathfinder.TryFindNearest(
            grid,
            creature.Tile,
            stand => TryJobFrom(context, grid, stand, player, priority, creature.Id, out _, out _),
            _route);

        if (!found) return false;

        TilePos standing = _route.Count > 0 ? _route[_route.Count - 1] : creature.Tile;
        if (!TryJobFrom(context, grid, standing, player, priority, creature.Id, out int tileIndex, out TileWorkKind kind))
            return false;

        creature.TargetTileIndex = tileIndex;
        creature.TargetWork = kind;
        creature.Path.Clear();
        creature.Path.AddRange(_route);
        creature.PathIndex = 0;

        if (_route.Count == 0)
        {
            creature.Activity = CreatureActivity.Working;
            creature.TimerTicks = SimulationTiming.WorkApplicationPeriodTicks;
        }
        else
        {
            creature.Activity = CreatureActivity.MovingToJob;
        }

        context.Emit(SystemName, "job_taken",
            $"imp={creature.Id} player={player} " +
            $"{Describe(world, tileIndex)} {kind} priority={priority} steps={_route.Count}");
        return true;
    }

    /// <summary>
    /// A job of the given priority that a worker at <paramref name="standing"/>
    /// could be doing — the tile underfoot first, then the four around it.
    ///
    /// <para>The tile underfoot comes first because a job that can be done where
    /// the worker already is beats one it would have to reach for; the neighbour
    /// order after that is the pathfinder's, so that the tile an imp takes is the
    /// tile the search stopped for.</para>
    /// </summary>
    private static bool TryJobFrom(
        SimulationContext context,
        WorldPathGrid grid,
        TilePos standing,
        byte playerId,
        JobPriority priority,
        int askingCreatureId,
        out int tileIndex,
        out TileWorkKind kind)
    {
        if (TryJobOn(context, grid, standing, standing, playerId, priority, askingCreatureId,
                     out tileIndex, out kind))
        {
            return true;
        }

        for (int i = 0; i < NeighbourDx.Length; i++)
        {
            var target = new TilePos(standing.X + NeighbourDx[i], standing.Y + NeighbourDy[i]);
            if (TryJobOn(context, grid, standing, target, playerId, priority, askingCreatureId,
                         out tileIndex, out kind))
            {
                return true;
            }
        }

        tileIndex = -1;
        kind = default;
        return false;
    }

    /// <summary>
    /// Whether a worker standing at <paramref name="standing"/> can do work of
    /// this priority on <paramref name="target"/>.
    ///
    /// <para>Three conditions, and the middle one is what changed with Q44.
    /// The work must <i>apply</i> to the tile; the worker must be in the
    /// <see cref="WorkStance"/> that job is done from — <b>on</b> the tile for
    /// floor, <b>beside</b> it for a wall; and, unless the keeper asked for the
    /// work by name, the tile must touch ground that keeper already holds.</para>
    /// </summary>
    private static bool TryJobOn(
        SimulationContext context,
        WorldPathGrid grid,
        TilePos standing,
        TilePos target,
        byte playerId,
        JobPriority priority,
        int askingCreatureId,
        out int tileIndex,
        out TileWorkKind kind)
    {
        tileIndex = -1;
        kind = default;

        WorldState world = context.World;
        if (!world.Contains(target)) return false;

        ref TileState tile = ref world.Tile(target);

        switch (priority)
        {
            case JobPriority.MarkedExcavation:
                if (!tile.IsTaggedBy(playerId)) return false;
                if (!TileWorkRules.TryTaggedWork(context.Database, tile, playerId, out kind)) return false;
                break;

            // Claiming floor and taking somebody else's room are the same
            // priority and two kinds of work, because the balance table gives
            // them separate rates (ClaimTileHealth +825, ConvertRoomHealth
            // −4000) and the terrain flag partitions them exactly: a room tile
            // is one and only one of the two. The owner describes both as
            // claiming (Q24, Q92), so nothing here has to choose an order
            // between them.
            case JobPriority.Claim:
                if (TileWorkRules.Applies(context.Database, tile, TileWorkKind.Claim, playerId))
                    kind = TileWorkKind.Claim;
                else if (TileWorkRules.Applies(context.Database, tile, TileWorkKind.ConvertRoom, playerId))
                    kind = TileWorkKind.ConvertRoom;
                else
                    return false;
                break;

            case JobPriority.Reinforce:
                if (!TileWorkRules.Applies(context.Database, tile, TileWorkKind.Reinforce, playerId))
                    return false;
                kind = TileWorkKind.Reinforce;
                break;

            default:
                return false;
        }

        // An imp claims a floor tile by standing on it (Q44, CONFIRMED) and digs
        // a wall from beside it (Q27, CONFIRMED). One stance rule decides which,
        // and a worker in the wrong one has no job here.
        bool onTheTile = WorkStance.IsOnTheTile(context.Database, world, grid, target, kind);
        if (onTheTile != (standing == target))
        {
            kind = default;
            return false;
        }

        // Claiming and reinforcing spread from ground the keeper already holds;
        // marked work does not, because the keeper asked for it by name.
        //
        // Which tile carries that condition follows from the stance, and this is
        // the other half of what Q44 moved. A wall job is taken from the keeper's
        // own land, so the STANDING tile must be owned. A floor job is done
        // standing on the tile being claimed — which is by definition not owned
        // yet — so it is the TARGET that must touch owned land instead. Both
        // readings say the same thing about the map (territory grows outwards,
        // never in islands); they differ only in where the imp's feet are (Q42).
        if (priority != JobPriority.MarkedExcavation)
        {
            bool reachesOwnLand = onTheTile
                ? TouchesOwnLand(context, world, target, playerId)
                : StandsOnOwnLand(context, world, standing, playerId);

            if (!reachesOwnLand)
            {
                kind = default;
                return false;
            }
        }

        // Three workers to a face for a wall and exactly ONE for a tile a worker
        // stands on to claim it (Q43, Q44), and the surplus goes and finds other
        // work rather than queueing. Rejecting the FACE rather than the tile is
        // what makes that happen: the search simply carries on to the next
        // standing tile, which may be another side of this same block, another
        // block, or another priority entirely.
        int index = (target.Y * world.Width) + target.X;
        if (ImpCrowding.FaceIsFull(
                world, context.Database, index, standing, askingCreatureId, onTheTile))
        {
            kind = default;
            return false;
        }

        tileIndex = index;
        return true;
    }

    /// <summary>
    /// Whether this tile belongs to the keeper and is the kind of tile a keeper
    /// can hold at all.
    /// </summary>
    private static bool StandsOnOwnLand(
        SimulationContext context, WorldState world, TilePos standing, byte playerId)
    {
        if (!world.Contains(standing)) return false;

        ref TileState tile = ref world.Tile(standing);
        return tile.OwnerPlayerId == playerId
            && context.Database.TerrainById(tile.TerrainId).IsOwnable;
    }

    /// <summary>
    /// Whether any tile orthogonally adjacent to <paramref name="target"/>
    /// belongs to the keeper — the condition on an unprompted job the worker
    /// does standing on the target itself.
    ///
    /// Orthogonal, matching the way work spreads (Q27), and deliberately not the
    /// eight-connected neighbourhood the mover uses: a keeper's territory
    /// creeping diagonally across a corner is not what the original shows.
    /// STRONGLY INFERRED; Q42.
    /// </summary>
    private static bool TouchesOwnLand(
        SimulationContext context, WorldState world, TilePos target, byte playerId)
    {
        for (int i = 0; i < NeighbourDx.Length; i++)
        {
            var neighbour = new TilePos(target.X + NeighbourDx[i], target.Y + NeighbourDy[i]);
            if (StandsOnOwnLand(context, world, neighbour, playerId)) return true;
        }

        return false;
    }

    /// <summary>
    /// Whether the creature's target is still workable — and, if the keeper
    /// asked for it by name, still asked for.
    ///
    /// The mark is only a condition on excavation. An unprompted claim or
    /// reinforce was never tagged, so testing the tag for those would abandon
    /// the job on the tick after it was taken and take it again on the next,
    /// forever.
    /// </summary>
    private static bool JobStillStands(SimulationContext context, CreatureState creature)
    {
        ref TileState tile = ref context.World.TileAtIndex(creature.TargetTileIndex);

        if (IsExcavation(creature.TargetWork) && !tile.IsTaggedBy(creature.OwnerPlayerId))
            return false;

        return TileWorkRules.Applies(
            context.Database, tile, creature.TargetWork, creature.OwnerPlayerId);
    }

    /// <summary>
    /// Whether this kind of work can only have come from a dig marker.
    ///
    /// Reads <see cref="TileWorkRules.TaggedWork"/> rather than listing kinds,
    /// so that a kind added to the marker mapping cannot quietly fall out of
    /// this test.
    /// </summary>
    private static bool IsExcavation(TileWorkKind kind)
    {
        IReadOnlyList<TileWorkKind> tagged = TileWorkRules.TaggedWork;
        for (int i = 0; i < tagged.Count; i++)
            if (tagged[i] == kind) return true;
        return false;
    }

    /// <summary>
    /// Whether the worker is where it needs to be to swing — which depends on
    /// the job, not only on the distance. See <see cref="WorkStance"/>.
    /// </summary>
    private static bool IsInWorkingPosition(
        SimulationContext context, CreatureState creature, WorldPathGrid grid)
    {
        WorldState world = context.World;
        var target = new TilePos(
            creature.TargetTileIndex % world.Width, creature.TargetTileIndex / world.Width);

        return WorkStance.IsInPosition(
            context.Database, world, grid, creature.Tile, target, creature.TargetWork);
    }

    /// <summary>
    /// Ends the current job and looks again on the next tick.
    ///
    /// Deliberately not the idle delay. An imp whose wall has just fallen moves
    /// straight to the next marked tile in the original — it does not stand
    /// around for five seconds — so the delay belongs to the imp that looked and
    /// found nothing, which is what "idle" names. STRONGLY INFERRED: the
    /// variable is <c>ImpIdleDelayBeforeReevaluation</c>, and an imp that has
    /// just finished a job has not been idle.
    /// </summary>
    private static void GoIdle(CreatureState creature)
    {
        creature.ClearJob();
        creature.TimerTicks = 0;
    }

    /// <summary>
    /// How long a worker with nothing to do waits before looking again.
    /// <c>ImpIdleDelayBeforeReevaluationSeconds</c> — DK2's own name for exactly
    /// this delay.
    /// </summary>
    private static int IdleDelayTicks(GameDatabase database) =>
        SimulationTiming.TicksFromSeconds(
            database[VariableTypeId.ImpIdleDelayBeforeReevaluationSeconds]);

    private static string Describe(WorldState world, int tileIndex) =>
        tileIndex < 0 ? "(none)" : $"({tileIndex % world.Width},{tileIndex / world.Width})";
}
