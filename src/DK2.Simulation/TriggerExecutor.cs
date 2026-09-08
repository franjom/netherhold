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
        // DK2 copies the resolved root condition's own repeat byte here.
        // Resolve also preserves its first-condition fallback for a missing id.
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

/// <summary>
/// Walks one world trigger tree and executes only established flag and timer actions.
/// Unsupported records block their child chain and are returned as a census.
/// </summary>
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
