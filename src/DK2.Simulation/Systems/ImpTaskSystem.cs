// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;
using DK2.Simulation.Combat;

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

    /// <summary>The rescue selector's Lair and Prison results for the body search in progress
    /// (<c>0x0046D810</c>'s <c>+0x105</c>/<c>+0x10F</c> caches), settled when a body first asks.</summary>
    private RoomNeedRules.RescueRoom? _lairResult, _prisonResult;

    /// <summary>The corpse selections' Graveyard tile for the search in progress, -1 for none
    /// (<c>0x0046CF40</c>, <c>0x0046D3C0</c>), settled when a corpse first asks.</summary>
    private int? _corpseGraveyard;

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

    private delegate void ActivityStep(SimulationContext context, CreatureState creature, CreatureDef definition, WorldPathGrid grid);

    private static readonly int ActivityCount = Enum.GetValues(typeof(CreatureActivity)).Cast<int>().Max() + 1;

    /// <summary>The activities whose handler owns the worker's whole tick, tested before the job is
    /// checked: the gold walks and the carries. Indexed by <see cref="CreatureActivity"/>.</summary>
    private readonly ActivityStep?[] _carrying;

    /// <summary>The job activities, run once the job is known to stand; any other activity goes idle.</summary>
    private readonly ActivityStep?[] _jobs;

    public ImpTaskSystem() : this(new GridPathfinder()) { }

    /// <summary>Takes the pathfinder explicitly so a replacement for Q5 can be dropped in.</summary>
    public ImpTaskSystem(IPathfinder pathfinder)
    {
        _pathfinder = pathfinder ?? throw new ArgumentNullException(nameof(pathfinder));
        _carrying = StepTable(
            (CreatureActivity.DeliveringGold, (context, creature, _, grid) => UpdateDelivering(context, creature, grid)),
            (CreatureActivity.FetchingGold, (context, creature, definition, grid) => UpdateFetching(context, creature, definition, grid)),
            (CreatureActivity.FetchingBody, (context, creature, _, grid) => UpdateFetchingBody(context, creature, grid)),
            (CreatureActivity.CarryingBody, (context, creature, _, grid) => UpdateCarryingBody(context, creature, grid)),
            (CreatureActivity.FetchingObject, (context, creature, _, grid) =>
            {
                if (IsCrateJob(creature)) UpdateFetchingCrate(context, creature, grid);
                else UpdateFetchingObject(context, creature, grid);
            }),
            (CreatureActivity.CarryingObject, (context, creature, _, grid) =>
            {
                if (IsCrateJob(creature)) UpdateCarryingCrate(context, creature);
                else UpdateCarryingObject(context, creature, grid);
            }));
        _jobs = StepTable(
            (CreatureActivity.Idle, (context, creature, definition, grid) => UpdateIdle(context, creature, definition, grid)),
            (CreatureActivity.MovingToJob, (context, creature, definition, grid) => UpdateMoving(context, creature, definition, grid)),
            (CreatureActivity.Working, (context, creature, definition, grid) => UpdateWorking(context, creature, definition, grid)));
    }

    private static ActivityStep?[] StepTable(params (CreatureActivity Activity, ActivityStep Step)[] steps)
    {
        var table = new ActivityStep?[ActivityCount];
        foreach ((CreatureActivity activity, ActivityStep step) in steps) table[(int)activity] = step;
        return table;
    }

    private static ActivityStep? StepFor(ActivityStep?[] table, CreatureState creature)
    {
        int activity = (int)creature.Activity;
        return activity >= 0 && activity < table.Length ? table[activity] : null;
    }

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
            if (creature.Possessed) continue;
            below = creature.Id;
            if (context.TremorRecoveryCompletedThisTick.Contains(creature)) continue;
            CreatureDef definition = context.Database.CreatureById(creature.CreatureTypeId);
            if (!definition.Flags.HasFlag(CreatureFlags.IsWorker)) continue;

            // A hero worker is HeroSystem's: DK2's state 1 sends a hero to 90 before it tests the
            // worker flag (ADR-0216).
            if (HeroJobRules.IsHero(creature)) continue;

            // Held: out of the dungeon until put down (ADR-0062). Down: its job was cleared when it
            // dropped, so this only stops it taking a new one (ADR-0051). A prisoner as a rule, not a
            // gap: its wandering is PrisonerWanderSystem's and its health PrisonSystem's (ADR-0073);
            // still a stated narrowing, the other half of Q172 (racing for a chicken dropped among them).
            if (CreatureEligibility.IsExcluded(creature,
                CreatureGuards.Held | CreatureGuards.Imprisoned | CreatureGuards.Down | CreatureGuards.Preoccupied)) continue;

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

        // The worker arm of the gold block (0x0048904E) tries the claim on the way (0x00464170)
        // before its pick-up: a worker-job state, or a walk resuming one (0x00478050 == 3), with
        // no object or creature carried; a wander walk resumes idle and never claims. Level 1
        // seeds 3, 4 and 5 open their first room this way from a walk to a dig (ADR-0303, ADR-0378).
        bool claimed = CreatureStateMachine.HasState(creature)
            && NeedRules.ResolvedCategory(creature) == CreatureStateCategory.WorkerJob
            && creature.CarriedObjectId < 0 && creature.CarriedCreatureId < 0
            && TryClaimOnTheWay(context, creature, grid);
        PickUpGoldInPassing(context, creature, definition);
        if (claimed) return;
        if (UpdateWander(context, creature, definition)) return;

        if (StepFor(_carrying, creature) is { } carry)
        {
            carry(context, creature, definition, grid);
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
            bool atWork = creature.Dk2StateId == ImpStates.For(creature.TargetWork).Work;
            int dug = creature.TargetTileIndex;
            TileWorkKind work = creature.TargetWork;
            WorkEnded(context, creature);
            GoIdle(creature);
            if (atWork && IsExcavation(work)) RaiseClaimOfWhatWasDug(context, creature.OwnerPlayerId, dug, work);
            return;
        }

        // No deposit before the choice: DK2's state 10 has none. An imp puts gold down after a
        // dig or a mine with no more excavation (kind 15, in TryTakeJob), on the aimed gold retry
        // with full hands, and when dropped on a store; a claimer carries its load on (ADR-0288).
        if (StepFor(_jobs, creature) is { } job) job(context, creature, definition, grid);
        else GoIdle(creature);
    }

    /// <summary>State 10's choice alone, for the decision checker (<c>dk2tools decide</c>), on the
    /// world as the caller has set it, after the walk-over pick-up that DK2's creature update runs
    /// first in the same tick (0x0048904E). No wander, timer or per-tick cap.</summary>
    public bool ChooseJob(SimulationContext context, CreatureState creature)
    {
        CreatureDef definition = context.Database.CreatureById(creature.CreatureTypeId);
        PickUpGoldInPassing(context, creature, definition);
        var grid = new WorldPathGrid(context.Database, context.World, definition);
        return TryTakeJob(context, creature, definition, grid);
    }



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
        BeginTaskWalk(context,
            creature, ImpStates.For(kind).Entry);
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
    private static void FromTaskSelection(SimulationContext context, CreatureState creature)
    {
        CreatureStateMachine.TrySetState(context.Hooks, creature, ImpStates.TaskSelection);
    }

    /// <summary>The gold-store chooser <c>0x00464400</c>: a walk in 6 resuming in 28, from
    /// whichever of 10, 26 and 27 called it.</summary>
    private static void ToTheStore(SimulationContext context, CreatureState creature)
    {
        BeginTaskWalk(context, creature, ImpStates.AtTheStore);
    }

    /// <summary>The walk ends in the entry state. Wall-work entries turn toward the target
    /// tile before <see cref="UpdateMoving"/> enters the work state (imp-state-exits.md §2).</summary>
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
    private static int IdleDelayTicks(GameDatabase database)
    {
        return SimulationTiming.TicksFromSeconds(
            database[VariableTypeId.ImpIdleDelayBeforeReevaluationSeconds]);
    }

    private static string Describe(WorldState world, int tileIndex)
    {
        return tileIndex < 0 ? "(none)" : $"({tileIndex % world.Width},{tileIndex / world.Width})";
    }
}
