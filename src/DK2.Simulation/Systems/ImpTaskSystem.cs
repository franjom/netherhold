// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;

namespace DK2.Simulation.Systems;

/// <summary>
/// Gives a worker — <see cref="CreatureFlags.IsWorker"/>, the Imp and the Dwarf
/// — a job: find a tile, walk to it, work it until it is gone.
/// <para><b>Two tiers</b>, STRONGLY INFERRED (ADR-0058,
/// <c>docs/research/imp-task-priority.md</c> §4). Tier A: one whole-map search
/// per kind, in the original's kind order — <see cref="TryTakeJob"/>. Tier B,
/// only once every Tier A kind is empty: one nearest-first sweep asking several
/// kinds at each tile — <see cref="TrySweep"/>. Carrying mined gold is in
/// neither; it is the tail of a mining job, not a job an imp chooses (Q31,
/// ADR-0018). Not implemented: idle wandering (Q41), dragging the unconscious
/// to a prison, fleeing a fight, gold somebody else dropped (imp-tasks.md).</para>
/// </summary>
[Dk2Behavior(Confidence.Hypothesis, "docs/research/imp-tasks.md")]
public sealed class ImpTaskSystem : ISimulationSystem
{
    private const string SystemName = "ImpTask";

    /// <summary>
    /// How far a worker the keeper has just put down looks around itself before
    /// it looks anywhere else: <b>three tiles</b>, Chebyshev — a seven by seven
    /// box centred on where it landed, its own tile first and then in rings.
    /// <para>Three is DK2's number and DK2 reaches it by accident: its scan is
    /// handed a radius of <c>0x2000</c>, two tiles at <c>0x1000</c> to the tile,
    /// but the loop tests its bound <i>after</i> scanning a ring, with a strict
    /// greater-than, from a ring counter starting at one — so it scans rings 1,
    /// 2 and 3 (<c>ImpUtils.cpp</c>, docs/research/hand-of-evil.md). CONFIRMED:
    /// the owner counted it on screen (2026-09-06) — three tiles boost, the
    /// fourth does not. Reproduced rather than corrected (AGENTS.md, Fidelity).</para>
    /// </summary>
    private const int DropScanTiles = 3;

    /// <summary>
    /// While set, every job search below is confined to
    /// <see cref="DropScanTiles"/> of this tile. Non-null only inside
    /// <see cref="TryTakeDroppedJob"/>, which is the one search DK2 bounds.
    /// </summary>
    private TilePos? _within;

    /// <summary>
    /// The tile-work jobs an imp looks for. The first three are Tier A and are
    /// searched over the whole map in this order; <see cref="Reinforce"/> is
    /// Tier B and is only ever found by the nearest-first sweep, alongside gold
    /// on the floor. See the type comment.
    /// </summary>
    private enum JobPriority
    {
        /// <summary>
        /// Claim floor a worker has dug out, once none of its eight neighbours
        /// is still marked for digging. <b>First of everything in the
        /// original</b> (its kind 14, Q154): the task is raised when a dig
        /// leaves plain unowned path beside the keeper's floor, and taking it
        /// is what makes a corridor get claimed behind the digging front rather
        /// than after the last mark on the map is gone. The work it does is an
        /// ordinary <see cref="TileWorkKind.Claim"/>; only the eligibility and
        /// the rank are different.
        /// </summary>
        ClaimFreshlyDug = 0,
        MarkedExcavation = 1,
        Claim = 2,
        Reinforce = 3,
    }

    /// <summary>
    /// The label gold on the floor is taken under. Not in
    /// <see cref="JobPriority"/> because it is not tile work — nothing is damaged
    /// and nothing converts — but it <i>is</i> a job an imp chooses, in Tier B,
    /// where distance is the primary key: a wall beats gold on the same tile, a
    /// nearer pile beats a farther wall (<see cref="TrySweep"/>, ADR-0058).
    /// <para><b>Only with room in its hands.</b> The original refuses the gold
    /// kind once the hands hold a level-scaled share of <c>MaxGoldHeld</c> and
    /// sends the imp to deposit instead (Q142, read); here an imp with gold in
    /// its hands is already delivering before job selection is reached, so the
    /// same outcome falls out without the threshold.</para>
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

            // A creature in the keeper's hand is out of the dungeon: it does
            // not act until it is put down (ADR-0062). A collapsed worker is
            // skipped below for the same reason (ADR-0051) — its job was cleared
            // when it dropped, so the guard only stops it taking a new one.
            if (HandRules.IsHeld(creature)) continue;

            // A prisoner is skipped here as a rule, not a gap: its cell is what
            // it does — wandering is PrisonerWanderSystem's, every tick, and its
            // health is PrisonSystem's, every second (ADR-0073). Still a stated
            // narrowing: the other half of Q172, racing for a chicken dropped
            // among them.
            if (PrisonRules.IsImprisoned(creature)) continue;
            if (CreatureDeathRules.IsDown(creature)) continue;
            if (Combat.CombatRules.Preoccupied(creature)) continue;

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
        CreatureStateMachine.ReleaseIfLeft(creature);

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

        if (creature.Activity == CreatureActivity.FetchingBody)
        {
            UpdateFetchingBody(context, creature, grid);
            return;
        }

        if (creature.Activity == CreatureActivity.CarryingBody)
        {
            UpdateCarryingBody(context, creature, grid);
            return;
        }

        if (creature.Activity == CreatureActivity.FetchingObject)
        {
            UpdateFetchingObject(context, creature, grid);
            return;
        }

        if (creature.Activity == CreatureActivity.CarryingObject)
        {
            UpdateCarryingObject(context, creature, grid);
            return;
        }

        if (creature.TargetTileIndex >= 0 && !JobStillStands(context, creature))
        {
            context.Emit(SystemName, "job_abandoned",
                $"imp={creature.Id} {Describe(context.World, creature.TargetTileIndex)} " +
                $"{creature.TargetWork} reason=gone");
            WorkEnded(context, creature);
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
            FromTaskSelection(creature);
            ToTheStore(creature);
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
            creature.StopMoving();
            creature.Activity = CreatureActivity.Working;
            creature.TimerTicks = SimulationTiming.WorkApplicationPeriodTicks;
            AtWork(context, creature);
            return;
        }

        if (creature.HasRoute) return;

