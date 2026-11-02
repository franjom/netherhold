// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Media;

namespace DK2.Presentation.Tests;

/// <summary>DK2's frequent-event throttle, from <c>0x00567080</c>, <c>0x00566FC0</c> and the
/// request gates <c>0x00567810</c>/<c>0x005678F0</c> (sound-effects.md §4).</summary>
public sealed class SoundThrottleTests
{
    private static readonly SoundId BattleCall = new(0xD6);
    private static readonly SoundId Hover = new(0x30F);   // a listed interface event; which widget plays it is inferred
    private static readonly SoundId Unlisted = new(0xDF);

    [Theory]
    [InlineData(0u, SoundFrameLoad.Normal)]
    [InlineData(300u, SoundFrameLoad.Normal)]
    [InlineData(301u, SoundFrameLoad.Slow)]
    [InlineData(499u, SoundFrameLoad.Slow)]
    [InlineData(500u, SoundFrameLoad.Heavy)]
    public void AFrameIsClassifiedByItsElapsedTime(uint elapsed, SoundFrameLoad load)
    {
        var throttle = new SoundThrottle(1000);
        throttle.BeginFrame(1000 + elapsed);
        Assert.Equal(load, throttle.Load);
    }

    [Fact]
    public void AClockThatRunsBackwardsCountsAsHeavy()
    {
        var throttle = new SoundThrottle(1000);
        throttle.BeginFrame(999);
        Assert.Equal(SoundFrameLoad.Heavy, throttle.Load);
    }

    [Fact]
    public void ANormalFrameAdmitsEveryRepeat()
    {
        var throttle = new SoundThrottle(0);
        throttle.BeginFrame(16);
        for (int i = 0; i < 10; i++) Assert.True(throttle.Admit(BattleCall));
    }

    [Fact]
    public void AHeavyFrameDropsListedEventsOnly()
    {
        var throttle = new SoundThrottle(0);
        throttle.BeginFrame(600);
        Assert.False(throttle.Admit(BattleCall));
        Assert.False(throttle.Admit(Hover));
        Assert.True(throttle.Admit(Unlisted));
        Assert.True(throttle.Admit(new SoundId(0x400)));
    }

    /// <summary>The 29 listed events, read at <c>0x0066F738</c>.</summary>
    [Fact]
    public void ExactlyTheTwentyNineListedEventsAreThrottled()
    {
        uint[] listed = [0xE5, 0x314, 0x332, 0x315, 0x333, 0x331, 0x337, 0x338, 0x147, 0x33F, 0xDC, 0x150, 0xD6,
            0xDB, 0x114, 0x1BB, 0x1BC, 0xD7, 0xD8, 0x334, 0x109, 0xF1, 0xF4, 0x110, 0xCB, 0xCC, 0xCA, 0xD0, 0x30F];
        var throttle = new SoundThrottle(0);
        throttle.BeginFrame(600);
        for (uint id = 1; id < 0x400; id++)
            Assert.Equal(!listed.Contains(id), throttle.Admit(new SoundId(id)));
    }

    /// <summary>On a slow frame an admitted event waits the gap table's next value plus one
    /// frames' worth of aging: the rotation starts at 0, so the first gap is table[1] = 1 and
    /// the second table[2] = 5; aging stops at 1, and 1 admits.</summary>
    [Fact]
    public void ASlowFrameSpacesAnEventByTheRotatingGapTable()
    {
        var throttle = new SoundThrottle(0);
        uint now = 0;
        void SlowFrame() => throttle.BeginFrame(now += 400);

        SlowFrame();
        Assert.True(throttle.Admit(BattleCall));   // counter 1 + table[1] = 2
        Assert.False(throttle.Admit(BattleCall));
        SlowFrame();                                // 2 -> 1
        Assert.True(throttle.Admit(BattleCall));   // counter 1 + table[2] = 6
        for (int frame = 0; frame < 4; frame++)
        {
            SlowFrame();
            Assert.False(throttle.Admit(BattleCall));
        }
        SlowFrame();                                // 6 -> 1 after five frames
        Assert.True(throttle.Admit(BattleCall));
    }

    [Fact]
    public void TheRotationIsSharedByEveryEventAndSurvivesAReset()
    {
        var throttle = new SoundThrottle(0);
        throttle.BeginFrame(400);
        Assert.True(throttle.Admit(BattleCall));   // rotation 1
        Assert.True(throttle.Admit(Hover));        // rotation 2: Hover's counter is 6
        throttle.Reset(400);
        throttle.BeginFrame(800);
        Assert.True(throttle.Admit(Hover));        // counters cleared, rotation 3: table[3] = 2
        Assert.False(throttle.Admit(Hover));
        throttle.BeginFrame(1200);
        Assert.False(throttle.Admit(Hover));       // 3 -> 2
    }

    [Fact]
    public void ANormalFrameNeitherConsumesNorAdvancesTheRotation()
    {
        var throttle = new SoundThrottle(0);
        throttle.BeginFrame(10);
        Assert.True(throttle.Admit(BattleCall));
        throttle.BeginFrame(410);
        Assert.True(throttle.Admit(BattleCall));   // first slow admission: table[1] = 1 -> counter 2
        Assert.False(throttle.Admit(BattleCall));
        throttle.BeginFrame(810);
        Assert.True(throttle.Admit(BattleCall));
    }
}
