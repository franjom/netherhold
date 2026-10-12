// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation.Tests;

public class RoomConstructionParticleTests
{
    [Fact]
    public void VisualClockWaitsForTheStrictFiftyMillisecondBoundary()
    {
        var model = new RoomConstructionParticles(Database(), 7, () => new EffectTime(0));
        model.Emit(Request());
        ConstructionParticle particle = model.Particles[0];
        int health = particle.Health;
        model.AdvanceTo(new EffectTime(50));
        Assert.Equal(health, particle.Health);
        model.AdvanceTo(new EffectTime(51));
        Assert.Equal(health - 1, particle.Health);
        model.AdvanceTo(new EffectTime(100));
        Assert.Equal(health - 1, particle.Health);
        model.AdvanceTo(new EffectTime(101));
        Assert.Equal(health - 2, particle.Health);
    }

    [Fact]
    public void DelayedVisualUpdateConsumesLifetimeInABatchButMovesAndAnimatesOnce()
    {
        var model = new RoomConstructionParticles(Database(artFlags: 4, elementFlags: 4), 7, () => new EffectTime(0));
        model.Emit(Request());
        ConstructionParticle chip = model.Particles.First(p => p.Definition.EffectElementId == 144);
        int health = chip.Health, velocity = chip.VelocityZ, height = chip.HeightRaw;
        model.AdvanceTo(new EffectTime(201));
        Assert.Equal(health - 4, chip.Health);
        Assert.Equal(velocity - chip.Definition.Mass, chip.VelocityZ);
        Assert.Equal(height + ((velocity - chip.Definition.Mass) >> 4), chip.HeightRaw);
        Assert.Equal(256, chip.PhaseRaw);
        Assert.Equal(chip.InitialScale + chip.ScaleIncrement, chip.ScaleRaw);
    }

    [Fact]
    public void ClockStartsAtConstructionAndSimulationEventTicksCannotAgeParticles()
    {
        long milliseconds = 100000;
        var model = new RoomConstructionParticles(Database(), 7, () => new EffectTime(milliseconds));
        model.Emit(Request() with { Tick = new GameTick(900000) });
        ConstructionParticle particle = model.Particles[0];
        Assert.Equal(particle.InitialHealth, particle.Health);
        milliseconds += 51;
        Assert.Equal(.02f, model.Advance(), precision: 3);
        Assert.Equal(particle.InitialHealth - 1, particle.Health);
        milliseconds += 25;
        Assert.Equal(.52f, model.Advance(), precision: 3);
        Assert.Equal(particle.InitialHealth - 1, particle.Health);
    }

    [Fact]
    public void ABuildBurstGeneratesEveryElementOnEveryPass()
    {
        var model = new RoomConstructionParticles(Database(), 7, () => new EffectTime(0));
        model.Emit(Request());
        Assert.Equal(64, model.Particles.Count);
        Assert.Equal(32, model.Particles.Count(p => p.Definition.EffectElementId == 144));
        Assert.Equal(32, model.Particles.Count(p => p.Definition.EffectElementId == 204));
        Assert.All(model.Particles, p => Assert.InRange(p.HeightRaw, 6144 + 640, 6144 + 1279));
    }

    [Fact]
    public void ParticlePhysicsApplyGravityThenDragThenTheFixedPointStep()
    {
        var model = new RoomConstructionParticles(Database(), 7, () => new EffectTime(0));
        model.Emit(Request());
        ConstructionParticle chip = model.Particles.First(p => p.Definition.EffectElementId == 144);
        int z = chip.HeightRaw, velocity = chip.VelocityZ, health = chip.Health;
        model.AdvanceTo(new EffectTime(51));
        Assert.Equal(velocity - chip.Definition.Mass, chip.VelocityZ);
        Assert.Equal(z + ((velocity - chip.Definition.Mass) >> 4), chip.HeightRaw);
        Assert.Equal(health - 1, chip.Health);
    }

    [Fact]
    public void ExpiredBurstsAreRemovedAndAnUnchangedClockDoesNotAdvanceThem()
    {
        var model = new RoomConstructionParticles(Database(), 7, () => new EffectTime(0));
        model.Emit(Request());
        Assert.NotEmpty(model.Particles);
        int health = model.Particles[0].Health;
        model.AdvanceTo(new EffectTime(0));
        Assert.Equal(health, model.Particles[0].Health);
        model.AdvanceTo(new EffectTime(5001));
        Assert.Empty(model.Particles);
    }

    [Fact]
    public void VisualRandomnessIsReproducibleAndUnrelatedEventsDoNotEmit()
    {
        var a = new RoomConstructionParticles(Database(), 7, () => new EffectTime(0));
        var b = new RoomConstructionParticles(Database(), 7, () => new EffectTime(0));
        a.Emit(new SimEvent(new GameTick(0), "room", "built", "ignored"));
        Assert.Empty(a.Particles);
        a.Emit(Request()); b.Emit(Request());
        Assert.Equal(a.Particles.Select(p => (p.XRaw, p.YRaw, p.HeightRaw, p.Health)),
            b.Particles.Select(p => (p.XRaw, p.YRaw, p.HeightRaw, p.Health)));
    }

