// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation.Tests;

public class HeartManaParticleTests
{
    [Theory]
    [InlineData(223)]
    [InlineData(205)]
    public void ManaWaitsForTheOwnersLidAndItsLastParticleToDisappear(ushort lidEffect)
    {
        GameDatabase heart = ShippedHeartDatabase();
        EffectDef lid = heart.Effects[59] with
        {
            EffectId = lidEffect, GenerateIds = [lidEffect == 205 ? (ushort)403 : (ushort)406], MinHp = 1, MaxHp = 1,
            NextEffectId = 0, DeathEffectId = 0
        };
        EffectElementDef cover = heart.EffectElements[98] with
        {
            EffectElementId = lidEffect == 205 ? (ushort)403 : (ushort)406, MinHp = 2, MaxHp = 2,
            MinSpeedXy = 0, MaxSpeedXy = 0, MinSpeedYz = 0, MaxSpeedYz = 0
        };
        var database = new GameDatabase([], heart.Rooms.Values, [], new GlobalVariables(),
            effects: [heart.Effects[59], lid], effectElements: [heart.EffectElements[98], cover]);
        EffectTime now = new(0);
        var model = new RoomConstructionParticles(database, 7, () => now, floorHeight: _ => new Fp32(0));
        var mana = new ViewEffectRequest(new EffectId(59), PlayerIds.FirstKeeper,
            new(new(10240), new(14336), new(8192)));
        model.Request(mana with { EffectId = new EffectId(lidEffect) });
        model.Request(mana);
        model.AdvanceTo(now = new(51));
        Assert.DoesNotContain(model.Particles, p => p.Definition.EffectElementId == 98);
        model.Request(mana);
        model.AdvanceTo(now = new(101));
        Assert.DoesNotContain(model.Particles, p => p.Definition.EffectElementId == 98);
        model.AdvanceTo(now = new(151));
        model.Request(mana);
        model.AdvanceTo(now = new(201));
        Assert.Contains(model.Particles, p => p.Definition.EffectElementId == 98);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(250)]
    public void ManaWaitsForTheFullRevealOnTheEffectClockAndOnlyBlocksItsOwner(ushort startHp)
    {
        GameDatabase heart = ShippedHeartDatabase();
        EffectDef start = heart.Effects[59] with
        {
            EffectId = 225, GenerateIds = [], MinHp = startHp, MaxHp = startHp, DeathEffectId = 215
        };
        EffectDef burst = start with { EffectId = 215, MinHp = 1, MaxHp = 1, DeathEffectId = 0 };
        var database = new GameDatabase([], heart.Rooms.Values, [], new GlobalVariables(),
            effects: [heart.Effects[59], start, burst], effectElements: heart.EffectElements.Values);
        EffectTime now = new(0);
        var model = new RoomConstructionParticles(database, 7, () => now, floorHeight: _ => new Fp32(0));
        var mana = new ViewEffectRequest(new EffectId(59), PlayerIds.FirstKeeper,
            new(new(10240), new(14336), new(8192)));
        model.Request(mana with { EffectId = new EffectId(225) });
        model.AdvanceTo(now = new(51));
        model.Request(mana);
        model.AdvanceTo(now = new(101));
        Assert.Empty(model.Particles);
        model.Request(mana with { Owner = new PlayerId(2) });
        model.AdvanceTo(now = new(151));
        Assert.NotEmpty(model.Particles);
        for (int ms = 201; ms <= 1501; ms += 50) model.AdvanceTo(now = new(ms));
        model.AdvanceTo(now = new(8951));
        model.Request(mana);
        model.AdvanceTo(now = new(9001));
        Assert.Empty(model.Particles);
        model.Request(mana);
        model.AdvanceTo(now = new(9051));
        if (startHp > 1)
        {
            Assert.Empty(model.Particles);
            model.AdvanceTo(now = new(12501));
            model.Request(mana);
            model.AdvanceTo(now = new(12551));
        }
        Assert.NotEmpty(model.Particles);
    }

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

