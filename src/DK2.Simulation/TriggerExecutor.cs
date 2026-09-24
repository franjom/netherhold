// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>One record an executor could not fully honour, and <b>the walker that
/// reached it</b>. The context is not decoration: a type is dispatched by the context's
/// own table first and only then delegated, so the same type number is a different arm —
/// and a different rule — depending on which tree it sits in. Ranking work by type alone
/// hides that.</summary>
public readonly record struct BlockedTrigger(
    int Tag, byte Type, ushort TriggerId, TriggerContext Context);

/// <summary>How many records of one tag, type and context an executor reported.</summary>
public readonly record struct TriggerCensusRow(
    int Tag, byte Type, TriggerContext Context, int Count);

public sealed class TriggerRunResult
{
    internal TriggerRunResult(
        IEnumerable<BlockedTrigger> blocked,
        IEnumerable<BlockedTrigger> partial,
        IEnumerable<BlockedTrigger> assumed,
        IEnumerable<BlockedTrigger> unhandled,
        HashSet<ushort>? reached = null,
        HashSet<ushort>? unsatisfied = null)
    {
        Blocked = Order(blocked);
        Partial = Order(partial);
        Assumed = Order(assumed);
        Unhandled = Order(unhandled);
        Reached = reached;
        Unsatisfied = unsatisfied;
    }

    /// <summary>Every record the walk resolved, when the executor was asked to track it.
    /// What a decline <b>cost</b> is the subtree behind it that this set does not already
    /// contain — see <see cref="TriggerReach"/>.</summary>
    public HashSet<ushort>? Reached { get; }

    /// <summary>Conditions reached that answered false, when reach was tracked.</summary>
    public HashSet<ushort>? Unsatisfied { get; }

    /// <summary>Records the executor declined outright: it ran nothing and did not
    /// descend, where the retail engine would have done both. Conditions only — an action
    /// is answered from its dispatcher's return value, so it is never declined.</summary>
    public IReadOnlyList<BlockedTrigger> Blocked { get; }

    /// <summary>Records the executor ran <b>in part</b>: the walk continues past them
    /// exactly as the original's would, but their effect on the world is missing or
    /// incomplete. Every action type this executor does not perform is here, because DK2's
    /// dispatcher succeeds at it and descending is the faithful answer — see
    /// <see cref="TriggerExecutor"/>. Types 1 and 14 are the incomplete cases rather than
    /// the missing ones: 14 arms a party's subtrees and creates no creatures, and 1 creates
    /// a creature without the flag byte the same arm writes onto it.</summary>
    public IReadOnlyList<BlockedTrigger> Partial { get; }

    /// <summary>Records <b>DK2's own dispatchers refuse</b> in the context they were
    /// reached in, printing <c>Unhandled Trigger When</c> and returning zero. Not a gap in
    /// this executor. Actions only, and 25 of the 51 world action types are in the set —
    /// but ten of those belong to the creature dispatcher and are refused only when reached
    /// inside a creature tree (<see cref="TriggerExecutor.CreatureOwnsAction"/>), which this executor does
    /// walk, so this is a statement about a context and not about a type.</summary>
    public IReadOnlyList<BlockedTrigger> Unhandled { get; }

    /// <summary>Records answered from a <b>modelling assumption</b> rather than from
    /// state we hold. Distinct from both of the above: the walk proceeded exactly as the
    /// original's would, but only because a value was supplied that this simulation cannot
    /// derive. Player-context condition type 72 is the only member — see
    /// <see cref="TriggerExecutor"/>.</summary>
    public IReadOnlyList<BlockedTrigger> Assumed { get; }

    public IEnumerable<TriggerCensusRow> Summary => Census(Blocked);

    public IEnumerable<TriggerCensusRow> PartialSummary => Census(Partial);

    public IEnumerable<TriggerCensusRow> AssumedSummary => Census(Assumed);

    public IEnumerable<TriggerCensusRow> UnhandledSummary => Census(Unhandled);

    private static BlockedTrigger[] Order(IEnumerable<BlockedTrigger> records) => records
        .OrderBy(b => b.Tag).ThenBy(b => b.Type).ThenBy(b => b.Context)
        .ThenBy(b => b.TriggerId).ToArray();

