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
/// <para>Nothing here decides <b>when</b> a trigger is evaluated. That is a
/// separate and still-open question (Q228), and no code in this class assumes
/// an answer to it. What Q228 <i>did</i> settle is the unit: the clock a timer
/// stamps is the game's own tick counter, so every value in the timer bank is
/// in ticks of 250 ms.</para>
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
    /// Timer ids 0..255. The write path masks the id with <c>0xFF</c> and
    /// applies no bound of its own, so the bank must be at least this wide.
    /// </summary>
    public const int TimerCount = 256;

    private readonly byte[] _flags = new byte[FlagCount];
    private readonly int[] _timers = new int[TimerCount];

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

    /// <summary>
    /// A timer's stored stamp. <b>A timer holds the moment it was started, not
    /// a countdown</b>: the condition side subtracts it from the current clock,
    /// so elapsed time is derived rather than counted down.
    /// </summary>
    public int ReadTimer(byte timerId) => _timers[timerId];

    /// <summary>Stamps a timer with <paramref name="now"/> — DK2's action type 8.</summary>
    public void StartTimer(byte timerId, int now) => _timers[timerId] = now;

    /// <summary>Sets a timer to an explicit stamp — DK2's action type 23.</summary>
    public void SetTimer(byte timerId, int value) => _timers[timerId] = value;

    /// <summary>
    /// Ticks elapsed since the timer was stamped. <b>Only meaningful when
    /// <see cref="IsStarted"/></b> — see it for why.
    /// </summary>
    public int Elapsed(byte timerId, int now) => now - _timers[timerId];

    /// <summary>
    /// Whether the timer has ever been stamped.
    ///
    /// <para><b>A stamp of zero means "not started", and it is a real
    /// distinction rather than a convention.</b> The condition path opens with
    /// a test of the stored stamp and bails before comparing anything, so an
    /// unstarted timer does not read as "a very long time ago" — its condition
    /// simply does not hold. Tick 0 is therefore not a usable stamp.</para>
    /// </summary>
    public bool IsStarted(byte timerId) => _timers[timerId] != 0;

    /// <summary>
    /// Whether <paramref name="value"/> ticks have passed since the timer was
    /// stamped — the whole condition, in the shape DK2 evaluates it.
    ///
    /// <para><b>A deadline, not a subtraction.</b> The executable computes
    /// <c>stamp + value</c> and compares the clock against it, and it is
    /// <b>ticks throughout</b>: the authored threshold is consumed raw, with no
    /// seconds-to-ticks multiply anywhere in the trigger code, while the rest
    /// of the game multiplies a second-authored duration by variable 19 before
    /// adding it to a stamp. So a threshold of 600 is 150 seconds, not ten
    /// minutes.</para>
    /// </summary>
    public bool HasElapsed(byte timerId, int value, int now) =>
        IsStarted(timerId) && now >= _timers[timerId] + value;

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

    public void HashInto(ref Hash64 hash)
    {
        foreach (byte f in _flags) hash.Add(f);
        foreach (int t in _timers) hash.Add(t);
        hash.Add(SpecialValue);
        hash.Add(SpecialValueWritten ? (byte)1 : (byte)0);
    }
}
