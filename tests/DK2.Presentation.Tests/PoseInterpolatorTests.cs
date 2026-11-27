// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art;
using DK2.Assets;
using DK2.Core;
using DK2.Data.Model;
using DK2.Presentation;

namespace DK2.Presentation.Tests;

public class PoseInterpolatorTests
{
    [Theory]
    [InlineData(.5f, 2.625)]
    [InlineData(1.5f, 2.75)]
    [InlineData(-.2f, 2.5)]
    public void DrawSecondsAreTheTickPlusTheClampedFractionAtFourTicksASecond(float fraction, double seconds)
    {
        Assert.Equal(seconds, PoseInterpolator.DrawSeconds(new GameTick(10), fraction));
    }

    [Fact]
    public void PositionLerpsEachAxisInTilesWithTheFractionClamped()
    {
        var creature = new CreatureView(1, new PlayerId(1), posX: 3 * Fp32.One, posY: 2 * Fp32.One, default, default, 0,
            height: 2 * Fp32.One, previousPosX: Fp32.One, previousPosY: 4 * Fp32.One, previousHeight: Fp32.One);
        Assert.Equal(new DrawPoint(1.5f, 3.5f, 1.25f), PoseInterpolator.Position(creature, .25f));
        Assert.Equal(new DrawPoint(3f, 2f, 2f), PoseInterpolator.Position(creature, 2f));
        Assert.Equal(new DrawPoint(1f, 4f, 1f), PoseInterpolator.Position(creature, -1f));
    }

    [Theory]
    [InlineData(2000, 40, 88)]
    [InlineData(40, 2000, -88)]
    [InlineData(0, 1023, 1023)]
    [InlineData(0, 1024, -1024)]
    [InlineData(100, 100, 0)]
    public void ShortestArcTakesTheShortWayRound(int previous, int current, int turn)
    {
        Assert.Equal(turn, PoseInterpolator.ShortestArc(previous, current));
    }

    [Fact]
    public void AnObjectTurnsTheShortWayAcrossNorthWithTheHalfTurnOnItsHeading()
    {
        ObjectView pose = Object(heading: 40, previousHeading: 2000);
        // (2000 + 1024 + 88 / 2) units: half a turn past the short-way midpoint, 2048 north.
        Assert.Equal(3068f * 360f / 2048f, PoseInterpolator.ObjectTurn(pose, .5f).Yaw);
        Assert.Equal(180f, PoseInterpolator.ObjectTurn(Object(heading: 0, previousHeading: 0), .5f).Yaw);
    }

    [Fact]
    public void AnObjectsPitchIsNegatedAndItsRollIsNotAndBothTakeTheShortWay()
    {
        RenderEuler still = PoseInterpolator.ObjectTurn(Object(pitch: 512, roll: 512), .5f);
        Assert.Equal(-90f, still.Pitch);
        Assert.Equal(90f, still.Roll);
        RenderEuler wrapping = PoseInterpolator.ObjectTurn(Object(pitch: 8, previousPitch: 2040, roll: 2040, previousRoll: 8), .5f);
        Assert.Equal(-360f, wrapping.Pitch);
        Assert.Equal(0f, wrapping.Roll);
    }

    [Fact]
    public void MeshTurnsConvertUnitsToDegreesWithThePitchNegated()
    {
        Assert.Equal(new RenderEuler(270f, 45f, -90f), MeshOrientation.Mesh(1536, 512, 256));
        Assert.Equal(90f, MeshOrientation.Heading(512));
    }

    [Fact]
    public void AParticleTurnsTheShortWayThenGainsTheHalfTurn()
    {
        var particle = new ConstructionParticle
        {
            Definition = null!, PreviousHeadingRaw = 2040, HeadingRaw = 8,
            PreviousPitchRaw = 512, PitchRaw = 512, PreviousRollRaw = 0, RollRaw = 256,
        };
        RenderEuler turn = MeshOrientation.Particle(particle, .5f);
        Assert.Equal(540f, turn.Yaw);
        Assert.Equal(-90f, turn.Pitch);
        Assert.Equal(22.5f, turn.Roll);
        Assert.Equal(361.40625f + 180f, MeshOrientation.Particle(particle, 4f).Yaw);
    }

    [Theory]
    [InlineData(350f, 10f, .5f, 360f)]
    [InlineData(10f, 350f, .5f, 0f)]
    [InlineData(0f, 90f, 2f, 90f)]
    [InlineData(0f, 270f, .5f, -45f)]
    public void LerpAngleIsUnitysShortWayWithAClampedFraction(float from, float to, float fraction, float angle)
    {
        Assert.Equal(angle, MeshOrientation.LerpAngle(from, to, fraction));
    }

    [Fact]
    public void ClipStartsTheTurnAfterItsStartElseNow()
    {
        Assert.Equal(2d, AnimationClipRequest.StartSeconds(new GameTick(7), 99d));
        Assert.Equal(1.25d, AnimationClipRequest.StartSeconds(null, 1.25d));
    }

