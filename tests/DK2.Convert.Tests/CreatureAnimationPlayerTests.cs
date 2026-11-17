// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Art;
using DK2.Art.Kmf;
using DK2.Core;
using DK2.Data.Model;

namespace DK2.Convert.Tests;

public class CreatureAnimationPlayerTests
{
    // Eight frames at eight a second: a quarter of the clip (1024) a turn.
    private static readonly CreatureAnimationTiming Quarter = new() { ResourceType = 6, FrameCount = 8, FramesPerSecond = 8 };

    [Fact]
    public void TheStartTurnIsTheFloorOfTheStartAndItsOwnTurnIsAdvanced()
    {
        var player = new CreatureAnimationPlayer<int>(new Surface());
        // 0.45 s is turn 1.8: the start turn is 1, and the draw at it advances once.
        player.Configure(Clips(), 1, Quarter, loop: true, start: .45);
        Assert.Equal(1024, player.Clock!.Phase);
        player.Draw(.7);
        Assert.Equal(2048, player.Clock.Phase);
    }

    [Fact]
    public void ADrawCatchesUpEveryTurnSinceTheLastIncludingTheCurrentOne()
    {
        var player = new CreatureAnimationPlayer<int>(new Surface());
        player.Configure(Clips(), 1, Quarter, loop: false, start: 0);
        Assert.Equal(1024, player.Clock!.Phase);
        player.Draw(.5);
        Assert.Equal(3072, player.Clock.Phase);
        player.Draw(.6);
        Assert.Equal(3072, player.Clock.Phase);
    }

    [Fact]
    public void ACompletedLoopRaisesTheBeatUntilConsumed()
    {
        var player = new CreatureAnimationPlayer<int>(new Surface());
        player.Configure(Clips(), 1, Quarter, loop: true, start: 0);
        Assert.False(player.BeatPending);
        player.Draw(.75);
        Assert.True(player.BeatPending);
        player.ConsumeBeat();
        Assert.False(player.BeatPending);
        player.AdvanceHealthPhase(new Fp32(3072));
        Assert.False(player.BeatPending);
        player.AdvanceHealthPhase(new Fp32(1024));
        Assert.True(player.BeatPending);
    }

    [Fact]
    public void ADeferredClipDrawsTheOldOneUpToNowThenWaitsForItToFinish()
    {
        var surface = new Surface();
        var player = new CreatureAnimationPlayer<int>(surface);
        player.Configure(Clips(), 1, Quarter, loop: true, start: 0);
        player.Configure(Clips(), 2, Quarter, loop: true, start: .25, replacement: AnimationReplacement.Deferred);
        Assert.Equal(new[] { 1 }, surface.Installs);
        Assert.Equal(2048, player.Clock!.Phase);
        player.Draw(.5);
        Assert.Equal(new[] { 1 }, surface.Installs);
        int revision = player.InstallationRevision;
        player.Draw(.75);
        Assert.Equal(new[] { 1, 2 }, surface.Installs);
        Assert.Equal(revision + 1, player.InstallationRevision);
    }

    [Fact]
    public void ADeferredClipReplacesAFinishedOneAtOnce()
    {
        var surface = new Surface();
        var player = new CreatureAnimationPlayer<int>(surface);
        player.Configure(Clips(), 1, Quarter, loop: false, start: 0);
        player.Draw(1);
        player.Configure(Clips(), 2, Quarter, loop: true, start: 1.25, replacement: AnimationReplacement.Deferred);
        Assert.Equal(new[] { 1, 2 }, surface.Installs);
    }

    [Fact]
    public void FootstepMarksAreNormalizedByTheClipsFrameCount()
    {
        var player = new CreatureAnimationPlayer<int>(new Surface());
        // Frame 6 of 8 is phase 3072, crossed by the turn from 3072 back to 0 a quarter in; raw 6 is not.
        player.Configure(Clips(), 1, Quarter, loop: true, start: 0, initialPhase: new Fp32(2048),
            footstepMarks: new FootstepMarkPair(6, 0));
        Assert.Equal(0, player.FootstepCrossing);
        player.Draw(.2499);
        Assert.Equal(0, player.FootstepCrossing);
        player.Draw(.3125);
        Assert.Equal(1, player.FootstepCrossing);
    }

