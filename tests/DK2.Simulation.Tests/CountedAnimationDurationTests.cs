// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Simulation.Tests;

public class CountedAnimationDurationTests
{
    private static CreatureDef Definition(CreatureAnimationTiming timing)
    {
        var slots = new CreatureAnimationTiming[32];
        slots[14] = timing;
        return Dk2TerrainFixture.Database().CreatureById(7) with { ArtSlots = slots };
    }

    private static CreatureAnimationTiming WarlockRead => new()
    {
        ResourceType = 6, Flags = CreatureAnimationFlags.StartSegment | CreatureAnimationFlags.EndSegment,
        FrameCount = 58, FramesPerSecond = 30, StartSegmentFrames = 21, EndSegmentFrames = 22,
    };

    [Fact]
    public void CapturedWarlockReadIncludesStartMainEndAndRateClear()
    {
        Assert.Equal(69, AnimationTimingRules.CountedHoldTicks(Definition(WarlockRead), 14, 7));
    }

    [Theory]
    [InlineData(2, 13)]
    [InlineData(3, 17)]
    [InlineData(7, 33)]
    [InlineData(64, 261)]
    public void AnExactFourTickMainKeepsItsFinalRateClearTurn(int plays, int expected)
    {
        var timing = new CreatureAnimationTiming { ResourceType = 6, FrameCount = 30, FramesPerSecond = 30 };
        Assert.Equal(expected, AnimationTimingRules.CountedHoldTicks(Definition(timing), 14, plays));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void SinglePlayKeepsTheExistingDuration(int plays)
    {
        Assert.Equal(AnimationTimingRules.ActionHoldTicks(WarlockRead),
            AnimationTimingRules.CountedHoldTicks(Definition(WarlockRead), 14, plays));
    }

    [Fact]
    public void MissingTimingStillCompletesAtTheNextPoll()
    {
        Assert.Equal(1, AnimationTimingRules.CountedHoldTicks(Definition(default), 14, 7));
    }

    [Theory]
    [InlineData(127, 513)]
    [InlineData(128, 132)]
    [InlineData(255, 4)]
    [InlineData(256, 5)]
    public void CountBytesKeepTheNativeSignedLoopGateAndWrapping(int plays, int expected)
    {
        var timing = new CreatureAnimationTiming { ResourceType = 6, FrameCount = 30, FramesPerSecond = 30 };
        Assert.Equal(expected, AnimationTimingRules.CountedHoldTicks(Definition(timing), 14, plays));
    }

    [Fact]
    public void ConsecutiveQueriesResetTheScratchClock()
    {
        var simple = new CreatureAnimationTiming { ResourceType = 6, FrameCount = 30, FramesPerSecond = 30 };
        Assert.Equal(69, AnimationTimingRules.CountedHoldTicks(Definition(WarlockRead), 14, 7));
        Assert.Equal(17, AnimationTimingRules.CountedHoldTicks(Definition(simple), 14, 3));
        Assert.Equal(69, AnimationTimingRules.CountedHoldTicks(Definition(WarlockRead), 14, 7));
    }
}