    [Fact]
    public void ACreatureClipIdentityDropsTheClipWhenNoAnimationPlaysAndCarriesTheReplacement()
    {
        var walk = AssetRef.ForCreature("Imp", AssetKind.Animation, "walk");
        var playing = new CreatureView(1, new PlayerId(1), 0, 0, default, walk, 0, playsAnimation: true,
            animationLoops: false, animationStartedAt: new GameTick(3), animationDeferred: true);
        var still = new CreatureView(1, new PlayerId(1), 0, 0, default, walk, 0, animationStartedAt: new GameTick(3));
        CreatureClipIdentity identity = AnimationClipRequest.Identity(playing, walk);
        Assert.Equal(new CreatureClipIdentity(walk, new GameTick(3), Loops: false, Deferred: true), identity);
        Assert.False(identity.Loops);
        Assert.True(identity.Deferred);
        Assert.Equal(default, AnimationClipRequest.Identity(still, walk).Clip);
        Assert.Equal(AnimationReplacement.Deferred, AnimationClipRequest.Replacement(playing));
        Assert.Equal(AnimationReplacement.Immediate, AnimationClipRequest.Replacement(still));
        Assert.Equal(AnimationClipRequest.Identity(playing, walk), AnimationClipRequest.Identity(playing, walk));
    }

    [Fact]
    public void AnObjectClipIdentityCarriesEachOfItsFields()
    {
        var mesh = AssetRef.ForObject("Gold", AssetKind.Mesh);
        var light = new TerrainLightColour(10, 20, 30);
        ObjectClipIdentity identity = AnimationClipRequest.Identity(Object(initialPhase: new Fp32(7), startedAt: new GameTick(5),
            loops: false, addedLight: light), mesh);
        Assert.Equal(new ObjectClipIdentity(mesh, new GameTick(5), Loops: false, light, new Fp32(7)), identity);
        Assert.Equal(identity, AnimationClipRequest.Identity(Object(initialPhase: new Fp32(7), startedAt: new GameTick(5),
            loops: false, addedLight: light), mesh));
        Assert.NotEqual(identity, AnimationClipRequest.Identity(Object(initialPhase: new Fp32(7), startedAt: new GameTick(5),
            loops: false, addedLight: light), AssetRef.ForObject("Gem", AssetKind.Mesh)));
        Assert.NotEqual(identity, AnimationClipRequest.Identity(Object(initialPhase: new Fp32(7), startedAt: new GameTick(6),
            loops: false, addedLight: light), mesh));
        Assert.NotEqual(identity, AnimationClipRequest.Identity(Object(initialPhase: new Fp32(7), startedAt: new GameTick(5),
            loops: true, addedLight: light), mesh));
        Assert.NotEqual(identity, AnimationClipRequest.Identity(Object(initialPhase: new Fp32(7), startedAt: new GameTick(5),
            loops: false), mesh));
        Assert.NotEqual(identity, AnimationClipRequest.Identity(Object(initialPhase: new Fp32(8), startedAt: new GameTick(5),
            loops: false, addedLight: light), mesh));
    }

    [Fact]
    public void TheArtClockTurnsAtTheSimulationsTicksPerSecond()
    {
        Assert.Equal(DK2.Simulation.SimulationTiming.OriginalTicksPerGameSecond, CreatureAnimationArt.TurnsPerSecond);
    }

    [Fact]
    public void AHeartDrawsItsDecayPhaseAndItsVerticalScale()
    {
        var heart = new DungeonHeartVisual(new Fp32(10), new Fp32(Fp32.One / 2), new Fp32(77));
        ObjectView beating = Object(scale: 2 * Fp32.One, texturePhase: new Fp32(5), heart: heart);
        Assert.Equal(new Fp32(77), beating.DrawnTexturePhase);
        Assert.Equal(2f, beating.MeshScale);
        Assert.Equal(1f, beating.MeshScaleY);
        ObjectView plain = Object(scale: 2 * Fp32.One, texturePhase: new Fp32(5));
        Assert.Equal(new Fp32(5), plain.DrawnTexturePhase);
        Assert.Equal(2f, plain.MeshScaleY);
        Assert.Null(Object().DrawnTexturePhase);
    }

    [Fact]
    public void ALightningPointLeavesTheRenderFrameAsXMinusZY()
    {
        Assert.Equal(new System.Numerics.Vector3(1, -3, 2),
            LightningControlPoint.FromRenderWorld(7, new System.Numerics.Vector3(1, 2, 3)).Position);
    }

    private static ObjectView Object(int heading = 0, int? previousHeading = null, int pitch = 0, int? previousPitch = null,
        int roll = 0, int? previousRoll = null, int scale = Fp32.One, Fp32? texturePhase = null,
        DungeonHeartVisual? heart = null, Fp32 initialPhase = default, GameTick? startedAt = null, bool loops = true,
        TerrainLightColour addedLight = default)
    {
        return new(1, 1, new PlayerId(1), new TilePos(1, 1), default, 0, Fp32.One, Fp32.One, Fp32.One, heading, scale,
            pitch: pitch, roll: roll, previousHeading: previousHeading, previousPitch: previousPitch,
            previousRoll: previousRoll, heartVisual: heart, texturePhase: texturePhase, animationInitialPhase: initialPhase,
            animationStartedAt: startedAt, animationLoops: loops, addedLight: addedLight);
    }
}