    [Fact]
    public void TheSurfaceShowsASegmentOnceAndTakesEveryDrawsSample()
    {
        var surface = new Surface();
        var player = new CreatureAnimationPlayer<int>(surface);
        RenderAnimation?[] clips = Clips();
        player.Configure(clips, 1, Quarter, loop: true, start: 0);
        player.Draw(.1);
        player.Draw(.2);
        Assert.Equal(new[] { AnimationSegment.Main }, surface.Shown);
        Assert.Equal(3, surface.Uploads);
        var expected = new Vector3[3];
        clips[1]!.Sample(player.Clock!.Sample(.8f).Phase, false, expected);
        Assert.Equal(expected, surface.Last);
    }

    [Fact]
    public void ControlPointsAreTheDrawnSampleAndLightningTakesIdsBelow2000()
    {
        var surface = new Surface();
        var player = new CreatureAnimationPlayer<int>(surface);
        RenderAnimation clip = new(KmfReader.Read(AnimatedKmfFixture.TwoFramesWithControls(
            (1u << 16, KmfControlPoint.Head), (2u << 16, 10), (0, 2000), (0, 1999))));
        Assert.False(player.TryGetControlPoint(KmfControlPoint.Head, out _));
        player.Configure([null, clip, null], 1, Quarter, loop: true, start: .1);
        Assert.True(player.TryGetControlPoint(KmfControlPoint.Head, out Vector3 head));
        Assert.Equal(surface.Last[1], head);
        Assert.False(player.TryGetControlPoint(77, out _));
        var points = new List<AnimationPoint>();
        player.CollectLightningPoints(points);
        Assert.Equal(new[] { new AnimationPoint(10, surface.Last[2]), new AnimationPoint(1999, surface.Last[0]) }, points);
    }

    [Fact]
    public void StopDropsAWaitingClip()
    {
        var surface = new Surface();
        var player = new CreatureAnimationPlayer<int>(surface);
        player.Configure(Clips(), 1, Quarter, loop: true, start: 0);
        player.Configure(Clips(), 2, Quarter, loop: true, start: 0, replacement: AnimationReplacement.Deferred);
        player.Stop();
        Assert.Null(player.Clock);
        player.Configure(Clips(), 3, Quarter, loop: false, start: 0);
        player.Draw(5);
        Assert.Equal(new[] { 1, 3 }, surface.Installs);
    }

    [Fact]
    public void DrawingAllocatesNothing()
    {
        // The first pass loads and compiles every path (which allocates); the second must not allocate.
        long allocated = 0;
        for (int pass = 0; pass < 2; pass++)
        {
            var player = new CreatureAnimationPlayer<int>(new Surface { Logs = false });
            player.Configure(Clips(), 1, Quarter, loop: true, start: 0, footstepMarks: new FootstepMarkPair(2, 6));
            player.Draw(.01, new Fp32(4096));
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 1; i < 200; i++) player.Draw(i * .03, new Fp32(4096));
            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        }
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void EachClipKeepsItsOwnSampleBufferAcrossRestarts()
    {
        // Per clip, not per segment: a walk and an idle of different vertex counts swap without a new buffer each start.
        var surface = new Surface { Logs = false };
        var player = new CreatureAnimationPlayer<int>(surface);
        RenderAnimation?[] walk = Clips(), idle = Clips();
        player.Configure(walk, 1, Quarter, loop: true, start: 0);
        Vector3[] walkBuffer = surface.Last;
        player.Configure(idle, 2, Quarter, loop: true, start: .25);
        Vector3[] idleBuffer = surface.Last;
        player.Configure(walk, 1, Quarter, loop: true, start: .5);
        Assert.NotSame(walkBuffer, idleBuffer);
        Assert.Same(walkBuffer, surface.Last);
    }

