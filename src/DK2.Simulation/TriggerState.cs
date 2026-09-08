// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation;

/// <summary>
/// How an action record combines its operand with a flag. Read off the
/// executable's own bit tests, not from a field name.
///
/// <para>The external field list calls the byte carrying these bits
/// <c>Flag</c>, which is what it is not: it selects an <b>operation and an
/// addressing mode</b>, and the flag it acts on is named by a different byte.
/// See <c>docs/research/trigger-state.md</c>.</para>
/// </summary>
public enum FlagWriteMode : byte
{
    /// <summary>Store the operand into the flag.</summary>
    Assign,

    /// <summary>Add the operand to the flag.</summary>
    Add,

    /// <summary>Subtract the operand from the flag.</summary>
    Subtract,
}

/// <summary>
/// The trigger system's own state: a bank of byte flags and a bank of timers,
/// both belonging to the level rather than to any player.
///
/// <para><b>Level-global, not per-player, and this was the question worth
/// settling before writing anything.</b> Getting it wrong puts the wrong shape
/// of state into the determinism hash, which is silent either way. Four
/// readings agree. Only two functions in the executable index either bank, and
/// both are trigger dispatchers. Every call site of the action dispatcher loads
/// its <c>this</c> from a global pointer. The <b>player</b>-context dispatcher
/// at <c>0x004C8620</c> — which is invoked for a specific player and holds that
/// player as its own <c>this</c> — deliberately does not pass it, loading the
/// global instead. And neither global is per-player: each is written once at
/// construction. A binary-wide scan of every initialised block, including
/// <c>cseg</c>, found 19 references to the flag bank and 21 to the timer bank,
/// with the run's positive control returning its expected 226.</para>
///
/// <para><b>Layout.</b> The flags are a byte array at <c>+0x3F70</c> of that
/// object and the timers a dword array immediately after it at
/// <c>+0x3FF0</c> — 0x80 bytes apart, which is what fixes the flag bank at
/// <see cref="FlagCount"/> entries and makes id <see cref="SpecialFlagId"/> the
/// first id past the end. The executable special-cases exactly that id rather
/// than running off into the timers.</para>
///
/// <para><b>Two units, and they are not the same one.</b> A <i>stamp</i> in the
/// timer bank is a tick count — the clock a timer records is the game's own
/// tick counter, and a live capture of campaign level 1 caught five timers
/// being stamped, each holding exactly the tick it was set at. An authored
/// <i>threshold</i> in a trigger record is in <b>seconds</b>, and is multiplied
/// by variable 19 before it is compared. See <see cref="HasElapsed"/>; the
/// evidence is in <c>docs/research/trigger-state.md</c> §5.</para>
///
/// <para>Nothing here decides <b>when</b> a trigger is evaluated. Q228 is still
/// open on the cadence, and no code in this class assumes an answer — though
/// the same capture bounds it: a threshold crossing showed up within one tick
/// of its predicted tick in four out of four measurements, so evaluation cannot
/// be rarer than about once per tick.</para>
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/trigger-state.md")]
public sealed class TriggerState
{
    /// <summary>
    /// Flag ids 0..127. The bank is 0x80 bytes wide because the timer bank
    /// begins 0x80 bytes after it.
    /// </summary>
    public const int FlagCount = 128;

    /// <summary>
    /// Id 128 — the first id past the flag bank, and <b>not a flag</b>.
    ///
    /// <para>Both dispatchers compare the id against this value before
    /// touching the bank and divert to a separate dword elsewhere on the same
    /// object (<c>+0x72A2</c>, with a companion set to 1 at <c>+0x72A6</c> and
    /// a virtual call at <c>+0x348</c>). Unlike a flag it takes the operand's
    /// <b>full 32 bits</b>. <b>What it is remains UNKNOWN</b> (Q229) — this
    /// type therefore models it as an opaque value rather than naming it
    /// something plausible.</para>
    /// </summary>
    public const byte SpecialFlagId = 0x80;