        // Walked the whole route and the target is still not next to it: the
        // world changed under the route. Drop the job rather than re-planning
        // from here, so an imp cannot get stuck chasing a moving answer.
        context.Emit(SystemName, "job_abandoned",
            $"imp={creature.Id} {Describe(context.World, creature.TargetTileIndex)} " +
            $"{creature.TargetWork} reason=out_of_reach");
        StateShadow.ToIdle(context, SystemName, creature);
        GoIdle(creature);
    }

    private void UpdateWorking(
        SimulationContext context, CreatureState creature, CreatureDef definition, WorldPathGrid grid)
    {
        if (!IsInWorkingPosition(context, creature, grid))
        {
            WorkEnded(context, creature);
            GoIdle(creature);
            return;
        }

        // Decrement first, then act on zero: setting the timer to N and spending
        // one tick each time makes applications exactly N ticks apart, starting
        // one period after the imp arrives. The period is
        // SimulationTiming.WorkApplicationPeriodTicks, from the Q19 reading that
        // the tabled health deltas are per second. UNKNOWN (Q20): whether a
        // single imp contributes the whole delta — here it does.
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
            // 26 walks to the store in 6 resuming in 28.
            BeginDelivering(creature);
            ToTheStore(creature);
            return;
        }

        context.World.QueueWork(new TileWorkOrder(
            creature.TargetTileIndex, creature.TargetWork, creature.OwnerPlayerId, creature.Id));
        creature.TimerTicks = SimulationTiming.WorkApplicationPeriodTicks;
    }

    /// <summary>
    /// Whether one more swing would free more gold than this worker can hold —
    /// the point at which an imp breaks off to carry.
    /// <para>Stopping before the swing is the only rule that neither wastes gold
    /// nor overfills a pair of hands: <c>MineGoldHealth</c> is −300 against a
    /// seam's 660 health and 3000 gold, so one swing frees 1363 where the Imp's
    /// <c>maxGoldHeld</c> is 1400 — almost exactly two swings' worth under Q47's
    /// reading (2 × 681 = 1362), independent support for it. Nothing here
    /// depends on that; at the whole-delta granularity we run, an imp makes one
    /// trip per swing. Whether there is anywhere to take the load is the
    /// caller's second question.</para>
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
    /// Commits a worker to picking up the gold on <paramref name="standing"/>,
    /// along the route the last search planned.
    /// <para><b>One imp to a pile tile</b>, the same cap a floor job gets
    /// (<see cref="ImpCrowding"/>: one for a tile a worker stands on). Gold is
    /// on the floor and an imp stands on it to pick it up, so the stance decides
    /// the number. Whether the original crowds a pile differently is
    /// UNKNOWN.</para>
    /// </summary>
    private void CommitFetch(SimulationContext context, CreatureState creature, TilePos standing)
    {
        WorldState world = context.World;
        int tileIndex = (standing.Y * world.Width) + standing.X;

        creature.TargetTileIndex = tileIndex;
        creature.TargetWork = default;
        context.Navigation.TryRoute(creature, standing, Navigation.MoveOptions.Plain);
        creature.Activity = CreatureActivity.FetchingGold;
        creature.TimerTicks = 0;
        creature.LastTaskKind = ImpTaskKind.FetchGold;
        FromTaskSelection(creature);
        CreatureStateMachine.BeginTravel(creature, StateShadow.TravelToPoint, ImpStates.PickingUpGold);

        context.Emit(SystemName, "fetch_taken",
            $"imp={creature.Id} player={creature.OwnerPlayerId} " +
            $"{Describe(world, tileIndex)} " +
            $"gold={FloorGold.OnTile(world, context.Database, tileIndex)} " +
            $"priority={FetchPriority} steps={_route.Count}");
    }

    /// <summary>
    /// Whether a worker standing here would be standing on gold it may pick up.
    /// A pile is worked from the tile itself, so this tests the tile underfoot
    /// and no neighbours — unlike a wall, worked from beside it.
    /// <para><b>The ground has to be the keeper's own, or water.</b> Read from
    /// the original's gold-fetching task, which refuses a tile that is neither
    /// water nor owned by the worker's player before it looks at anything on it.
    /// That is also why imps that mined a gem tile with unclaimed surroundings
    /// never carried the gold home (Q144) — not because they could not walk
    /// there. Why water is exempt is UNKNOWN; without the exception, gold that
    /// fell in would be unrecoverable for good.</para>
    /// </summary>
    private static bool CanFetchFrom(
        SimulationContext context, TilePos at, byte playerId, int askingCreatureId)
    {
        WorldState world = context.World;
        if (!world.Contains(at)) return false;

        int index = (at.Y * world.Width) + at.X;
        ref TileState tile = ref world.TileAtIndex(index);

        if (tile.OwnerPlayerId != playerId
            && !context.Database.TerrainById(tile.TerrainId).IsWater)
        {
            return false;
        }

        if (FloorGold.OnTile(world, context.Database, index) <= 0) return false;

        return !ImpCrowding.FaceIsFull(
            world, context.Database, index, at, askingCreatureId, workedFromTheTile: true);
    }

    /// <summary>
    /// One tick of going to get gold: arrive, fill your hands, become a carrier.
    /// <para><b>The pick-up is instant on arrival</b> rather than costing a work
    /// period. Picking gold up is not work — nothing is damaged, no health delta
    /// applies, and the balance table has no number for it — so charging it a
    /// swing would be inventing a duration. Whether the original animates a
    /// pause here is UNKNOWN (Q143), and wants a stopwatch.</para>
    /// <para>The rule that an imp will not take gold off ground that is not its
    /// keeper's own is in <see cref="CanFetchFrom"/>, not here (Q144).</para>
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
            WorkEnded(context, creature);
            GoIdle(creature);
            return;
        }

        // Still on the road.
        int here = (creature.Tile.Y * world.Width) + creature.Tile.X;
        if (here != creature.TargetTileIndex)
        {
            if (creature.HasRoute) return;

            context.Emit(SystemName, "fetch_abandoned",
                $"imp={creature.Id} {Describe(world, creature.TargetTileIndex)} " +
                "reason=out_of_reach");
            StateShadow.ToIdle(context, SystemName, creature);
            GoIdle(creature);
            return;
        }

        StateShadow.Arrive(context, SystemName, creature);
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
            WorkEnded(context, creature);
            GoIdle(creature);
            return;
        }

        // Straight into the carrying half, which is the same mechanic whatever
        // put the gold in its hands.
        if (TryRouteToSomewhereItFits(context, creature, grid))
        {
            BeginDelivering(creature);
            ToTheStore(creature);
            return;
        }

        context.Emit(SystemName, "delivery_blocked",
            $"imp={creature.Id} player={creature.OwnerPlayerId} gold={creature.GoldHeld} " +
            "reason=no_room after=fetch");
        WorkEnded(context, creature);
        GoIdle(creature);
        creature.TimerTicks = IdleDelayTicks(context.Database);
    }

    /// <summary>
    /// Takes up to <paramref name="room"/> off the piles on one tile, oldest
    /// first, and returns how much came off.
    ///
    /// <para>Oldest first — spawn order, as everything else in this project
    /// iterates, because it is reproducible. Ours: a creature's death leaves up
    /// to three piles on one tile (ADR-0055), so which one an imp empties first
    /// is a real question that nobody has watched.</para>
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

    /// <summary>
    /// Commits a worker to the road, once
    /// <see cref="TryRouteToSomewhereItFits"/> has planned a route to somewhere
    /// its load fits. Sets the fields by hand rather than calling
    /// <see cref="CreatureState.ClearJob"/>, which would throw away the route
    /// that was just planned.
    /// </summary>
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
    /// <para>The put-down test runs every tick rather than only on arrival, so
    /// an imp that walks over a treasury tile with room uses that one; ours,
    /// because what the original does when a nearer store appears mid-journey is
    /// UNKNOWN. Re-routing on arrival is also what makes a load spill from one
    /// treasury tile into the next: Q35's spillover, arrived at rather than
    /// coded.</para>
    /// <para>A worker that can then find nowhere else goes back to looking for
    /// work still holding its load, as the original's imps do (Q43).</para>
    /// </summary>
    private void UpdateDelivering(
        SimulationContext context, CreatureState creature, WorldPathGrid grid)
    {
        if (TryPutDown(context, creature))
        {
            // 28 moves to 29, which deposits what fits and sets 10.
            StateShadow.Arrive(context, SystemName, creature);
            StateShadow.Move(context, SystemName, creature, ImpStates.PuttingDownGold);
            StateShadow.Move(context, SystemName, creature, ImpStates.TaskSelection);

            if (creature.GoldHeld == 0)
            {
                CreatureStateMachine.Release(creature);
                GoIdle(creature);
                return;
            }

            // 10 walks on with the remainder, the spill into the next tile (Q35).
            ToTheStore(creature);
        }

        // Still on the road.
        if (creature.HasRoute) return;

        if (TryRouteToSomewhereItFits(context, creature, grid))
        {
            // Arrived with no room here: through 28 and 29 to 10, which walks on.
            StateShadow.Arrive(context, SystemName, creature);
            StateShadow.Move(context, SystemName, creature, ImpStates.PuttingDownGold);
            StateShadow.Move(context, SystemName, creature, ImpStates.TaskSelection);
            ToTheStore(creature);
            return;
        }

        context.Emit(SystemName, "delivery_blocked",
            $"imp={creature.Id} player={creature.OwnerPlayerId} gold={creature.GoldHeld} " +
            "reason=no_room (Q140: the original may put the load down on the floor here)");
        StateShadow.ToIdle(context, SystemName, creature);
        GoIdle(creature);
        creature.TimerTicks = IdleDelayTicks(context.Database);
    }

    /// <summary>
    /// Puts as much of the load as will fit into the tile the worker is standing
    /// on, and says whether any of it went in.
    /// <para>Deposits into <i>this</i> tile rather than through
    /// <see cref="GoldStore.Deposit"/>, because a carrier's gold lands where the
    /// carrier is. Which tile of a multi-tile treasury that turns out to be is
    /// therefore decided by the walk (Q35).</para>
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

        TilePos drop = _route.Count > 0 ? _route[_route.Count - 1] : creature.Tile;
        if (!context.Navigation.TryRoute(creature, drop, Navigation.MoveOptions.Plain)) return false;
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
    /// Chooses a job, in the two tiers of the type comment.
    /// <para><b>Tier A — kind first, one whole-map search per kind</b>, in DK2's
    /// table order: claim the tile just dug (kind 14), carry a loose object (8),
    /// build placed traps and doors (absent here), dig and mine marked tiles
    /// (1), claim floor (3), carry the unconscious (13), carry a corpse (12) —
    /// <c>imp-task-priority.md</c> §4, ADR-0058/0063/0065. Walking outwards and
    /// stopping at the first tile a job can be worked from gives the nearest job
    /// in steps, its route, and rules out the unreachable without sorting a
    /// candidate list. <b>Tier B — position first</b>, only once all of that is
    /// empty: <see cref="TrySweep"/>.</para>
    /// </summary>
    private bool TryTakeJob(
        SimulationContext context, CreatureState creature, CreatureDef definition, WorldPathGrid grid)
    {
        // Before any of it: the two things the keeper's hand does to this
        // choice. Both come from DK2's job chooser, which asks them in this
        // order and ahead of its own priority chain (ADR-0069).
        if (creature.JustDropped)
        {
            // The ticket is spent whether or not the look finds anything - DK2
            // clears it unconditionally, one instruction before the scan.
            creature.JustDropped = false;

            if (TryTakeDroppedJob(context, creature, definition, grid))
            {
                creature.DroppedTask = creature.LastTaskKind;
                context.Emit(SystemName, "dropped_onto_work",
                    $"imp={creature.Id} player={creature.OwnerPlayerId} " +
                    $"task={creature.DroppedTask} within={DropScanTiles}");
                return true;
            }

            context.Emit(SystemName, "dropped_onto_nothing",
                $"imp={creature.Id} player={creature.OwnerPlayerId} " +
                $"at=({creature.Tile.X},{creature.Tile.Y}) within={DropScanTiles}");

            // And on through the ordinary chain. A worker dropped in an empty
            // corridor still finds work - it just does not get paid extra for
            // it, because DroppedTask stays None.
        }
        else if (creature.DroppedTask != ImpTaskKind.None
                 && creature.DroppedTask == creature.LastTaskKind)
        {
            // Still on the kind it was aimed at, and its last job has ended.
            // More of that kind anywhere on the map and the aimed bonus carries
            // over; none and this is the one place in DK2 that clears it.
            if (TryTakeJobOfKind(context, creature, definition, grid, creature.DroppedTask))
                return true;

            context.Emit(SystemName, "aim_spent",
                $"imp={creature.Id} player={creature.OwnerPlayerId} " +
                $"task={creature.DroppedTask} reason=no_more_of_that_kind");
            creature.DroppedTask = ImpTaskKind.None;
        }

        // Tier A: kind first. One search per kind over the whole map, not one
        // scored search over a mixed list, because the order is an ordering.
        if (TryTakeJobAt(context, creature, grid, JobPriority.ClaimFreshlyDug)) return true;

        // Kind 8 - carry a loose object to its room - is SECOND in DK2's table,
        // above marked excavation and above claiming, so an imp puts a book away
        // before it digs a wall the keeper marked (ADR-0065).
        if (TryTakeObject(context, creature, definition, grid)) return true;

        if (TryTakeJobAt(context, creature, grid, JobPriority.MarkedExcavation)) return true;
        if (TryTakeJobAt(context, creature, grid, JobPriority.Claim)) return true;

        // The last two entries of DK2's Tier A table, in its order: kind 13,
        // carry an unconscious creature, then kind 12, a corpse. Both below
        // claiming, so an imp finishes claiming the dungeon before it picks
        // anybody up - the original's ordering, and not an obvious one.
        if (TryTakeBody(context, creature, definition, grid, corpses: false)) return true;
        if (TryTakeBody(context, creature, definition, grid, corpses: true)) return true;

        // Tier B: position first.
        if (TrySweep(context, creature, definition, grid)) return true;

        // Nothing anywhere. DK2 zeroes LastTaskType at exactly this point, and
        // it matters: with no kind to match, the DroppedTask comparison above can
        // never fire again, so an aimed worker that runs out of work keeps its
        // bonus rather than losing it. The original's arithmetic, not a choice.
        creature.LastTaskKind = ImpTaskKind.None;
        return false;
    }

    /// <summary>
    /// What a worker the keeper has just put down looks for, and where.
    /// <para><b>A different order from <see cref="TryTakeJob"/>'s</b>: DK2's drop
    /// scan is one composite per-tile predicate, ordered excavation, claiming,
    /// carrying a body, reinforcing, gold, objects, corpses — so a worker dropped
    /// beside a marked wall digs it rather than putting a book away (ADR-0065).
    /// Two of its nine kinds are absent (no traps or doors here) and one is
    /// unidentified; kind 14, claiming the just-dug tile, is not among them. All
    /// of it inside <see cref="DropScanTiles"/> of where the worker landed,
    /// measured to <b>the tile the work is at</b> — CONFIRMED, owner's count: a
    /// marked wall four away does not pay, though a floor tile three does.</para>
    /// </summary>
    private bool TryTakeDroppedJob(
        SimulationContext context, CreatureState creature, CreatureDef definition, WorldPathGrid grid)
    {
        _within = creature.Tile;
        try
        {
            if (TryTakeJobAt(context, creature, grid, JobPriority.MarkedExcavation)) return true;
            if (TryTakeJobAt(context, creature, grid, JobPriority.Claim)) return true;
            if (TryTakeBody(context, creature, definition, grid, corpses: false)) return true;
            if (TryTakeJobAt(context, creature, grid, JobPriority.Reinforce)) return true;
            if (TryTakeFloorGold(context, creature, definition, grid)) return true;
            if (TryTakeObject(context, creature, definition, grid)) return true;
            return TryTakeBody(context, creature, definition, grid, corpses: true);
        }
        finally
        {
            _within = null;
        }
    }

    /// <summary>
    /// One kind, whole map - DK2's <c>TakeJob(kind, 0x7fffffff)</c>, the retry
    /// that decides whether an aimed worker keeps its bonus.
    ///
    /// <para><see cref="ImpTaskKind.ClaimFreshlyDug"/> cannot arrive here:
    /// <see cref="KindOf"/> records kind 14's work as an ordinary
    /// <see cref="ImpTaskKind.Claim"/>, which is what DK2 stores.</para>
    /// </summary>
    private bool TryTakeJobOfKind(
        SimulationContext context, CreatureState creature, CreatureDef definition,
        WorldPathGrid grid, ImpTaskKind kind) => kind switch
    {
        ImpTaskKind.Excavate => TryTakeJobAt(context, creature, grid, JobPriority.MarkedExcavation),
        ImpTaskKind.Claim => TryTakeJobAt(context, creature, grid, JobPriority.Claim),
        ImpTaskKind.Reinforce => TryTakeJobAt(context, creature, grid, JobPriority.Reinforce),
        ImpTaskKind.FetchGold => TryTakeFloorGold(context, creature, definition, grid),
        ImpTaskKind.CarryObject => TryTakeObject(context, creature, definition, grid),
        ImpTaskKind.CarryCorpse => TryTakeBody(context, creature, definition, grid, corpses: true),
        ImpTaskKind.CarryUnconscious => TryTakeBody(context, creature, definition, grid, corpses: false),
        _ => false,
    };

    /// <summary>
    /// The nearest pile of floor gold this worker may pick up, on its own.
    ///
    /// <para><see cref="TrySweep"/> asks this question and the reinforce
    /// question at the same tile, because in Tier B distance is the primary key.
    /// Here it is asked alone, which is what the drop scan and the same-kind
    /// retry both need - DK2 has a per-kind entry point for exactly this.</para>
    /// </summary>
    private bool TryTakeFloorGold(
        SimulationContext context, CreatureState creature, CreatureDef definition, WorldPathGrid grid)
    {
        if (creature.GoldHeld >= definition.MaxGoldHeld) return false;

        byte player = creature.OwnerPlayerId;

        if (!_pathfinder.TryFindNearest(
                grid, creature.Tile,
                at => InScanRange(at) && CanFetchFrom(context, at, player, creature.Id),
                _route))
            return false;

        TilePos standing = _route.Count > 0 ? _route[_route.Count - 1] : creature.Tile;
        if (!CanFetchFrom(context, standing, player, creature.Id)) return false;

        CommitFetch(context, creature, standing);
        return true;
    }

    /// <summary>
    /// Whether a search may stop here - always, unless a drop scan is running.
    /// </summary>
    private bool InScanRange(TilePos at) =>
        _within is not TilePos centre
        || (Math.Abs(at.X - centre.X) <= DropScanTiles
            && Math.Abs(at.Y - centre.Y) <= DropScanTiles);

    /// <summary>The same test, for a search that names its work by tile index.</summary>
    private bool InScanRange(WorldState world, int tileIndex) =>
        _within is null
        || InScanRange(new TilePos(tileIndex % world.Width, tileIndex / world.Width));

    /// <summary>
    /// The task kind DK2 records for a job of this priority.
    ///
    /// <para><see cref="JobPriority.ClaimFreshlyDug"/> maps to
    /// <see cref="ImpTaskKind.Claim"/> because the original's kind 14 raises a
    /// kind 3 at the tile and takes that; the byte it leaves behind is a 3.</para>
    /// </summary>
    private static ImpTaskKind KindOf(JobPriority priority) => priority switch
    {
        JobPriority.MarkedExcavation => ImpTaskKind.Excavate,
        JobPriority.Reinforce => ImpTaskKind.Reinforce,
        _ => ImpTaskKind.Claim,
    };

    /// <summary>
    /// Tier B: the nearest tile at which there is <i>either</i> a wall to
    /// reinforce <i>or</i> gold to pick up, and at that tile the wall wins.
    /// <para>The shape of the original's last resort: once every whole-map kind is
    /// empty it scans outwards in rings and at each tile tries reinforce-or-repair,
    /// then floor gold, then a loose object, then one kind not yet identified. Two
    /// of the four exist here. Ours: rings measured in steps (HYPOTHESIS, Q25).</para>
    /// <para>One search and not two: the nearest wall then the nearest pile would
    /// put a wall across the dungeon above a pile at the imp's feet, the strict
    /// order the binary does not have. Distance is the primary key, kind the
    /// tie-break.</para>
    /// </summary>
    private bool TrySweep(
        SimulationContext context, CreatureState creature, CreatureDef definition, WorldPathGrid grid)
    {
        byte player = creature.OwnerPlayerId;
        bool handsFree = creature.GoldHeld < definition.MaxGoldHeld;

        bool found = _pathfinder.TryFindNearest(
            grid,
            creature.Tile,
            stand => TryJobFrom(context, grid, stand, player, JobPriority.Reinforce, creature.Id, out _, out _)
                     || (handsFree && CanFetchFrom(context, stand, player, creature.Id)),
            _route);

        if (!found) return false;

        TilePos standing = _route.Count > 0 ? _route[_route.Count - 1] : creature.Tile;

        // Reinforce first at the tile the search stopped on — the per-tile
        // order — and only then the gold underfoot.
        if (TryJobFrom(context, grid, standing, player, JobPriority.Reinforce, creature.Id,
                       out int tileIndex, out TileWorkKind kind))
        {
            CommitJob(context, creature, tileIndex, kind, JobPriority.Reinforce);
            return true;
        }

        if (handsFree && CanFetchFrom(context, standing, player, creature.Id))
        {
            CommitFetch(context, creature, standing);
            return true;
        }

        return false;
    }

    private bool TryTakeJobAt(
        SimulationContext context, CreatureState creature, WorldPathGrid grid, JobPriority priority)
    {
        byte player = creature.OwnerPlayerId;

        bool found = _pathfinder.TryFindNearest(
            grid,
            creature.Tile,
            stand => TryJobFrom(context, grid, stand, player, priority, creature.Id, out int found, out _)
                     && InScanRange(context.World, found),
            _route);

        if (!found) return false;

        TilePos standing = _route.Count > 0 ? _route[_route.Count - 1] : creature.Tile;
        if (!TryJobFrom(context, grid, standing, player, priority, creature.Id, out int tileIndex, out TileWorkKind kind))
            return false;

        CommitJob(context, creature, tileIndex, kind, priority);
        return true;
    }

    /// <summary>
    /// Commits a worker to tile work along the route the last search planned.
    /// </summary>
    private void CommitJob(
        SimulationContext context, CreatureState creature, int tileIndex, TileWorkKind kind, JobPriority priority)
    {
        creature.TargetTileIndex = tileIndex;
        creature.TargetWork = kind;
        context.Navigation.TryRoute(creature, _route.Count > 0 ? _route[^1] : creature.Tile, Navigation.MoveOptions.Plain);

        if (_route.Count == 0)
        {
            creature.Activity = CreatureActivity.Working;
            creature.TimerTicks = SimulationTiming.WorkApplicationPeriodTicks;
        }
        else
        {
            creature.Activity = CreatureActivity.MovingToJob;
        }

        SetOutInState(context, creature, kind, routed: _route.Count > 0);

        creature.LastTaskKind = KindOf(priority);

        context.Emit(SystemName, "job_taken",
            $"imp={creature.Id} player={creature.OwnerPlayerId} " +
            $"{Describe(context.World, tileIndex)} {kind} priority={priority} steps={_route.Count}");
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
            // The just-dug tile: claimable, freshly dug, and with none of its
            // EIGHT neighbours still carrying this keeper's dig mark. Eight and
            // not four, and a dig mark and not a mining one, because the
            // original's handler looks up its kind-1 task (a marked tile with no
            // GoldValue) on each of the eight tiles around it, and only kind 1
            // (Q154). After that it is an ordinary claim, done standing on it.
            case JobPriority.ClaimFreshlyDug:
                if (!tile.FreshlyDug) return false;
                if (!TileWorkRules.Applies(context.Database, tile, TileWorkKind.Claim, playerId)) return false;
                if (ADigIsStillMarkedAround(context, world, target, playerId)) return false;
                kind = TileWorkKind.Claim;
                break;

            case JobPriority.MarkedExcavation:
                if (!tile.IsTaggedBy(playerId)) return false;
                if (!TileWorkRules.TryTaggedWork(context.Database, tile, playerId, out kind)) return false;
                break;

            // Claiming floor and taking somebody else's room are one priority
            // and two kinds of work: the balance table gives them separate rates
            // (ClaimTileHealth +825, ConvertRoomHealth −4000) and the terrain
            // flag partitions them exactly, a room tile being one and only one
            // of the two. The owner describes both as claiming (Q24, Q92), so
            // no order between them has to be chosen here.
            case JobPriority.Claim:
                if (TileWorkRules.Applies(context.Database, tile, TileWorkKind.Claim, playerId))
                    kind = TileWorkKind.Claim;
                else if (TileWorkRules.Applies(context.World, context.Database, tile, TileWorkKind.ConvertRoom, playerId))
                    kind = TileWorkKind.ConvertRoom;
                else
                    return false;
                break;

            // A wall is only worth reinforcing where it faces the keeper's own
            // ground (Q295, owner). Today a guard rather than a change -- see
            // TouchesOwnFloor for why it cannot yet fire.
            case JobPriority.Reinforce:
                if (!TileWorkRules.Applies(context.Database, tile, TileWorkKind.Reinforce, playerId))
                    return false;
                if (!TouchesOwnFloor(context, world, target, playerId)) return false;
                kind = TileWorkKind.Reinforce;
                break;

            default:
                return false;
        }

        // An imp claims a floor tile by standing on it (Q44, CONFIRMED) and digs
        // a wall from beside it (Q27, CONFIRMED): a gold block takes three imps
        // a side, twelve in all. One stance rule decides which (WorkStance), and
        // a worker in the wrong one has no job here.
        bool onTheTile = WorkStance.IsOnTheTile(context.Database, world, grid, target, kind);
        if (onTheTile != (standing == target))
        {
            kind = default;
            return false;
        }

        // Claiming and reinforcing spread from ground the keeper already holds
        // (CONFIRMED, Q42); marked work does not, because the keeper asked for it
        // by name. Which tile carries the condition follows from the stance: a
        // wall job is taken from the keeper's own land, so the STANDING tile must
        // be owned; a floor job is done standing on the tile being claimed, not
        // owned yet, so it is the TARGET that must touch owned land instead.
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
        // stands on to claim it (CONFIRMED both ways, Q43/Q44); the surplus finds
        // other work rather than queueing. Rejecting the FACE rather than the
        // tile is what makes that happen: the search carries on to the next
        // standing tile, which may be another side of this block, another block,
        // or another priority entirely. The cap is per job, not per tile.
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
    /// does standing on the target itself (CONFIRMED, Q42).
    /// <para>Orthogonal rather than the eight-connected neighbourhood the mover
    /// uses, matching the way work spreads (Q27). That narrowing is STRONGLY
    /// INFERRED: a keeper's territory creeping diagonally across a corner is not
    /// what the original shows.</para>
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
    /// Whether any of the eight tiles around <paramref name="target"/> is
    /// marked by this keeper for work that is <i>digging</i> — the test that
    /// holds a just-dug tile's claim back while the corridor is still being
    /// cut (Q154). Mining a seam next door does not hold it back: the original
    /// looks for its dig kind only, and its dig kind is every marked tile whose
    /// terrain has no <c>GoldValue</c> — rock and walls alike — so here it is
    /// every tagged kind but <see cref="TileWorkKind.MineGold"/>.
    /// </summary>
    private static bool ADigIsStillMarkedAround(
        SimulationContext context, WorldState world, TilePos target, byte playerId)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                var at = new TilePos(target.X + dx, target.Y + dy);
                if (!world.Contains(at)) continue;

                ref TileState neighbour = ref world.Tile(at);
                if (!neighbour.IsTaggedBy(playerId)) continue;
                if (!TileWorkRules.TryTaggedWork(context.Database, neighbour, playerId, out TileWorkKind kind)) continue;
                if (kind != TileWorkKind.MineGold) return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether one of the wall's four sides faces floor this keeper owns, which
    /// is what makes it reinforceable. CONFIRMED (owner, 2026-09-10): "as soon
    /// as the floor tile is claimed walls are eligible to be reinforced".
    /// <para><b>Four, not eight</b> — ours: a wall reinforces the face it shows
    /// a corridor, and targets already come from four neighbours.</para>
    /// <para><b>It cannot fire today</b>, and is kept as a guard; ADR-0132 has
    /// the argument and what would change it.</para>
    /// </summary>
    private static bool TouchesOwnFloor(
        SimulationContext context, WorldState world, TilePos target, byte playerId)
    {
        ReadOnlySpan<int> dx = [1, -1, 0, 0];
        ReadOnlySpan<int> dy = [0, 0, 1, -1];

        for (int i = 0; i < 4; i++)
        {
            var at = new TilePos(target.X + dx[i], target.Y + dy[i]);
            if (!world.Contains(at)) continue;

            ref TileState neighbour = ref world.Tile(at);
            if (neighbour.OwnerPlayerId != playerId) continue;
            if (context.Database.TerrainById(neighbour.TerrainId).IsSolid) continue;
            return true;
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

        if (TileWorkRules.TryResolveDigTask(
                context.Database, tile, creature.TargetWork, creature.OwnerPlayerId,
                out TileWorkKind currentKind))
        {
            creature.TargetWork = currentKind;
            return true;
        }

        return TileWorkRules.Applies(
            context.World, context.Database, tile, creature.TargetWork, creature.OwnerPlayerId);
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



    // ------------------------------------------------------- carrying objects

    /// <summary>
    /// DK2's imp task <b>kind 8</b>: the nearest loose object that belongs in a
    /// room, and a walk to it.
    /// <para><b>Second in Tier A</b>, above marked excavation and above claiming
    /// (<c>imp-task-priority.md</c> §4, ADR-0065) - so an imp that finds a book
    /// on the floor shelves it before it digs anything the keeper marked.</para>
    /// <para>Like every other carry here the destination is settled before the
    /// walk: an object whose room the keeper has not built, or has no space left
    /// in, is an object nobody sets off for.</para>
    /// </summary>
    private bool TryTakeObject(
        SimulationContext context, CreatureState creature, CreatureDef definition,
        WorldPathGrid grid)
    {
        WorldState world = context.World;

        if (creature.CarriedObjectId >= 0 || creature.CarriedCreatureId >= 0) return false;
        if (creature.GoldHeld > 0) return false;

        bool found = _pathfinder.TryFindNearest(
            grid, creature.Tile,
            at => InScanRange(at) && LooseObjectAt(context, creature, definition, at) is not null,
            _route);

        if (!found) return false;

        TilePos at = _route.Count > 0 ? _route[_route.Count - 1] : creature.Tile;
        ObjectState? thing = LooseObjectAt(context, creature, definition, at);
        if (thing is null) return false;

        creature.Activity = CreatureActivity.FetchingObject;
        creature.TargetTileIndex = (at.Y * world.Width) + at.X;
        creature.TargetWork = default;
        creature.TargetObjectId = thing.Id;
        context.Navigation.TryRoute(creature, at, Navigation.MoveOptions.Plain);
        creature.TimerTicks = 0;
        creature.LastTaskKind = ImpTaskKind.CarryObject;

        context.Emit(SystemName, "object_claimed",
            $"imp={creature.Id} player={creature.OwnerPlayerId} object={thing.Id} " +
            $"type={ObjectName(context, thing)} " +
            $"at=({at.X},{at.Y}) steps={_route.Count}");
        return true;
    }

    /// <summary>
    /// The loose object on <paramref name="at"/> this worker could put away, or
    /// null. Ties between two on one tile go to the lower object id.
    /// </summary>
    private static ObjectState? LooseObjectAt(
        SimulationContext context, CreatureState worker, CreatureDef definition, TilePos at)
    {
        WorldState world = context.World;
        int index = (at.Y * world.Width) + at.X;
        ObjectState? best = null;

        foreach (ObjectState thing in world.Objects)
        {
            if (thing.TileIndex != index) continue;
            if (!ObjectCarryRules.CanCarry(context.Database, definition, worker, thing)) continue;

            // The tile it is standing on has to be the imp's own. DK2's test is
            // "own floor or water"; there is no reason here for a worker to
            // wade for a book, so this is the narrower half of it (Q170).
            if (world.TileAtIndex(index).OwnerPlayerId != worker.OwnerPlayerId) continue;

            if (ObjectCarryRules.TryDestinationFor(world, context.Database, worker, thing) < 0)
                continue;

            if (best is null || thing.Id < best.Id) best = thing;
        }

        return best;
    }

    /// <summary>Walking to a loose object, and picking it up on arrival.</summary>
    private void UpdateFetchingObject(
        SimulationContext context, CreatureState creature, WorldPathGrid grid)
    {
        WorldState world = context.World;
        ObjectState? thing = world.ObjectById(creature.TargetObjectId);

        if (thing is null || !ObjectCarryRules.IsLoose(context.Database, thing))
        {
            context.Emit(SystemName, "object_abandoned",
                $"imp={creature.Id} object={creature.TargetObjectId} reason=gone");
            creature.TargetObjectId = -1;
            GoIdle(creature);
            return;
        }

        int here = (creature.Tile.Y * world.Width) + creature.Tile.X;
        if (here != thing.TileIndex)
        {
            if (creature.HasRoute) return;

            context.Emit(SystemName, "object_abandoned",
                $"imp={creature.Id} object={thing.Id} reason=out_of_reach");
            creature.TargetObjectId = -1;
            GoIdle(creature);
            return;
        }

        int destination = ObjectCarryRules.TryDestinationFor(
            world, context.Database, creature, thing);

        if (destination < 0 || !RouteTo(context, creature, grid, destination))
        {
            context.Emit(SystemName, "object_abandoned",
                $"imp={creature.Id} object={thing.Id} reason=nowhere_to_take_it");
            creature.TargetObjectId = -1;
            GoIdle(creature);
            return;
        }

        creature.TargetObjectId = -1;
        ObjectCarryRules.PickUp(creature, thing);
        creature.Activity = CreatureActivity.CarryingObject;
        creature.TargetTileIndex = destination;

        context.Emit(SystemName, "object_picked_up",
            $"imp={creature.Id} player={creature.OwnerPlayerId} object={thing.Id} " +
            $"type={ObjectName(context, thing)} " +
            $"to={Describe(world, destination)} steps={creature.Body.PathTotalCount}");
    }

    /// <summary>Carrying a loose object, and shelving it on arrival.</summary>
    private void UpdateCarryingObject(
        SimulationContext context, CreatureState creature, WorldPathGrid grid)
    {
        WorldState world = context.World;
        ObjectState? thing = world.ObjectById(creature.CarriedObjectId);

        if (thing is null)
        {
            creature.CarriedObjectId = -1;
            GoIdle(creature);
            return;
        }

        ObjectCarryRules.FollowCarrier(world, creature, thing);

        int here = (creature.Tile.Y * world.Width) + creature.Tile.X;
        if (here != creature.TargetTileIndex)
        {
            if (creature.HasRoute) return;

            int destination = ObjectCarryRules.TryDestinationFor(
                world, context.Database, creature, thing);

            if (destination >= 0 && RouteTo(context, creature, grid, destination))
            {
                creature.TargetTileIndex = destination;
                return;
            }

            context.Emit(SystemName, "object_dropped",
                $"imp={creature.Id} object={thing.Id} " +
                $"at=({creature.Tile.X},{creature.Tile.Y}) reason=no_route");
            ObjectCarryRules.PutDown(creature, thing);
            GoIdle(creature);
            return;
        }

        // Somebody else shelved something here while this imp was walking.
        if (ObjectCarryRules.ObjectOn(world, here) is { } sitting && sitting.Id != thing.Id)
        {
            context.Emit(SystemName, "object_dropped",
                $"imp={creature.Id} object={thing.Id} " +
                $"at=({creature.Tile.X},{creature.Tile.Y}) reason=tile_taken");
            ObjectCarryRules.PutDown(creature, thing);
            GoIdle(creature);
            return;
        }

        ObjectCarryRules.Deliver(creature, thing, creature.OwnerPlayerId, here);

        context.Emit(SystemName, "object_stored",
            $"imp={creature.Id} player={creature.OwnerPlayerId} object={thing.Id} " +
            $"type={ObjectName(context, thing)} " +
            $"{Describe(world, here)}");

        GoIdle(creature);
    }

    // ---------------------------------------------------------------- carrying

    /// <summary>
    /// Looks for a body to carry: a collapsed creature when
    /// <paramref name="corpses"/> is false, a corpse when it is true.
    /// <para>Two calls rather than one search for both, because DK2 has two task
    /// kinds and searches them in order — an unconscious creature anywhere on
    /// the map outranks a corpse at the imp's feet. Within a kind the nearest by
    /// walking wins (HYPOTHESIS, Q25).</para>
    /// <para><b>A body with nowhere to go is not taken</b>: a collapsed creature
    /// whose bed was sold, and an enemy whose keeper has no prison at all. A
    /// <i>full</i> prison is not that case - the imp carries the enemy there and
    /// is turned away at the door (ADR-0148).</para>
    /// </summary>
    private bool TryTakeBody(
        SimulationContext context, CreatureState creature, CreatureDef definition,
        WorldPathGrid grid, bool corpses)
    {
        WorldState world = context.World;

        if (creature.CarriedCreatureId >= 0 || creature.GoldHeld > 0) return false;

        // The graveyard is found ONCE, not once per tile the search touches:
        // CarryRules.TryDestinationFor rebuilds the room index, and calling it
        // from inside a whole-map predicate makes the search quadratic in the
        // map.
        int graveyard = corpses
            ? CarryRules.NearestGraveyardTile(world, context.Database, creature)
            : -1;

        if (corpses && graveyard < 0) return false;

        bool found = _pathfinder.TryFindNearest(
            grid, creature.Tile,
            at => InScanRange(at)
                  && BodyToCarryAt(context, creature, definition, at, corpses, graveyard) is not null,
            _route);

        if (!found) return false;

        TilePos at = _route.Count > 0 ? _route[_route.Count - 1] : creature.Tile;
        CreatureState? body = BodyToCarryAt(context, creature, definition, at, corpses, graveyard);
        if (body is null) return false;

        creature.Activity = CreatureActivity.FetchingBody;
        creature.CarriedCreatureId = -1;
        creature.TargetTileIndex = (at.Y * world.Width) + at.X;
        creature.TargetWork = default;
        context.Navigation.TryRoute(creature, at, Navigation.MoveOptions.Plain);
        creature.TimerTicks = 0;
        creature.LastTaskKind = corpses ? ImpTaskKind.CarryCorpse : ImpTaskKind.CarryUnconscious;

        // Remembered so that the walk and the pick-up agree about which body
        // this is, even if a second one falls on the same tile before the imp
        // gets there.
        creature.TargetBodyId = body.Id;

        context.Emit(SystemName, "body_claimed",
            $"imp={creature.Id} player={creature.OwnerPlayerId} " +
            $"body={body.Id} kind={(corpses ? "corpse" : "unconscious")} " +
            $"at=({at.X},{at.Y}) steps={_route.Count}");
        return true;
    }

    /// <summary>
    /// The body on <paramref name="at"/> this worker could carry somewhere, or
    /// null. Ties between two bodies on one tile go to the lower creature id,
    /// which is spawn order and is stable.
    /// </summary>
    private static CreatureState? BodyToCarryAt(
        SimulationContext context, CreatureState worker, CreatureDef definition,
        TilePos at, bool corpses, int graveyard)
    {
        WorldState world = context.World;
        CreatureState? best = null;

        foreach (CreatureState body in world.Creatures)
        {
            if (body.Tile != at) continue;
            if (CreatureDeathRules.IsCorpse(body) != corpses) continue;
            if (!CarryRules.CanCarry(definition, worker, body)) continue;

            // A corpse goes to the graveyard already found; a collapsed
            // creature goes to its own bed, which is a per-body question and a
            // cheap one.
            int destination = corpses
                ? graveyard
                : CarryRules.TryDestinationFor(world, context.Database, worker, body);

            if (destination < 0) continue;
            if (best is null || body.Id < best.Id) best = body;
        }

        return best;
    }

    /// <summary>Walking to a body, and picking it up on arrival.</summary>
    private void UpdateFetchingBody(
        SimulationContext context, CreatureState creature, WorldPathGrid grid)
    {
        WorldState world = context.World;
        CreatureState? body = world.CreatureById(creature.TargetBodyId);

        // Somebody else took it, it rotted, or it was rescued by another imp.
        if (body is null || !CarryRules.IsCarryable(body))
        {
            context.Emit(SystemName, "body_abandoned",
                $"imp={creature.Id} body={creature.TargetBodyId} reason=gone");
            ForgetBody(creature);
            GoIdle(creature);
            return;
        }

        // Still on the road.
        if (creature.Tile != body.Tile)
        {
            if (creature.HasRoute) return;

            context.Emit(SystemName, "body_abandoned",
                $"imp={creature.Id} body={body.Id} reason=out_of_reach");
            ForgetBody(creature);
            GoIdle(creature);
            return;
        }

        int destination = CarryRules.TryDestinationFor(world, context.Database, creature, body);
        if (destination < 0 || !RouteTo(context, creature, grid, destination))
        {
            context.Emit(SystemName, "body_abandoned",
                $"imp={creature.Id} body={body.Id} reason=nowhere_to_take_it");
            ForgetBody(creature);
            GoIdle(creature);
            return;
        }

        ForgetBody(creature);
        CarryRules.PickUp(creature, body);
        creature.Activity = CreatureActivity.CarryingBody;
        creature.TargetTileIndex = destination;

        context.Emit(SystemName, "body_picked_up",
            $"imp={creature.Id} player={creature.OwnerPlayerId} body={body.Id} " +
            $"kind={(CreatureDeathRules.IsCorpse(body) ? "corpse" : "unconscious")} " +
            $"to={Describe(world, destination)} steps={creature.Body.PathTotalCount}");
    }

    /// <summary>Carrying a body, and delivering it on arrival.</summary>
    private void UpdateCarryingBody(
        SimulationContext context, CreatureState creature, WorldPathGrid grid)
    {
        WorldState world = context.World;
        CreatureState? body = world.CreatureById(creature.CarriedCreatureId);

        // It rotted in the imp's arms, which a long walk to a distant graveyard
        // really can do (Q164).
        if (body is null)
        {
            creature.CarriedCreatureId = -1;
            GoIdle(creature);
            return;
        }

        // The body goes where the arms go, and this runs after the mover, so it
        // is never a tick behind.
        CarryRules.FollowCarrier(creature, body);

        int here = (creature.Tile.Y * world.Width) + creature.Tile.X;
        if (here != creature.TargetTileIndex)
        {
            if (creature.HasRoute) return;

            // The destination moved or the route was lost. Try again from here;
            // failing that, put the body down where it stands.
            int destination = CarryRules.TryDestinationFor(world, context.Database, creature, body);
            if (destination >= 0 && RouteTo(context, creature, grid, destination))
            {
                creature.TargetTileIndex = destination;
                return;
            }

            context.Emit(SystemName, "body_dropped",
                $"imp={creature.Id} body={body.Id} " +
                $"at=({creature.Tile.X},{creature.Tile.Y}) reason=no_route");
            CarryRules.PutDown(creature, body);
            GoIdle(creature);
            return;
        }

        Deliver(context, creature, body);
    }

    /// <summary>
    /// Arrival: a corpse into the graveyard, the keeper's own collapsed
    /// creature into its bed, an enemy's into a cell.
    /// </summary>
    private static void Deliver(
        SimulationContext context, CreatureState creature, CreatureState body)
    {
        WorldState world = context.World;
        CreatureDef definition = context.Database.CreatureById(body.CreatureTypeId);

        if (CreatureDeathRules.IsCorpse(body))
        {
            RoomInstance? graveyard = RoomIndex.RoomAt(
                world, context.Database, creature.TargetTileIndex);
            if (graveyard is null
                || graveyard.Definition.TypeId != RoomTypeId.Graveyard
                || graveyard.OwnerPlayerId != creature.OwnerPlayerId)
            {
                context.Emit(SystemName, "body_dropped",
                    $"imp={creature.Id} body={body.Id} " +
                    $"at=({creature.Tile.X},{creature.Tile.Y}) reason=no_graveyard");
                CarryRules.PutDown(creature, body);
                GoIdle(creature);
                return;
            }

            GraveyardBurialResult result = GraveyardRules.Bury(
                world, context.Database, graveyard,
                definition.DecomposeValue, creature.TargetTileIndex);
            string raised = result.SpawnedCreatureId < 0
                ? "raised=none"
                : $"raised={context.Database.CreatureById(result.SpawnedCreatureTypeId).Name} " +
                  $"as={result.SpawnedCreatureId}";

            context.Emit(SystemName, "corpse_buried",
                $"imp={creature.Id} player={creature.OwnerPlayerId} body={body.Id} " +
                $"type={definition.Name} {Describe(world, creature.TargetTileIndex)} " +
                $"decompose_value={definition.DecomposeValue} " +
                $"tally={result.DecomposeValue} {raised}");

            CarryRules.PutDown(creature, body);
            world.RemoveCreature(body.Id);
            GoIdle(creature);
            return;
        }

        CarryRules.PutDown(creature, body);

        // Somebody else's creature goes into a cell rather than a bed - kind
        // 13's other branch, and the only route into a prison there is
        // (ADR-0064). Admission is decided HERE, at the door: a prison that was
        // full when the imp set off, or filled up during the walk, turns the
        // body away and it is left on the rim (ADR-0148).
        if (body.OwnerPlayerId != creature.OwnerPlayerId)
        {
            int cell = creature.TargetTileIndex;
            if (PrisonRules.AdmitsAt(world, context.Database, creature.OwnerPlayerId, cell)
                && PrisonRules.Imprison(
                    world, context.Database, body, creature.OwnerPlayerId, cell))
            {
                context.Emit(SystemName, "creature_imprisoned",
                    $"imp={creature.Id} player={creature.OwnerPlayerId} body={body.Id} " +
                    $"type={definition.Name} owner={body.OwnerPlayerId} " +
                    $"{Describe(world, cell)} health={body.Health}");
            }
            else
            {
                context.Emit(SystemName, "body_dropped",
                    $"imp={creature.Id} body={body.Id} " +
                    $"at=({creature.Tile.X},{creature.Tile.Y}) reason=not_admitted");
            }

            GoIdle(creature);
            return;
        }

        if (CreatureDeathRules.Rescue(world, context.Database, body))
        {
            context.Emit(SystemName, "creature_rescued",
                $"imp={creature.Id} player={creature.OwnerPlayerId} body={body.Id} " +
                $"type={definition.Name} {Describe(world, creature.TargetTileIndex)} " +
                $"health={body.Health} (the lair heals it from here, ADR-0049)");
        }
        else
        {
            context.Emit(SystemName, "body_dropped",
                $"imp={creature.Id} body={body.Id} " +
                $"at=({creature.Tile.X},{creature.Tile.Y}) reason=bed_gone");
        }

        GoIdle(creature);
    }

    /// <summary>
    /// An object's name for an event line, or its raw type id when the record
    /// is missing - which is a level referring to an object the OBJECTS table
    /// does not have, and is worth seeing rather than crashing on.
    /// </summary>
    private static string ObjectName(SimulationContext context, ObjectState thing) =>
        context.Database.ObjectById(thing.ObjectTypeId)?.Name ?? $"#{thing.ObjectTypeId}";

    /// <summary>Routes a worker to one tile by index. Returns false if there is no way.</summary>
    private bool RouteTo(
        SimulationContext context, CreatureState creature, WorldPathGrid grid, int tileIndex)
    {
        WorldState world = context.World;
        var to = new TilePos(tileIndex % world.Width, tileIndex / world.Width);

        return context.Navigation.TryRoute(creature, to, Navigation.MoveOptions.Plain);
    }

    /// <summary>Clears the "body I am walking towards" note.</summary>
    private static void ForgetBody(CreatureState creature) =>
        creature.TargetBodyId = -1;

    /// <summary>
    /// Ends the current job and looks again on the next tick.
    /// <para>Deliberately not the idle delay: in the original an imp whose wall
    /// has just fallen moves straight to the next marked tile, so the delay
    /// belongs to the imp that looked and found nothing, which is what "idle"
    /// names. STRONGLY INFERRED from the variable's name,
    /// <c>ImpIdleDelayBeforeReevaluation</c>. Also ours: a freshly placed worker
    /// looks immediately.</para>
    /// </summary>
    // ---- on DK2's state ids (ADR-0181) ---------------------------------------

    /// <summary>State 10 takes the task and walks through the imp wrapper in 6, resuming in
    /// the kind's entry state. An imp already in its stance still takes the walk, and the
    /// entry state moves to the work state on the same tick. The teleport branch (51, 52) is
    /// not built: every imp walks (Q418).</summary>
    private static void SetOutInState(
        SimulationContext context, CreatureState creature, TileWorkKind kind, bool routed)
    {
        CreatureStateMachine.Release(creature);
        StateShadow.FromIdle(creature);
        StateShadow.Move(context, SystemName, creature, ImpStates.TaskSelection);
        CreatureStateMachine.BeginTravel(
            creature, StateShadow.TravelToPoint, ImpStates.For(kind).Entry);
        if (!routed) AtWork(context, creature);
    }

    /// <summary>State 10 is where a worker's every task starts; ours arrives at it from an
    /// idle that holds no state id.</summary>
    private static void FromTaskSelection(CreatureState creature)
    {
        CreatureStateMachine.Release(creature);
        StateShadow.FromIdle(creature);
        CreatureStateMachine.TrySetState(creature, ImpStates.TaskSelection);
    }

    /// <summary>The gold-store chooser <c>0x00464400</c>: a walk in 6 resuming in 28, from
    /// whichever of 10, 26 and 27 called it.</summary>
    private static void ToTheStore(CreatureState creature) =>
        CreatureStateMachine.BeginTravel(creature, StateShadow.TravelToPoint, ImpStates.AtTheStore);

    /// <summary>The walk ends in the entry state, which finds the stance free and moves to
    /// the work state. The entry state's wait for an occupied stance is not modelled.</summary>
    private static void AtWork(SimulationContext context, CreatureState creature)
    {
        StateShadow.Arrive(context, SystemName, creature);
        StateShadow.Move(context, SystemName, creature, ImpStates.For(creature.TargetWork).Work);
    }

    /// <summary>The task or the stance has gone, or the tile is done: the entry or work state
    /// sets 10. Our idle is not on state ids, so the id is then handed back.</summary>
    private static void WorkEnded(SimulationContext context, CreatureState creature)
    {
        if (!CreatureStateMachine.HasState(creature)) return;
        StateShadow.Arrive(context, SystemName, creature);
        StateShadow.Move(context, SystemName, creature, ImpStates.TaskSelection);
        CreatureStateMachine.Release(creature);
    }

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
