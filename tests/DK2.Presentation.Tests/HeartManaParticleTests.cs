// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation.Tests;

public class HeartManaParticleTests
{
    [Fact]
    public void ExpiredRisingSparkleCreatesFreshFallingElementAtItsFinalPosition()
    {
        var model = Model(Database(deathElement: true));
        model.AdvanceTo(new EffectTime(51));
        ConstructionParticle rising = Assert.Single(model.Particles);
        model.AdvanceTo(new EffectTime(101));
        ConstructionParticle falling = Assert.Single(model.Particles);
        Assert.Equal((ushort)99, falling.Definition.EffectElementId);
        Assert.Equal(rising.HeightRaw, falling.HeightRaw);
        Assert.Equal(8448, falling.HeightRaw);
        Assert.Equal(0, falling.VisualUpdates);
        Assert.Equal(24, falling.Health);
        Assert.Equal(-4096, falling.VelocityZ);
        Assert.Equal(rising.HeadingSpinRaw, falling.HeadingSpinRaw);
        Assert.Equal(rising.PitchSpinRaw, falling.PitchSpinRaw);
        Assert.Equal(rising.RollSpinRaw, falling.RollSpinRaw);
    }

    [Fact]
    public void CircularMotionContinuesAfterParentEmissionEndsWithoutChangingVelocity()
    {
        var model = Model(Database(circularRate: 768));
        model.AdvanceTo(new EffectTime(51));
        ConstructionParticle particle = Assert.Single(model.Particles);
        int initialX = particle.XRaw, initialY = particle.YRaw;
        model.AdvanceTo(new EffectTime(101));
        Assert.Equal(10240 + initialY - 14336, particle.XRaw);
        Assert.Equal(14336 - (initialX - 10240), particle.YRaw);
        Assert.Equal(0, particle.VelocityX);
        Assert.Equal(0, particle.VelocityY);
        model.AdvanceTo(new EffectTime(151));
        Assert.Equal(10240 - (initialX - 10240), particle.XRaw);
        Assert.Equal(14336 - (initialY - 14336), particle.YRaw);
    }

    [Fact]
    public void AirborneStoppingFlakesSwayButRemainStationaryAfterFloorContact()
    {
        var model = Model(Database(fallingSway: true));
        model.AdvanceTo(new EffectTime(51));
        ConstructionParticle particle = Assert.Single(model.Particles);
        model.AdvanceTo(new EffectTime(101));
        int x = particle.XRaw, y = particle.YRaw;
        model.AdvanceTo(new EffectTime(151));
        Assert.NotEqual((x, y), (particle.XRaw, particle.YRaw));
        for (int call = 4; call <= 20; call++) model.AdvanceTo(new EffectTime(call * 50 + 1));
        Assert.Equal(4096, particle.HeightRaw);
        x = particle.XRaw; y = particle.YRaw;
        model.AdvanceTo(new EffectTime(1051));
        Assert.Equal((x, y, 4096), (particle.XRaw, particle.YRaw, particle.HeightRaw));
    }

    private static RoomConstructionParticles Model(GameDatabase database)
    {
        var model = new RoomConstructionParticles(database, 7, () => new EffectTime(0));
        model.Emit(new SimEvent(new GameTick(0), "view", "effect", "")
        {
            EffectRequest = new(new EffectId(59), PlayerIds.FirstKeeper,
                new(new(10240), new(14336), new(8192)))
        });
        return model;
    }

    private static GameDatabase Database(bool deathElement = false,
        ushort circularRate = 0, bool fallingSway = false)
    {
        var art = new KwdArtResource("Sparkle-03g", 0, new byte[12], 3, 0, 0, 0);
        var rising = new EffectElementDef([])
        {
            Name = "rise", EffectElementId = 98, ArtResource = art,
            MinHp = deathElement ? (ushort)1 : (ushort)30, MaxHp = deathElement ? (ushort)1 : (ushort)30,
            MinSpeedYz = deathElement ? 4096 : 0, MaxSpeedYz = deathElement ? 4096 : 0,
            MinScale = 1024, MaxScale = 1024, DeathElementId = deathElement ? (ushort)99 : (ushort)0,
            Flags = fallingSway ? 16u : 0u, Mass = fallingSway ? 819 : 0,
            Red = 10, Green = 255, Blue = 200
        };
        var falling = rising with
        {
            Name = "fall", EffectElementId = 99, MinHp = 24, MaxHp = 24,
            MinSpeedYz = -4096, MaxSpeedYz = -4096, DeathElementId = 0
        };
        var effect = new EffectDef([])
        {
            Name = "mana", EffectId = 59, ArtResource = art,
            Light = new(0, 0, 0, 0, 0, 0, 0, 0, 0), GenerateIds = [98],
            Flags = 0x80, GenerationType = 4, ElementsPerTurn = 1, MinHp = 1, MaxHp = 1,
            InnerOriginRange = circularRate != 0 ? (ushort)12 : (ushort)0,
            OuterOriginRange = circularRate != 0 ? (ushort)12 : (ushort)0,
            CircularPathRate = circularRate, SpriteSpinRateRange = deathElement ? (ushort)78 : (ushort)0
        };
        return new([], [], [], new GlobalVariables(), effects: [effect], effectElements: [rising, falling]);
    }
}
