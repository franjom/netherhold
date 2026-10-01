// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Simulation.Tests;

/// <summary>Puts a creature straight into a state for a test's setup. Not a move: the rule
/// is not asked. It writes what entering the state would: the id, the saved caller for a
/// travel state, and otherwise the hook countdown an entered category seeds. A room place
/// is released for a category that claims none, and never claimed (ADR-0309).</summary>
internal static class StatePlacement
{
    public static void Place(CreatureState creature, int stateId)
    {
        ref readonly CreatureStateDef state = ref CreatureStateTable.Get(stateId);
        if (state.IsUtility) creature.StateBeforeUtilityState = creature.Dk2StateId;
        else creature.HookCountdown = CreatureStateMachine.HookCountdownSeed;
        if (!state.IsUtility && !RoomCapacityRules.Claims(state.Category)) creature.HeldPlace = RoomPlace.None;
        creature.Dk2StateId = stateId;
    }

    /// <summary>A state that names <paramref name="activity"/> (<see cref="ActivityRules"/>),
    /// for a setup that wants the activity and not a particular state. At work is the
    /// creature's job's state, so the job is set first.</summary>
    public static void As(CreatureState creature, CreatureActivity activity)
    {
        switch (activity)
        {
            case CreatureActivity.Dead: CreatureStateMachine.Release(creature); break;
            case CreatureActivity.AtWork:
                Place(creature, JobRules.StateFor(creature.CurrentJob)
                    ?? throw new InvalidOperationException($"{creature.CurrentJob} has no state; set the job first."));
                break;
            case CreatureActivity.Withdrawing: Walk(creature, 202); break;
            case CreatureActivity.DeliveringGold: Walk(creature, ImpStates.AtTheStore); break;
            default: Place(creature, Standing(activity)); break;
        }

        Assert.Equal(activity, ActivityRules.Of(creature));
    }

    private static void Walk(CreatureState creature, int resume)
    {
        Place(creature, 6);
        creature.UtilityNewState = resume;
    }

    private static int Standing(CreatureActivity activity) => activity switch
    {
        CreatureActivity.Idle => CreatureStateMachine.Idle,
        CreatureActivity.Working => 14,
        CreatureActivity.Sleeping => LairRules.SleepingState,
        CreatureActivity.Imprisoned => PrisonRules.InTheCellState,
        CreatureActivity.BeingTortured => TortureRules.TorturedState,
        CreatureActivity.Leaving => LeavingRules.ChoosingState,
        CreatureActivity.Fighting => 199,
        CreatureActivity.Reporting => 254,
        CreatureActivity.Frozen => 218,
        CreatureActivity.Stunned => 255,
        CreatureActivity.Celebrating => 260,
        _ => throw new ArgumentOutOfRangeException(nameof(activity), activity, "No placement for it yet."),
    };
}
