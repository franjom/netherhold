// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Art;
using DK2.Art.Kmf;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation.Tests;

/// <summary>The small scene rules moved from Unity in godot step 2j part B: the heart pulse, the gem sparkle gate,
/// the head table, the frame helper, the flower layers, the dollar clock and the brightness exponent.</summary>
public sealed class SceneRuleTests
{
    private static ObjectView Heart(byte owner, EffectId mana, DungeonHeartVisual? visual = null) =>
        new(1, 13, new PlayerId(owner), new TilePos(5, 5), default, 0, 100, 200, 300, 0, Fp32.One,
            heartVisual: visual ?? new DungeonHeartVisual(new Fp32(7), new Fp32(Fp32.One), default) { ManaEffect = mana });

    [Fact]
    public void HealthChangesComeOutOldestFirstAndAdvanceEveryHeartOfTheirOwner()
    {
        var pulse = new HeartPulse();
        pulse.QueueHealthChange(new PlayerId(3), new DungeonHeartVisual(new Fp32(11), default, default));
        pulse.QueueHealthChange(new PlayerId(4), new DungeonHeartVisual(new Fp32(12), default, default));
        Assert.True(pulse.TryTakeHealthAdvance(out PlayerId owner, out Fp32 step));
        Assert.Equal((new PlayerId(3), new Fp32(11)), (owner, step));
        Assert.True(HeartPulse.IsHeartOf(Heart(3, EffectId.None), owner));
        Assert.False(HeartPulse.IsHeartOf(Heart(4, EffectId.None), owner));
        Assert.False(HeartPulse.IsHeartOf(new ObjectView(1, 13, new PlayerId(3), default, default, 0, 0, 0, 0, 0, 0), owner));
        pulse.ClearHealthAdvances();
        Assert.False(pulse.TryTakeHealthAdvance(out _, out _));
    }

    [Fact]
    public void ABeatIsConsumedOnlyForAVisiblePendingHeartAndTheClockIsReadOnlyThen()
    {
        var pulse = new HeartPulse();
        int reads = 0;
        long now = 5000;
        Func<EffectTime> clock = () => { reads++; return new EffectTime(now); };
        ObjectView heart = Heart(3, new EffectId(42));
        var plain = new ObjectView(1, 13, new PlayerId(3), default, default, 0, 0, 0, 0, 0, 0);

        Assert.Equal(default, pulse.TryBeat(plain, true, true, clock, new GameTick(100)));
        Assert.Equal(default, pulse.TryBeat(heart, false, true, clock, new GameTick(100)));
        Assert.Equal(default, pulse.TryBeat(heart, true, false, clock, new GameTick(100)));
        Assert.Equal(0, reads);
        Assert.False(pulse.TryBeat(heart, true, true, clock, new GameTick(100)).Consumed);   // the gate's first beat starts it
        now = 6001;
        HeartBeat beat = pulse.TryBeat(heart, true, true, clock, new GameTick(100));
        Assert.Equal(2, reads);
        Assert.True(beat.Consumed);
        Assert.Equal(new ViewEffectRequest(new EffectId(42), new PlayerId(3),
            new FpPosition3(new Fp32(100), new Fp32(200), new Fp32(300))), beat.Effect);
    }

    [Fact]
    public void ABeatRequestsNoManaEffectDuringTheIntroOrWithoutOne()
    {
        var pulse = new HeartPulse();
        long now = 0;
        Func<EffectTime> clock = () => new EffectTime(now);
        pulse.TryBeat(Heart(3, new EffectId(42)), true, true, clock, new GameTick(36));
        now = 1001;
        Assert.Equal(new HeartBeat(true, null), pulse.TryBeat(Heart(3, new EffectId(42)), true, true, clock, new GameTick(36)));
        now = 2002;
        Assert.Equal(new HeartBeat(true, null), pulse.TryBeat(Heart(3, EffectId.None), true, true, clock, new GameTick(37)));
        now = 3003;
        Assert.NotNull(pulse.TryBeat(Heart(3, new EffectId(42)), true, true, clock, new GameTick(37)).Effect);
    }

