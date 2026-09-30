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
/// ADR-0018). Idle wandering is ImpTaskSystem.Wander.cs (ADR-0224). Not implemented: dragging the unconscious
/// to a prison, fleeing a fight, gold somebody else dropped (imp-tasks.md).</para>
/// </summary>
[Dk2Behavior(Confidence.Hypothesis, "docs/research/imp-tasks.md")]
public sealed partial class ImpTaskSystem : ISimulationSystem
{
    private const string SystemName = "ImpTask";

    /// <summary>
    /// How far a worker the keeper has just put down looks around itself before
    /// it looks anywhere else: <b>three rings</b> of Manhattan distance, its own
    /// tile first (Q488).
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
    /// While set, the searches with no per-tile form (blueprints, bodies, objects) are confined to
    /// this one tile. Non-null only inside <see cref="TryTakeDroppedJob"/>, whose ring scan hands
    /// each tile to the chain in turn.
    /// </summary>
    private TilePos? _only;

    /// <summary>State 10's cap: how many workers a keeper may let choose a job in one tick
    /// (<c>0x00463E41</c>, the counter at <c>player+0x7A</c>, zeroed every player frame).</summary>
    public const int SelectionsPerTick = 4;

    /// <summary>The throttled arm's rest: 12's deadline is now + 20 + rand(80), 5–25 s.</summary>
    public const int ThrottleBaseTicks = 20;
    public const int ThrottleRandomTicks = 80;

    /// <summary>A tagged tile the dig walk may take, ordered as DK2 ranks them: nearest
    /// first, a wall before a seam at the same distance, then the most recently marked (DK2's
    /// task list takes new tasks at its head, <c>0x00507bb0</c>), then tile order.</summary>
    private readonly record struct ExcavationCandidate(long DistanceSquared, bool Seam, int TagOrder, int TileIndex)
        : IComparable<ExcavationCandidate>
    {
        public int CompareTo(ExcavationCandidate other)
        {
            int byDistance = DistanceSquared.CompareTo(other.DistanceSquared);
            if (byDistance != 0) return byDistance;
            int byKind = Seam.CompareTo(other.Seam); // false (wall) sorts before true (seam)
            if (byKind != 0) return byKind;
            int byOrder = other.TagOrder.CompareTo(TagOrder); // newest first
            return byOrder != 0 ? byOrder : TileIndex.CompareTo(other.TileIndex);
        }
    }

    // The dig walk's candidates, reused across updates.
    private readonly List<ExcavationCandidate> _excavation = [];

    // Per keeper, the tick the counter belongs to and the selections run in it. A tick's
    // count is dead once the tick ends, so nothing here outlives an update.

    private readonly long[] _selectionTick = new long[256];
    private readonly int[] _selections = new int[256];

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
    /// <para><b>Only with room in its hands</b> (<see cref="HandsFull"/>, Q142). A
    /// part-load tops up; a full one is refused here, and deposited only when the
    /// gold kind is the aimed retry (<c>0x00468520</c> kind 7).</para>
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

