// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation.Tests;

public class ViewEffectParticleTests
{
    [Fact]
    public void NewEffectsWaitForTheViewGateAndEmitTheirRemainingWaves()
    {
        var model = new RoomConstructionParticles(Database(), 7, () => new EffectTime(0));
        model.Emit(Request());
        Assert.Empty(model.Particles);
        model.AdvanceTo(new EffectTime(50));
        Assert.Empty(model.Particles);
        model.AdvanceTo(new EffectTime(51));
        Assert.Equal(4, model.Particles.Count);
        model.AdvanceTo(new EffectTime(101));
        Assert.Equal(8, model.Particles.Count);
        model.AdvanceTo(new EffectTime(151));
        Assert.Equal(8, model.Particles.Count);
    }

    [Fact]
    public void DelayedUpdatesConsumeParentLifetimeWithoutInventingMissedWaves()
    {
        var model = new RoomConstructionParticles(Database(), 7, () => new EffectTime(0));
        model.Emit(Request());
        model.AdvanceTo(new EffectTime(201));
        Assert.Equal(4, model.Particles.Count);
        model.AdvanceTo(new EffectTime(251));
        Assert.Equal(4, model.Particles.Count);
    }

    [Fact]
    public void RandomSlotGenerationEmitsOneElementPerPass()
    {
        var model = new RoomConstructionParticles(Database(0x480), 7, () => new EffectTime(0));
        model.Emit(Request());
        model.AdvanceTo(new EffectTime(51));
        Assert.Equal(2, model.Particles.Count);
    }

    [Fact]
    public void GroundBirthsUseTheNativeKeeperPaletteAndKeepAuthoredSpriteColours()
    {
        var model = new RoomConstructionParticles(Database(), 7, () => new EffectTime(0));
        model.Emit(Request());
        model.AdvanceTo(new EffectTime(51));
        ConstructionParticle keeper = model.Particles.First(p => p.Definition.EffectElementId == 1);
        ConstructionParticle authored = model.Particles.First(p => p.Definition.EffectElementId == 2);
        Assert.Equal((230, 50, 50), ((int)keeper.Red, (int)keeper.Green, (int)keeper.Blue));
        Assert.Equal((70, 80, 130), ((int)authored.Red, (int)authored.Green, (int)authored.Blue));
        Assert.All(model.Particles, p => Assert.Equal(4096, p.HeightRaw));
    }

    [Fact]
    public void HiddenEffectsDoNotConsumeTheVisibleParticleStream()
    {
        bool visible = false;
        var filtered = new RoomConstructionParticles(Database(), 7, () => new EffectTime(0),
            effectVisible: _ => visible);
        var reference = new RoomConstructionParticles(Database(), 7, () => new EffectTime(0));
        filtered.Emit(Request());
        visible = true;
        filtered.Emit(Request());
        reference.Emit(Request());
        filtered.AdvanceTo(new EffectTime(51));
        reference.AdvanceTo(new EffectTime(51));
        Assert.Equal(reference.Particles.Select(p => (p.XRaw, p.YRaw, p.HeightRaw)),
            filtered.Particles.Select(p => (p.XRaw, p.YRaw, p.HeightRaw)));
    }

    [Fact]
    public void InjectedFloorHeightControlsGroundEffectBirths()
    {
        var model = new RoomConstructionParticles(Database(), 7, () => new EffectTime(0),
            floorHeight: _ => new Fp32(4499));
        model.Emit(Request());
        model.AdvanceTo(new EffectTime(51));
        Assert.All(model.Particles, p => Assert.Equal(4499, p.HeightRaw));
    }

    [Fact]
    public void AuthoredSpriteColoursRemainBelowTheKeeperColourMinimum()
    {
        var model = new RoomConstructionParticles(Database(authoredColour: (10, 20, 30)),
            7, () => new EffectTime(0));
        model.Emit(Request());
        model.AdvanceTo(new EffectTime(51));
        ConstructionParticle authored = model.Particles.First(p => p.Definition.EffectElementId == 2);
        Assert.Equal((10, 20, 30), ((int)authored.Red, (int)authored.Green, (int)authored.Blue));
    }

    [Fact]
    public void BirthCollisionReflectsPenetrationAgainstTheFloorAtTheChildPoint()
    {
        var model = new RoomConstructionParticles(Database(originRange: 8, elasticity: 32768),
            7, () => new EffectTime(0), floorHeight: ChildFloor);
        model.Emit(Request());
        model.AdvanceTo(new EffectTime(51));
        Assert.Equal(4, model.Particles.Count);
        Assert.All(model.Particles, p => Assert.Equal(10240, p.HeightRaw));
    }

    [Fact]
    public void AContactDestroyedElementIsNotBornUnderARaisedChildFloor()
    {
        var model = new RoomConstructionParticles(Database(originRange: 8, elementFlags: 1),
            7, () => new EffectTime(0), floorHeight: ChildFloor);
        model.Emit(Request());
        model.AdvanceTo(new EffectTime(51));
        Assert.Empty(model.Particles);
    }

    [Fact]
    public void AStoppingElementSettlesOnTheRaisedFloorWithoutReflection()
    {
        var model = new RoomConstructionParticles(Database(originRange: 8, elementFlags: 16, elasticity: 32768),
            7, () => new EffectTime(0), floorHeight: ChildFloor);
        model.Emit(Request());
        model.AdvanceTo(new EffectTime(51));
        Assert.All(model.Particles, p => Assert.Equal(8192, p.HeightRaw));
    }

    private static Fp32 ChildFloor(FpPosition3 point) =>
        new(point.X.Raw == 10240 && point.Y.Raw == 14336 ? 4096 : 8192);

    private static SimEvent Request() => new(new GameTick(0), "view", "effect", "")
    {
        EffectRequest = new ViewEffectRequest(new EffectId(17), PlayerIds.FirstKeeper,
            new FpPosition3(new Fp32(10240), new Fp32(14336), new Fp32(0)))
    };

    private static GameDatabase Database(uint flags = 0x80,
        (byte Red, byte Green, byte Blue)? authoredColour = null,
        ushort originRange = 0, int elasticity = 0, uint elementFlags = 0)
    {
        (byte Red, byte Green, byte Blue) colour = authoredColour ?? (70, 80, 130);
        EffectElementDef Element(ushort id, uint artFlags) => new([])
        {
            Name = "element", EffectElementId = id,
            ArtResource = new KwdArtResource("sprite", artFlags, new byte[12], 3, 0, 0, 0),
            MinHp = 20, MaxHp = 20, MinScale = 1024, MaxScale = 1024,
            Red = colour.Red, Green = colour.Green, Blue = colour.Blue,
            Elasticity = elasticity, Flags = elementFlags
        };
        var effect = new EffectDef([])
        {
            EffectId = 17, Name = "bed", ArtResource = new KwdArtResource("", 0, new byte[12], 0, 0, 0, 0),
            Light = new KwdLight(0, 0, 0, 0, 0, 0, 0, 0, 0),
            GenerationType = 4, ElementsPerTurn = 2, GenerateIds = [1, 2], Flags = flags,
            MinHp = 2, MaxHp = 2, InnerOriginRange = originRange, OuterOriginRange = originRange
        };
        return new GameDatabase([], [], [], new GlobalVariables(), effects: [effect],
            effectElements: [Element(1, 0x80), Element(2, 0)]);
    }
}