    [Fact]
    public void AGemCarrierSparklesAtMostOncePerFiftyMillisecondQuantum()
    {
        var gate = new GemSparkleGate();
        var imp = new CreatureId(7);
        Assert.True(gate.TryPass(imp, new EffectTime(1000)));
        Assert.False(gate.TryPass(imp, new EffectTime(1049)));
        Assert.True(gate.TryPass(new CreatureId(8), new EffectTime(1049)));
        Assert.True(gate.TryPass(imp, new EffectTime(1050)));
        Assert.False(gate.TryPass(imp, new EffectTime(1020)));
        gate.Forget(imp);
        Assert.True(gate.TryPass(imp, new EffectTime(1099)));
    }

    [Fact]
    public void AnEffectTiedToACreatureFindsItsHeadAsLastDrawn()
    {
        var heads = new CreatureHeadTable();
        var imp = new CreatureId(7);
        var head = new FpPosition3(new Fp32(1), new Fp32(2), new Fp32(3));
        Assert.Equal(EffectAttachment.Gone, heads.Attachment(imp, KmfControlPoint.Head));
        heads.Add(imp);
        Assert.Equal(EffectAttachment.NotFound, heads.Attachment(imp, KmfControlPoint.Head));
        heads.Set(imp, head);
        Assert.Equal(EffectAttachment.At(head), heads.Attachment(imp, KmfControlPoint.Head));
        Assert.Equal(EffectAttachment.NotFound, heads.Attachment(imp, 10001));
        heads.Add(imp);
        Assert.Equal(EffectAttachment.At(head), heads.Attachment(imp, KmfControlPoint.Head));
        heads.Remove(imp);
        Assert.Equal(EffectAttachment.Gone, heads.Attachment(imp, KmfControlPoint.Head));
        heads.Set(imp, head);
        heads.Clear();
        Assert.Equal(EffectAttachment.Gone, heads.Attachment(imp, KmfControlPoint.Head));
    }

    [Fact]
    public void ADrawnPointGoesBackToNativeUnitsTruncatedAsXMinusZY()
    {
        Assert.Equal(new Vector3(1f, 3f, -2f), RenderWorld.Ground(1f, 2f, 3f));
        Assert.Equal(new FpPosition3(new Fp32(6144), new Fp32(11264), new Fp32(1024)),
            RenderWorld.ToNative(new Vector3(1.5f, .25f, -2.75f)));
        Assert.Equal(new FpPosition3(new Fp32(4096), new Fp32(0), new Fp32(-4096)),
            RenderWorld.ToNative(new Vector3(1.0001f, -1.0001f, .0001f)));
    }

    private static StatusFlower Flower(bool drawRing = true, int ring = 2, bool arcs = true, bool imprison = true,
        string? objective = "Objective", bool dollar = false) => new(7, 3, new PlayerId(3), new StatusFlowerColour(1, 2, 3),
        default, false, ring, "SL-03", arcs, .25f, objective, imprison, drawRing, dollar);

    [Fact]
    public void AFlowerDrawsTheRingTheCentreImprisonAndTheObjectiveInThatOrder()
    {
        var layers = new StatusFlowerLayer[StatusFlowerLayers.MaxLayers];
        Assert.Equal(4, StatusFlowerLayers.Build(Flower(), layers));
        Assert.Equal(new StatusFlowerLayer[]
        {
            new("H-02", 0, -1f, true), new("SL-03", 1, .25f, false), new("Imprison", 2, -1f, false),
            new("Objective", 3, -1f, false),
        }, layers);
    }

