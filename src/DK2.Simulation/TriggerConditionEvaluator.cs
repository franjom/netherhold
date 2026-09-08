// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>The outcome of attempting to evaluate one condition record.</summary>
public enum TriggerConditionResult
{
    /// <summary>The record is outside the evaluator's established behaviour.</summary>
    NotEvaluated,

    /// <summary>The condition was evaluated and did not hold.</summary>
    Unsatisfied,

    /// <summary>The condition was evaluated and held.</summary>
    Satisfied,
}

/// <summary>
/// Evaluates one tag-213 condition without walking or mutating a trigger tree.
/// Types 1 and 2 follow the parameter reads established in trigger-state.md;
/// every other type remains distinguishable from a false condition.
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/trigger-state.md")]
public static class TriggerConditionEvaluator
{
    /// <summary>Evaluates <paramref name="record"/> against existing trigger state.</summary>
    public static TriggerConditionResult Evaluate(
        TriggerRecord record,
        TriggerState state,
        int currentTick)
    {
        if (record is null) throw new ArgumentNullException(nameof(record));
        if (state is null) throw new ArgumentNullException(nameof(state));

        if (record.Tag != 213) return TriggerConditionResult.NotEvaluated;

        return record.Type switch
        {
            1 => EvaluateFlag(record.Parameters, state),
            2 => EvaluateTimer(record.Parameters, state, currentTick),
            _ => TriggerConditionResult.NotEvaluated,
        };
    }

    private static TriggerConditionResult EvaluateFlag(
        IReadOnlyDictionary<string, long> parameters,
        TriggerState state)
    {
        int left = ReadFlag(state, Byte(parameters, "FlagId"));
        int right = (Byte(parameters, "Flag") & 1) != 0
            ? Int(parameters, "Value")
            : ReadFlag(state, Byte(parameters, "IndirectFlagId"));

        return Result(TriggerState.Compare(Byte(parameters, "Comparison"), left, right));
    }

    private static TriggerConditionResult EvaluateTimer(
        IReadOnlyDictionary<string, long> parameters,
        TriggerState state,
        int currentTick)
    {
        byte timerId = Byte(parameters, "TimerId");
        if (timerId == TriggerState.SpecialTimerId)
            return TriggerConditionResult.NotEvaluated;
        if (!state.IsStarted(timerId))
            return TriggerConditionResult.Unsatisfied;

        int operand;
        if ((Byte(parameters, "Flag") & 1) != 0)
        {
            operand = SimulationTiming.TicksFromSeconds(Int(parameters, "Value"));
        }
        else
        {
            byte indirectId = Byte(parameters, "IndirectTimerId");
            if (indirectId == TriggerState.SpecialTimerId)
                return TriggerConditionResult.NotEvaluated;
            operand = state.ReadTimer(indirectId);
        }

        int deadline = unchecked(state.ReadTimer(timerId) + operand);
        return Result(TriggerState.Compare(
            Byte(parameters, "Comparison"), currentTick, deadline));
    }

    private static int ReadFlag(TriggerState state, byte id) =>
        id == TriggerState.SpecialFlagId ? state.SpecialValue : state.Read(id);

    private static byte Byte(IReadOnlyDictionary<string, long> parameters, string name) =>
        checked((byte)parameters[name]);

    private static int Int(IReadOnlyDictionary<string, long> parameters, string name) =>
        checked((int)parameters[name]);

    private static TriggerConditionResult Result(bool value) =>
        value ? TriggerConditionResult.Satisfied : TriggerConditionResult.Unsatisfied;
}
