// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Tests;

/// <summary>
/// <see cref="TriggerState"/>'s operations, as the executable performs them.
/// The bank sizes are checked against the shipped campaign in
/// <c>DK2.Behavior.Tests.TriggerStateOnRealDataTests</c>.
/// </summary>
public class TriggerStateTests
{
    [Fact]
    public void AFlagIsAByte_SoTheOperandIsTruncatedToItsLowEight()
    {
        // The record carries 32 bits and the bank is bytes wide. Only the
        // special target gets all 32.
        var triggers = new TriggerState();

        triggers.Write(3, FlagWriteMode.Assign, 0x1234);

        Assert.Equal(0x34, triggers.Read(3));
    }

    [Fact]
    public void AddAndSubtractAreSigned_ButReadingZeroExtends()
    {
        // DK2's asymmetry, reproduced rather than fixed: the arithmetic loads
        // the flag as a signed char, and every read zero-extends. A flag driven
        // below zero therefore reads as 128..255, not as a negative number, and
        // a condition comparing it against 0 sees a LARGE value.
        var triggers = new TriggerState();

        triggers.Write(1, FlagWriteMode.Assign, 3);
        triggers.Write(1, FlagWriteMode.Subtract, 5);

        Assert.Equal(254, triggers.Read(1));
        Assert.True(TriggerState.Compare(4, triggers.Read(1), 0));

        // And it wraps at the top too.
        triggers.Write(2, FlagWriteMode.Assign, 250);
        triggers.Write(2, FlagWriteMode.Add, 10);
        Assert.Equal(4, triggers.Read(2));
    }

    [Fact]
    public void FlagIdOneHundredAndTwentyEightIsNotAFlag_AndTakesTheWholeDword()
    {
        // The id one past the bank. Both dispatchers compare against it before
        // touching the bank, and divert to a separate dword - which is why it
        // keeps all 32 bits where a flag keeps 8. What it actually IS is
        // unknown (Q229), so this asserts the mechanism and not a meaning.
        var triggers = new TriggerState();

        Assert.False(triggers.SpecialValueWritten);

        triggers.Write(TriggerState.SpecialFlagId, FlagWriteMode.Assign, 0x1234);

        Assert.Equal(0x1234, triggers.SpecialValue);
        Assert.True(triggers.SpecialValueWritten);

        // Not truncated, and not stored in the bank.
        triggers.Write(TriggerState.SpecialFlagId, FlagWriteMode.Add, 1);
        Assert.Equal(0x1235, triggers.SpecialValue);
        Assert.Equal(0, triggers.Read(0));
    }

    [Fact]
    public void ReadingPastTheBankIsRefused_RatherThanFallingIntoTheTimers()
    {
        // In the original, an id above 128 would index past the flag bank and
        // into the timer bank 0x80 bytes later, because the write path masks
        // the id with 0xFF and bounds it no further. No shipped level does it
        // (asserted over the corpus), so this refuses instead of reproducing a
        // memory overrun whose consequences nobody has observed - Q230 records
        // what would have to be watched before choosing differently.
        var triggers = new TriggerState();

        Assert.Throws<ArgumentOutOfRangeException>(() => triggers.Read(TriggerState.SpecialFlagId));
        Assert.Throws<ArgumentOutOfRangeException>(() => triggers.Read(200));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => triggers.Write(200, FlagWriteMode.Assign, 1));
    }

    [Fact]
    public void ATimerStoresWhenItStarted_NotHowLongIsLeft()
    {
        // A timer is a stamp and elapsed time is derived by subtraction, which
        // is what the condition side does. Nothing counts down, so nothing has
        // to be decremented once a tick.
        var triggers = new TriggerState();

        triggers.StartTimer(5, now: 100);

        Assert.Equal(100, triggers.ReadTimer(5));
        Assert.Equal(40, triggers.Elapsed(5, now: 140));
        Assert.Equal(0, triggers.Elapsed(5, now: 100));

        // Action type 23 sets a stamp outright rather than stamping now.
        triggers.SetTimer(5, 20);
        Assert.Equal(120, triggers.Elapsed(5, now: 140));
    }

    [Fact]
    public void TheSixComparisonCodesAreSigned()
    {
        // Signed is the correction, not the code list: the previous reader
        // exposed generic operands as unsigned, which inverts every comparison
        // against a negative threshold (ADR-0092).
        Assert.True(TriggerState.Compare(1, -5, 1));
        Assert.False(TriggerState.Compare(4, -5, 1));

        Assert.True(TriggerState.Compare(2, 7, 7));
        Assert.True(TriggerState.Compare(3, 7, 7));
        Assert.True(TriggerState.Compare(5, 7, 7));
        Assert.False(TriggerState.Compare(6, 7, 7));

        Assert.Throws<ArgumentOutOfRangeException>(() => TriggerState.Compare(0, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => TriggerState.Compare(7, 1, 1));
    }

    [Fact]
    public void TheBanksAreWorldStateAndTheyAreHashed()
    {
        // The reason they are in the hash at all: an action writes a flag and a
        // later condition reads it, so two worlds disagreeing about one will
        // diverge as soon as that condition is evaluated - late, and far from
        // the cause.
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState a = WorldState.FromMap(database, Dk2TerrainFixture.Map(), [(byte)3]);
        WorldState b = WorldState.FromMap(database, Dk2TerrainFixture.Map(), [(byte)3]);

        Assert.Equal(Hash(a), Hash(b));

        a.Triggers.Write(9, FlagWriteMode.Assign, 1);
        Assert.NotEqual(Hash(a), Hash(b));

        b.Triggers.Write(9, FlagWriteMode.Assign, 1);
        Assert.Equal(Hash(a), Hash(b));

        // Timers too, and the special target with them.
        a.Triggers.StartTimer(0, now: 4);
        Assert.NotEqual(Hash(a), Hash(b));
        b.Triggers.StartTimer(0, now: 4);
        Assert.Equal(Hash(a), Hash(b));

        a.Triggers.Write(TriggerState.SpecialFlagId, FlagWriteMode.Assign, 77);
        Assert.NotEqual(Hash(a), Hash(b));

        static ulong Hash(WorldState world)
        {
            var hash = new Hash64();
            world.HashInto(ref hash);
            return hash.Value;
        }
    }
}
