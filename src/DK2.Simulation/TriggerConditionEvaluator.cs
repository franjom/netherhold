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
/// Types 1, 2 and 34 follow the parameter reads established in trigger-state.md
/// and trigger-semantics.md; every other type remains distinguishable from a
/// false condition.
/// <para>Six of the world context's nine computed types are still missing — 35,
/// 43, 44, 45, 64 and 67 — and the other 57 types the corpus ships fail in the
/// retail engine too, at the default arm <c>0x00518929</c>, so declining them is
/// not a gap (trigger-semantics.md, "The world context computes only nine
/// condition types").</para>
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

        // Type 46 is false, not unknown. The world dispatcher's byte table gives it its
        // own slot, and that slot's jump entry is 0x00518941 -- the tail of the unhandled
        // path, *past* the printf. So DK2 returns zero for it deliberately and silently,
        // where every other type it does not implement prints "Unhandled Trigger When".
        // One shipped record uses it (level 1, in a player tree, reaching this by
        // delegation).
        return record.Type switch
        {
            1 => EvaluateFlag(record.Parameters, state),
            2 => EvaluateTimer(record.Parameters, state, currentTick),
            34 => EvaluateLevelTime(record.Parameters, currentTick),
            46 => TriggerConditionResult.Unsatisfied,
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

        if ((Byte(parameters, "Flag") & 1) != 0)
        {
            int threshold = SimulationTiming.TicksFromSeconds(Int(parameters, "Value"));
            int deadline = unchecked(state.ReadTimer(timerId) + threshold);
            return Result(TriggerState.Compare(
                Byte(parameters, "Comparison"), currentTick, deadline));
        }

        byte indirectId = Byte(parameters, "IndirectTimerId");
        if (indirectId == TriggerState.SpecialTimerId)
            return TriggerConditionResult.NotEvaluated;

        return Result(TriggerState.Compare(
            Byte(parameters, "Comparison"),
            state.ReadTimer(indirectId),
            state.ReadTimer(timerId)));
    }

    /// <summary>Type 34 — how long the level has been running, compared against an
    /// authored number of <b>seconds</b>.</summary>
    /// <remarks>
    /// <para>Rule: <c>Compare(byte 00, the current game turn, Value seconds)</c>. The arm
    /// at <c>0x0051829D</c> calls <c>FUN_00517490</c>, so the left side is the same
    /// counter the walker is driven by and there is no separate clock.</para>
    /// <para>The multiply is <b>ours to do and DK2's to have done already</b>: its loader
    /// scales the dword in place at <c>0x0050C30E</c>, ours keeps the authored seconds —
    /// the same split <see cref="EvaluateTimer"/> makes. STRONGLY INFERRED, and the
    /// seconds reading has thirteen live measurements behind it (trigger-semantics.md
    /// "Type 34 is level time"; trigger-state.md §5).</para>
    /// </remarks>
    private static TriggerConditionResult EvaluateLevelTime(
        IReadOnlyDictionary<string, long> parameters,
        int currentTick) =>
        Result(TriggerState.Compare(
            Byte(parameters, "Comparison"),
            currentTick,
            SimulationTiming.TicksFromSeconds(Int(parameters, "Value"))));

    private static int ReadFlag(TriggerState state, byte id) =>
        id == TriggerState.SpecialFlagId ? state.SpecialValue : state.Read(id);

    private static byte Byte(IReadOnlyDictionary<string, long> parameters, string name) =>
        checked((byte)parameters[name]);

    private static int Int(IReadOnlyDictionary<string, long> parameters, string name) =>
        checked((int)parameters[name]);

    private static TriggerConditionResult Result(bool value) =>
        value ? TriggerConditionResult.Satisfied : TriggerConditionResult.Unsatisfied;
}