    /// <summary>ADR-0474: the owner's adjustment, not a binary reading (Q594). With the shipped
    /// numbers the native climb starts 0.78 tiles over the Heart and peaks at 2.79; the adjustment
    /// starts inside the Heart mesh (0.25) and peaks at about 1.5, the Arch Pillars' top as judged
    /// from screenshots. Native stays the default.</summary>
    [Theory]
    [InlineData(HeartSparkleStyle.Native, 10878, 11974)]
    [InlineData(HeartSparkleStyle.PillarHeight, 5900, 6400)]
    public void HeartSparklePeakIsNativeOrTheOwnersPillarHeight_AnAdjustmentNotAReading(
        HeartSparkleStyle style, int lowestPeak, int highestPeak)
    {
        var model = new RoomConstructionParticles(ShippedHeartDatabase(), 7, () => new EffectTime(0),
            floorHeight: _ => new Fp32(0), heartSparkleStyle: style);
        model.Emit(new SimEvent(new GameTick(0), "view", "effect", "")
        {
            EffectRequest = new(new EffectId(59), PlayerIds.FirstKeeper, new(new(10240), new(14336), new(0)))
        });
        int peak = 0, lowest = int.MaxValue;
        for (int call = 1; call <= 24; call++)
        {
            model.AdvanceTo(new EffectTime(call * 50 + 1));
            foreach (ConstructionParticle p in model.Particles)
                if (p.Definition.EffectElementId == 98)
                {
                    peak = Math.Max(peak, p.HeightRaw);
                    lowest = Math.Min(lowest, p.HeightRaw);
                }
        }
        Assert.InRange(peak, lowestPeak, highestPeak);
        Assert.Equal(style == HeartSparkleStyle.Native ? 3200 : 1024, lowest);
    }

    /// <summary>Effect 59 and element 98 as shipped (heart-mana-effects.md §1), one element per wave.</summary>
    private static GameDatabase ShippedHeartDatabase()
    {
        var art = new KwdArtResource("Sparkle-03g", 0, new byte[12], 3, 0, 0, 0);
        var rising = new EffectElementDef([])
        {
            Name = "rise", EffectElementId = 98, ArtResource = art, MinHp = 24, MaxHp = 24,
            AirFriction = 393, MinSpeedXy = 819, MaxSpeedXy = 819, MinSpeedYz = 14336, MaxSpeedYz = 16384,
            MinScale = 819, MaxScale = 2457, Red = 10, Green = 255, Blue = 200
        };
        var effect = new EffectDef([])
        {
            Name = "Mana To Heart", EffectId = 59, ArtResource = art,
            Light = new(0, 0, 0, 0, 0, 0, 0, 0, 0), GenerateIds = [98],
            Flags = 0x2280, GenerationType = 4, ElementsPerTurn = 1, MinHp = 2, MaxHp = 2,
            OuterOriginRange = 12, LowerHeightLimit = 200, UpperHeightLimit = 200,
            WhirlpoolRate = 32, CircularPathRate = 12, SpriteSpinRateRange = 78
        };
        var heart = new RoomDef
        {
            Name = "Dungeon Heart", RoomId = (byte)RoomTypeId.DungeonHeart, Flags = RoomFlags.PlaceableOnLand,
            Cost = 0, ReturnPercentage = 0, TerrainId = 14, Construction = TileConstruction.Normal,
            CreatedCreatureId = 0, ObjectIds = [], HealthGain = 0, CeilingHeight = new Fp32(0),
            RecommendedSizeX = 5, RecommendedSizeY = 5, OrderInEditor = 0, NameStringId = 0, TooltipStringId = 0,
            CreatedObjectEffectId = 59,
        };
        return new([], [heart], [], new GlobalVariables(), effects: [effect], effectElements: [rising]);
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