        // Newest first, as DK2's world turn walks its creature list (0x004B7830), where a new
        // creature is linked at the head (0x004B4A10). So a worker robbed of its task by a newer
        // one chooses again this same tick, and one robbed by an older one on the next
        // (imp-task-stealing.md §8). The list is in id order and ids only climb, so the walk goes
        // by id: a creature removed during it (a buried corpse) shifts nothing onto a second
        // update, and one added during it waits for the next tick.
        int below = int.MaxValue;
        for (int i = creatures.Count - 1; ; i--)
        {
            if (i >= creatures.Count) i = creatures.Count - 1;
            while (i >= 0 && creatures[i].Id >= below) i--;
            if (i < 0) break;
            CreatureState creature = creatures[i];
            below = creature.Id;
            CreatureDef definition = context.Database.CreatureById(creature.CreatureTypeId);
            if (!definition.Flags.HasFlag(CreatureFlags.IsWorker)) continue;

            // A hero worker is HeroSystem's: DK2's state 1 sends a hero to 90 before it tests the
            // worker flag (ADR-0216).
            if (HeroJobRules.IsHero(creature)) continue;

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

        PickUpGoldInPassing(context, creature, definition);
        if (UpdateWander(context, creature, definition)) return;

        if (creature.Activity == CreatureActivity.DeliveringGold)
        {
            if (TryClaimOnTheWay(context, creature, grid)) return;
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
            if (IsCrateJob(creature)) UpdateFetchingCrate(context, creature, grid);
            else UpdateFetchingObject(context, creature, grid);
            return;
        }

        if (creature.Activity == CreatureActivity.CarryingObject)
        {
            if (IsCrateJob(creature)) UpdateCarryingCrate(context, creature);
            else UpdateCarryingObject(context, creature, grid);
            return;
        }

        if (creature.TargetTileIndex >= 0 && !JobStillStands(context, creature))
        {
            // The work state holds while its action runs, and looks at the task only when the
            // action ends (0x004655D0 returns at once while 0x00496D00 reports one running).
            if (creature.Dk2StateId == ImpStates.For(creature.TargetWork).Work
                && !WorkCadence.ActionEnded(creature.TargetWork, context.Tick.Value, creature.StateEnteredTick))
                return;
            if (TryClaimWhatWasAttacked(context, creature)) return;
            context.Emit(SystemName, "job_abandoned",
                $"imp={creature.Id} {Describe(context.World, creature.TargetTileIndex)} " +
                $"{creature.TargetWork} reason=gone");
            WorkEnded(context, creature);
            GoIdle(creature);
            return;
        }

        // No deposit before the choice: DK2's state 10 has none. An imp puts gold down after a
        // dig or a mine with no more excavation (kind 15, in TryTakeJob), on the aimed gold retry
        // with full hands, and when dropped on a store; a claimer carries its load on (ADR-0288).
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

    /// <summary>State 10's choice alone, for the decision checker (<c>dk2tools decide</c>), on the
    /// world as the caller has set it. No wander, timer or per-tick cap.</summary>
    public bool ChooseJob(SimulationContext context, CreatureState creature)
    {
        CreatureDef definition = context.Database.CreatureById(creature.CreatureTypeId);
        var grid = new WorldPathGrid(context.Database, context.World, definition);
        return TryTakeJob(context, creature, definition, grid);
    }

    /// <summary>The store chooser <c>0x00464400</c> from state 10: a walk to the nearest store with
    /// room, if there is one.</summary>
    private bool TryDepositTheLoad(SimulationContext context, CreatureState creature)
    {
        if (!TryRouteToSomewhereItFits(context, creature)) return false;
        BeginDelivering(creature);
        ToTheStore(creature);
        return true;
    }

    /// <summary>DK2's hands test <c>0x00464360(1)</c>: full at <c>MaxGoldHeld</c> scaled by the
    /// imp's level (level 1 unscaled), less variable 74, 340 in every shipped level
    /// (imp-hands-full.md).</summary>
    private static bool HandsFull(SimulationContext context, CreatureState creature, CreatureDef definition) =>
        creature.GoldHeld >= FullAt(context, creature, definition);

    private static int FullAt(SimulationContext context, CreatureState creature, CreatureDef definition) =>
        CreatureLevelRules.MaxGoldHeld(context.Database, definition, creature.Level) - HandsMargin(context.Database);

    /// <summary>Variable 74 (<c>0x006F28B6</c>), the hands test's margin. A database without it is
    /// read as 0, as DK2's global is before a level loads.</summary>
    private static int HandsMargin(GameDatabase database) =>
        database.Variables.TryGet(VariableTypeId.GameTicks, out int margin) ? margin : 0;

    /// <summary>A pile's centre within this of a worker, on both axes and strictly, is picked up
    /// in passing: a third of a tile (<c>0x555</c>).</summary>
    private const int PassingReach = 0x555;

    /// <summary>
    /// The worker arm of DK2's walk-over pick-up (<c>0x0048904E</c>), which the creature update
    /// runs every tick before the state handler, so state 10 then sees the gold in the hands. A
    /// keeper's worker below the hands test's mark takes the first neutral pile whose centre is
    /// within <see cref="PassingReach"/>, as much as it lacks against the level's full maximum
    /// (imp-hands-full.md, gold-on-the-floor.md §12, ADR-0293).
    /// </summary>
    private static void PickUpGoldInPassing(SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        if (!PlayerIds.IsKeeper(creature.OwnerPlayerId) || creature.GoldHeld >= FullAt(context, creature, definition))
            return;
        WorldState world = context.World;
        foreach (ObjectState pile in world.Objects)
        {
            if (!FloorGold.IsPile(context.Database, pile) || pile.InHand || pile.CarriedByCreatureId >= 0
                || pile.OwnerPlayerId != PlayerIds.Neutral || pile.TileIndex < 0)
                continue;
            (long px, long py) = PileCentre(world, pile);
            if (Math.Abs(px - creature.PosX) >= PassingReach || Math.Abs(py - creature.PosY) >= PassingReach) continue;

            int lacks = CreatureLevelRules.MaxGoldHeld(context.Database, definition, creature.Level) - creature.GoldHeld;
            int taken = FloorGold.Take(world, context.Database, pile, lacks);
            creature.GoldHeld += taken;
            context.Emit(SystemName, "gold_picked_up_in_passing",
                $"imp={creature.Id} at={Describe(world, pile.TileIndex)} gold={taken} holding={creature.GoldHeld}");
            return;
        }
    }

    /// <summary>A pile's point: the centre of its third-of-a-tile cell, or of the tile.</summary>
    private static (long X, long Y) PileCentre(WorldState world, ObjectState pile)
    {
        long x = (pile.TileIndex % world.Width) * 4096L, y = (pile.TileIndex / world.Width) * 4096L;
        if (pile.Cell == MiningSlots.AnyCell) return (x + 2048, y + 2048);
        return (x + 682 + (pile.Cell % 3) * 1365, y + 682 + (pile.Cell / 3) * 1365);
    }

    private void UpdateIdle(
        SimulationContext context, CreatureState creature, CreatureDef definition, WorldPathGrid grid)
    {
        creature.TimerTicks--;
        if (creature.TimerTicks > 0) return;

        // State 1 hands a worker to 10 (0x004773D0), and 10 looks on its own next update: both
        // captures show one tick in 1 and one in 10 before every walk or rest (mining-cadence.md
        // §6). A worker whose job ended is already in 10 and looks at once.
        if (creature.Dk2StateId is CreatureStateMachine.Idle or CreatureStateMachine.NoState)
        {
            StateShadow.Move(context, SystemName, creature, ImpStates.TaskSelection);
            return;
        }

        // DK2's state 10 hands a worker that found nothing to 12 at once (ADR-0224).
        if (creature.OwnerPlayerId != PlayerIds.Neutral && !creature.JustDropped && Throttled(context, creature))
            return;
        if (creature.OwnerPlayerId != PlayerIds.Neutral) creature.WorkerIdle = false;
        if (!TryTakeJob(context, creature, definition, grid)) ToRest(context, creature);
    }

    /// <summary>State 10's cap (<c>0x00463E2C</c>–<c>0x00463E9C</c>): a keeper's fifth worker to
    /// choose in one tick rests in 12 until now + 20 + rand(80) instead, the draw taken only once
    /// 12 has accepted it; otherwise the count goes up and the choice runs. A just-dropped
    /// worker skips the cap. The binary also skips it when the worker's last evaluation stamp
    /// is 40 or more ticks in the future, which a stamp written from the current tick never is.</summary>
    private bool Throttled(SimulationContext context, CreatureState creature)
    {
        long now = context.Tick.Value;
        byte owner = (byte)creature.OwnerPlayerId;
        if (_selectionTick[owner] != now)
        {
            _selectionTick[owner] = now;
            _selections[owner] = 0;
        }
        if (_selections[owner] >= SelectionsPerTick
            && StateShadow.TryMove(context, SystemName, creature, ImpStates.Resting))
        {
            creature.StateDeadlineTick = now + ThrottleBaseTicks + context.Rng.NextInt(ThrottleRandomTicks);
            context.Emit(SystemName, "throttled",
                $"imp={creature.Id} player={creature.OwnerPlayerId} until={creature.StateDeadlineTick}");
            return true;
        }
        _selections[owner]++;
        return false;
    }

    private void UpdateMoving(SimulationContext context, CreatureState creature, WorldPathGrid grid)
    {
        if (IsInWorkingPosition(context, creature, grid))
        {
            (int entry, int work) = ImpStates.For(creature.TargetWork);
            if (creature.Dk2StateId == entry)
            {
                // The tick after arrival: the entry state's turn is done. This tick counts
                // towards the first application, as DK2's work state's first tick does.
                StateShadow.Move(context, SystemName, creature, work);
                creature.TimerTicks--;
                return;
            }
            // Arrived on entering the tile; a route whose point lies in it runs on, so the worker
            // settles on its stance point while it works (ADR-0289).
            if (creature.Destination != creature.Tile) creature.StopMoving();
            creature.TimerTicks = WorkCadence.FirstApplicationTicks;
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

        // Decrement first, then act on zero. The timer is WorkCadence's: three ticks
        // from arrival, a kind-dependent gap, then every tick (ADR-0280).
        creature.TimerTicks--;
        if (creature.TimerTicks > 0) return;

        // Stop before the swing that would not fit — but only if there is
        // somewhere to take the load. An imp mines into its own hands up to
        // maxGoldHeld and then runs to the drop spot (CONFIRMED, Q31); an imp
        // with nowhere to run to keeps mining instead, which is also what the
        // original does. See WouldOverfillItsHands.
        if (WouldOverfillItsHands(context, creature, definition)
            && TryRouteToSomewhereItFits(context, creature))
        {
            // 26 walks to the store in 6 resuming in 28.
            BeginDelivering(creature);
            ToTheStore(creature);
            return;
        }

        // A miner not going to deliver gives its slot up once the pile in the slot's cell holds
        // more than MaxGoldPileOutsideTreasury, and state 26 falls to 10 (0x00465870; its overflow
        // lands in that cell, TileWorkSystem.HandTo). The chooser may then give it the face's next
        // slot, which is how a seam gets mined out with nowhere to take the gold (Q140).
        if (creature.TargetWork == TileWorkKind.MineGold
            && FloorGold.PileInCell(context.World, creature.Tile.Y * context.World.Width + creature.Tile.X,
                creature.WorkSlot == MiningSlots.None ? MiningSlots.AnyCell : MiningSlots.Cell(creature.WorkSlot))
                > FloorGold.MaxPile(context.Database))
        {
            context.Emit(SystemName, "job_abandoned",
                $"imp={creature.Id} {Describe(context.World, creature.TargetTileIndex)} " +
                $"{creature.TargetWork} reason=pile_underfoot_full");
            WorkEnded(context, creature);
            GoIdle(creature);
            return;
        }

        context.World.QueueWork(new TileWorkOrder(
            creature.TargetTileIndex, creature.TargetWork, creature.OwnerPlayerId, creature.Id));
        creature.TimerTicks = WorkCadence.AfterApplication(
            creature.TargetWork, context.Tick.Value, creature.StateEnteredTick);

        // A miner walks to the store on the tick of the strike after which another would not
        // fit: in both captures the last strike and state 6 share a tick. This strike's gold
        // reaches the hands later this tick (TileWorkSystem), so count it as held.
        if (creature.TargetWork == TileWorkKind.MineGold
            && WouldOverfillItsHands(context, creature, definition, pendingStrikes: 1)
            && TryRouteToSomewhereItFits(context, creature))
        {
            BeginDelivering(creature);
            ToTheStore(creature);
        }
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
        SimulationContext context, CreatureState creature, CreatureDef definition, int pendingStrikes = 0)
    {
        ref TileState tile = ref context.World.TileAtIndex(creature.TargetTileIndex);
        int yield = TileWorkRules.GoldYield(
            context.Database, tile, creature.TargetWork, creature.OwnerPlayerId);
        int held = creature.GoldHeld + (pendingStrikes * yield);
        if (held <= 0) return false;

        return yield > 0 && held + yield > CreatureLevelRules.MaxGoldHeld(context.Database, definition, creature.Level);
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
        int room = CreatureLevelRules.MaxGoldHeld(context.Database, definition, creature.Level) - creature.GoldHeld;
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

        // State 27 (0x00466850): full hands go to the store chooser (resume 28); a part-load, or
        // a full one with no store that has room, sets 10 at once, and the next choice may be
        // more gold, a claim or a wall with the gold still in its hands (ADR-0288).
        if (HandsFull(context, creature, definition) && TryDepositTheLoad(context, creature)) return;

        WorkEnded(context, creature);
        GoIdle(creature);
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
        creature.TimerTicks = 0;
    }

    /// <summary>
    /// One tick of carrying: put the load down if this is somewhere it can go,
    /// otherwise keep walking, otherwise look for somewhere else.
    /// <para>The put-down test runs every tick rather than only on arrival, so
    /// an imp that walks over a treasury tile with room uses that one. What the
    /// original does when a nearer store appears mid-journey is UNKNOWN.</para>
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
                GoIdle(creature);
                return;
            }

            // 10 walks on with the remainder, the spill into the next tile (Q35).
            ToTheStore(creature);
        }

        // Still on the road.
        if (creature.HasRoute) return;

