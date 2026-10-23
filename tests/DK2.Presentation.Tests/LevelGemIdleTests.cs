// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Presentation;

namespace DK2.Presentation.Tests;

/// <summary>Object process state 50 (<c>0x004A3C30</c>), the loose Portal Gem's bob, spin and
/// once-a-second idle effect.</summary>
public class LevelGemIdleTests
{
    [Theory]
    [InlineData(0, 0x1400)]
    [InlineData(1, 0x1400 + 97)] // 0x61F7 >> 8
    [InlineData(2, 0x1400 + 181)] // 0xB504 >> 8
    [InlineData(3, 0x1400 + 236)] // 0xEC83 >> 8
    [InlineData(4, 0x1400 + 256)] // 0x10000 >> 8
    [InlineData(9, 0x1400 - 97)] // rounded toward zero, as the process's bias does
    [InlineData(12, 0x1400 - 256)]
    [InlineData(16, 0x1400)] // sixteen turns a bob
    public void TheGemBobsOnTheSineTableAboutAQuarterTileUp(long turn, int height) =>
        Assert.Equal(height, LevelGemIdle.Height(turn));

    [Fact]
    public void TheGemSpinsAFullTurnIn32Turns()
    {
        Assert.Equal(0x100, LevelGemIdle.Heading(0x80, 2));
        Assert.Equal(0x80, LevelGemIdle.Heading(0x80, 32));
    }

    [Fact]
    public void TheIdleEffectComesOnceAGameSecondOnDk2sTurn()
    {
        // The process tests DK2's turn, our tick + 1 (0x00517490; LevelGemIdle.Turn), so in a
        // four-tick second our tick 3 is DK2's turn 4 and emits, and our tick 0 (turn 1) does not.
        Assert.Equal(4, DK2.Simulation.SimulationTiming.TicksPerSecond);
        bool[] emits = new bool[8];
        for (long tick = 0; tick < emits.Length; tick++) emits[tick] = LevelGemIdle.EmitsOn(tick + 1);
        Assert.Equal(new[] { false, false, false, true, false, false, false, true }, emits);
    }
}