    private static IEnumerable<TriggerCensusRow> Census(
        IReadOnlyList<BlockedTrigger> records) => records
        .GroupBy(b => (b.Tag, b.Type, b.Context))
        .Select(g => new TriggerCensusRow(g.Key.Tag, g.Key.Type, g.Key.Context, g.Count()));
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

/// <summary>One player's tree root, <b>with the player it belongs to</b>. DK2's frame
/// walks a player's root with that player as the condition dispatcher's <c>this</c>
/// (<c>0x004B9F10</c>), so a root without its owner cannot answer a single one of the 30
/// player-owned condition types.</summary>
public readonly record struct TriggerPlayerRoot(byte PlayerId, ushort TriggerId);

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
        IEnumerable<HeroPartyThing>? parties = null,
        IEnumerable<TriggerPlayerRoot>? playerRoots = null)
    {
        Records = records?.ToArray() ?? throw new ArgumentNullException(nameof(records));
        RootTriggerId = rootTriggerId;
        Parties = BuildParties(parties);
        PlayerRoots = (playerRoots ?? []).Where(r => r.TriggerId != 0).ToArray();

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

    /// <summary>Each player's trigger root, zeros dropped. Walked last in the turn, one
    /// per player, by the <b>player</b> walker.</summary>
    /// <remarks>
    /// <para><b>UNKNOWN — the order.</b> DK2 walks the world's runtime player list
    /// (world <c>+0x7A</c>, link <c>+0x3A</c>), not the PLAYERS file, and nothing
    /// establishes that the two agree. This keeps file order. It is unobservable on
    /// campaign level 1, which has one root, and observable in principle on the <b>36 of
    /// 66</b> levels that have more than one (<c>dk2tools kwd roots</c>). It only matters
    /// where two player trees write the same flag on the same turn.</para>
    /// </remarks>
    public IReadOnlyList<TriggerPlayerRoot> PlayerRoots { get; }

    internal TriggerRecord? FirstCondition { get; }
    internal TriggerRecord? FirstAction { get; }

    /// <summary>Resolves an id in the <b>condition</b> array whatever its sign — DK2's
    /// <c>FUN_00518090</c>. The player frame uses it on the root id before checking that
    /// the id is positive, so an action id as a player root would take the first
    /// condition's repeat byte. No shipped level does that.</summary>
    internal TriggerRecord? ResolveCondition(ushort id) =>
        id == 0 ? null : _conditions.GetValueOrDefault(id) ?? FirstCondition;

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

/// <summary>Which walker reached a record, and therefore which dispatcher decides it.
/// </summary>
public enum TriggerContext
{
    /// <summary>The world tree and the party roots.</summary>
    World,

    /// <summary>One player's tree. Types the player dispatcher does not own are decided
    /// by the world dispatcher anyway — see <see cref="TriggerExecutor"/>.</summary>
    Player,

    /// <summary>One action point's tree. Its dispatcher owns seven condition types and
    /// <b>no action types at all</b> — <see cref="TriggerAreaConditions"/>.</summary>
    Area,

    /// <summary>One creature's tree. Conditions and actions delegate through the
    /// creature's owning player and then the world.</summary>
    Creature,
}

/// <summary>One action point's trigger root, with the repeat argument its frame passes.
/// </summary>
/// <remarks>DK2 caches the repeat on the point at <c>+0x36</c> when the root is attached,
/// copying the resolved record's own byte then (<c>0x004B0C90</c>), and the per-turn frame
/// only reads it (<c>0x004B05E9</c>). So it is a load-time snapshot: re-arming the root
/// later does not change what the frame passes. Resolved once here for the same reason.
/// </remarks>
internal readonly record struct TriggerAreaRoot(ActionPointState Point, byte Repeat);

/// <summary>One turn's accumulating result, so the walk carries one argument instead of
/// four.</summary>
internal sealed class TriggerRun(int currentTick)
{
    public int CurrentTick { get; } = currentTick;

    /// <summary>The action point whose tree is being walked, or null outside one. Every
    /// area-owned condition measures over this point's rectangle.</summary>
    public ActionPointState? Area { get; set; }

    /// <summary>The player whose tree is being walked, or null outside one. It is the
    /// player condition dispatcher's <c>this</c>, so every player-owned type needs it.
    /// </summary>
    public byte? Player { get; set; }

    /// <summary>The creature whose tree is being walked, or null outside one.</summary>
    public CreatureState? Creature { get; set; }

    /// <summary>The map's derived rooms, built only if a reached condition asks for them.
    /// One run sees one world state because trigger actions that alter terrain are not yet
    /// performed; when one is, it must invalidate this after the write.</summary>
    public IReadOnlyList<RoomInstance>? Rooms { get; set; }

    /// <summary>Every record the walk resolved this turn, or null when reach tracking is
    /// off. Off by default: the simulation runs this every turn and does not need it, and
    /// the set costs an allocation and a hash per record. `dk2tools` turns it on.</summary>
    public HashSet<ushort>? Reached { get; set; }

    /// <summary>Conditions the walk reached and that answered <b>false</b>, tracked
    /// alongside <see cref="Reached"/>. Their subtrees are the largest reason a shipped
    /// record never runs, and no dispatcher arm opens them — only a world in which
    /// something happens does.</summary>
    public HashSet<ushort>? Unsatisfied { get; set; }

    public List<BlockedTrigger> Blocked { get; } = [];
    public List<BlockedTrigger> Partial { get; } = [];
    public List<BlockedTrigger> Assumed { get; } = [];
    public List<BlockedTrigger> Unhandled { get; } = [];

    public TriggerRunResult Result() =>
        new(Blocked, Partial, Assumed, Unhandled, Reached, Unsatisfied);
}

/// <summary>Walks every level trigger root once a game turn — world, 24 parties, action
/// points, creatures and players — performing supported actions and accounting for the rest.</summary>
/// <remarks>
/// <para>Rule: a record runs only while its active bit is set; a child chain is entered
/// only on success; the next sibling is visited either way; a condition passes its own
/// repeat to its children, an action the inherited one, and inherited 1 retires a
/// successful action. The player walker is the same shape — trigger-walkers.md.</para>
/// <para>Gaps are counted, not hidden, and which list one lands in follows DK2's own
/// answer: <see cref="TriggerRunResult.Partial"/>, <see cref="TriggerRunResult.Blocked"/>,
/// <see cref="TriggerRunResult.Unhandled"/>, <see cref="TriggerRunResult.Assumed"/>
/// (ADR-0107).</para>
/// </remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/trigger-walkers.md")]
public sealed class TriggerExecutor
{
    private readonly TriggerTree? _tree;
    private readonly TriggerState _state;
    private readonly WorldState? _world;
    private readonly GameDatabase? _database;
    private readonly TriggerAreaRoot[] _areaRoots = [];

    /// <summary>Builds an executor over <paramref name="tree"/>.</summary>
    /// <param name="world">The world whose action points carry the area roots, and whose
    /// tiles the area conditions measure. Without it no action-point tree is walked —
    /// which is what every caller that has no map wants.</param>
    /// <param name="database">Terrain definitions, for the two area conditions that read
    /// a terrain flag.</param>
    /// <summary>Records every record each turn's walk resolved, for
    /// <see cref="TriggerReach"/>. Off by default — see <see cref="TriggerRun.Reached"/>.
    /// </summary>
    public bool TrackReach { get; set; }

    public TriggerExecutor(
        TriggerTree tree,
        TriggerState state,
        WorldState? world = null,
        GameDatabase? database = null)
    {
        _tree = tree ?? throw new ArgumentNullException(nameof(tree));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _world = world;
        _database = database;
        _state.InitializeRecords(tree.Records);
        DisablePartySubtrees();
        if (world is not null && database is not null)
        {
            _areaRoots = BuildAreaRoots(world);
            AttachCreatureRoots(world);
        }
    }

    internal TriggerExecutor(TriggerState state) => _state = state;

    public TriggerTree? Tree => _tree;

    /// <summary>Runs one game turn: the world tree's twenty-five roots, then entity roots,
    /// then every player's — the frame's own order (<c>0x0050A75B</c> onward).
    /// </summary>
    /// <remarks>
    /// <para>Rule: the id at <c>+0x7310</c> is walked with the repeat byte at
    /// <c>+0x731A</c>, then each of the twenty-four party slots' non-zero ids <b>with
    /// repeat argument 1</b> (<c>0x0050A760</c>, stride <c>0x208</c>) — the value that
    /// retires a successful action, so a party root's own actions are one-shot.</para>
    /// <para>The party roots are not an optional extra: without them a level arms its
    /// party subtrees through action type 14 and never traverses them. STRONGLY INFERRED
    /// — trigger-tree.md "The walk"; the other three walkers, trigger-walkers.md.</para>
    /// </remarks>
    public TriggerRunResult Run(int currentTick)
    {
        var run = new TriggerRun(currentTick)
        {
            Reached = TrackReach ? [] : null,
            Unsatisfied = TrackReach ? [] : null,
        };
        if (_tree is null) return run.Result();

        Walk(unchecked((short)_tree.RootTriggerId), _tree.RootRepeat, TriggerContext.World, run);
        foreach (TriggerPartySlot slot in _tree.Parties)
        {
            if (slot.TriggerId == 0) continue;
            Walk(unchecked((short)slot.TriggerId), 1, TriggerContext.World, run);
        }

        // Before the players and after the parties, because the frame walks the world's
        // eight entity lists there (0x0050A7D7). Action points and creatures share that
        // pass. Their relative order here remains ours (Q330), while each projected
        // class preserves THINGS order.
        foreach (TriggerAreaRoot root in _areaRoots)
        {
            run.Area = root.Point;
            Walk(unchecked((short)root.Point.TriggerId), root.Repeat, TriggerContext.Area, run);
        }
        run.Area = null;

        foreach (CreatureState creature in _world?.Creatures ?? [])
        {
            if (creature.TriggerId == 0) continue;
            run.Creature = creature;
            run.Player = creature.OwnerPlayerId;
            Walk(
                unchecked((short)creature.TriggerId), creature.TriggerRepeat,
                TriggerContext.Creature, run);
        }
        run.Creature = null;
        run.Player = null;

        // Last in the turn, and in the player context: 0x0050A7F5 calls the loop that
        // gives every player its frame, and the frame walks that player's own root.
        // The repeat argument is read off the resolved record NOW (0x004B9F04), not
        // copied at load as the world's +0x731A is -- so a root re-armed this game will
        // pass 1 here where the world root would still pass its authored value.
        foreach (TriggerPlayerRoot root in _tree.PlayerRoots)
        {
            TriggerRecord? record = _tree.ResolveCondition(root.TriggerId);
            byte repeat = record is null ? (byte)0 : _state.RecordRepeat(record);
            run.Player = root.PlayerId;
            Walk(unchecked((short)root.TriggerId), repeat, TriggerContext.Player, run);
        }
        run.Player = null;

        return run.Result();
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

    /// <summary>Each action point that carries a root, with the repeat byte its frame
    /// would have cached — see <see cref="TriggerAreaRoot"/>.</summary>
    private TriggerAreaRoot[] BuildAreaRoots(WorldState world) => world.ActionPoints
        .Where(point => point.TriggerId != 0)
        .Select(point => new TriggerAreaRoot(
            point, _tree!.ResolveCondition(point.TriggerId)?.RepeatCount ?? 0))
        .ToArray();

    /// <summary>Models the creature root setter at <c>0x0049A950</c>: attach the id,
    /// resolve it in the condition array regardless of sign, and snapshot byte
    /// <c>+0x0F</c> beside the creature for later frame calls.</summary>
    private void AttachCreatureRoots(WorldState world)
    {
        foreach (CreatureState creature in world.Creatures)
        {
            if (creature.TriggerId == 0) continue;
            creature.TriggerRepeat = _tree!.ResolveCondition(creature.TriggerId)?.RepeatCount ?? 0;
        }
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

    private void Walk(short id, byte inheritedRepeat, TriggerContext context, TriggerRun run)
    {
        while (id != 0)
        {
            TriggerRecord record = _tree!.Resolve(id)
                ?? throw new InvalidDataException($"No tag {(id > 0 ? 213 : 214)} trigger records exist.");
            run.Reached?.Add(record.TriggerId);
            bool descend = false;
            byte childRepeat = inheritedRepeat;

            if (_state.IsRecordActive(record))
            {
                if (record.Tag == 213)
                {
                    TriggerConditionResult result = EvaluateCondition(record, context, run);
                    if (result == TriggerConditionResult.NotEvaluated)
                        run.Blocked.Add(
                            new BlockedTrigger(record.Tag, record.Type, record.TriggerId, context));
                    else if (result == TriggerConditionResult.Satisfied)
                    {
                        descend = true;
                        childRepeat = _state.RecordRepeat(record);
                    }
                    else
                    {
                        run.Unsatisfied?.Add(record.TriggerId);
                    }
                }
                else if (RunAction(record, context, run))
                {
                    descend = true;
                    if (inheritedRepeat == 1)
                        _state.SetRecordActive(record, active: false, repeat: null);
                }
            }

            if (descend)
                Walk(unchecked((short)record.ChildTriggerId), childRepeat, context, run);
            id = unchecked((short)record.NextTriggerId);
        }
    }

    /// <summary>Evaluates one condition in the context whose walker reached it.</summary>
    /// <remarks>
    /// <para>The creature dispatcher answers its own status types, then delegates to the
    /// owning player's dispatcher; the player's default delegates to the world. Thus types
    /// 1, 2 and 34 still reach the level-global banks. `trigger-walkers.md`.</para>
    /// <para>The area dispatcher (<c>0x004B0D70</c>) delegates the same way, and owns
    /// seven types of its own — <see cref="TriggerAreaConditions"/>.</para>
    /// </remarks>
    private TriggerConditionResult EvaluateCondition(
        TriggerRecord record, TriggerContext context, TriggerRun run)
    {
        if (context == TriggerContext.Area && TriggerAreaConditions.Owns(record.Type))
            return TriggerAreaConditions.Evaluate(record, run.Area!, _world!, _database!);

        if (context == TriggerContext.Creature && CreatureOwnsCondition(record.Type))
            return EvaluateCreatureCondition(record, run.Creature!, _database!);

        bool reachesPlayer = context is TriggerContext.Player or TriggerContext.Creature;
        if (!reachesPlayer || !PlayerOwnsCondition(record.Type))
            return TriggerConditionEvaluator.Evaluate(record, _state, run.CurrentTick);

        if (PlayerConditionIsConstantFalse(record.Type))
            return TriggerConditionResult.Unsatisfied;

        // Answered whole, not measured: types 29 and 47 read no Comparison and no Value, so
        // neither can go through the shared compare path below.
        if (record.Type == 47 && _world is not null)
            return Result(KilledBy(record.Parameters, _world, run.Player!.Value));

        if (record.Type == 29 && _world is not null)
            return Result(SpellAvailable(record.Parameters, _world, run.Player!.Value));

        if (record.Type == 73 && _world is not null)
            return Possessing(_world, run.Player!.Value)
                ? TriggerConditionResult.NotEvaluated
                : TriggerConditionResult.Unsatisfied;

        if (_world is not null && _database is not null
            && PlayerCounter(
                record.Type, record.Parameters, _world, _database, run.Player!.Value, run) is int measured)
            return Result(TriggerState.Compare(
                Byte(record.Parameters, "Comparison"),
                measured,
                unchecked((int)record.Parameters["Value"])));

        if (record.Type != 72) return TriggerConditionResult.NotEvaluated;

        run.Assumed.Add(new BlockedTrigger(record.Tag, record.Type, record.TriggerId, context));
        return TriggerConditionResult.Satisfied;
    }

    [Dk2Behavior(Confidence.StronglyInferred, "docs/research/trigger-walkers.md")]
    private static TriggerConditionResult EvaluateCreatureCondition(
        TriggerRecord record, CreatureState creature, GameDatabase database)
    {
        if (CreatureConditionIsConstantFalse(record.Type))
            return TriggerConditionResult.Unsatisfied;

        int measured = record.Type switch
        {
            36 => creature.Health * 100 / CreatureLevelRules.MaxHealth(
                database, database.CreatureById(creature.CreatureTypeId), creature.Level),
            37 => creature.GoldHeld * 100 / CreatureLevelRules.MaxGoldHeld(
                database, database.CreatureById(creature.CreatureTypeId), creature.Level),
            49 => creature.Level,
            _ => throw new ArgumentOutOfRangeException(nameof(record), record.Type, null),
        };
        return Result(TriggerState.Compare(
            Byte(record.Parameters, "Comparison"), measured,
            unchecked((int)record.Parameters["Value"])));
    }

    /// <summary>Creature condition types whose jump-table entries return zero without
    /// reading their bodies. Their event-filter path is separate and out of scope.</summary>
    private static bool CreatureConditionIsConstantFalse(byte type) =>
        type is >= 3 and <= 16 or 50 or 54 or 55 or 63 or 65;

    /// <summary>The player quantity a condition type compares, or null for a type whose
    /// arm we cannot answer. Types 19, 24, 25, 27, 28 and 52 walk owned things; type 24 assumes
    /// the room dumper's <c>Size</c> is slab count and uses
    /// <see cref="RoomInstance.TileCount"/>. Type 25's byte overflow is unreachable in
    /// shipped levels and is not modelled; its <c>Comparison == 0</c> availability mode
    /// at <c>0x004C7ECF</c> is declined.
    /// Types 30–32 and 71 read named player dwords, and type 52 walks owned creatures.
    /// See trigger-semantics.md and room-conditions.md.</summary>
    private static int? PlayerCounter(
        byte type,
        IReadOnlyDictionary<string, long> parameters,
        WorldState world,
        GameDatabase database,
        byte playerId,
        TriggerRun run) => type switch
    {
        19 => CreatureCount(parameters, world, database, playerId),
        24 => Rooms(parameters, world, database, playerId, run).Sum(room => room.TileCount),
        25 when Byte(parameters, "Comparison") != 0 =>
            Rooms(parameters, world, database, playerId, run).Count(),
        27 when Byte(parameters, "Comparison") != 0 => DoorCount(parameters, world, playerId),
        28 when Byte(parameters, "Comparison") != 0 => TrapCount(parameters, world, database, playerId),
        30 => GoldStore.Total(world, playerId),        // +0x7E "Money"
        31 => world.Player(playerId).GoldMined,        // +0x15A "GoldMined"
        32 => world.Player(playerId).Mana,             // +0x82 "CurrentMana"
        52 => LeveledCreatureCount(parameters, world, database, playerId),
        71 => world.Player(playerId).SlapCount,        // +0xEE6 "NumberOfSlaps"
        _ => null,
    };

    private static int DoorCount(
        IReadOnlyDictionary<string, long> parameters, WorldState world, byte playerId)
    {
        byte flags = Byte(parameters, "Flag");
        IEnumerable<DoorState> owned = world.Doors.Where(door => door.PlayerId == playerId);
        if ((flags & 0x08) != 0) return owned.Count(door => door.UnderConstruction);
        if ((flags & 0x10) != 0) return owned.Count(door => !door.UnderConstruction);
        byte wanted = Byte(parameters, "TargetId");
        return owned.Count(door => wanted == 0 || door.DoorId == wanted);
    }

    /// <summary>The Boulder projectile's shot type, and the Boulder object that rolls
    /// once one is fired. Condition 28 counts a keeper's Boulder traps <b>and</b> the
    /// boulders already in flight, which is the one place a trap count reaches past the
    /// traps themselves (`doors-and-traps.md`). Both ids are the shipped definitions'
    /// own: object 74 is <c>Boulder</c> in <c>objects.md</c>.</summary>
    private const byte BoulderShotTypeId = 26;
    private const byte BoulderObjectId = 74;

    private static int TrapCount(
        IReadOnlyDictionary<string, long> parameters,
        WorldState world,
        GameDatabase database,
        byte playerId)
    {
        byte flags = Byte(parameters, "Flag");
        IEnumerable<TrapState> owned = world.Traps.Where(trap => trap.PlayerId == playerId);
        if ((flags & 0x08) != 0) return owned.Count(trap => trap.UnderConstruction);
        if ((flags & 0x10) != 0) return owned.Count(trap => !trap.UnderConstruction);

        byte wanted = Byte(parameters, "TargetId");
        int count = owned.Count(trap => wanted == 0 || trap.TrapId == wanted);
        if (wanted != 0
            && database.Traps.TryGetValue(wanted, out TrapDef? trapDefinition)
            && trapDefinition.ShotTypeId == BoulderShotTypeId)
        {
            count += world.Objects.Count(
                item => item.OwnerPlayerId == playerId
                    && item.ObjectTypeId == BoulderObjectId);
        }
        return count;
    }

    /// <summary>Condition type 47 — <b>has this keeper been killed, and by whom</b>
    /// (<c>0x004C7B4B</c>). Not a comparison: the arm returns 0 outright when
    /// <c>KilledByPlayerId</c> is zero, 1 when the record names no player, and otherwise
    /// tests equality itself. So it is answered here and never reaches
    /// <see cref="TriggerState.Compare"/>, whose <c>Comparison</c> byte this type does not
    /// even read.</summary>
    private static bool KilledBy(
        IReadOnlyDictionary<string, long> parameters, WorldState world, byte playerId)
    {
        byte killer = world.Player(playerId).KilledByPlayerId;
        if (killer == 0) return false;
        byte wanted = Byte(parameters, "PlayerId");
        return wanted == 0 || killer == wanted;
    }

    /// <summary>Condition type 29 — <b>is this keeper's spell available</b>
    /// (<c>0x004C83C8</c>). Also not a comparison: the arm reads one byte, <c>TargetId</c>,
    /// and returns a bare true or false. Zero asks whether <i>any</i> spell is available,
    /// walking the keeper's spell table at <c>player+0x6E</c>/<c>+0x72</c>; a named id
    /// looks that one up. Both test the state <b>strictly against AVAILABLE (3)</b>, so —
    /// unlike creatures, where <see cref="PortalRules.IsUnlocked"/> also accepts
    /// RESEARCHABLE (2) — a researchable spell does not answer this true. The corpus makes
    /// that distinction load-bearing: 271 shipped kind-5 entries hold 1, and one holds 4.
    /// </summary>
    private static bool SpellAvailable(
        IReadOnlyDictionary<string, long> parameters, WorldState world, byte playerId)
    {
        const ushort KindSpell = 5;
        const int Available = 3;
        byte wanted = Byte(parameters, "TargetId");
        return wanted == 0
            ? world.HasAvailability(playerId, KindSpell, Available)
            : world.Availability(playerId, KindSpell, wanted) == Available;
    }

    /// <summary>Condition type 52 — <b>how many of this keeper's creatures have reached
    /// a level</b> (<c>0x004C7E4E</c>). Walks the owned-creature list and counts every
    /// creature whose level is <b>at least</b> the record's, comparing that count against
    /// <c>Value</c>. Workers are excluded, by the same <c>+0x686</c> bit 0 that type 19's
    /// total excludes them by — so an imp never counts however high it is trained.
    /// </summary>
    /// <remarks>The three shipped records all ask "at least": one creature at level 4,
    /// one at 8, and twenty at 2. The threshold is byte 03, which the previous reader
    /// called a player id; the arm compares it against the creature's own level
    /// (<c>+0x403</c>), not against an owner.</remarks>
    private static int LeveledCreatureCount(
        IReadOnlyDictionary<string, long> parameters,
        WorldState world,
        GameDatabase database,
        byte playerId)
    {
        byte level = Byte(parameters, "Level");
        int count = 0;
        foreach (CreatureState creature in world.Creatures)
        {
            if (creature.OwnerPlayerId != playerId) continue;
            if (IsWorker(database, creature.CreatureTypeId)) continue;
            if (creature.Level < level) continue;
            count++;
        }

        return count;
    }

    /// <summary>Whether this keeper is possessing a creature — DK2's
    /// <c>player+0x134</c>, which condition type 73 reads before anything else.</summary>
    /// <remarks>The arm's shape is why type 73 is answered here and not through
    /// <see cref="PlayerCounter"/>: on zero it returns false <b>without</b> reading its
    /// comparison or value, and only a keeper who <i>is</i> possessing reaches the compare
    /// — against the possessed creature's <c>+0x488</c>, which is unread (Q279). Nothing
    /// sets the field, so today this is always false and the decline below is unreachable;
    /// it exists so that adding possession turns type 73 into an honest gap rather than a
    /// silent wrong answer.</remarks>
    private static bool Possessing(WorldState world, byte playerId) =>
        world.Player(playerId).PossessedCreatureId != 0;

    /// <summary>Condition type 19 — how many creatures this keeper owns
    /// (<c>0x004C7B9D</c>). <b>The two paths are not the same count</b>: a named type
    /// reads a cached per-type total, id 0 reads the owned total <i>minus every
    /// worker</i>. So workers are excluded from the total and counted when asked for by
    /// name, which is DK2's asymmetry and not ours. trigger-semantics.md, ADR-0113.
    /// </summary>
    /// <remarks>UNVERIFIED: whether DK2 keeps a creature in the owned list while a hand
    /// holds it. Ours does not, so a keeper mid-pickup may count one fewer (Q268).
    /// </remarks>
    private static int CreatureCount(
        IReadOnlyDictionary<string, long> parameters,
        WorldState world,
        GameDatabase database,
        byte playerId)
    {
        byte wanted = Byte(parameters, "CreatureId");
        int count = 0;
        foreach (CreatureState creature in world.Creatures)
        {
            if (creature.OwnerPlayerId != playerId) continue;
            if (wanted != 0)
            {
                if (creature.CreatureTypeId != wanted) continue;
            }
            else if (IsWorker(database, creature.CreatureTypeId))
            {
                continue;
            }

            count++;
        }

        return count;
    }

    /// <summary>Bit 0 of the creature definition's runtime <c>+0x686</c> flag dword,
    /// which is file offset <c>0xEFC</c> — <see cref="CreatureFlags.IsWorker"/>. Imp and
    /// Dwarf are the only two shipped creatures that set it, which is the roster a worker
    /// flag should select.</summary>
    private static bool IsWorker(GameDatabase database, byte creatureTypeId) =>
        (database.CreatureById(creatureTypeId).Flags & CreatureFlags.IsWorker) != 0;

    private static IEnumerable<RoomInstance> Rooms(
        IReadOnlyDictionary<string, long> parameters,
        WorldState world,
        GameDatabase database,
        byte playerId,
        TriggerRun run)
    {
        byte roomId = Byte(parameters, "RoomId");
        run.Rooms ??= RoomIndex.Build(world, database);
        return run.Rooms.Where(room =>
            room.OwnerPlayerId == playerId
            && (roomId == 0 || room.Definition.RoomId == roomId));
    }

    private static TriggerConditionResult Result(bool held) => held
        ? TriggerConditionResult.Satisfied
        : TriggerConditionResult.Unsatisfied;

    private static byte Byte(IReadOnlyDictionary<string, long> p, string name) =>
        checked((byte)p[name]);

    /// <summary>The eight player condition types whose jump-table entry <b>is</b> the
    /// dispatcher's zero-return epilogue (<c>0x004C850C</c>): they read nothing and answer
    /// false. Not the same as declining — this is DK2's answer, so 163 shipped records
    /// leave the blocked census instead of sitting in it as unfinished work.</summary>
    /// <remarks>Type 73 shares the epilogue but not the entry: its arm starts six
    /// instructions earlier at <c>0x004C8500</c> and falls into the zero return only when
    /// the player's <c>+0x134</c> is clear. These eight skip that test.</remarks>
    private static bool PlayerConditionIsConstantFalse(byte type) =>
        type is 48 or 53 or (>= 56 and <= 61);

    /// <summary>Runs one action, or answers for one this executor does not perform.</summary>
    /// <remarks>
    /// <para>Rule: whether an action succeeds is decided by the jump table, not by what
    /// its arm does. The world dispatcher has one success exit (<c>0x0051988C</c>) and one
    /// failure exit that prints <c>Unhandled Trigger When</c> (<c>0x00519893</c>); the
    /// player dispatcher's thirteen own arms all set <c>EAX</c> to 1 and the rest delegate
    /// to the world one. trigger-semantics.md, "What an arm returns".</para>
    /// <para>So a type nobody has implemented can still return what DK2 returns, and does.
    /// What that buys is the one-shot retire below and an honest census — <b>not</b> reach:
    /// all 5,408 shipped action records are leaves, so no shipped walk has ever descended
    /// past an action (ADR-0107).</para>
    /// </remarks>
    private bool RunAction(TriggerRecord record, TriggerContext context, TriggerRun run)
    {
        // WIN_GAME and LOSE_GAME are the player dispatcher's first two arms
        // (0x004C8662, 0x004C8675): each ends the game for the player whose tree —
        // or whose creature's tree, the creature dispatcher delegating with the
        // owner as `this` — reached it. The world dispatcher refuses them, which the
        // fall-through below reproduces (ADR-0162, win-and-defeat.md §2).
        if (record.Type is 10 or 11)
        {
            if (context != TriggerContext.World) return ExecuteAction(record, context, run);
            run.Unhandled.Add(new BlockedTrigger(record.Tag, record.Type, record.TriggerId, context));
            return false;
        }

        if (context == TriggerContext.Creature && CreatureOwnsAction(record.Type))
            return Unperformed(record, context, run.Partial);
        if (context == TriggerContext.Creature && PlayerOwnsAction(record.Type))
            return Unperformed(record, context, run.Partial);
        if (context == TriggerContext.Player && PlayerOwnsAction(record.Type))
            return Unperformed(record, context, run.Partial);
        if (Performs(record.Type))
            return ExecuteAction(record, context, run);
        if (WorldActionSucceeds(record.Type)) return Unperformed(record, context, run.Partial);

        run.Unhandled.Add(new BlockedTrigger(record.Tag, record.Type, record.TriggerId, context));
        return false;
    }

    private static bool Unperformed(
        TriggerRecord record, TriggerContext context, List<BlockedTrigger> partial)
    {
        partial.Add(new BlockedTrigger(record.Tag, record.Type, record.TriggerId, context));
        return true;
    }

    /// <summary>Whether the world action dispatcher (<c>0x00518AD0</c>) reaches its
    /// success exit for a type. Read off its own tables: a byte table at
    /// <c>0x00519930</c> indexed by <c>type - 1</c>, a jump table at <c>0x005198C4</c>,
    /// and a range check that sends type 0 and anything past 51 to the failure exit with
    /// the other 25.</summary>
    private static bool WorldActionSucceeds(byte type) => type switch
    {
        < 1 or > 51 => false,
        3 or 15 or 24 => false,
        >= 10 and <= 13 => false,
        >= 19 and <= 21 => false,
        >= 26 and <= 31 => false,
        >= 33 and <= 38 => false,
        42 or 43 or 50 => false,
        _ => true,
    };

    /// <summary>The 30 condition types the player dispatcher computes itself
    /// (<c>0x004C7B20</c>, base 19, range <c>0x37</c>). Everything else it delegates.
    /// </summary>
    private static bool PlayerOwnsCondition(byte type) => type switch
    {
        >= 19 and <= 33 => true,
        47 or 48 or 52 or 53 => true,
        >= 56 and <= 61 => true,
        68 or 71 or 72 or 73 or 74 => true,
        _ => false,
    };

    /// <summary>The 13 action types the player dispatcher computes itself
    /// (<c>0x004C8620</c>, base 10, range <c>0x28</c>). Note 7, 8 and 14 are <b>not</b>
    /// among them — they fall below the base and reach the world dispatcher.</summary>
    private static bool PlayerOwnsAction(byte type) => type switch
    {
        10 or 11 or 31 or 34 or 37 or 38 or 42 or 50 => true,
        >= 24 and <= 28 => true,
        _ => false,
    };

    /// <summary>The creature condition dispatcher computes three per-instance counters
    /// and gives its constant-false event types an answer of its own.</summary>
    private static bool CreatureOwnsCondition(byte type) =>
        CreatureConditionIsConstantFalse(type) || type is 36 or 37 or 49;

    /// <summary>The ten action types owned by the creature dispatcher
    /// (<c>0x0049AC80</c>, base 15, range <c>0x1C</c>). Every other type delegates
    /// to the owning player's dispatcher, whose default delegates to the world.</summary>
    private static bool CreatureOwnsAction(byte type) => type switch
    {
        15 or 20 or 21 or 29 or 30 or 33 or 35 or 36 or 42 or 43 => true,
        _ => false,
    };

    /// <summary>Whether this executor <b>carries out</b> an action type, as opposed to the
    /// many more it answers for. Flag write, timer start, party arming — <b>none of them
    /// touches terrain</b>, which is what lets <see cref="TriggerRun.Rooms"/> hold one
    /// derived room list for a whole run.</summary>
    /// <remarks>Adding a type here without invalidating that cache would leave a room
    /// condition reading the map as it was before the write.
    /// <c>TriggerExecutorTests.OnlyActionsThatCannotAlterTerrainArePerformed</c> is what
    /// makes that a failure rather than a silence.</remarks>
    public static bool Performs(byte type) =>
        type is 1 or 6 or 7 or 8 or 10 or 11 or 14 or 22 or 44 or 45;

    private bool ExecuteAction(TriggerRecord record, TriggerContext context, TriggerRun run)
    {
        IReadOnlyDictionary<string, long> p = record.Parameters;
        List<BlockedTrigger> partial = run.Partial;
        if (record.Type == 6)
        {
            if (_world is null) return Unperformed(record, context, partial);
            byte kind = Byte(p, "AvailabilityKind");
            if (kind is >= 1 and <= 5)
                _world.SetAvailability(
                    Byte(p, "PlayerId"), kind, Byte(p, "TypeId"),
                    Byte(p, "Available") == 0 ? 1 : 3);
            return true;
        }
        if (record.Type == 8)
        {
            _state.StartTimer(checked((byte)p["TimerId"]), run.CurrentTick);
            return true;
        }
        if (record.Type is 10 or 11)
        {
            if (_world is null || run.Player is not byte player)
                return Unperformed(record, context, partial);
            _world.EndGame(player, record.Type == 10 ? PlayerStatus.Won : PlayerStatus.Lost);
            return true;
        }
        if (record.Type == 44)
        {
            _state.SetCreatureAngerEnabled(Byte(p, "Available") == 1);
            return true;
        }
        if (record.Type == 45)
        {
            _state.SetNotificationsEnabled(Byte(p, "Available") == 1);
            return true;
        }
        if (record.Type == 1) return CreateCreature(record, context, p, run);
        if (record.Type == 14) return CreateParty(record, context, p, partial);
        if (record.Type == 22) return AlterTerrain(record, context, p, run);
        if (record.Type != 7)
            throw new ArgumentException($"Action type {record.Type} has no arm here.", nameof(record));

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

    /// <summary>Action type 1 — put one creature on the map (ADR-0137).</summary>
    /// <remarks>
    /// <para>From the world arm <c>0x00518B12</c>. <b>The coordinates are zero-based</b>,
    /// where type 22's are one-based: nothing decrements them before <c>FUN_00519B20</c>
    /// makes each a tile centre. <c>Level</c> is one-based and applied by repetition — the
    /// arm calls the level-up routine <c>Level - 1</c> times, so 0 and 1 both leave a
    /// level-1 creature.</para>
    /// <para><b>Every record reports partial</b>, because the <c>Flag</c> byte the same arm
    /// writes onto the creature is dropped and bit 0 of it is <c>DestroysRooms</c>, which
    /// all 268 set (Q302–Q305).</para>
    /// </remarks>
    private bool CreateCreature(
        TriggerRecord record, TriggerContext context,
        IReadOnlyDictionary<string, long> p, TriggerRun run)
    {
        if (_world is null || _database is null)
            return Unperformed(record, context, run.Partial);

        var at = new TilePos(checked((int)p["PosX"]), checked((int)p["PosY"]));
        if (!_world.Contains(at)) return Unperformed(record, context, run.Partial);

        byte creatureTypeId = checked((byte)p["CreatureId"]);
        CreatureState creature = _world.SpawnCreature(
            _database, creatureTypeId, checked((byte)p["PlayerId"]), at);

        // The player byte selects the record at index PlayerId - 1 and the creature is
        // created with the word at its +0x04, TagId. Passed through as the id itself
        // here, which is the same number for every shipped level (Q305).
        // Bit 2 of the flag byte is DieInstantly (0x0048E140). The other bits land on
        // the creature's own Flags byte, which nothing here reads yet (Q302).
        creature.DieInstantly = (checked((byte)p["Flag"]) & 0x04) != 0;

        int level = CreatureLevelRules.Clamp(checked((int)p["Level"]));
        creature.Level = level;
        creature.Health = CreatureLevelRules.MaxHealth(
            _database, _database.CreatureById(creatureTypeId), level);

        return Unperformed(record, context, run.Partial);
    }

    /// <summary>Action type 22 — write a terrain type onto one tile (ADR-0136).</summary>
    /// <remarks>
    /// <para>From the world arm <c>0x00519425</c>. <b>The coordinates are one-based</b>:
    /// it decrements each before the lookup. Two cases succeed without writing — off the
    /// map, and a tile already carrying the terrain (<c>0x00519484</c>).</para>
    /// <para><b>Room tiles are declined.</b> The arm also demolishes a room on the tile
    /// and notifies the map, neither of which is read; half-performing would leave
    /// <see cref="RoomIndex"/> describing a room whose tiles had moved (Q301).</para>
    /// </remarks>
    private bool AlterTerrain(
        TriggerRecord record, TriggerContext context,
        IReadOnlyDictionary<string, long> p, TriggerRun run)
    {
        if (_world is null || _database is null)
            return Unperformed(record, context, run.Partial);

        var at = new TilePos(
            checked((int)p["PosX"]) - 1, checked((int)p["PosY"]) - 1);
        if (!_world.Contains(at)) return true;

        byte terrainId = checked((byte)p["TerrainId"]);
        ref TileState tile = ref _world.Tile(at);
        if (tile.TerrainId == terrainId) return true;

        TerrainDef next = _database.TerrainById(terrainId);
        if (next.IsRoom
            || _database.TerrainById(tile.TerrainId).IsRoom
            || RoomIndex.RoomAt(_world, _database, (at.Y * _world.Width) + at.X) is not null)
            return Unperformed(record, context, run.Partial);

        tile.TerrainId = terrainId;
        tile.Health = next.StartingHealth;
        tile.OwnerPlayerId = next.IsOwnable
            ? checked((byte)p["PlayerId"])
            : PlayerIds.Unowned;
        if (!next.IsTaggable) tile.TaggedBy = 0;

        // The map this run has already derived is now wrong about this tile.
        run.Rooms = null;
        return true;
    }

    /// <summary>Action type 14 — <b>the arming half only</b>. Creates no creatures.</summary>
    /// <remarks>
    /// <para>Rule, in <c>FUN_00519F90</c>'s order: arm slot <c>PartyId</c>'s own subtree
    /// (<c>0x0051A197</c>), then per member slot skip a zero creature id
    /// (<c>0x0051A1CF</c>) or else create the creature and arm that member's subtree
    /// (<c>0x0051A58F</c>). trigger-tree.md, "Action type 14 creates a party" — including
    /// the <c>PartyType</c> branch that can arm nothing and still succeed (Q240).</para>
    /// <para><b>Why it still returns true.</b> The creature-creation block is missing, so
    /// this reports through <see cref="TriggerRunResult.Partial"/> rather than passing as
    /// success — but it succeeds, as its arm does. The subtrees it arms are party roots,
    /// walked from the world's own slot table and not from this record's children.</para>
    /// </remarks>
    private bool CreateParty(
        TriggerRecord record, TriggerContext context,
        IReadOnlyDictionary<string, long> p, List<BlockedTrigger> partial)
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

        partial.Add(new BlockedTrigger(record.Tag, record.Type, record.TriggerId, context));
        return true;
    }
}
