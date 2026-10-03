// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Simulation.Tests;

public sealed class AnimationTimingTests
{
    [Theory]
    [InlineData(51, 8)]
    [InlineData(69, 11)]
    [InlineData(29, 5)]
    [InlineData(84, 13)]
    [InlineData(16, 4)]
    public void GetUpTimingMatchesIndependentCapturedHolds_StronglyInferredFromBinary(int frames, int expectedTicks)
    {
        var animation = new CreatureAnimationTiming
        {
            ResourceType = 6, FrameCount = (ushort)frames, FramesPerSecond = 30,
        };
        Assert.Equal(expectedTicks, AnimationTimingRules.ActionHoldTicks(animation));
    }

    [Theory]
    [InlineData(26, 5)]
    [InlineData(39, 7)]
    [InlineData(27, 5)]
    [InlineData(14, 3)]
    [InlineData(15, 3)]
    public void KnockdownIncludesTheRateClearUpdate_StronglyInferredFromBinary(int frames, int expectedTicks)
    {
        var animation = new CreatureAnimationTiming
        {
            ResourceType = 6, FrameCount = (ushort)frames, FramesPerSecond = 30,
        };
        Assert.Equal(expectedTicks, AnimationTimingRules.ActionHoldTicks(animation));
    }

    [Fact]
    public void HornysSegmentedFallbackIncludesBothSegments_StronglyInferredFromBinary()
    {
        var animation = new CreatureAnimationTiming
        {
            ResourceType = 6, Flags = (CreatureAnimationFlags)537,
            FrameCount = 30, FramesPerSecond = 30, StartSegmentFrames = 6, EndSegmentFrames = 14,
        };
        Assert.Equal(9, AnimationTimingRules.ActionHoldTicks(animation));
    }

    [Fact]
    public void MissingAnimationCompletesAtTheNextPoll_StronglyInferredFromBinary() =>
        Assert.Equal(1, AnimationTimingRules.ActionHoldTicks(default));
}