    /// <summary>
    /// Timer ids 0..15. <b>Sixteen, not the 256 the masked write path first
    /// suggested</b>, and two independent readings agree: the next known field
    /// on the object sits at <c>+0x4030</c>, leaving the bank at
    /// <c>+0x3FF0</c> exactly 0x40 bytes — sixteen dwords — and the condition
    /// path compares the id against <c>0x10</c> before touching it.
    /// </summary>
    public const int TimerCount = 16;

    /// <summary>
    /// Id 16 — the first id past the timer bank, and <b>not a timer</b>, the
    /// same shape as <see cref="SpecialFlagId"/> one past the flag bank.
    ///
    /// <para>The condition path compares the id against this value
    /// (<c>CMP DL,0x10</c> at <c>0x005184EB</c>) and on a match reads its value
    /// through a virtual call at slot <c>+0x34C</c> instead of indexing the
    /// bank. There is only <b>one</b> indexed store to the timer bank in the
    /// whole executable — action type 8's, which stamps the clock — so a write
    /// naming this id does not reach the bank either.</para>
    ///
    /// <para><b>It is heavily used and completely unidentified.</b> All 86
    /// action type 23 records in the campaign target it, and five type 2
    /// conditions test it. <b>What it is remains UNKNOWN (Q232)</b>, so this
    /// type refuses it rather than modelling a stored value it has not seen
    /// written — the read side is a virtual call and the write side is
    /// somewhere this reading has not reached, and they need not be the same
    /// field.</para>
    /// </summary>
    public const byte SpecialTimerId = 16;

    private readonly byte[] _flags = new byte[FlagCount];
    private readonly int[] _timers = new int[TimerCount];
    private readonly Dictionary<(int Tag, ushort Id), TriggerRecordState> _records = [];

    /// <summary>
    /// The value written when an action names <see cref="SpecialFlagId"/>.
    /// Opaque: see that constant.
    /// </summary>
    public int SpecialValue { get; set; }

    /// <summary>
    /// Set whenever <see cref="SpecialValue"/> is written, mirroring the
    /// companion the executable sets to 1 beside it. Its purpose is UNKNOWN;
    /// it is carried so a snapshot round-trips what the original would hold.
    /// </summary>
    public bool SpecialValueWritten { get; set; }

    /// <summary>
    /// A flag's value, <b>zero-extended</b>. The asymmetry is DK2's, not ours:
    /// see <see cref="Write"/>.
    /// </summary>
    public int Read(byte flagId)
    {
        if (flagId >= FlagCount)
            throw new ArgumentOutOfRangeException(
                nameof(flagId), flagId,
                $"Flag id {flagId} is past the bank; {SpecialFlagId} is the special target, "
                + "which is read through SpecialValue.");

        return _flags[flagId];
    }

