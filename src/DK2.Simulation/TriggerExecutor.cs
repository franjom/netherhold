// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

public readonly record struct BlockedTrigger(int Tag, byte Type, ushort TriggerId);

public sealed class TriggerRunResult
{
    internal TriggerRunResult(
        IEnumerable<BlockedTrigger> blocked, IEnumerable<BlockedTrigger> partial)
    {
        Blocked = Order(blocked);
        Partial = Order(partial);
    }

    /// <summary>Records the executor declined outright: it ran nothing and did not
    /// descend, where the retail engine would have done both.</summary>
    public IReadOnlyList<BlockedTrigger> Blocked { get; }

    /// <summary>Records the executor ran <b>in part</b>: enough of the original's effect
    /// to descend honestly, but not all of it. A partial record is not a blocked one —
    /// the walk continues past it exactly as the original's would — and it is not a
    /// finished one either, so it is counted separately rather than disappearing into
    /// success. Action type 14 is the only member: it arms the party's subtrees and does
    /// not create the party's creatures.</summary>
    public IReadOnlyList<BlockedTrigger> Partial { get; }

    public IEnumerable<(int Tag, byte Type, int Count)> Summary => Census(Blocked);

    public IEnumerable<(int Tag, byte Type, int Count)> PartialSummary => Census(Partial);

    private static BlockedTrigger[] Order(IEnumerable<BlockedTrigger> records) => records
        .OrderBy(b => b.Tag).ThenBy(b => b.Type).ThenBy(b => b.TriggerId).ToArray();

    private static IEnumerable<(int Tag, byte Type, int Count)> Census(
        IReadOnlyList<BlockedTrigger> records) => records
        .GroupBy(b => (b.Tag, b.Type))
        .Select(g => (g.Key.Tag, g.Key.Type, g.Count()));
}

/// <summary>One member slot of a party: the subtree it arms, and whether a creature
/// occupies the slot at all.</summary>
/// <remarks>
/// <para>Both, because DK2 reads them at different times: the load sweep disables every
/// non-zero id ungated (<c>0x0050C29F</c>), the runtime arm skips a slot whose creature id
/// byte is zero (<c>0x0051A1CF</c>). So an empty slot is disabled and never re-enabled.
/// <b>No shipped record distinguishes the two</b> — every member with a trigger id has a
/// creature id (<c>dk2tools kwd parties</c>) — so the gate is here because the executable
/// has it, not because the corpus forces it. trigger-tree.md, "The 24 slots".</para>
/// </remarks>
public readonly record struct TriggerPartyMember(ushort TriggerId, bool HasCreature);

