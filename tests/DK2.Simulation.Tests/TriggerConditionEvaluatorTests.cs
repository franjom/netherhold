// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Simulation.Tests;

public class TriggerConditionEvaluatorTests
{
    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(5, false)]
    [InlineData(6, true)]
    public void FlagConditionsUseAllSixSignedComparisonCodes(byte comparison, bool expected)
    {
        var state = new TriggerState();
        state.Write(37, FlagWriteMode.Assign, 1);
        TriggerRecord record = Condition(1, new Dictionary<string, long>
        {
            ["Comparison"] = comparison,
            ["FlagId"] = 37,
            ["Flag"] = 1,
            ["IndirectFlagId"] = 99,
            ["Value"] = 2,
        });

        Assert.Equal(
            expected ? TriggerConditionResult.Satisfied : TriggerConditionResult.Unsatisfied,
            TriggerConditionEvaluator.Evaluate(record, state, currentTick: 0));
    }

    [Fact]
    public void FlagConditionCanCompareTheFlagInByteOneAgainstTheFlagInByteThree()
    {
        var state = new TriggerState();
        state.Write(3, FlagWriteMode.Assign, 9);
        state.Write(4, FlagWriteMode.Assign, 5);
        TriggerRecord record = Condition(1, new Dictionary<string, long>
        {
            ["Comparison"] = 4,
            ["FlagId"] = 3,
            ["Flag"] = 0,
            ["IndirectFlagId"] = 4,
            ["Value"] = 100,
        });

        Assert.Equal(
            TriggerConditionResult.Satisfied,
            TriggerConditionEvaluator.Evaluate(record, state, currentTick: 0));

        state.Write(3, FlagWriteMode.Assign, 1);
        Assert.Equal(
            TriggerConditionResult.Unsatisfied,
            TriggerConditionEvaluator.Evaluate(record, state, currentTick: 0));
    }

    [Fact]
    public void TimerThresholdUsesAuthoredSecondsAndDoesNotFireOneTickEarly()
    {
        var state = new TriggerState();
        state.StartTimer(7, now: 10);
        TriggerRecord record = Condition(2, new Dictionary<string, long>
        {
            ["Comparison"] = 5,
            ["TimerId"] = 7,
            ["Flag"] = 1,
            ["IndirectTimerId"] = 0,
            ["Value"] = 2,
        });

        Assert.Equal(
            TriggerConditionResult.Unsatisfied,
            TriggerConditionEvaluator.Evaluate(record, state, currentTick: 17));
        Assert.Equal(
            TriggerConditionResult.Satisfied,
            TriggerConditionEvaluator.Evaluate(record, state, currentTick: 18));
    }

    [Fact]
    public void HandlerBackedIndirectTimerModeComparesSecondStampAgainstNamedStamp()
    {
        var state = new TriggerState();
        state.StartTimer(2, now: 10);
        state.StartTimer(3, now: 8);
        TriggerRecord record = Condition(2, new Dictionary<string, long>
        {
            ["Comparison"] = 1,
            ["TimerId"] = 2,
            ["Flag"] = 0,
            ["IndirectTimerId"] = 3,
            ["Value"] = 100,
        });

        Assert.Equal(
            TriggerConditionResult.Satisfied,
            TriggerConditionEvaluator.Evaluate(record, state, currentTick: 1000));

        state.StartTimer(3, now: 12);
        Assert.Equal(
            TriggerConditionResult.Unsatisfied,
            TriggerConditionEvaluator.Evaluate(record, state, currentTick: 1000));
    }

    [Fact]
    public void FlagConditionCanReadTheOpaqueSpecialValue()
    {
        var state = new TriggerState { SpecialValue = -7 };
        TriggerRecord record = Condition(1, new Dictionary<string, long>
        {
            ["Comparison"] = 3,
            ["FlagId"] = TriggerState.SpecialFlagId,
            ["Flag"] = 1,
            ["IndirectFlagId"] = 0,
            ["Value"] = -7,
        });

        Assert.Equal(
            TriggerConditionResult.Satisfied,
            TriggerConditionEvaluator.Evaluate(record, state, currentTick: 0));
    }

    [Fact]
    public void AnUnstartedTimerConditionIsFalseEvenForNotEqual()
    {
        var state = new TriggerState();
        TriggerRecord record = Condition(2, new Dictionary<string, long>
        {
            ["Comparison"] = 6,
            ["TimerId"] = 7,
            ["Flag"] = 1,
            ["IndirectTimerId"] = 0,
            ["Value"] = 2,
        });

        Assert.Equal(
            TriggerConditionResult.Unsatisfied,
            TriggerConditionEvaluator.Evaluate(record, state, currentTick: 100));
    }

    [Fact]
    public void UnsupportedAndUnknownSpecialTimerConditionsAreNotFalse()
    {
        var state = new TriggerState();
        TriggerRecord unsupported = Condition(3, new Dictionary<string, long>());
        TriggerRecord specialTimer = Condition(2, new Dictionary<string, long>
        {
            ["Comparison"] = 5,
            ["TimerId"] = TriggerState.SpecialTimerId,
            ["Flag"] = 1,
            ["IndirectTimerId"] = 0,
            ["Value"] = 1,
        });

        Assert.Equal(
            TriggerConditionResult.NotEvaluated,
            TriggerConditionEvaluator.Evaluate(unsupported, state, currentTick: 0));
        Assert.Equal(
            TriggerConditionResult.NotEvaluated,
            TriggerConditionEvaluator.Evaluate(specialTimer, state, currentTick: 0));
    }

    private static TriggerRecord Condition(
        byte type,
        IReadOnlyDictionary<string, long> parameters) =>
        new GenericTriggerRecord(new byte[16])
        {
            Body = new byte[8],
            Parameters = parameters,
            Type = type,
        };
}
