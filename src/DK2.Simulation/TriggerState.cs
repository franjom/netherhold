// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation;

/// <summary>How an action record combines its operand with a flag. Read off the
/// executable's own bit tests, not from a field name: the external field list calls the
/// byte carrying these bits <c>Flag</c>, which it is not — it selects an <b>operation
/// and an addressing mode</b>, and a different byte names the flag
/// (docs/research/trigger-state.md).</summary>
public enum FlagWriteMode : byte
{
    /// <summary>Store the operand into the flag.</summary>
    Assign,

    /// <summary>Add the operand to the flag.</summary>
    Add,

    /// <summary>Subtract the operand from the flag.</summary>
    Subtract,
}

/// <summary>The trigger system's own state: a bank of byte flags and a bank of timers,
/// both belonging to the <b>level</b> rather than to any player. STRONGLY INFERRED from
/// four agreeing readings — only two functions index either bank and both are trigger
/// dispatchers; every action-dispatcher call site loads <c>this</c> from a global; the
/// player-context dispatcher at <c>0x004C8620</c> declines to pass its own player; and
/// neither global is per-player. Getting it wrong silently puts the wrong shape of state into the determinism hash.
/// <para>Layout: flags a byte array at <c>+0x3F70</c>, timers a dword array at
/// <c>+0x3FF0</c>, 0x80 apart — which fixes <see cref="FlagCount"/> and makes
/// <see cref="SpecialFlagId"/> the first id past the end. Two units, not one: a timer
/// <i>stamp</i> is ticks, an authored <i>threshold</i> is <b>seconds</b> multiplied by
/// variable 19 before comparison (<see cref="HasElapsed"/>, trigger-state.md §5). When a
/// trigger is evaluated is not decided here: the world frame, once a game turn (Q228).</para></summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/trigger-state.md")]
public sealed class TriggerState
{
    /// <summary>Flag ids 0..127. The bank is 0x80 bytes wide because the timer bank
    /// begins 0x80 bytes after it.</summary>
    public const int FlagCount = 128;

    /// <summary>Id 128 — the first id past the flag bank, and <b>not a flag</b>. Both
    /// dispatchers compare the id against it before touching the bank and divert to a
    /// dword at <c>+0x72A2</c>, with a companion set to 1 at <c>+0x72A6</c> and a virtual
    /// call at <c>+0x348</c>; unlike a flag it takes the operand's <b>full 32 bits</b>. What it is remains <b>UNKNOWN</b> (Q229), so this type models it as an opaque value.</summary>
    public const byte SpecialFlagId = 0x80;

    /// <summary>Timer ids 0..15. <b>Sixteen</b>, not the 256 the masked write path
    /// suggests, on two agreeing readings: the next known field sits at <c>+0x4030</c>,
    /// leaving the bank at <c>+0x3FF0</c> exactly 0x40 bytes — sixteen dwords — and the
    /// condition path compares the id against <c>0x10</c> first.</summary>
    public const int TimerCount = 16;

    /// <summary>Id 16 — the first id past the timer bank and <b>not a timer</b>: it names
    /// <see cref="LevelTimeLimit"/>. The condition path compares against it (<c>CMP DL,0x10</c>
    /// at <c>0x005184EB</c>) and on a match reads world slot <c>+0x34C</c>, <c>0x00509660</c>,
    /// which returns <c>world+0x72AA</c>; action type 23 writes that field (Q232).</summary>
    public const byte SpecialTimerId = 16;

    private readonly byte[] _flags = new byte[FlagCount];
    private readonly int[] _timers = new int[TimerCount];
    private readonly Dictionary<(int Tag, ushort Id), TriggerRecordState> _records = [];
    private byte _worldOptions = 0x07;

    /// <summary>Whether anger deltas may change a creature's six counters.</summary>
    public bool CreatureAngerEnabled => (_worldOptions & 0x01) != 0;

