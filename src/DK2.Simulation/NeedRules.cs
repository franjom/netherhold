// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>Whether a need may take a creature away from what it is doing: DK2's needs routine
/// <c>0x00477C20</c>, which the dispatcher runs for every non-worker every turn before the
/// state's handler (ADR-0198, <c>creature-needs-routine.md</c>).</summary>
/// <remarks>The routine does nothing for a state of group 7, the needs' own. Otherwise each
/// need asks the transition rule's entry-mask half about its own category, Eating (8),
/// Sleeping (9) or Payday (20), against the creature's category, read through a walk to the
/// state it resumes. All three masks are <c>0x800A3C3B</c>: idle, the job categories,
/// Sulking, the Casino, Spectating and category 0 are admitted; the Lair, Prison, Torture,
/// the Combat Pit and combat are not. So a hungry researcher breaks off to eat, and a
/// creature on a torture device does not.</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/creature-needs-routine.md")]
public static class NeedRules
{
    /// <summary>The needs' own state group: the routine returns at once for it.</summary>
    public const byte NeedGroup = 7;

    /// <summary>The group the busy test <c>0x00477B40</c> refuses every attempt from: captives,
    /// the devices, leaving, the pit fighter.</summary>
    public const byte BusyGroup = 1;

    /// <summary>Whether <paramref name="need"/> may act on this creature now. An idle creature
    /// always may; one at a job, or walking to one, when its state admits the need.
    /// Everything else still on our own activities (combat, leaving) is left alone.</summary>
    public static bool MayAsk(CreatureState creature, CreatureStateCategory need)
    {
        if (creature.Activity == CreatureActivity.Idle) return true;
        if (creature.Activity is not (CreatureActivity.AtWork or CreatureActivity.GoingToJob)) return false;
        return CreatureStateMachine.HasState(creature) && Admits(creature, need);
    }

    /// <summary>The routine's own test on a creature holding a state id.</summary>
    public static bool Admits(CreatureState creature, CreatureStateCategory need)
    {
        ref readonly CreatureStateDef current = ref CreatureStateTable.Get(creature.Dk2StateId);
        if (current.Group is NeedGroup or BusyGroup) return false;

        CreatureStateCategory from = ResolvedCategory(creature);

        // Hunger and sleep also refuse their own category (`!= 8`, `!= 9`); pay has no such
        // test, and only the group-7 skip keeps it from asking a creature already at it.
        if (need != CreatureStateCategory.Payday && from == need) return false;

        // The rule's three arms: the same category, a zero mask, or the bit.
        return from == need
            || CreatureStateCategoryTable.Get(need).AdmitsFrom(CreatureStateCategoryTable.Get(from));
    }

    /// <summary>Turns between two tries of one need: hunger's <c>+0x50</c>, sleep's <c>+0x5C</c>
    /// and pay's <c>+0x60</c> are stamped four turns on.</summary>
    public const int RetryTurns = 4;

    /// <summary>How long a creature dropped on a room is left alone: five seconds of
    /// <c>LastDroppedCounter</c> (<c>+0x46D</c>, <c>5 ×</c> variable 19 turns).</summary>
    public const int DroppedOnARoomGraceTicks = 5 * SimulationTiming.OriginalTicksPerGameSecond;

    /// <summary>The parts of the busy test <c>0x00477B40</c> our activity filters do not already
    /// cover: a creature dropped on a room (<c>+0x3E6</c> bit 26, set by 76 from the landing tile)
    /// within <see cref="DroppedOnARoomGraceTicks"/> of the drop, and a Combat Pit fighter.</summary>
    /// <remarks>The rest of the test, read: group 1, a prison owner, combat, a possessed creature following, the stunned states 209 and 255–257, the alert
    /// timer <c>+0x45B</c>, a Call To Arms (player <c>+0x76</c> bit 2, <c>CallToArmsOn</c>,
    /// states 190–192), <c>RebelActive</c>, <c>Leaving</c> and the Casino's big-winner countdown.
    /// Each is either one of our activities, which the needs never ask, or unbuilt.</remarks>
    public static bool IsBusy(WorldState world, CreatureState creature, long now) =>
        creature.CombatPitFighter
        || CallToArmsRules.IsUnderCallToArms(world, creature)
        || (creature.DroppedOnARoom && now - creature.PickupTick < DroppedOnARoomGraceTicks);

