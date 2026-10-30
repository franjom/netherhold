// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation.Tests;

public class ShotTrailTests
{
    [Fact]
    public void ABodyAttachmentUsesThePreviousNativePointRatherThanTheCurrentPosition()
    {
        var creature = new CreatureState
        {
            PosX = 9000, PosY = 8000, PosZ = 7000,
            LastPosX = 1000, LastPosY = 2000, LastPosZ = 300,
        };
        EffectAttachment attachment = EffectAttachment.AtPreviousBody(creature, new Fp32(4096));
        Assert.True(attachment.CreatureExists);
        Assert.Equal(new FpPosition3(new Fp32(1000), new Fp32(2000), new Fp32(4396)), attachment.Point);
        Assert.Equal(EffectAttachment.Gone, EffectAttachment.AtPreviousBody(null, new Fp32(4096)));
    }

    [Fact]
    public void Flag8000BypassesOnlyTheCameraGateAndATrailBypassesBothGates()
    {
        bool fogVisible = true;
        var model = new RoomConstructionParticles(Database(), 1, () => new EffectTime(0),
            effectVisible: _ => fogVisible, cameraVisible: _ => false, floorHeight: _ => default);
        ViewEffectRequest request = Trail() with { AttachedShotId = ShotId.None, Visibility = EffectVisibility.Gated };
        model.Request(request);
        model.AdvanceTo(new EffectTime(51));
        Assert.Empty(model.Particles);
        var bypass = new RoomConstructionParticles(Database(cameraBypass: true), 1, () => new EffectTime(0),
            effectVisible: _ => fogVisible, cameraVisible: _ => false, floorHeight: _ => default);
        bypass.Request(request);
        bypass.AdvanceTo(new EffectTime(51));
        Assert.Single(bypass.Particles);
        fogVisible = false;
        EffectTime clock = default;
        var hidden = new RoomConstructionParticles(Database(cameraBypass: true), 1, () => clock,
            effectVisible: _ => fogVisible, cameraVisible: _ => false, floorHeight: _ => default);
        hidden.Request(request);
        hidden.AdvanceTo(new EffectTime(51));
        Assert.Empty(hidden.Particles);
        clock = new EffectTime(51);
        hidden.Request(Trail());
        hidden.AdvanceTo(new EffectTime(101));
        Assert.Single(hidden.Particles);
    }

    [Fact]
    public void ATiedTrailOutlivesItsAuthoredHealthAndMovesWithoutDraggingBornElements()
    {
        FpPosition3 point = At(1000);
        bool exists = true;
        var model = new RoomConstructionParticles(Database(), 1, () => new EffectTime(0),
            floorHeight: _ => default,
            shotAttachment: id => exists ? ShotEffectAttachment.At(point, At(9000)) : ShotEffectAttachment.Gone);
        model.Request(Trail());
        model.AdvanceTo(new EffectTime(51));
        ConstructionParticle first = Assert.Single(model.Particles);
        Assert.Equal(1000, first.XRaw);
        point = At(2000);
        model.AdvanceTo(new EffectTime(101));
        Assert.Equal(1000, first.XRaw);
        Assert.Contains(model.Particles, p => p.XRaw == 2000);
        point = At(3000);
        model.AdvanceTo(new EffectTime(151));
        Assert.Equal(3, model.Particles.Count);
        exists = false;
        model.AdvanceTo(new EffectTime(201));
        Assert.Equal(2, model.Particles.Count);
        var lights = new List<EffectLightView>();
        model.CollectLights(lights);
        Assert.Single(lights);
        model.AdvanceTo(new EffectTime(251));
        model.AdvanceTo(new EffectTime(301));
        model.CollectLights(lights);
        Assert.Empty(lights);
    }

    [Fact]
    public void TheTrailBypassesVisibilityButItsDeathEffectUsesTheGate()
    {
        bool exists = true;
        var model = new RoomConstructionParticles(Database(), 1, () => new EffectTime(0),
            effectVisible: _ => false, floorHeight: _ => default,
            shotAttachment: _ => exists ? ShotEffectAttachment.At(At(1000), At(9000)) : ShotEffectAttachment.Gone);
        model.Request(Trail());
        model.AdvanceTo(new EffectTime(51));
        Assert.Single(model.Particles);
        exists = false;
        for (int step = 2; step <= 7; step++) model.AdvanceTo(new EffectTime(step * 50 + 1));
        Assert.Empty(model.Particles);
    }

    [Fact]
    public void ImmediateRemovalDropsAllLiveChildrenWhenTheShotDisappears()
    {
        bool exists = true;
        var model = new RoomConstructionParticles(Database(), 1, () => new EffectTime(0),
            floorHeight: _ => default,
            shotAttachment: _ => exists ? ShotEffectAttachment.At(At(1000), At(9000)) : ShotEffectAttachment.Gone);
        model.Request(Trail() with { RemoveWithShot = true });
        model.AdvanceTo(new EffectTime(51));
        Assert.Single(model.Particles);
        exists = false;
        model.AdvanceTo(new EffectTime(101));
        Assert.Empty(model.Particles);
    }

    private static ViewEffectRequest Trail() => new(new EffectId(10), new PlayerId(3), At(1000), new Dk2Angle(256))
    {
        AttachedShotId = new ShotId(1), Visibility = EffectVisibility.Ungated,
    };

    private static FpPosition3 At(int x) => new(new Fp32(x), default, new Fp32(3000));

    private static GameDatabase Database(bool cameraBypass = false)
    {
        var art = new KwdArtResource("spark", 0, new byte[12], 2, 0, 0, 0);
        var element = new EffectElementDef([])
        {
            Name = "spark", EffectElementId = 20, ArtResource = art,
            MinHp = 3, MaxHp = 3, MinScale = 4096, MaxScale = 4096,
        };
        var trail = new EffectDef([])
        {
            Name = "trail", EffectId = 10, ArtResource = new("", 0, new byte[12], 0, 0, 0, 0),
            Light = new(0, 0, 0, 100, 255, 0, 0, 0, 1), GenerateIds = [20], GenerationType = 4,
            MinHp = 1, MaxHp = 1, ElementsPerTurn = 1, DeathEffectId = 11,
            Flags = cameraBypass ? 0x8000u : 0u,
        };
        return new([], [], [], new GlobalVariables(), effects: [trail, trail with { EffectId = 11, DeathEffectId = 0,
            Light = new(0, 0, 0, 0, 0, 0, 0, 0, 0) }], effectElements: [element]);
    }
}
