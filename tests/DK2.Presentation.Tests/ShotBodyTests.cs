// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Assets;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

public class ShotBodyTests
{
    [Fact]
    public void MeshBodiesUseAuthoredScaleAndYawWithoutPitchOrRoll()
    {
        byte[] block = new byte[12]; BitConverter.GetBytes(8192).CopyTo(block, 0);
        ShotDef definition = Definition(new("arrow", 0, block, 5, 0, 0, 0));
        var shot = new ShotState { Id = 1, ShotTypeId = 1, PosX = 1000, PosY = 2000, VelX = 100, Health = 3 };
        var model = new ShotBodyParticles(Database(definition), 1);
        model.Capture([shot], 1);
        ConstructionParticle body = Assert.Single(model.Particles);
        Assert.Equal(8192, body.ScaleRaw);
        Assert.Equal(512, body.HeadingRaw);
        Assert.Equal((0, 0), (body.PitchRaw, body.RollRaw));
        Assert.Equal(AssetRef.ForShot("test"), body.SpriteAsset);
    }

    [Fact]
    public void SpritePlaybackAdvancesOnGameTicksRatherThanTheEffectClock()
    {
        byte[] block = new byte[12]; BitConverter.GetBytes((ushort)16).CopyTo(block, 8);
        var model = new ShotBodyParticles(Database(Definition(new("spark", 4, block, 3, 0, 0, 0))), 1);
        var shot = new ShotState { Id = 1, ShotTypeId = 1, Age = 3, Health = 4 };
        model.Capture([shot], 1);
        Assert.Equal(768, Assert.Single(model.Particles).PhaseRaw);
        model.Capture([shot], .5f);
        Assert.Equal(640, Assert.Single(model.Particles).PhaseRaw);
    }

    [Fact]
    public void AnimatedMeshesUseTheNativeQuantizedPhaseAtFourTicksASecond()
    {
        byte[] block = new byte[12]; BitConverter.GetBytes((ushort)60).CopyTo(block, 0);
        BitConverter.GetBytes((ushort)30).CopyTo(block, 4);
        var model = new ShotBodyParticles(Database(Definition(new("cyclone", 0, block, 6, 0, 0, 0))), 1);
        model.Capture([new ShotState { Id = 1, ShotTypeId = 1, Age = 2, Health = 4 }], 1);
        Assert.Equal(1024, Assert.Single(model.Particles).MeshPhaseRaw);
    }

    [Fact]
    public void AStoppedMeshReachesPhaseOneRatherThanStoppingJustShortOfItsLastFrame()
    {
        byte[] block = new byte[12]; BitConverter.GetBytes((ushort)60).CopyTo(block, 0);
        BitConverter.GetBytes((ushort)30).CopyTo(block, 4);
        var model = new ShotBodyParticles(Database(Definition(new("cyclone", 0x80, block, 6, 0, 0, 0))), 1);
        model.Capture([new ShotState { Id = 1, ShotTypeId = 1, Age = 12, Health = 4 }], 1);
        Assert.Equal(4096, Assert.Single(model.Particles).MeshPhaseRaw);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1024, 14)]
    [InlineData(4096, 59)]
    public void MeshFrameMappingUsesFrameCountMinusOne(int phase, int expected)
    {
        Assert.Equal(expected, ShotBodyParticles.FrameIndex(new Fp32(phase), 60));
    }

    [Fact]
    public void BodyCaptureIsReadOnlyAndDropsARemovedShot()
    {
        var shot = new ShotState { Id = 1, ShotTypeId = 1, Age = 2, Health = 4, PosX = 1000, PosY = 2000 };
        var before = Hash64.Start(); shot.HashInto(ref before);
        var model = new ShotBodyParticles(Database(Definition(new("ball", 0, new byte[12], 2, 0, 0, 0))), 1);
        model.Capture([shot], .5f);
        var after = Hash64.Start(); shot.HashInto(ref after);
        Assert.Equal(before.Value, after.Value);
        Assert.Single(model.Particles);
        model.Capture([], 1);
        Assert.Empty(model.Particles);
    }

    [Fact]
    public void AShotLightUsesTheInterpolatedPointAndItsOwnDescriptor()
    {
        ShotDef definition = Definition(new("arrow", 0, new byte[12], 5, 0, 0, 0)) with
        { Light = new(10, 20, 30, 4096, 1, 100, 200, 255, 255) };
        var model = new ShotBodyParticles(Database(definition), 1);
        model.Capture([new ShotState { Id = 1, ShotTypeId = 1, Health = 2, PosX = 1234, PosY = 5678 }], 1);
        EffectLightView light = Assert.Single(model.Lights);
        Assert.Equal(-1, light.Id);
        Assert.Equal((1234f, 5678f, 4096f), (light.PosX, light.PosY, light.Height));
        Assert.Equal(definition.Light, light.Light);
    }

    [Fact]
    public void PositionInterpolationUsesAllThreeObservationAxes()
    {
        Assert.Equal(new FpPosition3(new(150), new(300), new(450)),
            ShotBodyParticles.Interpolate(new(new(100), new(200), new(300)), new(new(300), new(600), new(900)), .25f));
    }

    [InstallFact]
    public void ShippedShotBodiesHaveNoLightsOrSegmentedMeshAnimations()
    {
        GameDatabase database = DK2.Data.GameDatabaseLoader.Load(Dk2InstallGate.Path);
        Assert.All(database.Shots.Values, s => Assert.Equal(0u, s.Light.Flags));
        Assert.All(database.Shots.Values.Where(s => s.MeshResource.Type == 6),
            s => Assert.Equal(0u, s.MeshResource.Flags & 0x18));
    }

    private static GameDatabase Database(ShotDef definition) => new([], [], [], new GlobalVariables(), shots: [definition]);
    private static ShotDef Definition(KwdArtResource art) => new([])
    {
        Name = "test", ShotId = 1, MeshResource = art, Light = new(0, 0, 0, 0, 0, 0, 0, 0, 0), SoundCategory = "",
    };
}