    /// <summary>The health a tired creature that is not asleep loses a second:
    /// <c>CannotSleepModifyCreatureHealthPerSecond</c> ÷ variable 19 a turn (a truncating
    /// <c>IDIV</c>, <c>0x00477E74</c>), so four times that a second. Zero in every shipped level.</summary>
    /// <remarks>DK2 takes it inside the needs routine: keepers' non-workers only, not in a
    /// group-7 state, while the sleep latch <c>+0x58</c> is set and the category through a
    /// walk is not Sleeping. Ours reads the latch as <see cref="IsTired"/>, wounds aside.</remarks>
    public static int CannotSleepHealthPerSecond(
        GameDatabase database, CreatureDef definition, CreatureState creature)
    {
        if (!IsAsked(definition, creature) || !IsTired(definition, creature)) return 0;
        if (CreatureStateMachine.HasState(creature)
            && (CreatureStateTable.Get(creature.Dk2StateId).Group == NeedGroup
                || ResolvedCategory(creature) == CreatureStateCategory.Sleeping)) return 0;
        int perTurn = database[VariableTypeId.CannotSleepModifyCreatureHealthPerSecond]
                      / SimulationTiming.OriginalTicksPerGameSecond;
        return perTurn * SimulationTiming.OriginalTicksPerGameSecond;
    }

    /// <summary>Whether the needs routine runs for this creature at all: the dispatcher skips
    /// workers (definition flag bit 0) and the hero and neutral players' creatures.</summary>
    public static bool IsAsked(CreatureDef definition, CreatureState creature) =>
        (definition.Flags & CreatureFlags.IsWorker) == 0 && PlayerIds.IsKeeper(creature.OwnerPlayerId);

    /// <summary>The sleep latch: <c>TimeSleep</c> (<c>+0x5D6</c>) non-zero and <c>TimeAwake</c>
    /// (<c>+0x5D2</c>) passed since the creature last woke.</summary>
    public static bool IsTired(CreatureDef definition, CreatureState creature) =>
        definition.TimeSleep != 0 && definition.TimeAwake < CreatureHealthRules.Never
        && creature.AwakeTicks >= SimulationTiming.TicksFromSeconds(definition.TimeAwake);

    /// <summary>The category DK2 judges a creature by: its state's, or through a walk the state
    /// the walk resumes (<c>+0xDF</c>).</summary>
    public static CreatureStateCategory ResolvedCategory(CreatureState creature) =>
        CreatureStateTable.Get(ResolvedStateId(creature)).Category;

    /// <summary>The state behind <see cref="ResolvedCategory"/>: the current one, or through a
    /// walk the one it resumes (<c>0x00478050</c> reads <c>+0xDF</c> when the category is 2).</summary>
    public static int ResolvedStateId(CreatureState creature)
    {
        ref readonly CreatureStateDef current = ref CreatureStateTable.Get(creature.Dk2StateId);
        return current.IsUtility ? creature.UtilityNewState : creature.Dk2StateId;
    }

    /// <summary>Whether the hunger clock runs: not in a state flagged
    /// <see cref="CreatureStateFlags.SuppressHungerClock"/>, the Casino's. The clock's time
    /// still passes, so a helping overdue on leaving lands at once.</summary>
    public static bool HungerClockRuns(CreatureState creature) =>
        !Flagged(creature, CreatureStateFlags.SuppressHungerClock);

    /// <summary>Whether tiredness can be felt: not in a state flagged
    /// <see cref="CreatureStateFlags.SuppressTirednessClock"/>, the Casino's.</summary>
    public static bool TirednessClockRuns(CreatureState creature) =>
        !Flagged(creature, CreatureStateFlags.SuppressTirednessClock);

    /// <summary>A need has taken the creature off its job: no job and no job tile. The need's own
    /// route, already set, is kept.</summary>
    public static void EndJob(CreatureState creature)
    {
        creature.CurrentJob = JobType.None;
        creature.JobTileIndex = -1;
        creature.TortureTargetTile = -1;
        creature.WhipTurnsLeft = 0;
    }

    /// <summary>Whether the creature is at a job or on its way to one, the case a need
    /// interrupts rather than starts from idle.</summary>
    public static bool IsAtAJob(CreatureState creature) =>
        creature.Activity is CreatureActivity.AtWork or CreatureActivity.GoingToJob;

    private static bool Flagged(CreatureState creature, CreatureStateFlags flag) =>
        CreatureStateMachine.HasState(creature)
        && (CreatureStateTable.Get(creature.Dk2StateId).Flags & flag) != 0;
}
