// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Simulation.Steering;

namespace DK2.Simulation.Tests.Steering;

/// <summary>Bearings and the one-tick turn (docs/research/steering-behaviours.md §5.3).</summary>
public class BearingTests
{
    /// <summary>The original's 257-entry table is floor(atan(i/256) · 2048 / 2π) exactly, which is
    /// why the table in code is generated from the formula rather than copied.</summary>
    [Fact]
    public void TheArctangentTableIsTheFormula()
    {
        for (int i = 0; i <= 256; i++)
        {
            int expected = (int)Math.Floor(Math.Atan(i / 256.0) * 2048 / (2 * Math.PI));
            Assert.Equal(expected, Bearing.ArctangentEntry(i));
        }
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 0, 0x200)]
    [InlineData(0, 1, 0x400)]
    [InlineData(-1, 0, 0x600)]
    [InlineData(0, -1, 0)]
    [InlineData(1, 1, 0x300)]
    [InlineData(-1, -1, 0x700)]
    [InlineData(1, -1, 0x100)]
    [InlineData(-1, 1, 0x500)]
    [InlineData(2, 1, 0x200 + 151)]
    public void TheBearingIsByOctantWithPlusXAtAQuarterTurn(int dx, int dy, int expected) =>
        Assert.Equal(expected, Bearing.Of(dx, dy));

    [Fact]
    public void TheDistanceIsTheShorterWayRound()
    {
        Assert.Equal(1024, Bearing.Distance(0, 1024));
        Assert.Equal(148, Bearing.Distance(100, 2000));
        Assert.Equal(148, Bearing.Distance(2000, 100));
        Assert.Equal(0, Bearing.Distance(700, 700));
    }

    /// <summary>A Warlock's 341 a tick reaches a target 1000 away in three ticks and lands on it
    /// exactly, because the last step is the remainder.</summary>
    [Fact]
    public void TheTurnStepsByTheRateAndLandsExactly()
    {
        int heading = 0;
        Assert.Equal(659, Bearing.TurnToward(ref heading, 1000, 341));
        Assert.Equal(318, Bearing.TurnToward(ref heading, 1000, 341));
        Assert.Equal(0, Bearing.TurnToward(ref heading, 1000, 341));
        Assert.Equal(1000, heading);
    }

    [Fact]
    public void TheTurnGoesTheShortWayAcrossZero()
    {
        int heading = 100;
        Assert.Equal(0, Bearing.TurnToward(ref heading, 2000, 512));
        Assert.Equal(2000, heading);
    }

    [Fact]
    public void AZeroRateNeverArrives()
    {
        int heading = 0;
        Assert.Equal(300, Bearing.TurnToward(ref heading, 300, 0));
        Assert.Equal(0, heading);
    }
}