/// <summary>One of the world's twenty-four party slots at <c>+0x4030</c>: the party's own
/// subtree and its sixteen members'. See <see cref="TriggerTree.Parties"/>.</summary>
public sealed record TriggerPartySlot(
    ushort TriggerId, IReadOnlyList<TriggerPartyMember> Members)
{
    /// <summary>A slot no shipped party occupies. Its ids are zero, which every walk in
    /// this file treats as the end of a chain, so an empty slot costs nothing.</summary>
    public static TriggerPartySlot Empty { get; } = new(0, []);
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

    /// <summary>Slots in the world's party array at <c>+0x4030</c>: twenty-four, counted
    /// off the level initialiser's own loop bound (<c>0x0050C28F</c>, <c>0x18</c>) at a
    /// stride of <c>0x208</c>.</summary>
    public const int PartySlotCount = 24;

    public TriggerTree(
        IEnumerable<TriggerRecord> records,
        ushort rootTriggerId,
        IEnumerable<HeroPartyThing>? parties = null)
    {
        Records = records?.ToArray() ?? throw new ArgumentNullException(nameof(records));
        RootTriggerId = rootTriggerId;
        Parties = BuildParties(parties);

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

    /// <summary>The level's party table, always <see cref="PartySlotCount"/> long and
    /// padded with <see cref="TriggerPartySlot.Empty"/>. <b>Twenty-four of the world's
    /// twenty-five trigger roots live here</b>, which is why a tree carries it: the world
    /// frame walks each occupied slot's id every turn, so a table left empty silently
    /// drops most of a level's script.</summary>
    /// <remarks>Slots are filled by <see cref="HeroPartyThing.PartyId"/>, because that is
    /// what selects one (<c>0x0051A16C</c>). <b>Filling them in file order would be
    /// indistinguishable</b> — campaign-wide, no party's id differs from its position and
    /// none repeats — so this follows the consumer's arithmetic, not the data's
    /// permission. trigger-tree.md, "The 24 slots".</remarks>
    public IReadOnlyList<TriggerPartySlot> Parties { get; }

    internal TriggerRecord? FirstCondition { get; }
    internal TriggerRecord? FirstAction { get; }

    private static TriggerPartySlot[] BuildParties(IEnumerable<HeroPartyThing>? parties)
    {
        var slots = new TriggerPartySlot[PartySlotCount];
        var taken = new bool[PartySlotCount];
        Array.Fill(slots, TriggerPartySlot.Empty);

        foreach (HeroPartyThing party in parties ?? [])
        {
            // DK2 masks the action's party byte to eight bits and indexes with no bound
            // check at all (0x0051A16C), so ids 24..255 would run off the array into the
            // world's next fields. Shipped ids reach 19. Refuse rather than model where
            // an overrun would land.
            if (party.PartyId >= PartySlotCount)
                throw new ArgumentOutOfRangeException(
                    nameof(parties), party.PartyId,
                    $"Party id {party.PartyId} is past the {PartySlotCount} world slots. In "
                    + "DK2 action type 14 would index past the array; no shipped level does it.");

            if (taken[party.PartyId])
                throw new ArgumentException(
                    $"Two parties claim slot {party.PartyId}; no shipped level does.",
                    nameof(parties));

            taken[party.PartyId] = true;
            slots[party.PartyId] = new TriggerPartySlot(
                party.TriggerId,
                party.Members
                    .Select(m => new TriggerPartyMember(m.TriggerId, m.CreatureId != 0))
                    .ToArray());
        }

        return slots;
    }

    internal TriggerRecord? Resolve(short id)
    {
        if (id == 0) return null;
        ushort raw = unchecked((ushort)id);
        if (id > 0) return _conditions.GetValueOrDefault(raw) ?? FirstCondition;
        return _actions.GetValueOrDefault(raw) ?? FirstAction;
    }
}

/// <summary>Walks the world's twenty-five trigger roots once a game turn, executing flag
/// (action type 7), timer (type 8) and party (type 14) actions; unsupported records block
/// their child chain and are censused in <see cref="TriggerRunResult.Summary"/>.</summary>
/// <remarks>
/// <para>Rule: a record is evaluated only while its active bit is set; a child chain is
/// entered only after the record succeeds; the next sibling is visited either way. A
/// condition passes its own repeat count to its children, an action passes the inherited
/// value through, and inherited value 1 clears a successful action's active bit.
/// STRONGLY INFERRED — trigger-tree.md "The walk", "Repeat and active state".</para>
/// <para>Not implemented: every action type but 7, 8 and 14, and the creature-creation
/// half of 14 — see <see cref="TriggerRunResult.Partial"/>, which counts what a run left
/// half-done rather than letting it pass as finished.</para></remarks>
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
        DisablePartySubtrees();
    }

    internal TriggerExecutor(TriggerState state) => _state = state;

    public TriggerTree? Tree => _tree;

    /// <summary>Runs one game turn of the world tree — <b>all twenty-five roots of it</b>.
    /// </summary>
    /// <remarks>
    /// <para>Rule: the id at <c>+0x7310</c> is walked with the repeat byte at
    /// <c>+0x731A</c>, and then each of the twenty-four party slots' non-zero ids is
    /// walked <b>with repeat argument 1</b> (<c>0x0050A760</c>: <c>LEA EDI,[ESI+0x4030]</c>,
    /// <c>MOV EBP,0x18</c>, stride <c>0x208</c>). Repeat 1 is the value that retires a
    /// successful action, so every action directly under a party root is one-shot.</para>
    /// <para>The party roots are not an optional extra: without them a level arms its
    /// party subtrees through action type 14 and then never traverses them, so their
    /// contents can never run. STRONGLY INFERRED — trigger-tree.md "The walk".</para>
    /// </remarks>
    public TriggerRunResult Run(int currentTick)
    {
        var blocked = new List<BlockedTrigger>();
        var partial = new List<BlockedTrigger>();
        if (_tree is null) return new TriggerRunResult(blocked, partial);

        Walk(unchecked((short)_tree.RootTriggerId), _tree.RootRepeat, currentTick, blocked, partial);
        foreach (TriggerPartySlot slot in _tree.Parties)
        {
            if (slot.TriggerId == 0) continue;
            Walk(unchecked((short)slot.TriggerId), 1, currentTick, blocked, partial);
        }

        return new TriggerRunResult(blocked, partial);
    }

    /// <summary>Sets or clears the active bit of <paramref name="triggerId"/> <b>and of
    /// every record its chain reaches</b> — DK2's <c>FUN_00519A20</c>, which is the only
    /// caller either active-bit setter has.</summary>
    /// <remarks>
    /// <para>Rule: recurse into <c>ChildTriggerId</c> <b>only for a condition</b>, into
    /// <c>NextTriggerId</c> either way, then apply the flag. Enabling a condition writes
    /// <b>1</b> to its repeat byte and disabling writes 0 (<c>0x00508660</c>); an action's
    /// repeat byte is untouched in both directions (<c>0x00508840</c>).</para>
    /// <para><b>An action's children are not reached</b>, so a subtree under an action
    /// keeps whatever state it had — the quirk that decides how much of a party subtree
    /// comes alive. No visited set, as DK2 has none. trigger-tree.md, "Re-arming".</para>
    /// </remarks>
    public void SetActive(short triggerId, bool active)
    {
        if (triggerId == 0)
            throw new ArgumentOutOfRangeException(
                nameof(triggerId), triggerId, "Trigger id zero has no record.");
        SetSubtreeActive(triggerId, active);
    }

    private void SetSubtreeActive(ushort triggerId, bool active) =>
        SetSubtreeActive(unchecked((short)triggerId), active);

    private void SetSubtreeActive(short id, bool active)
    {
        if (id == 0) return;
        TriggerRecord record = _tree!.Resolve(id)
            ?? throw new InvalidDataException($"No tag {(id > 0 ? 213 : 214)} trigger records exist.");

        if (record.Tag == 213)
            SetSubtreeActive(unchecked((short)record.ChildTriggerId), active);
        SetSubtreeActive(unchecked((short)record.NextTriggerId), active);

        _state.SetRecordActive(
            record, active, record.Tag == 213 ? (active ? (byte)1 : (byte)0) : null);
    }

    /// <summary>The level initialiser's opening move: every party subtree starts
    /// <b>disabled</b>.</summary>
    /// <remarks>
    /// <para>Rule: per slot, disable the sixteen member subtrees and then the party's own
    /// (<c>0x0050C289</c>–<c>0x0050C2D9</c>), <b>gating on nothing but a non-zero id</b>.
    /// Without it a party root would run from turn 1 and every level would open by
    /// executing script only action type 14 is supposed to reach.</para>
    /// <para><b>UNKNOWN, reproduced as always-on:</b> the sweep is skipped when bit
    /// <c>0x40</c> of <c>+0x7239</c> is set (<c>0x0050C280</c>) and no writer is traced
    /// (Q241). Every path we have runs it.</para>
    /// </remarks>
    private void DisablePartySubtrees()
    {
        foreach (TriggerPartySlot slot in _tree!.Parties)
        {
            foreach (TriggerPartyMember member in slot.Members)
                SetSubtreeActive(member.TriggerId, active: false);
            SetSubtreeActive(slot.TriggerId, active: false);
        }
    }

    private void Walk(
        short id,
        byte inheritedRepeat,
        int currentTick,
        List<BlockedTrigger> blocked,
        List<BlockedTrigger> partial)
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
                else if (ExecuteAction(record, currentTick, partial))
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
                Walk(unchecked((short)record.ChildTriggerId), childRepeat, currentTick, blocked, partial);
            id = unchecked((short)record.NextTriggerId);
        }
    }

    private bool ExecuteAction(
        TriggerRecord record, int currentTick, List<BlockedTrigger> partial)
    {
        IReadOnlyDictionary<string, long> p = record.Parameters;
        if (record.Type == 8)
        {
            _state.StartTimer(checked((byte)p["TimerId"]), currentTick);
            return true;
        }
        if (record.Type == 14) return CreateParty(record, p, partial);
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

    /// <summary>Action type 14 — <b>the arming half only</b>. Creates no creatures.</summary>
    /// <remarks>
    /// <para>Rule, in <c>FUN_00519F90</c>'s order: arm slot <c>PartyId</c>'s own subtree
    /// (<c>0x0051A197</c>), then per member slot skip a zero creature id
    /// (<c>0x0051A1CF</c>) or else create the creature and arm that member's subtree
    /// (<c>0x0051A58F</c>). trigger-tree.md, "Action type 14 creates a party" — including
    /// the <c>PartyType</c> branch that can arm nothing and still succeed (Q240).</para>
    /// <para><b>Why it still descends.</b> The creature-creation block is missing, so this
    /// reports through <see cref="TriggerRunResult.Partial"/> rather than passing as
    /// success — but returns true, because the retail engine descends and blocking would
    /// cut off the subtree our own arming just enabled.</para>
    /// </remarks>
    private bool CreateParty(
        TriggerRecord record, IReadOnlyDictionary<string, long> p, List<BlockedTrigger> partial)
    {
        int partyId = checked((byte)p["PartyId"]);
        if (partyId >= TriggerTree.PartySlotCount)
            throw new InvalidDataException(
                $"Action {record.TriggerId} names party {partyId}, past the "
                + $"{TriggerTree.PartySlotCount} world slots. DK2 masks the byte and indexes "
                + "without a bound check; no shipped level does it.");

        TriggerPartySlot slot = _tree!.Parties[partyId];
        SetSubtreeActive(slot.TriggerId, active: true);
        foreach (TriggerPartyMember member in slot.Members)
        {
            if (!member.HasCreature) continue;
            SetSubtreeActive(member.TriggerId, active: true);
        }

        partial.Add(new BlockedTrigger(record.Tag, record.Type, record.TriggerId));
        return true;
    }
}