    /// <summary>Whether ordinary player messages, including keeper AI inputs, may be created.</summary>
    public bool NotificationsEnabled => (_worldOptions & 0x02) != 0;

    internal void SetCreatureAngerEnabled(bool enabled) => SetWorldOption(0x01, enabled);

    internal void SetNotificationsEnabled(bool enabled) => SetWorldOption(0x02, enabled);

    private void SetWorldOption(byte mask, bool enabled) =>
        _worldOptions = enabled
            ? (byte)(_worldOptions | mask)
            : (byte)(_worldOptions & ~mask);

    /// <summary>The value written when an action names <see cref="SpecialFlagId"/>.
    /// Opaque: see that constant.</summary>
    public int SpecialValue { get; set; }

    /// <summary>Set whenever <see cref="SpecialValue"/> is written, mirroring the
    /// companion the executable sets to 1 beside it. Purpose UNKNOWN; carried so a
    /// snapshot round-trips what the original would hold.</summary>
    public bool SpecialValueWritten { get; set; }

    /// <summary>DK2's <c>LevelTimeLimit</c> (<c>world+0x72AA</c>, named by the world dumper at
    /// <c>0x0050ED6A</c>), in game turns. Action type 23 sets it to its <c>Value</c>
    /// (<c>0x00519177</c>); the per-turn world update counts it down while it is not zero
    /// (<c>0x0050A64E</c>), see <see cref="CountDownLevelTimeLimit"/>.</summary>
    public int LevelTimeLimit { get; set; }

    /// <summary>Bit <c>0x08</c> of the world options (<c>world+0x72A1</c>): set when
    /// <see cref="LevelTimeLimit"/> runs out (<c>0x0050A671</c>).</summary>
    public bool LevelTimeUp => (_worldOptions & 0x08) != 0;

    /// <summary>One turn of <c>0x0050A64E</c>: a non-zero limit drops by one, and on reaching
    /// zero sets <see cref="LevelTimeUp"/>. Returns whether it ran out this turn. What DK2 does
    /// next, making every keeper lose, runs only in game modes 2 and 3, which are not modelled;
    /// the gate <c>[[world+4]+0x284]</c> that skips the countdown is the window-focus suspension
    /// (game action <c>0x5C</c>), which a simulation with no window never raises.</summary>
    internal bool CountDownLevelTimeLimit()
    {
        if (LevelTimeLimit == 0) return false;
        LevelTimeLimit--;
        if (LevelTimeLimit != 0) return false;
        _worldOptions |= 0x08;
        return true;
    }

    /// <summary>DK2's <c>SpecialInfoNextRoomTypeId</c> (<c>world+0x72BC</c>, named by the world
    /// dumper), set by action type 47 (<c>0x005098C0</c>). Only the interface
    /// (<c>0x00427BA0</c>) and the dumpers read it, through world slot <c>+0x374</c>.</summary>
    public byte SpecialInfoNextRoomTypeId { get; set; }

    /// <summary>A flag's value, <b>zero-extended</b>. The asymmetry is DK2's, not ours:
    /// see <see cref="Write"/>.</summary>
    public int Read(byte flagId)
    {
        if (flagId >= FlagCount)
            throw new ArgumentOutOfRangeException(
                nameof(flagId), flagId,
                $"Flag id {flagId} is past the bank; {SpecialFlagId} is the special target, "
                + "which is read through SpecialValue.");

        return _flags[flagId];
    }