        if (TryRouteToSomewhereItFits(context, creature))
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
    /// The carrier picks the nearest reachable store room by its first slab,
    /// then a random start and the first free tile along that room's chain.
    /// </summary>
    private bool TryRouteToSomewhereItFits(SimulationContext context, CreatureState creature)
    {
        WorldState world = context.World;
        byte player = creature.OwnerPlayerId;
        int w = world.Width;
        var rooms = new List<(long Distance, RoomInstance Room)>();
        foreach (RoomInstance room in RoomIndex.Build(world, context.Database))
        {
            if (room.OwnerPlayerId != player || room.Definition.TypeId is not
                (RoomTypeId.Treasury or RoomTypeId.DungeonHeart)) continue;
            if (!room.TileIndices.Any(tile => GoldStore.SpaceIn(world, context.Database, tile) > 0)) continue;
            int first = SlabChain.FirstSlab(world, room);
            long dx = CreatureState.CentreOf(first % w) - (long)creature.PosX;
            long dy = CreatureState.CentreOf(first / w) - (long)creature.PosY;
            rooms.Add((((dx * dx) >> 12) + ((dy * dy) >> 12), room));
        }
        rooms.Sort((a, b) => a.Distance.CompareTo(b.Distance));

        foreach ((_, RoomInstance room) in rooms)
        {
            int first = SlabChain.FirstSlab(world, room);
            if (!context.Navigation.TryRoute(creature, new TilePos(first % w, first / w), Navigation.MoveOptions.Plain)) continue;
            int[] chain = SlabChain.Order(world, room);
            int start = chain[context.Rng.NextInt(chain.Length)];
            int target = GoldStore.DepositTileAlongChain(world, context.Database, room, start);
            if (target < 0) continue;
            TilePos drop = new(target % w, target / w);
            if (!context.Navigation.TryRoute(creature, drop, Navigation.MoveOptions.Plain)) continue;
            context.Emit(SystemName, "delivering",
                $"imp={creature.Id} player={player} gold={creature.GoldHeld} " +
                $"to=({drop.X},{drop.Y}) steps={creature.Body.PathTotalCount}");
            return true;
        }
        return false;
    }

    /// <summary>
    /// Chooses a job, in the two tiers of the type comment.
    /// <para><b>Tier A — kind first, one whole-map search per kind</b>, in DK2's
    /// table order: claim the tile just dug (kind 14), carry a loose object (8),
    /// build placed traps and doors (9, 10), dig and mine marked tiles
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
        // First, the kind of the job just ended (DK2's imp +0x98, written by every job handler
        // as it takes a job and zeroed by state 11 on a drop, so a dropped worker skips this).
        // The aimed retry below is one arm; the other is a per-kind record (0x006986A0) whose
        // only flag any kind sets is "this kind again": dig, mine and claim look for more of
        // their own kind, anywhere, before the tiers. So a worker that has claimed a tile
        // claims the nearest next one, and a claimed corridor runs on to the room at its end.
        ImpTaskKind last = creature.LastTaskKind;
        if (!creature.JustDropped && last != ImpTaskKind.None)
        {
            if (creature.DroppedTask == last)
            {
                // Still on the kind it was aimed at, and its last job has ended. More of
                // that kind anywhere on the map and the aimed bonus carries over; none
                // and this is the one place in DK2 that clears it.
                if (TryTakeJobOfKind(context, creature, definition, grid, last)) return true;

                context.Emit(SystemName, "aim_spent",
                    $"imp={creature.Id} player={creature.OwnerPlayerId} " +
                    $"task={creature.DroppedTask} reason=no_more_of_that_kind");
                creature.DroppedTask = ImpTaskKind.None;
            }
            else if (last is ImpTaskKind.Excavate or ImpTaskKind.Mine or ImpTaskKind.Claim
                     && TryTakeJobOfKind(context, creature, definition, grid, last))
            {
                return true;
            }

            // Kind 15, after a dig or a mine and only then: nothing more to excavate, so put
            // down what it holds. Digging on comes first, so an imp whose seam ran out beside
            // another marked wall digs that with the load still in its hands.
            // A deposit keeps the kind (0x00464005 returns before the zeroing at 0x00464010),
            // so the walk to the store can claim on the way.
            if (last is ImpTaskKind.Excavate or ImpTaskKind.Mine
                && creature.GoldHeld > 0 && TryDepositTheLoad(context, creature))
                return true;
            creature.LastTaskKind = ImpTaskKind.None;
        }