    [Fact]
    public void EachLaterFlowerLayerIsOnlyDrawnWhenItsGateIsOpen()
    {
        var layers = new StatusFlowerLayer[StatusFlowerLayers.MaxLayers];
        Assert.Equal(1, StatusFlowerLayers.Build(Flower(drawRing: false, arcs: false, imprison: false, objective: null), layers));
        Assert.Equal(new StatusFlowerLayer("SL-03", 1, StatusFlowerLayers.NoWedge, false), layers[0]);
        Assert.Equal(2, StatusFlowerLayers.Build(Flower(drawRing: false, imprison: false), layers));
        Assert.Equal(new StatusFlowerLayer("Objective", 3, -1f, false), layers[1]);
        Assert.Equal(0, StatusFlowerLayers.Build(Flower(dollar: true), layers));
    }

    [Theory]
    [InlineData(-3, "H-00")]
    [InlineData(5, "H-05")]
    [InlineData(9, "H-05")]
    public void TheRingIsClampedToItsSixTextures(int ring, string texture)
    {
        var layers = new StatusFlowerLayer[StatusFlowerLayers.MaxLayers];
        StatusFlowerLayers.Build(Flower(ring: ring), layers);
        Assert.Equal(texture, layers[0].Name);
    }

    [Fact]
    public void TheFlowersLiftAndFolder()
    {
        Assert.Equal(.01f, StatusFlowerLayers.CentreLift);
        Assert.Equal("GUI\\moods\\", StatusFlowerLayers.Folder);
    }

    [Fact]
    public void TheDollarClockAdvancesAfterEachDollarAndRestartsAfterAFlower()
    {
        var clock = new FlowerDollarClock();
        Assert.Equal(0f, clock.Phase(1f));
        clock.Advance();
        Assert.Equal(.25f, clock.Phase(1f));   // 30 frames at 30 a second: a quarter of the clip a turn
        clock.Advance();
        Assert.Equal(.375f, clock.Phase(.5f));
        clock.Reset();
        clock.Advance();
        Assert.Equal(0f, clock.Phase(1f));
        for (int i = 0; i < 5; i++) clock.Advance();
        Assert.Equal(.25f, clock.Phase(1f));   // it loops
        Assert.Equal(180f, FlowerDollarClock.YawDegrees);

        var first = new FlowerDollarClock();
        first.Phase(0f);
        for (int i = 0; i < 5; i++) first.Advance();
        Assert.Equal(.25f, first.Phase(1f));   // the first clock loops too
    }

    [Fact]
    public void ARestartedDollarClockRunsAsANewOne()
    {
        var restarted = new FlowerDollarClock();
        restarted.Phase(0f);
        for (int i = 0; i < 3; i++) restarted.Advance();
        restarted.Reset();
        var fresh = new RenderAnimationClock(FlowerDollarClock.Timing, loop: true);
        for (int i = 0; i < 9; i++)
        {
            Assert.Equal(fresh.Sample(.3f).Phase, restarted.Phase(.3f));
            fresh.Advance();
            restarted.Advance();
        }
        Assert.Equal(new CreatureAnimationTiming { ResourceType = 6, FrameCount = 30, FramesPerSecond = 30 }, FlowerDollarClock.Timing);
    }

    [Theory]
    [InlineData(0d, 0f)]
    [InlineData(2.6d, .4f)]
    [InlineData(1.25d, 0f)]
    public void TheDollarIsDrawnAtTheWallClocksQuarterSeconds(double seconds, float fraction) =>
        Assert.Equal(fraction, FlowerDollarClock.Fraction(seconds), 5);

    [Fact]
    public void BrightnessIsDrawnOnlyAwayFromTheDefaultGamma()
    {
        Assert.Equal(30, DisplaySettings.DefaultGamma);
        Assert.Null(DisplaySettings.BrightnessExponent(30));
        Assert.Equal(30f / 45f, DisplaySettings.BrightnessExponent(45));
        Assert.Equal(2f, DisplaySettings.BrightnessExponent(15));
    }
}
