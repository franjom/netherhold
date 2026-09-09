// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

public readonly record struct BlockedTrigger(int Tag, byte Type, ushort TriggerId);

public sealed class TriggerRunResult
{
    internal TriggerRunResult(IEnumerable<BlockedTrigger> blocked) =>
        Blocked = blocked.OrderBy(b => b.Tag).ThenBy(b => b.Type).ThenBy(b => b.TriggerId).ToArray();

    public IReadOnlyList<BlockedTrigger> Blocked { get; }

    public IEnumerable<(int Tag, byte Type, int Count)> Summary => Blocked
        .GroupBy(b => (b.Tag, b.Type))
        .Select(g => (g.Key.Tag, g.Key.Type, g.Count()));
}

/// <summary>Immutable records and the root arguments used by the world walker.</summary>
/// <remarks>
/// <para>Rule: trigger ids are signed 16-bit. A positive id selects a tag 213
/// condition, a negative id a tag 214 action, zero ends a chain. A non-zero id
/// with no record falls back to the first record of that tag. No other tag loads.</para>
/// <para>Source: DKII.EXE 0x00518150 — STRONGLY INFERRED
/// (docs/research/trigger-tree.md "The walk"). The fallback is unreachable in
/// shipped data: all 9,161 records across the 66 shipped trigger files resolve
/// inside their own file.</para>
/// </remarks>
public sealed class TriggerTree
{
    private readonly Dictionary<ushort, TriggerRecord> _conditions = [];
    private readonly Dictionary<ushort, TriggerRecord> _actions = [];

    public TriggerTree(IEnumerable<TriggerRecord> records, ushort rootTriggerId)
    {
        Records = records?.ToArray() ?? throw new ArgumentNullException(nameof(records));
        RootTriggerId = rootTriggerId;

        foreach (TriggerRecord record in Records)
        {
            Dictionary<ushort, TriggerRecord> index = record.Tag switch
            {
                213 => _conditions,
                214 => _actions,
                _ => throw new ArgumentException($"Unsupported trigger tag {record.Tag}.", nameof(records)),
            };
            if (!index.TryAdd(record.TriggerId, record))
                throw new ArgumentException(
                    $"Duplicate tag {record.Tag} trigger id {record.TriggerId}.", nameof(records));
        }

        FirstCondition = Records.FirstOrDefault(r => r.Tag == 213);
        FirstAction = Records.FirstOrDefault(r => r.Tag == 214);
        // The level initialiser copies the resolved root record's own RepeatCount
        // byte into world +0x731A — STRONGLY INFERRED (docs/research/trigger-state.md,
        // "+0x731A is the root trigger's repeat count"). Resolve keeps the binary's
        // first-record fallback here, which
        // TriggerExecutorTests.RootRepeatComesFromResolvedRootIncludingFirstRecordFallback pins.
        RootRepeat = Resolve(unchecked((short)rootTriggerId))?.RepeatCount ?? 0;
    }

    public IReadOnlyList<TriggerRecord> Records { get; }
    public ushort RootTriggerId { get; }
    public byte RootRepeat { get; }
    internal TriggerRecord? FirstCondition { get; }
    internal TriggerRecord? FirstAction { get; }

    internal TriggerRecord? Resolve(short id)
    {
        if (id == 0) return null;
        ushort raw = unchecked((ushort)id);
        if (id > 0) return _conditions.GetValueOrDefault(raw) ?? FirstCondition;
        return _actions.GetValueOrDefault(raw) ?? FirstAction;
    }
}

