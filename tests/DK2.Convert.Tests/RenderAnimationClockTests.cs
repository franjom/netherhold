// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art;
using DK2.Core;
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

    [Theory]
    [InlineData(0, 1024)]
    [InlineData(512, 128)]
    [InlineData(1024, 256)]
    [InlineData(2048, 512)]
    public void WalkingRecalculatesItsNextAdvanceFromActualVelocity(int speed, int nextStep)
    {
        var clock = new RenderAnimationClock(Timing(), loop: true, authoredMovementScale: new Fp32(16384));
        clock.Advance(new Fp32(speed));
        Assert.Equal(.25f, clock.Sample(1).Phase);
        clock.Advance(new Fp32(speed));
        Assert.Equal((1024 + nextStep) / 4096f, clock.Sample(1).Phase);
    }

    [Fact]
    public void AZeroAuthoredMovementScaleRetainsTheFirstWalkingPose()
    {
        var clock = new RenderAnimationClock(Timing(), loop: true, authoredMovementScale: new Fp32(0));
        clock.Advance(new Fp32(1000));
        clock.Advance(new Fp32(1000));
        Assert.Equal(0f, clock.Sample(1).Phase);
    }

    [Fact]
    public void DefaultInstallationRetainsScalar4096ForCallersWithoutAnInitialMovementSpeed()
    {
        var clock = new RenderAnimationClock(Timing(), loop: true, authoredMovementScale: new Fp32(4096));
        clock.Advance(new Fp32(512));
        Assert.Equal(4095 / 4096f, clock.Sample(1).Phase);
        clock.Advance(new Fp32(512));
        Assert.Equal(511 / 4096f, clock.Sample(1).Phase);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(256, 256)]
    [InlineData(512, 512)]
    public void WalkingInstallationUsesItsNativeSpeedBeforeTheFirstAdvance(int speed, int phase)
    {
        var clock = new RenderAnimationClock(Timing(), loop: true,
            authoredMovementScale: new Fp32(4096), initialMovementSpeed: new Fp32(speed));
        clock.Advance(new Fp32(speed));
        Assert.Equal(phase / 4096f, clock.Sample(1).Phase);
    }

    [Fact]
    public void MovementScalingPrecedesTheAdvanceRateCap()
    {
        var clock = new RenderAnimationClock(Timing(frames: 1, fps: 8), loop: true,
            authoredMovementScale: new Fp32(4096));
        clock.Advance(new Fp32(128));
        clock.Advance(new Fp32(128));
        Assert.Equal(1023 / 4096f, clock.Sample(1).Phase);
    }

    [Fact]
    public void AQueuedReplacementPlaysTheOutgoingEndBeforeInstallingWithoutAnAdvance()
    {
        var outgoing = Timing() with { Flags = CreatureAnimationFlags.EndSegment, EndSegmentFrames = 2 };
        var clock = new RenderAnimationClock(outgoing, loop: true);
        clock.Advance();
        clock.Replace(Timing(frames: 16), loop: true, AnimationReplacement.Deferred);
        for (int i = 0; i < 4; i++) clock.Advance();
        Assert.Equal(0, clock.ResourceRevision);
        Assert.Equal(AnimationSegment.End, clock.Sample(1).Segment);
        clock.Advance();
        Assert.Equal(1, clock.ResourceRevision);
        Assert.Equal(0f, clock.Sample(1).Phase);
        clock.Advance();
        Assert.Equal(.125f, clock.Sample(1).Phase);
    }

    [Fact]
    public void TheLatestQueuedReplacementWins()
    {
        var clock = new RenderAnimationClock(Timing(), loop: true);
        clock.Advance();
        clock.Replace(Timing(frames: 16), true, AnimationReplacement.Deferred);
        clock.Replace(Timing(frames: 32), true, AnimationReplacement.Deferred);
        for (int i = 0; i < 3; i++) clock.Advance();
        Assert.Equal(1, clock.ResourceRevision);
        Assert.Equal(0f, clock.Sample(1).Phase);
        clock.Advance();
        Assert.Equal(.0625f, clock.Sample(1).Phase);
    }

    [Fact]
    public void ImmediateReplacementClearsThePendingResourceAndStartsFromZero()
    {
        var clock = new RenderAnimationClock(Timing(), loop: true);
        clock.Advance();
        clock.Replace(Timing(frames: 16), true, AnimationReplacement.Deferred);
        clock.Replace(Timing(frames: 32), true, AnimationReplacement.Immediate);
        Assert.Equal(1, clock.ResourceRevision);
        Assert.Equal(0f, clock.Sample(.5f).Phase);
        for (int i = 0; i < 20; i++) clock.Advance();
        Assert.Equal(1, clock.ResourceRevision);
    }

    [Fact]
    public void FrozenPlaybackHoldsBothInterpolationEndpoints()
    {
        var clock = new RenderAnimationClock(Timing(), loop: true);
        clock.Advance();
        clock.Advance(hold: true);
        Assert.Equal(.25f, clock.Sample(0).Phase);
        Assert.Equal(.25f, clock.Sample(1).Phase);
        clock.Advance();
        Assert.Equal(.5f, clock.Sample(1).Phase);
    }

    [Fact]
    public void ZeroWalkingSpeedReusesThePreviousScalarOnceThenUsesOne()
    {
        var clock = new RenderAnimationClock(Timing(), true, new Fp32(16384));
        clock.Advance(new Fp32(1024));
        clock.Advance(new Fp32(0));
        clock.Advance(new Fp32(0));
        Assert.Equal(1536 / 4096f, clock.Sample(1).Phase);
        clock.Advance(new Fp32(0));
        Assert.Equal(1536 / 4096f, clock.Sample(1).Phase);
    }

    [Fact]
    public void TheAuthoredStopLoopFlagWinsOverTheRequestedLoop()
    {
        var clock = new RenderAnimationClock(Timing() with { Flags = (CreatureAnimationFlags)0x80 }, true);
        for (int i = 0; i < 5; i++) clock.Advance();
        Assert.Equal(1f, clock.Sample(1).Phase);
    }

    [Fact]
    public void TheAlternateRateAdvancesEvenWhenTheNominalRateIsZero()
    {
        var clock = new RenderAnimationClock(Timing(fps: 0), true);
        clock.Advance(phaseStepOverride: new Fp32(512));
        Assert.Equal(.125f, clock.Sample(1).Phase);
    }
}
