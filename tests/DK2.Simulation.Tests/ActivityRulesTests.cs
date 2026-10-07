// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Simulation.Tests;

/// <summary>The activity a state names (ADR-0210). The rows are the pairs the whole suite
/// was seen to hold at the end of a tick; the ambiguous ones are the reason for the extra
/// inputs.</summary>
public sealed class ActivityRulesTests
{
    [Theory]
    [InlineData(1, 0, CreatureActivity.Idle)]
    [InlineData(10, 1, CreatureActivity.Idle)]
    [InlineData(6, 13, CreatureActivity.MovingToJob)]
    [InlineData(14, 1, CreatureActivity.Working)]
    [InlineData(6, 27, CreatureActivity.FetchingGold)]
    [InlineData(6, 28, CreatureActivity.DeliveringGold)]
    [InlineData(6, 33, CreatureActivity.CarryingObject)]
    [InlineData(6, 42, CreatureActivity.FetchingBody)]
    [InlineData(6, 45, CreatureActivity.CarryingBody)]
    [InlineData(7, 92, CreatureActivity.SeekingFood)]
    [InlineData(6, 93, CreatureActivity.FetchingChicken)]
    [InlineData(7, 80, CreatureActivity.SeekingLair)]
    [InlineData(6, 82, CreatureActivity.GoingToSleep)]
    [InlineData(83, 1, CreatureActivity.Sleeping)]
    [InlineData(6, 188, CreatureActivity.SeekingWages)]
    [InlineData(7, 108, CreatureActivity.GoingToJob)]
    [InlineData(108, 1, CreatureActivity.AtWork)]
    [InlineData(6, 128, CreatureActivity.AtWork)]
    [InlineData(6, 130, CreatureActivity.AtWork)]
    [InlineData(130, 1, CreatureActivity.BeingTortured)]
    [InlineData(6, 118, CreatureActivity.Imprisoned)]
    [InlineData(6, 134, CreatureActivity.Rescuing)]
    [InlineData(6, 195, CreatureActivity.Leaving)]
    [InlineData(217, 1, CreatureActivity.Dying)]
    [InlineData(199, 1, CreatureActivity.Fighting)]
    [InlineData(203, 1, CreatureActivity.Fleeing)]
    [InlineData(6, 202, CreatureActivity.Withdrawing)]
    [InlineData(6, 251, CreatureActivity.RaisingAlarm)]
    [InlineData(6, 254, CreatureActivity.Reporting)]
    [InlineData(218, 1, CreatureActivity.Frozen)]
    [InlineData(255, 1, CreatureActivity.Stunned)]
    [InlineData(6, 259, CreatureActivity.GoingToBody)]
    [InlineData(260, 1, CreatureActivity.Celebrating)]
    public void AStateNamesItsActivity(int state, int resume, CreatureActivity activity)
    {
        var creature = new CreatureState { Dk2StateId = state, UtilityNewState = resume };
        Assert.Equal(activity, ActivityRules.Of(creature));
    }

    [Fact]
    public void EightyOneIsTheSleepJobsWorkOrATiredCreaturesWalkToItsBed()
    {
        // DK2 has the one state for both; the job tells ours apart.
        var job = new CreatureState { Dk2StateId = 81, CurrentJob = JobType.Sleep };
        var need = new CreatureState { Dk2StateId = 81 };
        Assert.Equal(CreatureActivity.AtWork, ActivityRules.Of(job));
        Assert.Equal(CreatureActivity.GoingToSleep, ActivityRules.Of(need));

        job.Dk2StateId = need.Dk2StateId = 7;
        job.UtilityNewState = need.UtilityNewState = 81;
        Assert.Equal(CreatureActivity.GoingToJob, ActivityRules.Of(job));
        Assert.Equal(CreatureActivity.GoingToSleep, ActivityRules.Of(need));
    }

    [Fact]
    public void TheSlapsThreeStatesAreTheSlapsReaction_CaptiveOrNot()
    {
        // 77 is the slap's, not the hand drop's (76, ADR-0226); 67 and 120 are its Workshop and
        // Prison moves, and a captive in 120 is still a captive by its prison field (ADR-0401).
        var captive = new CreatureState { Dk2StateId = SlapRules.PrisonState, PrisonOwnerPlayerId = 4 };
        Assert.Equal(CreatureActivity.Slapped, ActivityRules.Of(captive));
        Assert.True(PrisonRules.IsImprisoned(captive));
        Assert.Equal(CreatureActivity.Slapped, ActivityRules.Of(new CreatureState { Dk2StateId = SlapRules.ReactionState }));
        Assert.Equal(CreatureActivity.Slapped, ActivityRules.Of(new CreatureState { Dk2StateId = SlapRules.WorkshopState }));
    }

    [Fact]
    public void ACorpseIsDeadAndAStateNoSystemEntersNamesNothing()
    {
        Assert.Equal(CreatureActivity.Dead, ActivityRules.Of(new CreatureState { Dk2StateId = CreatureStateMachine.NoState }));
        Assert.Null(ActivityRules.Of(new CreatureState { Dk2StateId = 0 }));
    }
}