/// <summary>Walks one world trigger tree, executing only flag (action type 7) and timer
/// (type 8) actions; unsupported records block their child chain and are censused in
/// <see cref="TriggerRunResult.Summary"/>.</summary>
/// <remarks>
/// <para>Rule: a record is evaluated only while its active bit is set; a child chain is
/// entered only after the record succeeds; the next sibling is visited either way. A
/// condition passes its own repeat count to its children, an action passes the inherited
/// value through, and inherited value 1 clears a successful action's active bit.
/// STRONGLY INFERRED — trigger-tree.md "The walk", "Repeat and active state".</para>
/// <para>Not implemented: every action type but 7 and 8, including type 14, the in-game
/// re-arm that sets each condition in a subtree to repeat 1, making that subtree's actions
/// one-shot (259 records ship; level 1 uses one) — trigger-tree.md "Re-arming".</para></remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/trigger-tree.md")]
public sealed class TriggerExecutor
{
    private readonly TriggerTree? _tree;
    private readonly TriggerState _state;

    public TriggerExecutor(TriggerTree tree, TriggerState state)
    {
        _tree = tree ?? throw new ArgumentNullException(nameof(tree));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _state.InitializeRecords(tree.Records);
    }

    internal TriggerExecutor(TriggerState state) => _state = state;

    public TriggerTree? Tree => _tree;

    public TriggerRunResult Run(int currentTick)
    {
        var blocked = new List<BlockedTrigger>();
        if (_tree is null) return new TriggerRunResult(blocked);
        Walk(unchecked((short)_tree.RootTriggerId), _tree.RootRepeat, currentTick, blocked);
        return new TriggerRunResult(blocked);
    }

    /// <summary>
    /// External active-bit path: disabling zeros repeat; re-arming a condition sets it to one.
    /// </summary>
    public void SetActive(short triggerId, bool active)
    {
        TriggerRecord record = (_tree?.Resolve(triggerId))
            ?? throw new ArgumentOutOfRangeException(nameof(triggerId), triggerId, "Trigger id zero has no record.");
        byte? repeat = active && record.Tag == 213 ? (byte)1 : !active ? (byte)0 : null;
        _state.SetRecordActive(record, active, repeat);
    }

    private void Walk(short id, byte inheritedRepeat, int currentTick, List<BlockedTrigger> blocked)
    {
        while (id != 0)
        {
            TriggerRecord record = _tree!.Resolve(id)
                ?? throw new InvalidDataException($"No tag {(id > 0 ? 213 : 214)} trigger records exist.");
            bool descend = false;
            byte childRepeat = inheritedRepeat;

            if (_state.IsRecordActive(record))
            {
                if (record.Tag == 213)
                {
                    TriggerConditionResult result =
                        TriggerConditionEvaluator.Evaluate(record, _state, currentTick);
                    if (result == TriggerConditionResult.NotEvaluated)
                        blocked.Add(new BlockedTrigger(record.Tag, record.Type, record.TriggerId));
                    else if (result == TriggerConditionResult.Satisfied)
                    {
                        descend = true;
                        childRepeat = _state.RecordRepeat(record);
                    }
                }
                else if (ExecuteAction(record, currentTick))
                {
                    descend = true;
                    if (inheritedRepeat == 1)
                        _state.SetRecordActive(record, active: false, repeat: null);
                }
                else
                {
                    blocked.Add(new BlockedTrigger(record.Tag, record.Type, record.TriggerId));
                }
            }

            if (descend)
                Walk(unchecked((short)record.ChildTriggerId), childRepeat, currentTick, blocked);
            id = unchecked((short)record.NextTriggerId);
        }
    }

    private bool ExecuteAction(TriggerRecord record, int currentTick)
    {
        IReadOnlyDictionary<string, long> p = record.Parameters;
        if (record.Type == 8)
        {
            _state.StartTimer(checked((byte)p["TimerId"]), currentTick);
            return true;
        }
        if (record.Type != 7) return false;

        byte modeByte = checked((byte)p["Flag"]);
        int operand = unchecked((int)(uint)p["Value"]);
        if ((modeByte & 0x02) == 0)
            operand = _state.Read(unchecked((byte)operand));

        FlagWriteMode mode = (modeByte & 0x08) != 0
            ? FlagWriteMode.Assign
            : (modeByte & 0x10) != 0 ? FlagWriteMode.Add : FlagWriteMode.Subtract;
        _state.Write(checked((byte)p["FlagId"]), mode, operand);
        return true;
    }
}