    private static SimEvent Request() => new(new GameTick(0), "room", "construction_effect", "")
    { ConstructionEffect = new RoomConstructionEffect(PlayerIds.FirstKeeper, new TilePos(2, 3)) };

    [Fact]
    public void ColourDecayUsesTheRemainingLifetimeAfterTheHealthStep()
    {
        var model = new RoomConstructionParticles(Database(elementFlags: 8), 7, () => new EffectTime(0));
        model.Emit(Request());
        ConstructionParticle particle = model.Particles[0];
        model.AdvanceTo(new EffectTime(51));
        Assert.Equal(255 * (particle.Health - 1) / particle.Health, particle.Red);
    }

    [Fact]
    public void AnimationAloneDoesNotRandomizeTheInitialSpritePhase()
    {
        var model = new RoomConstructionParticles(Database(artFlags: 4), 7, () => new EffectTime(0));
        model.Emit(Request());
        Assert.All(model.Particles, p => Assert.Equal(0, p.InitialPhase));
    }

    [Fact]
    public void ScaleGrowthAddsTheOnceTruncatedIncrementOnEachUpdate()
    {
        var model = new RoomConstructionParticles(Database(elementFlags: 4), 7, () => new EffectTime(0));
        model.Emit(Request());
        model.AdvanceTo(new EffectTime(51));
        model.AdvanceTo(new EffectTime(101));
        Assert.All(model.Particles, p =>
        {
            int target = (short)((p.InitialScale * 10240) >> 12);
            int increment = (short)((target - p.InitialScale) / p.InitialHealth);
            Assert.Equal(p.InitialScale + 2 * increment, p.ScaleRaw);
        });
    }

    [Fact]
    public void HiddenBuildsDoNotEmitOrConsumeTheVisibleRandomStream()
    {
        bool visible = false;
        var filtered = new RoomConstructionParticles(Database(), 7, () => new EffectTime(0), _ => visible);
        var reference = new RoomConstructionParticles(Database(), 7, () => new EffectTime(0));
        filtered.Emit(Request());
        Assert.Empty(filtered.Particles);
        visible = true;
        filtered.Emit(Request()); reference.Emit(Request());
        Assert.Equal(reference.Particles.Select(p => (p.XRaw, p.YRaw, p.HeightRaw)),
            filtered.Particles.Select(p => (p.XRaw, p.YRaw, p.HeightRaw)));
    }

    [Fact]
    public void ChipsBounceOffTheRoomFloorInsteadOfFallingThroughIt()
    {
        var model = new RoomConstructionParticles(Database(chipMass: 30000), 7, () => new EffectTime(0));
        model.Emit(Request());
        ConstructionParticle chip = model.Particles.First(p => p.Definition.EffectElementId == 144);
        model.AdvanceTo(new EffectTime(51));
        model.AdvanceTo(new EffectTime(101));
        Assert.True(chip.HeightRaw >= 4096);
        Assert.True(chip.VelocityZ >= 0);
    }

    private static GameDatabase Database(uint artFlags = 0, uint elementFlags = 0, int chipMass = 1228)
    {
        byte[] descriptor = new byte[12]; descriptor[8] = 16;
        var art = new KwdArtResource("sprite", artFlags, descriptor, 2, 0, 0, 0);
        EffectElementDef Element(ushort id, int mass, int drag) => new([])
        {
            Name = $"element{id}", EffectElementId = id, ArtResource = art,
            MinHp = 12, MaxHp = 16, MinScale = 1228, MaxScale = 2047,
            MinSpeedXy = 0, MaxSpeedXy = 6144, MinSpeedYz = 8192, MaxSpeedYz = 12288,
            Mass = mass, AirFriction = drag,
            Flags = elementFlags, Red = 255, ScaleRatio = 10240, Elasticity = 32768,
        };
        var effect = new EffectDef([])
        {
            Name = "build", EffectId = RoomConstructionEffect.DefinitionId, ArtResource = art,
            Light = new KwdLight(0, 0, 0, 0, 0, 0, 0, 0, 0),
            GenerateIds = [204, 144], GenerationType = 4, ElementsPerTurn = 32,
            InnerOriginRange = 0, OuterOriginRange = 16, LowerHeightLimit = 40, UpperHeightLimit = 80,
            Flags = 128,
        };
        return new GameDatabase([], [], [], new GlobalVariables(), effects: [effect],
            effectElements: [Element(204, -122, 130), Element(144, chipMass, 0)]);
    }
}
