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
/// <item><b>Claiming and reinforcing are done from ground the keeper already
/// holds.</b> An imp will only take one of those jobs if the tile it would stand
/// on belongs to its keeper, which is what makes territory spread outwards from
/// a dungeon rather than appear in islands across the map. STRONGLY INFERRED
/// from how the original visibly grows; the alternative — any reachable floor,
/// anywhere — is Q42. Marked work carries no such condition: a keeper may tag a
/// wall anywhere.</item>
/// <item><b>No reservation.</b> Several imps happily converge on one wall,
/// because nothing stops them. The original caps this at three imps a face and
/// twelve a tile (CONFIRMED), which is not implemented yet — see Q43.</item>
/// <item><b>Worked from an orthogonal neighbour.</b> An imp stands on a tile
/// sharing an edge with its target. CONFIRMED by the same observation: a gold
/// block takes three imps a side and twelve in all, and twelve is four sides of
/// three (Q27).</item>
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

            Update(context, creature, definition);
        }
    }

    private void Update(SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        if (creature.Activity == CreatureActivity.DeliveringGold)
        {
            UpdateDelivering(context, creature, definition);
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
            && TryRouteToSomewhereItFits(context, creature, definition))
        {
            BeginDelivering(creature);
            return;
        }

        switch (creature.Activity)
        {
            case CreatureActivity.Idle:
                UpdateIdle(context, creature, definition);
                break;

            case CreatureActivity.MovingToJob:
                UpdateMoving(context, creature);
                break;

            case CreatureActivity.Working:
                UpdateWorking(context, creature, definition);
                break;

            default:
                GoIdle(creature);
                break;
        }
    }

    private void UpdateIdle(SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        creature.TimerTicks--;
        if (creature.TimerTicks > 0) return;

        if (!TryTakeJob(context, creature, definition))
            creature.TimerTicks = IdleDelayTicks(context.Database);
    }

    private void UpdateMoving(SimulationContext context, CreatureState creature)
    {
        if (IsInWorkingPosition(context.World, creature))
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
        SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        if (!IsInWorkingPosition(context.World, creature))
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
            && TryRouteToSomewhereItFits(context, creature, definition))
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
        SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        if (TryPutDown(context, creature) && creature.GoldHeld == 0)
        {
            GoIdle(creature);
            return;
        }

        // Still on the road.
        if (creature.PathIndex < creature.Path.Count) return;

        if (TryRouteToSomewhereItFits(context, creature, definition)) return;

        context.Emit(SystemName, "delivery_blocked",
            $"imp={creature.Id} player={creature.OwnerPlayerId} gold={creature.GoldHeld} " +
            "reason=no_room (floor piles are objects, not implemented)");
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

        ref TileState tile = ref world.Tile(here);
        if (tile.OwnerPlayerId != creature.OwnerPlayerId) return false;

        int put = GoldStore.DepositInto(context.Database, ref tile, creature.GoldHeld);
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
        SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        WorldState world = context.World;
        var grid = new WorldPathGrid(context.Database, world, definition);
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

        ref TileState tile = ref world.Tile(at);
        return tile.OwnerPlayerId == playerId && GoldStore.SpaceIn(context.Database, tile) > 0;
    }

    /// <summary>
    /// Looks for the nearest tile this keeper has marked that this creature can
    /// get beside, and takes it.
    ///
    /// One search answers both halves of the question. Walking outwards from the
    /// creature and stopping at the first tile that has a job beside it gives
    /// the nearest job measured in steps, and gives the route to it, and rules
    /// out everything unreachable, all without ever building a list of
    /// candidates to sort.
    /// </summary>
    private bool TryTakeJob(SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        WorldState world = context.World;
        var grid = new WorldPathGrid(context.Database, world, definition);

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
            stand => TryJobBeside(context, stand, player, priority, creature.Id, out _, out _),
            _route);

        if (!found) return false;

        TilePos standing = _route.Count > 0 ? _route[_route.Count - 1] : creature.Tile;
        if (!TryJobBeside(context, standing, player, priority, creature.Id, out int tileIndex, out TileWorkKind kind))
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
    /// The first tile orthogonally adjacent to <paramref name="standing"/> that
    /// offers work of the given priority, in the fixed neighbour order.
    /// </summary>
    private static bool TryJobBeside(
        SimulationContext context,
        TilePos standing,
        byte playerId,
        JobPriority priority,
        int askingCreatureId,
        out int tileIndex,
        out TileWorkKind kind)
    {
        WorldState world = context.World;

        // Claiming and reinforcing spread from ground the keeper already holds;
        // marked work does not, because the keeper asked for it by name.
        if (priority != JobPriority.MarkedExcavation
            && !StandsOnOwnLand(context, world, standing, playerId))
        {
            tileIndex = -1;
            kind = default;
            return false;
        }

        for (int i = 0; i < NeighbourDx.Length; i++)
        {
            int x = standing.X + NeighbourDx[i];
            int y = standing.Y + NeighbourDy[i];
            if (!world.Contains(x, y)) continue;

            ref TileState tile = ref world.Tile(x, y);

            switch (priority)
            {
                case JobPriority.MarkedExcavation:
                    if (!tile.IsTaggedBy(playerId)) continue;
                    if (!TileWorkRules.TryTaggedWork(context.Database, tile, playerId, out kind)) continue;
                    break;

                case JobPriority.Claim:
                    if (!TileWorkRules.Applies(context.Database, tile, TileWorkKind.Claim, playerId))
                        continue;
                    kind = TileWorkKind.Claim;
                    break;

                case JobPriority.Reinforce:
                    if (!TileWorkRules.Applies(context.Database, tile, TileWorkKind.Reinforce, playerId))
                        continue;
                    kind = TileWorkKind.Reinforce;
                    break;

                default:
                    continue;
            }

            // Three workers to a face, and a fourth goes and finds other work
            // rather than queueing (Q43). Rejecting the FACE rather than the
            // tile is what makes that happen: the search simply carries on to
            // the next standing tile, which may be another side of this same
            // block or another block entirely.
            tileIndex = (y * world.Width) + x;
            if (ImpCrowding.FaceIsFull(
                    world, context.Database, tileIndex, standing, askingCreatureId))
            {
                continue;
            }

            return true;
        }

        tileIndex = -1;
        kind = default;
        return false;
    }

    /// <summary>
    /// Whether this tile belongs to the keeper — the condition an unprompted
    /// claim or reinforce is taken from.
    ///
    /// It is the standing tile that is tested, not the target: an imp reaches
    /// out from its own territory into the next tile, which is what makes a
    /// dungeon grow outwards from its heart instead of in islands. STRONGLY
    /// INFERRED; Q42.
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

    private static bool IsInWorkingPosition(WorldState world, CreatureState creature)
    {
        int targetX = creature.TargetTileIndex % world.Width;
        int targetY = creature.TargetTileIndex / world.Width;
        return creature.Tile.ManhattanDistance(new TilePos(targetX, targetY)) == 1;
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