    /// <summary>
    /// Applies one action record's write to a flag.
    ///
    /// <para><b>The operand is truncated to its low byte</b>, because the bank
    /// is bytes wide. The record carries 32 bits and only the special target
    /// receives all of them.</para>
    ///
    /// <para><b>Add and subtract are signed; reading is not.</b> The executable
    /// loads the flag as a <i>signed</i> char for the arithmetic and stores the
    /// result back as a byte, while every read zero-extends. A flag driven
    /// below zero by a subtraction therefore compares as 128..255 rather than
    /// as a negative number. That is reproduced, not corrected — it is exactly
    /// the class of quirk this project exists to keep.</para>
    /// </summary>
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
                $"Timer id {SpecialTimerId} is the special target, not a timer slot. Every "
                + "action type 23 record in the campaign names it and nobody knows what it is "
                + "yet (Q232).");

        if (timerId >= TimerCount)
            throw new ArgumentOutOfRangeException(
                nameof(timerId), timerId,
                $"Timer id {timerId} is past the {TimerCount}-slot bank. In DK2 this would "
                + "index past it into the object's next field; no shipped level does it.");

        return timerId;
    }

    /// <summary>
    /// A timer's stored stamp. <b>A timer holds the moment it was started, not
    /// a countdown</b>: the condition side compares the clock against
    /// <c>stamp + value</c>, so elapsed time is derived rather than counted
    /// down.
    /// </summary>
    public int ReadTimer(byte timerId) => _timers[CheckTimer(timerId)];

    /// <summary>Stamps a timer with <paramref name="now"/> — DK2's action type 8.</summary>
    public void StartTimer(byte timerId, int now) => _timers[CheckTimer(timerId)] = now;

    /// <summary>
    /// Sets a timer to an explicit stamp.
    ///
    /// <para><b>This is not DK2's action type 23</b>, which is what an earlier
    /// version of this comment claimed. Every one of that type's 86 shipped
    /// records names <see cref="SpecialTimerId"/>, and the bank has exactly one
    /// indexed store in the whole executable — type 8's. So type 23 does not
    /// write a timer slot at all, and this method exists for tests and for a
    /// snapshot restore.</para>
    /// </summary>
    public void SetTimer(byte timerId, int value) => _timers[CheckTimer(timerId)] = value;

    /// <summary>
    /// Ticks elapsed since the timer was stamped. <b>Only meaningful when
    /// <see cref="IsStarted"/></b> — see it for why.
    /// </summary>
    public int Elapsed(byte timerId, int now) => now - _timers[CheckTimer(timerId)];

    /// <summary>
    /// Whether the timer has ever been stamped.
    ///
    /// <para><b>A stamp of zero means "not started", and it is a real
    /// distinction rather than a convention.</b> The condition path opens with
    /// a test of the stored stamp and bails before comparing anything, so an
    /// unstarted timer does not read as "a very long time ago" — its condition
    /// simply does not hold. Tick 0 is therefore not a usable stamp.</para>
    /// </summary>
    public bool IsStarted(byte timerId) => _timers[CheckTimer(timerId)] != 0;

    /// <summary>
    /// Whether <paramref name="seconds"/> <b>seconds</b> have passed since the
    /// timer was stamped — the whole condition, in the shape DK2 evaluates it.
    ///
    /// <para><b>A deadline, not a subtraction.</b> The executable computes a
    /// deadline from the stamp and compares the clock against it, rather than
    /// subtracting and testing the difference. With a saturating clock the two
    /// are not the same, so the shape is kept.</para>
    ///
    /// <para><b>The threshold is in seconds, and an earlier version of this
    /// file said otherwise.</b> It claimed the authored value was consumed raw
    /// as ticks, on the strength of a scan that found no seconds-to-ticks
    /// multiply in the trigger path. That scan covered <c>0x517400</c> to
    /// <c>0x519A00</c> and no more, and the conversion is not in it. Two live
    /// captures of campaign level 1 settle the direction: for each timer
    /// condition, the tick at which <i>the specific flag its subtree writes</i>
    /// actually changed was compared against both predictions.</para>
    ///
    /// <para>Under seconds the residual is a constant <c>+1</c> tick — the
    /// sampler's own lag — in four out of four measurements across two runs,
    /// two timers and two thresholds. Under ticks the residual is exactly
    /// <c>3 * threshold + 1</c> every time, which is the arithmetic signature
    /// of a missing multiply by variable 19 rather than of noise. Between the
    /// two runs timer 2's stamp moved by 153 ticks and the seconds model, which
    /// has no free parameter, tracked it. Twelve further conditions predicted
    /// flags that would have had to change under ticks and did not.</para>
    ///
    /// <para>So a threshold of 600 is ten minutes, not 150 seconds.</para>
    /// </summary>
    public bool HasElapsed(byte timerId, int seconds, int now) =>
        IsStarted(timerId)
        && now >= _timers[CheckTimer(timerId)] + SimulationTiming.TicksFromSeconds(seconds);

    /// <summary>
    /// Compares two values with DK2's comparison codes. The routine at
    /// <c>0x00519980</c> implements 1 to 6 as signed <c>&lt;</c>,
    /// <c>&lt;=</c>, <c>==</c>, <c>&gt;</c>, <c>&gt;=</c> and <c>!=</c>.
    ///
    /// <para><b>Signed.</b> The previous reader exposed generic comparison
    /// operands as unsigned, which inverts every comparison against a negative
    /// threshold (ADR-0092).</para>
    /// </summary>
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