    /// <summary>Applies one action record's write to a flag. <b>The operand is truncated
    /// to its low byte</b> because the bank is bytes wide; only the special target takes
    /// all 32 bits. <b>Add and subtract are signed; reading is not</b> — the executable
    /// loads the flag as a signed char and stores a byte back while every read
    /// zero-extends, so a flag driven below zero compares as 128..255. Reproduced.</summary>
    public void Write(byte flagId, FlagWriteMode mode, int operand)
    {
        if (flagId == SpecialFlagId)
        {
            // The full 32 bits, unlike a flag.
            SpecialValue = mode switch
            {
                FlagWriteMode.Assign => operand,
                FlagWriteMode.Add => SpecialValue + operand,
                FlagWriteMode.Subtract => SpecialValue - operand,
                _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
            };
            SpecialValueWritten = true;
            return;
        }

        if (flagId > SpecialFlagId)
            throw new ArgumentOutOfRangeException(
                nameof(flagId), flagId,
                $"Flag id {flagId} is past the bank and past the special target. In DK2 this "
                + "would write into the timer bank; no shipped level does it (Q230).");

        byte value = unchecked((byte)operand);
        _flags[flagId] = mode switch
        {
            FlagWriteMode.Assign => value,
            FlagWriteMode.Add => unchecked((byte)((sbyte)_flags[flagId] + value)),
            FlagWriteMode.Subtract => unchecked((byte)((sbyte)_flags[flagId] - value)),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
    }

    private static byte CheckTimer(byte timerId)
    {
        if (timerId == SpecialTimerId)
            throw new ArgumentOutOfRangeException(
                nameof(timerId), timerId,
                $"Timer id {SpecialTimerId} is not a timer slot: it names LevelTimeLimit (Q232).");

        if (timerId >= TimerCount)
            throw new ArgumentOutOfRangeException(
                nameof(timerId), timerId,
                $"Timer id {timerId} is past the {TimerCount}-slot bank. In DK2 this would "
                + "index past it into the object's next field; no shipped level does it.");

        return timerId;
    }

    /// <summary>A timer's stored stamp. <b>A timer holds the moment it was started, not
    /// a countdown</b>: the condition side compares the clock against
    /// <c>stamp + value</c>, so elapsed time is derived.</summary>
    public int ReadTimer(byte timerId) => _timers[CheckTimer(timerId)];

    /// <summary>Stamps a timer with <paramref name="now"/> — DK2's action type 8.</summary>
    public void StartTimer(byte timerId, int now) => _timers[CheckTimer(timerId)] = now;

    /// <summary>Sets a timer to an explicit stamp. <b>Not DK2's action type 23</b>, which sets
    /// <see cref="LevelTimeLimit"/>; the bank's only indexed store is type 8's. This exists for
    /// tests and for a snapshot restore.</summary>
    public void SetTimer(byte timerId, int value) => _timers[CheckTimer(timerId)] = value;

    /// <summary>Ticks elapsed since the timer was stamped. <b>Only meaningful when
    /// <see cref="IsStarted"/></b> — see it for why.</summary>
    public int Elapsed(byte timerId, int now) => now - _timers[CheckTimer(timerId)];

    /// <summary>Whether the timer has ever been stamped. <b>A stamp of zero means "not
    /// started"</b>, and it is DK2's distinction rather than a convention: the condition
    /// path tests the stored stamp and bails before comparing anything, so an unstarted
    /// timer does not read as "a very long time ago" — its condition simply does not
    /// hold, and tick 0 is not a usable stamp.</summary>
    public bool IsStarted(byte timerId) => _timers[CheckTimer(timerId)] != 0;

    /// <summary>Whether <paramref name="seconds"/> <b>seconds</b> have passed since the
    /// timer was stamped — the whole condition, in the shape DK2 evaluates it. <b>A
    /// deadline, not a subtraction</b>: the executable computes a deadline from the stamp
    /// and compares the clock against it, which differs from subtracting once the clock
    /// saturates, so the shape is kept.
    /// <para>The threshold is in <b>seconds</b>: CONFIRMED by two live captures of
    /// campaign level 1 (trigger-state.md §5), where the residual against the specific
    /// flag each subtree writes is a constant +1 tick — the sampler's lag — in four of
    /// four measurements, while a ticks reading leaves exactly <c>3 * threshold + 1</c>, the signature of a missing multiply by variable 19. So 600 is ten minutes, not 150 seconds.</para></summary>
    public bool HasElapsed(byte timerId, int seconds, int now) =>
        IsStarted(timerId)
        && now >= _timers[CheckTimer(timerId)] + SimulationTiming.TicksFromSeconds(seconds);

    /// <summary>Compares two values with DK2's comparison codes: the routine at
    /// <c>0x00519980</c> implements 1 to 6 as <b>signed</b> <c>&lt;</c>, <c>&lt;=</c>,
    /// <c>==</c>, <c>&gt;</c>, <c>&gt;=</c> and <c>!=</c>. Signedness is load-bearing —
    /// unsigned operands invert every comparison against a negative threshold
    /// (ADR-0092).</summary>
    /// <remarks><b>4 and 5 are the way round they look wrong.</b> The six arms sit in
    /// memory as <c>&lt; &lt;= == &gt;= &gt; !=</c>, so reading them in address order
    /// swaps <c>&gt;</c> and <c>&gt;=</c>; the jump table at <c>0x00519A04</c> is not in
    /// address order and sends 4 to <c>SETG</c> (<c>0x005199D9</c>) and 5 to <c>SETGE</c>
    /// (<c>0x005199C7</c>). Verified against the table 2026-09-09 while chasing a one-tick
    /// timer discrepancy that this would have explained — it was not this.</remarks>
    public static bool Compare(byte comparison, int left, int right) => comparison switch
    {
        1 => left < right,
        2 => left <= right,
        3 => left == right,
        4 => left > right,
        5 => left >= right,
        6 => left != right,
        _ => throw new ArgumentOutOfRangeException(
                 nameof(comparison), comparison, "DK2 defines comparison codes 1 to 6."),
    };

    internal byte[] RawFlags => _flags;
    internal int[] RawTimers => _timers;
    internal byte RawWorldOptions
    {
        get => _worldOptions;
        set => _worldOptions = value;
    }
    internal IEnumerable<KeyValuePair<(int Tag, ushort Id), TriggerRecordState>> RawRecords =>
        _records.OrderBy(p => p.Key.Tag).ThenBy(p => p.Key.Id);

    internal void InitializeRecords(IEnumerable<DK2.Data.Model.TriggerRecord> records)
    {
        foreach (DK2.Data.Model.TriggerRecord record in records)
            _records.TryAdd(
                (record.Tag, record.TriggerId),
                new TriggerRecordState(record.Tag == 214 || record.RepeatCount != 0, record.RepeatCount));
    }

    internal bool IsRecordActive(DK2.Data.Model.TriggerRecord record) =>
        _records[(record.Tag, record.TriggerId)].Active;

    internal byte RecordRepeat(DK2.Data.Model.TriggerRecord record) =>
        _records[(record.Tag, record.TriggerId)].Repeat;

    internal void SetRecordActive(
        DK2.Data.Model.TriggerRecord record, bool active, byte? repeat)
    {
        TriggerRecordState value = _records[(record.Tag, record.TriggerId)];
        _records[(record.Tag, record.TriggerId)] = value with
        {
            Active = active,
            Repeat = repeat ?? value.Repeat,
        };
    }

    internal void RestoreRecord(int tag, ushort id, bool active, byte repeat) =>
        _records[(tag, id)] = new TriggerRecordState(active, repeat);

    public void HashInto(ref Hash64 hash)
    {
        foreach (byte f in _flags) hash.Add(f);
        foreach (int t in _timers) hash.Add(t);
        hash.Add(SpecialValue);
        hash.Add(SpecialValueWritten ? (byte)1 : (byte)0);
        hash.Add(_worldOptions);
        hash.Add(LevelTimeLimit);
        hash.Add(SpecialInfoNextRoomTypeId);
        hash.Add(_records.Count);
        foreach (((int tag, ushort id), TriggerRecordState record) in RawRecords)
        {
            hash.Add(tag);
            hash.Add(id);
            hash.Add(record.Active ? (byte)1 : (byte)0);
            hash.Add(record.Repeat);
        }
    }
}

internal readonly record struct TriggerRecordState(bool Active, byte Repeat);
