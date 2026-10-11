// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art;
using DK2.Data.Model;

namespace DK2.Convert.Tests;

public class RenderAnimationClockTests
{
    private static CreatureAnimationTiming Timing(ushort frames = 8, ushort fps = 8) => new()
    {
        ResourceType = 6, FrameCount = frames, FramesPerSecond = fps,
    };

    [Fact]
    public void ALoopBlendsTheQuantizedPhaseAcrossItsWrap()
    {
        var clock = new RenderAnimationClock(Timing(), loop: true);
        for (int i = 0; i < 4; i++) clock.Advance();
        Assert.Equal(.75f, clock.Sample(0).Phase);
        Assert.Equal(.875f, clock.Sample(.5f).Phase);
        Assert.Equal(1f, clock.Sample(1).Phase);
        clock.Advance();
        Assert.Equal(.125f, clock.Sample(.5f).Phase);
    }

    [Fact]
    public void AOneShotKeepsTheLastPoseAfterItCompletes()
    {
        var clock = new RenderAnimationClock(Timing(), loop: false);
        for (int i = 0; i < 20; i++) clock.Advance();
        Assert.Equal(1f, clock.Sample(.5f).Phase);
    }

    [Fact]
    public void AStartToMainTransitionDrawsTheCorrectSeparateMesh()
    {
        var timing = Timing() with { Flags = CreatureAnimationFlags.StartSegment, StartSegmentFrames = 2 };
        var clock = new RenderAnimationClock(timing, loop: true);
        clock.Advance();
        clock.Advance();
        Assert.Equal(AnimationSegment.Start, clock.Sample(0).Segment);
        Assert.Equal(AnimationSegment.Main, clock.Sample(1).Segment);
        Assert.InRange(clock.Sample(1).Phase, .249f, .251f);
    }

    [Fact]
    public void AOneShotReachesItsEndMeshWhileALoopKeepsItsMainMesh()
    {
        var timing = Timing() with { Flags = CreatureAnimationFlags.EndSegment, EndSegmentFrames = 2 };
        var once = new RenderAnimationClock(timing, loop: false);
        var loop = new RenderAnimationClock(timing, loop: true);
        for (int i = 0; i < 4; i++) { once.Advance(); loop.Advance(); }
        Assert.Equal(AnimationSegment.Main, once.Sample(1).Segment);
        Assert.Equal(AnimationSegment.Main, loop.Sample(1).Segment);
        once.Advance();
        Assert.Equal(AnimationSegment.End, once.Sample(1).Segment);
        for (int i = 0; i < 3; i++) once.Advance();
        Assert.Equal(AnimationSegment.End, once.Sample(1).Segment);
        Assert.Equal(1f, once.Sample(1).Phase);
    }

    [Fact]
    public void TheDescriptorRateUsesIntegerDivisionRatherThanNominalSeconds()
    {
        var clock = new RenderAnimationClock(Timing(frames: 7, fps: 12), loop: true);
        clock.Advance();
        Assert.Equal(1755 / 4096f, clock.Sample(1).Phase);
    }

    [Fact]
    public void AnAbsentRateRetainsTheFirstPose()
    {
        var clock = new RenderAnimationClock(Timing(fps: 0), loop: true);
        clock.Advance();
        Assert.Equal(0f, clock.Sample(1).Phase);
    }
}