    [Fact]
    public void ArtResourcesNameTheirSegmentsTimingAndMovementScale()
    {
        var resource = new Dk2ArtResource("walk", 6)
        {
            FrameCount = 20, FramesPerSecond = 10, MovementScale = 300, StartSegmentFrames = 4, EndSegmentFrames = 5,
            AnimationFlags = CreatureAnimationFlags.Present | CreatureAnimationFlags.EndSegment,
        };
        Assert.False(CreatureAnimationArt.HasSegment(resource, AnimationSegment.Start));
        Assert.True(CreatureAnimationArt.HasSegment(resource, AnimationSegment.Main));
        Assert.True(CreatureAnimationArt.HasSegment(resource, AnimationSegment.End));
        Assert.Equal(new CreatureAnimationTiming
        {
            ResourceType = 6, FrameCount = 20, FramesPerSecond = 10, StartSegmentFrames = 4, EndSegmentFrames = 5,
            Flags = CreatureAnimationFlags.Present | CreatureAnimationFlags.EndSegment,
        }, CreatureAnimationArt.Timing(resource));
        Assert.Equal(new Fp32(300), CreatureAnimationArt.AuthoredMovementScale(resource, new Fp32(1)));
        Assert.Null(CreatureAnimationArt.AuthoredMovementScale(resource, null));
    }

    [Fact]
    public void TheDrawThatInstallsADeferredClipShowsAndUploadsTheNewOne()
    {
        var surface = new Surface();
        var player = new CreatureAnimationPlayer<int>(surface);
        player.Configure(Clips(), 1, Quarter, loop: true, start: 0);
        RenderAnimation next = new(KmfReader.Read(AnimatedKmfFixture.KeyedAtFramesZeroAndTwo()));
        player.Configure([null, next, null], 2, Quarter, loop: true, start: .25, replacement: AnimationReplacement.Deferred);
        player.Draw(.5);
        surface.Events.Clear();
        player.Draw(.75);
        Assert.Equal(new[] { "install 2", "show Main", "upload Main" }, surface.Events);
        Assert.Equal(new[] { AnimationSegment.Main }, surface.Shown);
        var expected = new Vector3[3];
        next.Sample(player.Clock!.Sample(0f).Phase, false, expected);
        Assert.Equal(expected, surface.Last);
    }

    [Fact]
    public void AFootstepWindowSpansEveryTurnSinceTheLastDraw()
    {
        var player = new CreatureAnimationPlayer<int>(new Surface());
        // Frame 1 of 8 is phase 512. The draw at turn 2.25 looks back over both turns since the draw at turn 0
        // (phases 0 to 2304); one turn back would start at 1024 and miss it.
        player.Configure(Clips(), 1, Quarter, loop: true, start: 0, footstepMarks: new FootstepMarkPair(1, 0));
        Assert.Equal(0, player.FootstepCrossing);
        player.Draw(.5625);
        Assert.Equal(1, player.FootstepCrossing);
    }

    private static RenderAnimation?[] Clips() => [null, new RenderAnimation(KmfReader.Read(AnimatedKmfFixture.TwoFrames())), null];

    private sealed class Surface : ICreatureAnimationSurface<int>
    {
        public List<int> Installs { get; } = [];
        public List<AnimationSegment> Shown { get; } = [];
        public int Uploads { get; private set; }
        public Vector3[] Last { get; private set; } = [];
        public List<string> Events { get; } = [];
        public bool Logs { get; init; } = true;

        public void Install(RenderAnimation?[] clips, int resources)
        {
            Installs.Add(resources);
            Shown.Clear();
            if (Logs) Events.Add($"install {resources}");
        }

        public void Show(AnimationSegment segment)
        {
            Shown.Add(segment);
            if (Logs) Events.Add($"show {segment}");
        }

        public void Upload(AnimationSegment segment, Vector3[] vertices)
        {
            Uploads++;
            Last = vertices;
            if (Logs) Events.Add($"upload {segment}");
        }
    }
}