        // The keeper's hand: a just-dropped worker looks around where it landed
        // before the tiers (ADR-0069).
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
            // corridor still finds work - it just does not get paid extra for it.
        }

        // Tier A: kind first. One search per kind over the whole map, not one
        // scored search over a mixed list, because the order is an ordering.
        // DK2's table opens with kind 14, but its search 0x00468520 has no arm for
        // 14 (jump table 0x00468770 sends it to the default), so the just-dug claim
        // is taken only on the way (TryClaimOnTheWay, ADR-0286).

        // Kind 8 - carry a loose object to its room - is SECOND in DK2's table,
        // above marked excavation and above claiming, so an imp puts a book away
        // before it digs a wall the keeper marked (ADR-0065).
        if (TryTakeObject(context, creature, definition, grid)) return true;

        // Kinds 9 and 10, third: a crate from a Workshop to a blueprint (ADR-0214).
        if (TryTakeBlueprint(context, creature, grid)) return true;

        if (TryTakeMarkedExcavation(context, creature, grid)) return true;
        if (TryTakeClaim(context, creature, grid)) return true;

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
    /// traps and doors, carrying a body, reinforcing, gold, objects, corpses — so a
    /// worker dropped beside a marked wall digs it rather than putting a book away
    /// (ADR-0065, ADR-0214). One of its nine kinds is unidentified; kind 14,
    /// claiming the just-dug tile, is not among them. All
    /// of it inside <see cref="DropScanTiles"/> of where the worker landed,
    /// measured to <b>the tile the work is at</b> — CONFIRMED, owner's count: a
    /// marked wall four away does not pay, though a floor tile three does.</para>
    /// </summary>
    private bool TryTakeDroppedJob(
        SimulationContext context, CreatureState creature, CreatureDef definition, WorldPathGrid grid)
    {
        // The ring scan of radius 0x2000 (0x0046402C): the landing tile, then diamond rings 1 to 3
        // from the chooser's side, each tile the filter admits given the chain 0x00469240 in its
        // order (Q488, ADR-0292). The kinds without a per-tile form search the one tile.
        WorldState world = context.World;
        TilePos at = creature.Tile;
        byte player = creature.OwnerPlayerId;
        bool handsFree = !HandsFull(context, creature, definition);
        var rings = new DiamondRings(world, at, ClaimStartSide(context, world, at, player));
        try
        {
            while (rings.MoveNext())
            {
                TilePos tile = rings.Current;
                if (Math.Abs(tile.X - at.X) + Math.Abs(tile.Y - at.Y) > DropScanTiles) return false;
                if (!RingScanVisits(context, world, tile)) continue;
                _only = tile;
                if (TryExcavationAt(context, creature, grid, tile, player)) return true;
                if (TryClaimAt(context, creature, grid, tile)) return true;
                if (TryTakeBlueprintNear(context, creature, grid)) return true;
                if (TryTakeBody(context, creature, definition, grid, corpses: false)) return true;
                if (TryReinforceAt(context, creature, grid, tile)) return true;
                if (handsFree && CanFetchFrom(context, tile, player, creature.Id)
                    && _pathfinder.TryFindNearest(grid, creature.Tile, s => s == tile, _route))
                {
                    CommitFetch(context, creature, tile);
                    return true;
                }
                if (TryTakeObject(context, creature, definition, grid)) return true;
                if (TryTakeBody(context, creature, definition, grid, corpses: true)) return true;
            }
            return false;
        }
        finally
        {
            _only = null;
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
        ImpTaskKind.Excavate or ImpTaskKind.Mine => TryTakeMarkedExcavation(context, creature, grid),
        ImpTaskKind.Claim => TryTakeClaim(context, creature, grid),
        ImpTaskKind.Reinforce => TryTakeReinforce(context, creature, grid),
        ImpTaskKind.FetchGold => HandsFull(context, creature, definition)
            ? TryDepositTheLoad(context, creature)
            : TryTakeFloorGold(context, creature, definition, grid),
        ImpTaskKind.CarryObject => TryTakeObject(context, creature, definition, grid),
        ImpTaskKind.BuildTrap or ImpTaskKind.BuildDoor => TryTakeBlueprint(context, creature, grid),
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
        if (HandsFull(context, creature, definition)) return false;

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
    private bool InScanRange(TilePos at) => _only is not TilePos only || at == only;

    /// <summary>
    /// The task kind DK2 records for a job of this priority.
    ///
    /// <para><see cref="JobPriority.ClaimFreshlyDug"/> maps to
    /// <see cref="ImpTaskKind.Claim"/> because the original's kind 14 raises a
    /// kind 3 at the tile and takes that; the byte it leaves behind is a 3.</para>
    /// </summary>
    private static ImpTaskKind KindOf(JobPriority priority, TileWorkKind kind) => priority switch
    {
        _ when kind == TileWorkKind.MineGold => ImpTaskKind.Mine,
        JobPriority.MarkedExcavation => ImpTaskKind.Excavate,
        JobPriority.Reinforce => ImpTaskKind.Reinforce,
        _ => ImpTaskKind.Claim,
    };

    /// <summary>
    /// Tier B (<c>0x004640bb</c>): the ring scan from the worker over the whole map, started on
    /// the side <see cref="ClaimStartSide"/> picks, with the chain <c>0x00469580</c> at each tile:
    /// the tile as a wall to reinforce or repair (<see cref="TryReinforceAt"/>), then the gold on
    /// it. The first tile that gives a job wins, so a wall one ring nearer beats a pile under the
    /// next ring. The chain's objects and kind-11 steps are not built here. Q493, ADR-0291.
    /// </summary>
    private bool TrySweep(
        SimulationContext context, CreatureState creature, CreatureDef definition, WorldPathGrid grid)
    {
        WorldState world = context.World;
        byte player = creature.OwnerPlayerId;
        bool handsFree = !HandsFull(context, creature, definition);
        TilePos at = creature.Tile;

        var rings = new DiamondRings(world, at, ClaimStartSide(context, world, at, player));
        while (rings.MoveNext())
        {
            TilePos tile = rings.Current;
            if (!RingScanVisits(context, world, tile)) continue;
            if (TryReinforceAt(context, creature, grid, tile)) return true;
            if (handsFree && CanFetchFrom(context, tile, player, creature.Id)
                && _pathfinder.TryFindNearest(grid, creature.Tile, s => s == tile, _route))
            {
                CommitFetch(context, creature, tile);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// The reinforce handler (<c>0x0046aea0</c>) on one wall: its four sides from the one
    /// <see cref="MiningSlots.StartSide"/> gives for the worker, each a stand of the keeper's own
    /// floor; the first whose face is free, or held by a farther worker, and which the worker can
    /// reach is taken. imp-task-stealing.md §4.
    /// </summary>
    private bool TryReinforceAt(SimulationContext context, CreatureState creature, WorldPathGrid grid, TilePos wall)
    {
        WorldState world = context.World;
        if (!context.Database.TerrainById(world.Tile(wall).TerrainId).IsSolid) return false;
        (int side, int step) = MiningSlots.StartSide(creature.Tile.X - wall.X, creature.Tile.Y - wall.Y);
        for (int turn = 0; turn < 4; turn++, side = (side + step) & 3)
        {
            var stand = new TilePos(wall.X + SideDx[side], wall.Y + SideDy[side]);
            if (!world.Contains(stand)) continue;
            if (!TryJobOn(context, grid, stand, wall, creature.OwnerPlayerId, JobPriority.Reinforce, creature.Id,
                          out int tileIndex, out TileWorkKind kind))
                continue;
            if (!_pathfinder.TryFindNearest(grid, creature.Tile, s => s == stand, _route)) continue;
            CommitJob(context, creature, tileIndex, kind, JobPriority.Reinforce);
            return true;
        }
        return false;
    }

    /// <summary>
    /// The ring scan's tile filter (<c>0x00468d10</c>): tile byte <c>+0xB</c> bit <c>0x20</c>,
    /// recomputed by <c>0x004502d0</c>, is clear when the tile or one of its four neighbours is
    /// owned, the tile is not water or lava, and it or a neighbour is open floor. A set tile is
    /// visited only when it carries a dig or mine mark (Q487).
    /// </summary>
    private static bool RingScanVisits(SimulationContext context, WorldState world, TilePos at)
    {
        ref TileState tile = ref world.Tile(at);
        if (tile.IsTaggedByAnyone) return true;
        TerrainDef terrain = context.Database.TerrainById(tile.TerrainId);
        if (terrain.IsWater || terrain.IsLava) return false;
        bool owned = IsOwned(tile.OwnerPlayerId), open = !terrain.IsSolid;
        for (int i = 0; i < 4 && !(owned && open); i++)
        {
            var n = new TilePos(at.X + SideDx[i], at.Y + SideDy[i]);
            if (!world.Contains(n)) continue;
            ref TileState next = ref world.Tile(n);
            TerrainDef nt = context.Database.TerrainById(next.TerrainId);
            owned |= IsOwned(next.OwnerPlayerId);
            open |= !nt.IsSolid && !nt.IsWater && !nt.IsLava;
        }
        return owned && open;

        static bool IsOwned(byte owner) => owner is not (PlayerIds.Unowned or PlayerIds.Neutral);
    }

    /// <summary>The dispatcher's kinds 6 and 16 (<c>0x00468520</c>): the ring scan over the whole
    /// map with the reinforce handler alone, started on a <c>rand(4)</c> side, not the chooser's.
    /// ADR-0292.</summary>
    private bool TryTakeReinforce(SimulationContext context, CreatureState creature, WorldPathGrid grid)
    {
        WorldState world = context.World;
        var rings = new DiamondRings(world, creature.Tile, context.Rng.NextInt(4));
        while (rings.MoveNext())
        {
            TilePos tile = rings.Current;
            if (RingScanVisits(context, world, tile) && TryReinforceAt(context, creature, grid, tile)) return true;
        }
        return false;
    }

    /// <summary>
    /// DK2's dig-and-mine walk (<c>0x00469AF0</c>, kinds 1 and 2 alike): every marked wall,
    /// then every marked seam, ranked by <b>straight-line</b> distance from the worker to the
    /// tile's centre, and the nearest with a face it can work from is taken. A later task
    /// replaces the best only when strictly nearer, so a wall beats a seam at the same
    /// distance. After a mining job, seams are measured from the seam just mined instead of
    /// from the worker. Of two walls at one distance the later marked comes first: DK2's list
    /// order, which its diggers' off-centre stance makes decide real choices (ADR-0288).
    /// <para>The drop scan hands each tile of its rings to <see cref="TryExcavationAt"/> instead.</para>
    /// </summary>
    private bool TryTakeMarkedExcavation(SimulationContext context, CreatureState creature, WorldPathGrid grid)
    {
        WorldState world = context.World;
        byte player = creature.OwnerPlayerId;
        int width = world.Width;
        bool fromSeam = creature.LastTaskKind == ImpTaskKind.Mine && creature.LastTaskTileIndex >= 0;
        long seamX = fromSeam ? (creature.LastTaskTileIndex % width) * 4096L + 2048 : 0;
        long seamY = fromSeam ? (creature.LastTaskTileIndex / width) * 4096L + 2048 : 0;

        _excavation.Clear();
        for (int y = 0; y < world.Height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                ref TileState tile = ref world.Tile(x, y);
                if (!tile.IsTaggedBy(player)) continue;
                if (!TileWorkRules.TryTaggedWork(context.Database, tile, player, out TileWorkKind kind)) continue;
                if (!HasOpenFace(world, context.Database, x, y)) continue;
                bool seam = kind == TileWorkKind.MineGold;
                long dx = x * 4096L + 2048 - (seam && fromSeam ? seamX : creature.PosX);
                long dy = y * 4096L + 2048 - (seam && fromSeam ? seamY : creature.PosY);
                _excavation.Add(new ExcavationCandidate(dx * dx + dy * dy, seam, tile.TagOrder, y * width + x));
            }
        }
        _excavation.Sort();

        foreach (ExcavationCandidate candidate in _excavation)
        {
            var target = new TilePos(candidate.TileIndex % width, candidate.TileIndex / width);
            if (TryExcavationAt(context, creature, grid, target, player)) return true;
        }
        return false;
    }

    /// <summary>
    /// One wall of DK2's dig-and-mine walk (<c>0x00469AF0</c>): its four sides from the one facing
    /// the worker, or facing the seam it last mined (<see cref="MiningSlots.StartSide"/>), each open
    /// and reachable side's three slots in order. The first free slot is taken. A held one counts
    /// only when the worker is nearer the wall's centre than the holder is to its side tile's centre;
    /// for a mine, when the worker, from where it stands, is nearer the side tile's corner than the
    /// holder is. The last such is taken if no slot is free, the
    /// holder set to 10. A slot whose cell's pile is over the limit is passed over when mining
    /// (Q140). Q155, ADR-0289.
    /// </summary>
    private bool TryExcavationAt(
        SimulationContext context, CreatureState creature, WorldPathGrid grid, TilePos target, byte player)
    {
        WorldState world = context.World;
        int targetIndex = target.Y * world.Width + target.X;
        bool afterMining = creature.LastTaskKind == ImpTaskKind.Mine && creature.LastTaskTileIndex >= 0;
        TilePos from = afterMining
            ? new TilePos(creature.LastTaskTileIndex % world.Width, creature.LastTaskTileIndex / world.Width)
            : creature.Tile;
        (int side, int step) = MiningSlots.StartSide(from.X - target.X, from.Y - target.Y);

        TilePos? taken = null;
        byte takenSlot = MiningSlots.None;
        TileWorkKind takenWork = default;
        CreatureState? takenFrom = null;
        long mine = -1;
        for (int turn = 0; turn < 4; turn++, side = (side + step) & 3)
        {
            var stand = new TilePos(target.X + SideDx[side], target.Y + SideDy[side]);
            if (!world.Contains(stand) || context.Database.TerrainById(world.Tile(stand).TerrainId).IsSolid) continue;
            if (!TryJobOn(context, grid, stand, target, player, JobPriority.MarkedExcavation, creature.Id,
                          out _, out TileWorkKind work, crowding: false))
                continue;
            if (!_pathfinder.TryFindNearest(grid, creature.Tile, s => s == stand, _route)) continue;

            bool mining = work == TileWorkKind.MineGold;
            if (mining)
                mine = TaskStealing.DistanceSquared(creature.PosX, creature.PosY, stand.X * 4096L, stand.Y * 4096L);
            else if (mine < 0)
                mine = TaskStealing.DistanceSquared(creature.PosX, creature.PosY,
                    CreatureState.CentreOf(target.X), CreatureState.CentreOf(target.Y));
            (long px, long py) = mining
                ? (stand.X * 4096L, stand.Y * 4096L)
                : (CreatureState.CentreOf(stand.X), CreatureState.CentreOf(stand.Y));

            int face = MiningSlots.Face(stand, target);
            int standIndex = stand.Y * world.Width + stand.X;
            for (int along = 0; along < MiningSlots.PerFace; along++)
            {
                byte slot = (byte)(face * MiningSlots.PerFace + along);
                if (work == TileWorkKind.MineGold
                    && FloorGold.PileInCell(world, standIndex, MiningSlots.Cell(slot)) > FloorGold.MaxPile(context.Database))
                    continue;
                CreatureState? holder = MiningSlots.Holder(world, targetIndex, slot, creature.Id);
                if (holder is null)
                {
                    (taken, takenSlot, takenWork, takenFrom) = (stand, slot, work, null);
                    break;
                }
                if (!TaskStealing.CanLoseItsTask(holder)) continue;
                (long hx, long hy) = TaskStealing.HolderPosition(holder, MiningSlots.StancePoint(stand, slot));
                if (mine < TaskStealing.DistanceSquared(hx, hy, px, py))
                    (taken, takenSlot, takenWork, takenFrom) = (stand, slot, work, holder);
            }
            if (taken is not null && takenFrom is null) break;
        }

        if (taken is not TilePos chosen) return false;
        if (!_pathfinder.TryFindNearest(grid, creature.Tile, s => s == chosen, _route)) return false;
        if (takenFrom is not null) TakeTaskFrom(context, takenFrom, creature, targetIndex);
        CommitJob(context, creature, targetIndex, takenWork, JobPriority.MarkedExcavation, takenSlot);
        return true;
    }

    /// <summary>DK2's four sides in its own order (<c>0x00698A60</c>): north, east, south, west.</summary>
    private static readonly int[] SideDx = [0, 1, 0, -1];
    private static readonly int[] SideDy = [-1, 0, 1, 0];

    /// <summary>The handler's <c>0x0048AD30(10)</c> on the holder: it goes to 10 without its task
    /// and chooses again; its last kind stays.</summary>
    private static void TakeTaskFrom(SimulationContext context, CreatureState holder, CreatureState taker, int tileIndex)
    {
        context.Emit(SystemName, "task_stolen",
            $"imp={taker.Id} from={holder.Id} {Describe(context.World, tileIndex)} {holder.TargetWork} " +
            $"holder_state={holder.Dk2StateId}");
        holder.ClearJob();
        if (CreatureStateMachine.HasState(holder))
        {
            StateShadow.Arrive(context, SystemName, holder);
            StateShadow.Move(context, SystemName, holder, ImpStates.TaskSelection);
        }
        holder.TimerTicks = 0;
    }

    /// <summary>A claim or reinforce holder of this face the asker may take the task from: the one
    /// worker there, able to lose it, and farther from the tile's centre (Q155, ADR-0289).</summary>
    private static CreatureState? HolderToTakeFrom(
        SimulationContext context, int tileIndex, TilePos face, int askingCreatureId, TileWorkKind kind)
    {
        if (kind is not (TileWorkKind.Claim or TileWorkKind.AttackTile or TileWorkKind.ConvertRoom
                or TileWorkKind.Reinforce or TileWorkKind.RepairWall))
            return null;
        WorldState world = context.World;
        CreatureState? asking = world.CreatureById(askingCreatureId);
        if (asking is null) return null;
        int faceIndex = face.Y * world.Width + face.X;
        foreach (CreatureState other in world.Creatures)
        {
            if (other.Id == askingCreatureId || other.TargetTileIndex != tileIndex) continue;
            if (ImpCrowding.FaceOf(world, other) != faceIndex) continue;
            if (!TaskStealing.CanLoseItsTask(other)) return null;
            var tile = new TilePos(tileIndex % world.Width, tileIndex / world.Width);
            (long X, long Y) stance = face == tile
                ? (CreatureState.CentreOf(tile.X), CreatureState.CentreOf(tile.Y))
                : TaskStealing.ReinforceStance(face, MiningSlots.Face(face, tile));
            return TaskStealing.NearerTheTile(asking, TaskStealing.HolderPosition(other, stance), tile) ? other : null;
        }
        return null;
    }

    /// <summary>DK2's first test of a face (<c>terrain+0x17B &amp; 1</c>): a side that is not
    /// solid. Cheap, so a marked tile deep in rock never costs a route search.</summary>
    private static bool HasOpenFace(WorldState world, GameDatabase database, int x, int y)
    {
        for (int i = 0; i < NeighbourDx.Length; i++)
        {
            int nx = x + NeighbourDx[i], ny = y + NeighbourDy[i];
            if (nx < 0 || ny < 0 || nx >= world.Width || ny >= world.Height) continue;
            if (!database.TerrainById(world.Tile(nx, ny).TerrainId).IsSolid) return true;
        }
        return false;
    }

    /// <summary>
    /// A keeper's worker walking to deposit claims a just-dug tile it steps onto (<c>0x00464170</c>,
    /// from the creature update <c>0x00488930</c>): on entering a new tile, more than two tiles
    /// (either axis) from its last task tile, with a last task of kind 1–7 or 16, the kind-14
    /// handler <c>0x00469600</c> runs on the tile underfoot — the only taker of a kind-14 task. The
    /// code admits job walks too; DK2's captures show it on a deposit walk only (Q489, ADR-0286).
    /// </summary>
    private bool TryClaimOnTheWay(SimulationContext context, CreatureState creature, WorldPathGrid grid)
    {
        if (!PlayerIds.IsKeeper(creature.OwnerPlayerId) || creature.LastTaskTileIndex < 0) return false;
        TilePos at = creature.Tile;
        if (at.X == creature.LastPosX / Fp32.One && at.Y == creature.LastPosY / Fp32.One) return false;

        WorldState world = context.World;
        int tx = creature.LastTaskTileIndex % world.Width, ty = creature.LastTaskTileIndex / world.Width;
        if (Math.Abs(at.X - tx) <= 2 && Math.Abs(at.Y - ty) <= 2) return false;
        if ((byte)creature.LastTaskKind is not ((>= 1 and <= 7) or 16)) return false;

        ref TileState tile = ref world.Tile(at);
        if (!tile.FreshlyDug) return false;
        if (!TileWorkRules.Applies(context.Database, tile, TileWorkKind.Claim, creature.OwnerPlayerId))
        {
            tile.FreshlyDug = false;
            return false;
        }
        if (!TryJobOn(context, grid, at, at, creature.OwnerPlayerId, JobPriority.ClaimFreshlyDug, creature.Id,
                      out int tileIndex, out TileWorkKind kind))
            return false;

        _route.Clear();
        context.Emit(SystemName, "claimed_on_the_way",
            $"imp={creature.Id} at=({at.X},{at.Y}) gold={creature.GoldHeld}");
        CommitJob(context, creature, tileIndex, kind, JobPriority.ClaimFreshlyDug);
        return true;
    }

    /// <summary>
    /// DK2's claim search (kind 3: <c>0x00468520</c> runs the ring scan <c>0x00468d10</c> with the
    /// claim handler <c>0x0046ab50</c>, whole map). The worker's own tile first, then diamond rings
    /// of Manhattan radius 1, 2, …, each walked clockwise from one corner to the next — north to
    /// east, east to south, south to west, west to north, both corners visited — starting at the
    /// side <see cref="ClaimStartSide"/> picks. The first tile the handler accepts is taken: the
    /// claim predicates, then a route to it (a tile with no route is passed over).
    /// <para>Not built: taking a held claim from a farther imp (Q155), and the <c>+0xB</c> bit
    /// <c>0x20</c> pre-filter (Q487). imp-task-priority.md §2.2, ADR-0285.</para>
    /// </summary>
    private bool TryTakeClaim(SimulationContext context, CreatureState creature, WorldPathGrid grid)
    {

        WorldState world = context.World;
        TilePos at = creature.Tile;
        var rings = new DiamondRings(world, at, ClaimStartSide(context, world, at, creature.OwnerPlayerId));
        while (rings.MoveNext())
            if (TryClaimAt(context, creature, grid, rings.Current)) return true;
        return false;
    }

    /// <summary>
    /// The ring scan's walk (<c>0x00468d10</c>): the centre first, then diamond rings of Manhattan
    /// radius 1, 2, …, each walked clockwise from one corner to the next — north to east, east to
    /// south, south to west, west to north, both corners visited — from the given start side,
    /// until a whole ring lies outside the map. imp-claim-path.md §2.
    /// </summary>
    private struct DiamondRings
    {
        private readonly WorldState _world;
        private readonly TilePos _at;
        private readonly int _side;
        private int _r, _s, _i;
        private bool _inMap;

        public DiamondRings(WorldState world, TilePos at, int side)
        {
            _world = world;
            _at = at;
            _side = side;
            _r = 0;
            _s = 0;
            _i = -1;
            _inMap = false;
            Current = at;
        }

        public TilePos Current { get; private set; }

        public bool MoveNext()
        {
            if (_r == 0)
            {
                _r = 1;
                return _world.Contains(_at);
            }
            while (true)
            {
                if (++_i > _r)
                {
                    _i = 0;
                    if (++_s == 4)
                    {
                        if (!_inMap) return false;
                        _s = 0;
                        _r++;
                        _inMap = false;
                    }
                }
                int k = (_side + _s) & 3;
                Current = k switch
                {
                    0 => new TilePos(_at.X + _i, _at.Y - _r + _i),
                    1 => new TilePos(_at.X + _r - _i, _at.Y + _i),
                    2 => new TilePos(_at.X - _i, _at.Y + _r - _i),
                    _ => new TilePos(_at.X - _r + _i, _at.Y - _i),
                };
                if (!_world.Contains(Current)) continue;
                _inMap = true;
                return true;
            }
        }
    }

    private bool TryClaimAt(SimulationContext context, CreatureState creature, WorldPathGrid grid, TilePos target)
    {
        if (!TryJobOn(context, grid, target, target, creature.OwnerPlayerId, JobPriority.Claim, creature.Id,
                      out int tileIndex, out TileWorkKind kind))
            return false;
        if (!_pathfinder.TryFindNearest(grid, creature.Tile, stand => stand == target, _route)) return false;
        CommitJob(context, creature, tileIndex, kind, JobPriority.Claim);
        return true;
    }

    /// <summary>
    /// Which side of the ring the claim search starts on (<c>0x00469340</c>): the four neighbours
    /// in the order north, east, south, west, each graded by <see cref="ClaimSideGrade"/>, and the
    /// first of the lowest grades wins.
    /// </summary>
    private static int ClaimStartSide(SimulationContext context, WorldState world, TilePos at, byte playerId)
    {
        int best = 0, bestGrade = int.MaxValue;
        for (int k = 0; k < 4; k++)
        {
            var n = k switch
            {
                0 => new TilePos(at.X, at.Y - 1),
                1 => new TilePos(at.X + 1, at.Y),
                2 => new TilePos(at.X, at.Y + 1),
                _ => new TilePos(at.X - 1, at.Y),
            };
            int grade = ClaimSideGrade(context, world, n, playerId);
            if (grade >= bestGrade) continue;
            best = k;
            bestGrade = grade;
        }
        return best;
    }

    /// <summary>
    /// A neighbour's grade for <see cref="ClaimStartSide"/>, lower first: <c>0x00469460</c> sorts the
    /// tile into a class and the byte table at <c>0x00698628</c> ranks the classes. A marked wall or
    /// seam 1; floor to take that touches the keeper's floor — a rival's room 3, a rival's floor 4,
    /// unowned floor 5; a wall to reinforce or repair (not gold, below MaxHealth, unowned or the
    /// keeper's own, touching its floor: <c>0x00453dc0</c>, <c>0x00453e30</c>) 12; anything else 127.
    /// </summary>
    private static int ClaimSideGrade(SimulationContext context, WorldState world, TilePos at, byte playerId)
    {
        if (!world.Contains(at)) return 127;
        ref TileState tile = ref world.Tile(at);
        TerrainDef terrain = context.Database.TerrainById(tile.TerrainId);
        bool unowned = tile.OwnerPlayerId is PlayerIds.Unowned or PlayerIds.Neutral;
        if (!terrain.IsSolid)
        {
            if (terrain.IsWater || terrain.IsLava || tile.OwnerPlayerId == playerId
                || !TouchesOwnLand(context, world, at, playerId))
                return 127;
            return terrain.IsRoom ? 3 : unowned ? 5 : 4;
        }
        if (tile.IsTaggedBy(playerId)) return 1;
        if (!terrain.IsImpenetrable && terrain.GoldValue == 0 && tile.Health != terrain.MaxHealth
            && (unowned || tile.OwnerPlayerId == playerId) && TouchesOwnLand(context, world, at, playerId))
            return 12;
        return 127;
    }

    /// <summary>
    /// Commits a worker to tile work along the route the last search planned.
    /// </summary>
    private void CommitJob(
        SimulationContext context, CreatureState creature, int tileIndex, TileWorkKind kind, JobPriority priority,
        byte? slot = null)
    {
        TilePos standing = _route.Count > 0 ? _route[^1] : creature.Tile;
        WorldState world = context.World;
        bool onTheTile = standing.Y * world.Width + standing.X == tileIndex;
        if (slot is null && ImpCrowding.FaceIsFull(world, context.Database, tileIndex, standing, creature.Id, onTheTile, kind)
            && HolderToTakeFrom(context, tileIndex, standing, creature.Id, kind) is CreatureState holder)
            TakeTaskFrom(context, holder, creature, tileIndex);

        creature.TargetTileIndex = tileIndex;
        creature.TargetWork = kind;
        creature.WorkSlot = slot ?? (kind == TileWorkKind.MineGold
            ? MiningSlots.FreeSlot(world, context.Database, tileIndex, standing,
                new TilePos(tileIndex % world.Width, tileIndex / world.Width), creature.Id)
            : MiningSlots.None);

        // A digger walks to its slot's point and a reinforcer to its side's, as DK2's handlers
        // route; the rest to the tile's centre. Where it stands is what task stealing measures.
        var target = new TilePos(tileIndex % world.Width, tileIndex / world.Width);
        int face = MiningSlots.Face(standing, target);
        if (creature.WorkSlot != MiningSlots.None)
        {
            (long sx, long sy) = MiningSlots.StancePoint(standing, creature.WorkSlot);
            context.Navigation.TryRouteToStance(creature, sx, sy, Navigation.MoveOptions.Plain);
        }
        else if (kind is TileWorkKind.Reinforce or TileWorkKind.RepairWall && face >= 0)
        {
            (long sx, long sy) = TaskStealing.ReinforceStance(standing, face);
            context.Navigation.TryRouteToStance(creature, sx, sy, Navigation.MoveOptions.Plain);
        }
        else
        {
            context.Navigation.TryRoute(creature, standing, Navigation.MoveOptions.Plain);
        }

        if (_route.Count == 0) creature.TimerTicks = WorkCadence.FirstApplicationTicks;

        SetOutInState(context, creature, kind, routed: _route.Count > 0);

        creature.LastTaskKind = KindOf(priority, kind);
        creature.LastTaskTileIndex = tileIndex;

        context.Emit(SystemName, "job_taken",
            $"imp={creature.Id} player={creature.OwnerPlayerId} " +
            $"{Describe(context.World, tileIndex)} {kind} priority={priority} steps={_route.Count}");
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
        out TileWorkKind kind,
        bool crowding = true)
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

            // Claiming floor, attacking a rival's floor and taking somebody
            // else's room are one priority and three kinds of work, each with its
            // own rate (ClaimTileHealth +825, AttackTileHealth −140,
            // ConvertRoomHealth −4000). Their predicates partition the tiles, so
            // no order between them has to be chosen here. Hostile floor falls to
            // Dirt Path and is then an ordinary claim (Q68, owner).
            case JobPriority.Claim:
                if (TileWorkRules.Applies(context.Database, tile, TileWorkKind.Claim, playerId))
                    kind = TileWorkKind.Claim;
                else if (TileWorkRules.Applies(context.Database, tile, TileWorkKind.AttackTile, playerId))
                    kind = TileWorkKind.AttackTile;
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

        // Three workers per dig face, one per reinforce face or claim (Q43/Q44). Rejecting the
        // FACE, not the tile, sends the surplus on to the next standing tile, block or priority
        // rather than queueing. A full face stays open to a nearer claimer or reinforcer, whom
        // CommitJob lets take it (Q155).
        int index = (target.Y * world.Width) + target.X;
        if (crowding
            && ImpCrowding.FaceIsFull(world, context.Database, index, standing, askingCreatureId, onTheTile, kind)
            && HolderToTakeFrom(context, index, standing, askingCreatureId, kind) is null)
        {
            kind = default;
            return false;
        }

        // Mining needs a free slot on this face: one no other miner holds, whose cell's pile holds
        // no more than MaxGoldPileOutsideTreasury (0x00469AF0, 0x0046A550; Q140).
        if (crowding && kind == TileWorkKind.MineGold
            && MiningSlots.FreeSlot(world, context.Database, index, standing, target, askingCreatureId)
                == MiningSlots.None)
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
    /// Whether any tile orthogonally adjacent to <paramref name="target"/> is the
    /// keeper's own <b>floor</b> — the condition on an unprompted job the worker
    /// does standing on the target itself (CONFIRMED, Q42).
    /// <para>READ from <c>0x00453b30</c>: each of the four neighbours counts only
    /// when its owner is the keeper, its terrain is Ownable and it is not Solid, so
    /// the keeper's own walls do not start a claim. Level 2 live capture
    /// (level2-live1): the heart's walls touch the dug Lair on three sides, and the
    /// first claim is the one tile touching the heart's gap, then outward from it.</para>
    /// </summary>
    internal static bool TouchesOwnLand(
        SimulationContext context, WorldState world, TilePos target, byte playerId)
    {
        for (int i = 0; i < NeighbourDx.Length; i++)
        {
            var neighbour = new TilePos(target.X + NeighbourDx[i], target.Y + NeighbourDy[i]);
            if (StandsOnOwnLand(context, world, neighbour, playerId)
                && !context.Database.TerrainById(world.Tile(neighbour).TerrainId).IsSolid)
                return true;
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

        if (WorkStance.IsInPosition(context.Database, world, grid, creature.Tile, target, creature.TargetWork))
            return true;

        // A worker at work still settling on its stance point may be nudged across the tile's edge
        // by a neighbour; its route still ends on the stance tile, and that is where it works.
        (int entry, int work) = ImpStates.For(creature.TargetWork);
        return creature.Dk2StateId == entry || creature.Dk2StateId == work
            ? creature.IsRouted && WorkStance.IsInPosition(
                context.Database, world, grid, creature.Destination, target, creature.TargetWork)
            : false;
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
            at => InScanRange(at) && LooseObjectAt(context, creature, definition, at) is { } loose
                  && (ObjectCarryRules.TryDestinationFor(world, context.Database, creature, loose) >= 0
                      || (CrateRules.IsCrate(context.Database, loose) && BlueprintForLooseCrate(context, creature, grid, loose) != null)),
            _route);

        if (!found) return false;

        TilePos at = _route.Count > 0 ? _route[_route.Count - 1] : creature.Tile;
        ObjectState? thing = LooseObjectAt(context, creature, definition, at);
        if (thing is null) return false;

        // Kind 8's crate branch (0x00468910) comes before the crate's Workshop: straight to a
        // blueprint that needs it, as kind 9 or 10 (ADR-0214).
        if (CrateRules.IsCrate(context.Database, thing)
            && BlueprintForLooseCrate(context, creature, grid, thing) is { } blueprint)
            return CommitCrateFetch(context, creature, blueprint, thing, "loose");

        if (ObjectCarryRules.TryDestinationFor(world, context.Database, creature, thing) < 0) return false;

        creature.TargetTileIndex = (at.Y * world.Width) + at.X;
        creature.TargetWork = default;
        creature.TargetObjectId = thing.Id;
        context.Navigation.TryRoute(creature, at, Navigation.MoveOptions.Plain);
        creature.TimerTicks = 0;
        creature.LastTaskKind = ImpTaskKind.CarryObject;
        FromTaskSelection(creature);
        CreatureStateMachine.BeginTravel(creature, StateShadow.TravelToPoint, ImpStates.PickingUpObject);

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
            WorkEnded(context, creature);
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
            StateShadow.ToIdle(context, SystemName, creature);
            GoIdle(creature);
            return;
        }

        StateShadow.Arrive(context, SystemName, creature);
        int destination = CrateRules.IsCrate(context.Database, thing)
            ? CrateHome(context, creature)
            : ObjectCarryRules.TryDestinationFor(world, context.Database, creature, thing);

        if (destination < 0 || !RouteTo(context, creature, grid, destination))
        {
            context.Emit(SystemName, "object_abandoned",
                $"imp={creature.Id} object={thing.Id} reason=nowhere_to_take_it");
            creature.TargetObjectId = -1;
            WorkEnded(context, creature);
            GoIdle(creature);
            return;
        }

        // 30 walks it to its room in 6, resuming in the state for its kind.
        CreatureStateMachine.BeginTravel(creature, StateShadow.TravelToPoint,
            ImpStates.CarryingStateFor(context.Database.ObjectById(thing.ObjectTypeId)!.Flags));

        creature.TargetObjectId = -1;
        ObjectCarryRules.PickUp(creature, thing);
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
            // An invalid carried id: 31-33 return 0, which is idle.
            creature.CarriedObjectId = -1;
            StateShadow.ToIdle(context, SystemName, creature);
            GoIdle(creature);
            return;
        }

        ObjectCarryRules.FollowCarrier(world, creature, thing);

        int here = (creature.Tile.Y * world.Width) + creature.Tile.X;
        if (here != creature.TargetTileIndex)
        {
            if (creature.HasRoute) return;

            int destination = CrateRules.IsCrate(context.Database, thing)
                ? CrateHome(context, creature)
                : ObjectCarryRules.TryDestinationFor(world, context.Database, creature, thing);

            if (destination >= 0 && RouteTo(context, creature, grid, destination))
            {
                // The carry state finds the tile unsuitable and asks the helper again.
                creature.TargetTileIndex = destination;
                int carrying = creature.UtilityNewState;
                StateShadow.Arrive(context, SystemName, creature);
                CreatureStateMachine.BeginTravel(creature, StateShadow.TravelToPoint, carrying);
                return;
            }

            context.Emit(SystemName, "object_dropped",
                $"imp={creature.Id} object={thing.Id} " +
                $"at=({creature.Tile.X},{creature.Tile.Y}) reason=no_route");
            ObjectCarryRules.PutDown(creature, thing);
            StateShadow.ToIdle(context, SystemName, creature);
            GoIdle(creature);
            return;
        }

        if (CrateRules.IsCrate(context.Database, thing))
        {
            StoreCrate(context, creature, grid, thing, here);
            return;
        }

        // Somebody else shelved something here while this imp was walking.
        if (ObjectCarryRules.ObjectOn(world, here) is { } sitting && sitting.Id != thing.Id)
        {
            context.Emit(SystemName, "object_dropped",
                $"imp={creature.Id} object={thing.Id} " +
                $"at=({creature.Tile.X},{creature.Tile.Y}) reason=tile_taken");
            ObjectCarryRules.PutDown(creature, thing);
            StateShadow.ToIdle(context, SystemName, creature);
            GoIdle(creature);
            return;
        }

        ObjectCarryRules.Deliver(creature, thing, creature.OwnerPlayerId, here);

        // A book joining a Library is the room's take (0x004E12A0): it makes its item
        // available, or is destroyed when it duplicates a spell the keeper already has.
        if (context.Database.ObjectById(thing.ObjectTypeId)?.IsSpellBook == true)
            ResearchTreeRules.Shelve(world, context.Database, creature.OwnerPlayerId, thing);

        context.Emit(SystemName, "object_stored",
            $"imp={creature.Id} player={creature.OwnerPlayerId} object={thing.Id} " +
            $"type={ObjectName(context, thing)} " +
            $"{Describe(world, here)}");

        // Stored: the carry state sets 10.
        WorkEnded(context, creature);
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

        creature.CarriedCreatureId = -1;
        creature.TargetTileIndex = (at.Y * world.Width) + at.X;
        creature.TargetWork = default;
        context.Navigation.TryRoute(creature, at, Navigation.MoveOptions.Plain);
        creature.TimerTicks = 0;
        creature.LastTaskKind = corpses ? ImpTaskKind.CarryCorpse : ImpTaskKind.CarryUnconscious;
        FromTaskSelection(creature);
        CreatureStateMachine.BeginTravel(creature, StateShadow.TravelToPoint,
            corpses ? ImpStates.PickingUpCorpse : ImpStates.PickingUpCasualty);

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
    /// null. Casualties on this tile prefer marked foreigners, own creatures,
    /// then other foreigners; equal-rank ties retain the simulation's spawn order.
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
            int rank = corpses ? 0 : RescuePriorityRules.Rank(body, worker.OwnerPlayerId);
            int bestRank = best is null ? int.MaxValue
                : corpses ? 0 : RescuePriorityRules.Rank(best, worker.OwnerPlayerId);
            if (rank < bestRank || (rank == bestRank && body.Id < best!.Id)) best = body;
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
            WorkEnded(context, creature);
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
            StateShadow.ToIdle(context, SystemName, creature);
            GoIdle(creature);
            return;
        }

        StateShadow.Arrive(context, SystemName, creature);
        int destination = CarryRules.TryDestinationFor(world, context.Database, creature, body);
        if (destination < 0 || !RouteTo(context, creature, grid, destination))
        {
            context.Emit(SystemName, "body_abandoned",
                $"imp={creature.Id} body={body.Id} reason=nowhere_to_take_it");
            ForgetBody(creature);
            WorkEnded(context, creature);
            GoIdle(creature);
            return;
        }

        // 40 walks a corpse to 41; 42 chooses by ownership through 0x0046E610.
        CreatureStateMachine.BeginTravel(creature, StateShadow.TravelToPoint,
            CreatureDeathRules.IsCorpse(body) ? ImpStates.CarryingCorpse
            : body.OwnerPlayerId == creature.OwnerPlayerId ? ImpStates.CarryingToLair
            : ImpStates.CarryingToPrison);

        ForgetBody(creature);
        CarryRules.PickUp(creature, body);
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
            // A bad carried id: the carry state returns 0, which is idle.
            creature.CarriedCreatureId = -1;
            StateShadow.ToIdle(context, SystemName, creature);
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
                // 41, 43 and 45 each re-route to themselves.
                creature.TargetTileIndex = destination;
                int carrying = creature.UtilityNewState;
                StateShadow.Arrive(context, SystemName, creature);
                CreatureStateMachine.BeginTravel(creature, StateShadow.TravelToPoint, carrying);
                return;
            }

            context.Emit(SystemName, "body_dropped",
                $"imp={creature.Id} body={body.Id} " +
                $"at=({creature.Tile.X},{creature.Tile.Y}) reason=no_route");
            CarryRules.PutDown(creature, body);
            StateShadow.ToIdle(context, SystemName, creature);
            GoIdle(creature);
            return;
        }

        StateShadow.Arrive(context, SystemName, creature);
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
                StateShadow.ToIdle(context, SystemName, creature);
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
            WorkEnded(context, creature);
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
            // Our one walk ends at the cell, so 43's inner leg to 44 and 44's own walks fall
            // in this tick: admitted walks away through 46, refused puts it down through 47
            // and 48, and both end in 10.
            WalkOn(context, creature, ImpStates.AtThePrisonDoor);
            WorkerPickupRules.SetEnabled(creature, false);
            int cell = creature.TargetTileIndex;
            if (PrisonRules.AdmitsAt(world, context.Database, creature.OwnerPlayerId, cell)
                && PrisonRules.Imprison(
                    world, context.Database, body, creature.OwnerPlayerId, cell))
            {
                WalkOn(context, creature, ImpStates.LeavingThePrisoner);
                WorkerPickupRules.SetEnabled(creature, true);
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
                WalkOn(context, creature, ImpStates.TakingTheRefusedBody);
                WorkerPickupRules.SetEnabled(creature, true);
                WalkOn(context, creature, ImpStates.PuttingDownTheRefusedBody);
            }

            WorkEnded(context, creature);
            GoIdle(creature);
            return;
        }

        if (CreatureDeathRules.Rescue(world, context.Database, body))
        {
            context.Emit(SystemName, "creature_rescued",
                $"imp={creature.Id} player={creature.OwnerPlayerId} body={body.Id} " +
                $"type={definition.Name} {Describe(world, creature.TargetTileIndex)} " +
                $"health={body.Health} (the lair heals it from here, ADR-0049)");
            WorkEnded(context, creature);
        }
        else
        {
            context.Emit(SystemName, "body_dropped",
                $"imp={creature.Id} body={body.Id} " +
                $"at=({creature.Tile.X},{creature.Tile.Y}) reason=bed_gone");
            StateShadow.ToIdle(context, SystemName, creature);
        }

        GoIdle(creature);
    }

    /// <summary>A walk of DK2's that ours does not make: 6 resuming in the state, arrived at on
    /// the same tick.</summary>
    private static void WalkOn(SimulationContext context, CreatureState creature, int resume)
    {
        if (!CreatureStateMachine.HasState(creature)) return;
        CreatureStateMachine.BeginTravel(creature, StateShadow.TravelToPoint, resume);
        StateShadow.Arrive(context, SystemName, creature);
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
        StateShadow.Move(context, SystemName, creature, ImpStates.TaskSelection);
        CreatureStateMachine.BeginTravel(
            creature, StateShadow.TravelToPoint, ImpStates.For(kind).Entry);
        if (!routed) AtWork(context, creature);
    }

    /// <summary>An attacked floor tile that has gone neutral is claimed by its attacker under the
    /// same task: enemy floor is DK2's kind-3 task with subkind 2, and state 20 (<c>0x00465F20</c>)
    /// sets the subkind to 0 and goes to 15, not to 10, so there is no second choice and no second
    /// chance to be robbed of it (imp-claim-path.md §8).</summary>
    private static bool TryClaimWhatWasAttacked(SimulationContext context, CreatureState creature)
    {
        if (creature.TargetWork != TileWorkKind.AttackTile
            || creature.Dk2StateId != ImpStates.For(TileWorkKind.AttackTile).Work)
            return false;
        ref TileState tile = ref context.World.TileAtIndex(creature.TargetTileIndex);
        if (!TileWorkRules.Applies(context.Database, tile, TileWorkKind.Claim, creature.OwnerPlayerId))
            return false;

        creature.TargetWork = TileWorkKind.Claim;
        StateShadow.Move(context, SystemName, creature, ImpStates.For(TileWorkKind.Claim).Entry);
        creature.TimerTicks = WorkCadence.FirstApplicationTicks;
        context.Emit(SystemName, "attack_to_claim",
            $"imp={creature.Id} {Describe(context.World, creature.TargetTileIndex)}");
        return true;
    }

    /// <summary>State 10 is where a worker's every task starts, and where it rests between them.</summary>
    private static void FromTaskSelection(CreatureState creature) =>
        CreatureStateMachine.TrySetState(creature, ImpStates.TaskSelection);

    /// <summary>The gold-store chooser <c>0x00464400</c>: a walk in 6 resuming in 28, from
    /// whichever of 10, 26 and 27 called it.</summary>
    private static void ToTheStore(CreatureState creature) =>
        CreatureStateMachine.BeginTravel(creature, StateShadow.TravelToPoint, ImpStates.AtTheStore);

    /// <summary>The walk ends in the entry state (13, 15, 21, 25), which turns the imp to face
    /// the tile (<c>0x004654F0</c> for 13) and sets the work state once the turn is done. Ours
    /// takes the one tick every one of 416 captured entries took (mining-cadence.md §5); the
    /// work state follows in <see cref="UpdateMoving"/>.</summary>
    private static void AtWork(SimulationContext context, CreatureState creature)
    {
        StateShadow.Arrive(context, SystemName, creature);
    }

    /// <summary>The task or the stance has gone, or the tile is done: the entry or work state
    /// sets 10.</summary>
    private static void WorkEnded(SimulationContext context, CreatureState creature)
    {
        if (!CreatureStateMachine.HasState(creature)) return;
        StateShadow.Arrive(context, SystemName, creature);
        StateShadow.Move(context, SystemName, creature, ImpStates.TaskSelection);
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
