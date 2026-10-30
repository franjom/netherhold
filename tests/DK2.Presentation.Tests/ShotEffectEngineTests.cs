// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

public class ShotEffectEngineTests
{
    [Theory]
    [InlineData(false, 4596)]
    [InlineData(true, 7500)]
    public void GroundRelativeFlamesResolveBeforeTheVisibilityGateWithoutChangingTheirRandomOffset(bool hasCreature, int expected)
    {
        FpPosition3 observed = default;
        var model = new RoomConstructionParticles(new GameDatabase([], [], [], new GlobalVariables(),
            effects: [Effect()], effectElements: [Element()]), 7, () => default,
            floorHeight: point => new(point.X.Raw < 10000 ? 4096 : 0), effectVisible: request => { observed = request.Position; return true; },
            creatureGroundHeight: id => id.Value == 3 ? new Fp32(7000) : null);
        model.Request(Request() with { Position = Point(10000, 12000, 500), HeightSpace = EffectHeightSpace.AboveGround,
            GroundAnchor = Point(9999, 12000, 0), GroundCreatureId = new(hasCreature ? 3 : 0) });
        Assert.Equal(expected, observed.Z.Raw);
        model.AdvanceTo(new(51));
        Assert.Equal(expected, Assert.Single(model.Particles).HeightRaw);
    }

    [Fact]
    public void ViewRandomnessUsesTheNativeGeneratorAndKeepsTheDiscardedUniformDraw()
    {
        var moving = Element() with { MinSpeedXy = 4096, MaxSpeedXy = 4096 };
        var uniform = Model([Effect()], [moving]);
        uniform.Request(Request());
        uniform.AdvanceTo(new(51));
        // Seed7 ->3D20BDBA(origin angle261) ->E9DAB1D1(velocity angle1749).
        ConstructionParticle particle = Assert.Single(uniform.Particles);
        Assert.Equal((-3252, -2490), (particle.VelocityX, particle.VelocityY));
        var jet = Model([Effect() with { DirectionalSpread = 1 }], [moving]);
        jet.Request(Request());
        jet.AdvanceTo(new(51));
        // The third step9CCC40FC supplies2 of8: heading512+2-4=510.
        particle = Assert.Single(jet.Particles);
        Assert.Equal((4095, -25), (particle.VelocityX, particle.VelocityY));
    }

    [Fact]
    public void InheritanceAddsBothCarriedAndEmitterMotionOnlyWithFlag100()
    {
        var source = Effect() with { Flags = 0x100, MinSpeedYz = 160, MaxSpeedYz = 160 };
        var inherited = Model([source]);
        inherited.Request(Request() with { CarriedVelocity = Point(32, 48, 64) });
        inherited.AdvanceTo(new(51));
        var child = Assert.Single(inherited.Particles);
        Assert.Equal((32, 48, 224), (child.VelocityX, child.VelocityY, child.VelocityZ));
        var plain = Model([source with { Flags = 0 }]);
        plain.Request(Request() with { CarriedVelocity = Point(32, 48, 64) });
        plain.AdvanceTo(new(51));
        Assert.Equal(0, Assert.Single(plain.Particles).VelocityZ);
    }

    [Theory]
    [InlineData(0u, 512)]
    [InlineData(0x400u, 0)]
    public void ElementHeadingDropsParentOnlyWith400(uint flags, int heading)
    {
        var model = Model([Effect() with { Flags = flags }]);
        model.Request(Request());
        model.AdvanceTo(new(51));
        Assert.Equal(heading, Assert.Single(model.Particles).HeadingRaw);
    }

    [Fact]
    public void DirectionalSpreadMakesAJetAndFlightYawUsesItsActualVelocity()
    {
        var element = Element() with { Flags = 0x40, MinSpeedXy = 16384, MaxSpeedXy = 16384 };
        var model = Model([Effect() with { DirectionalSpread = 1, ElementsPerTurn = 20 }], [element]);
        model.Request(Request());
        model.AdvanceTo(new(51));
        Assert.Equal(20, model.Particles.Count);
        Assert.All(model.Particles, p =>
        {
            Assert.InRange(p.VelocityX, 16380, 16384);
            Assert.InRange(p.VelocityY, -202, 202);
            Assert.InRange(p.HeadingRaw, 508, 515);
        });
    }

    [Theory]
    [InlineData(0x40u, 0)]
    [InlineData(0x440u, 512)]
    public void GeneratedEffectHeadingAddsParentOnlyWith400(uint flags, int heading)
    {
        var model = Model([Effect() with { Flags = flags, GenerateIds = [11] }, Effect() with { EffectId = 11 }]);
        model.Request(Request());
        model.AdvanceTo(new(51));
        model.AdvanceTo(new(101));
        Assert.Equal(heading, Assert.Single(model.Particles).HeadingRaw);
    }

    [Fact]
    public void Type18MakesOnlyFourSlotsPerQuarterCountAtTheExactPoint()
    {
        var burst = Effect() with { GenerationType = 18, Flags = 0x40, ElementsPerTurn = 8,
            GenerateIds = [11, 11, 11, 11, 11], OuterOriginRange = 100 };
        var model = Model([burst, Effect() with { EffectId = 11 }]);
        model.Request(Request());
        model.AdvanceTo(new(51));
        model.AdvanceTo(new(101));
        Assert.Equal(8, model.Particles.Count);
        Assert.All(model.Particles, p => Assert.Equal((10000, 12000, 14000), (p.XRaw, p.YRaw, p.HeightRaw)));
    }

    [Fact]
    public void Type6UsesAnInclusiveIdRangeAndExactOrigin()
    {
        var model = Model([Effect() with { GenerationType = 6, GenerateIds = [20, 22], ElementsPerTurn = 100 }],
            [Element(), Element() with { EffectElementId = 21 }, Element() with { EffectElementId = 22 }]);
        model.Request(Request());
        model.AdvanceTo(new(51));
        Assert.Equal(new ushort[] { 20, 21, 22 }, model.Particles.Select(p => p.Definition.EffectElementId).Distinct().Order());
        Assert.All(model.Particles, p => Assert.Equal((10000, 12000, 14000), (p.XRaw, p.YRaw, p.HeightRaw)));
    }

    [Fact]
    public void Type3InitialBurstUsesTheMiddleHalfTileAndOwnVelocityKick()
    {
        var model = Model([Effect() with { GenerationType = 3, ElementsPerTurn = 10,
            MinSpeedYz = 160, MaxSpeedYz = 160 }]);
        model.Request(Request());
        model.AdvanceTo(new(51));
        Assert.Equal(10, model.Particles.Count);
        Assert.All(model.Particles, p =>
        {
            Assert.InRange(p.XRaw, 11024, 13071);
            Assert.InRange(p.YRaw, 13024, 15071);
            Assert.Equal(18106, p.HeightRaw);
            Assert.Equal(160, p.VelocityZ);
        });
    }

    [Fact]
    public void ElementNextEffectIsRequestedEveryUpdate()
    {
        var model = Model([Effect(), Effect() with { EffectId = 11, GenerateIds = [21] }],
            [Element() with { NextEffectId = 11 }, Element() with { EffectElementId = 21 }]);
        model.Request(Request());
        for (int step = 1; step <= 4; step++) model.AdvanceTo(new(step * 50 + 1));
        Assert.Equal(2, model.Particles.Count(p => p.Definition.EffectElementId == 21));
    }

    [Theory]
    [InlineData(TerrainFlags.Lava, 21)]
    [InlineData(TerrainFlags.Water, 22)]
    [InlineData((TerrainFlags)0, 23)]
    public void EffectFloorContactPreservesTheNativeSwappedLiquidFields(TerrainFlags terrain, int expected)
    {
        var falling = Effect() with { Flags = 1, MinHp = 10, MaxHp = 10, GenerateIds = [],
            MinSpeedYz = -16000, MaxSpeedYz = -16000,
            HitWaterEffectId = 11, HitLavaEffectId = 12, HitSolidEffectId = 13 };
        var model = Model([falling, Effect() with { EffectId = 11, GenerateIds = [21] },
            Effect() with { EffectId = 12, GenerateIds = [22] }, Effect() with { EffectId = 13, GenerateIds = [23] }],
            [Element() with { EffectElementId = 21 }, Element() with { EffectElementId = 22 }, Element() with { EffectElementId = 23 }],
            terrain: _ => terrain);
        model.Request(Request() with { Position = Point(10000, 12000, 500) });
        model.AdvanceTo(new(51));
        model.AdvanceTo(new(101));
        Assert.Equal(expected, Assert.Single(model.Particles).Definition.EffectElementId);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    public void WallContactRaisesHitSolidWithOrWithoutKillFlag(uint flags)
    {
        var source = Effect() with { Flags = flags, MinHp = 10, MaxHp = 10, GenerateIds = [],
            MinSpeedXy = 65536, MaxSpeedXy = 65536, HitSolidEffectId = 11 };
        var model = Model([source, Effect() with { EffectId = 11 }], solid: tile => tile.X >= 3);
        model.Request(Request());
        model.AdvanceTo(new(51));
        model.AdvanceTo(new(101));
        Assert.Single(model.Particles);
        var lights = new List<EffectLightView>();
        model.CollectLights(lights);
        // A wall response never kills the emitter; with flag1 it stops at the previous point.
        model.AdvanceTo(new(151));
        Assert.Single(model.Particles);
    }

    [Fact]
    public void ElementFloorContactChoosesWaterIdWithoutInspectingTerrain()
    {
        var falling = Element() with { Flags = 1, MinSpeedYz = -16000, MaxSpeedYz = -16000,
            HitWaterElementId = 21, HitLavaElementId = 22, HitSolidElementId = 23 };
        var model = Model([Effect()], [falling, Element() with { EffectElementId = 21 },
            Element() with { EffectElementId = 22 }, Element() with { EffectElementId = 23 }]);
        model.Request(Request() with { Position = Point(10000, 12000, 500) });
        model.AdvanceTo(new(51));
        model.AdvanceTo(new(101));
        Assert.Equal(21, Assert.Single(model.Particles).Definition.EffectElementId);
    }

    [Fact]
    public void Type6RequestsItsSecondaryEffectOnlyAtAgesDivisibleByThree()
    {
        var model = Model([Effect() with { GenerationType = 6, GenerateIds = [20], ElementsPerTurn = 0,
            OuterOriginRange = 11, MinHp = 10, MaxHp = 10 }, Effect() with { EffectId = 11 }]);
        model.Request(Request());
        for (int step = 1; step <= 3; step++) model.AdvanceTo(new(step * 50 + 1));
        Assert.Empty(model.Particles);
        model.AdvanceTo(new(201));
        Assert.Single(model.Particles);
        for (int step = 5; step <= 7; step++) model.AdvanceTo(new(step * 50 + 1));
        Assert.Equal(2, model.Particles.Count);
    }

    [Theory]
    [InlineData((byte)18)]
    [InlineData((byte)6)]
    public void GeneratedEffectsBypassVisibilityAfterTheirParentHasBeenAccepted(byte type)
    {
        var parent = Effect() with { GenerationType = type, Flags = 0x40, GenerateIds = [11],
            ElementsPerTurn = type == 18 ? (byte)4 : (byte)0, OuterOriginRange = 11, MinHp = 5, MaxHp = 5 };
        var child = Effect() with { EffectId = 11 };
        bool visible = true;
        var model = new RoomConstructionParticles(new GameDatabase([], [], [], new GlobalVariables(),
            effects: [parent, child], effectElements: [Element()]), 7, () => default,
            floorHeight: _ => default, effectVisible: _ => visible, cameraVisible: _ => visible);
        model.Request(Request());
        visible = false;
        for (int step = 1; step <= 4; step++) model.AdvanceTo(new(step * 50 + 1));
        Assert.NotEmpty(model.Particles);
    }

    [Fact]
    public void InheritedVelocityDoesNotIndexAnUnsupportedChildAllocation()
    {
        var model = Model([Effect() with { Flags = 0x140, GenerateIds = [11] },
            Effect() with { EffectId = 11, GenerationType = 14 }]);
        model.Request(Request());
        model.AdvanceTo(new(51));
        Assert.Empty(model.Particles);
    }

    [Fact]
    public void AnInheritedOwnArtSpriteMovesWithItsEmitter()
    {
        var sprite = Effect() with { EffectId = 11, GenerationType = 0, ArtResource = Element().ArtResource,
            GenerateIds = [], MinHp = 10, MaxHp = 10 };
        var model = Model([Effect() with { Flags = 0x140, GenerateIds = [11] }, sprite]);
        model.Request(Request() with { CarriedVelocity = Point(160, 0, 0) });
        model.AdvanceTo(new(51));
        Assert.Equal(10000, Assert.Single(model.Particles).XRaw);
        model.AdvanceTo(new(101));
        Assert.Equal(10010, Assert.Single(model.Particles).XRaw);
    }

    [Fact]
    public void ABlockingElementWallHitRemovesTheElementAndLeavesItsContactChild()
    {
        var moving = Element() with { Flags = 1, MinSpeedXy = 65536, MaxSpeedXy = 65536,
            HitSolidElementId = 21 };
        var model = Model([Effect() with { DirectionalSpread = 1 }],
            [moving, Element() with { EffectElementId = 21 }], solid: tile => tile.X >= 3);
        model.Request(Request());
        model.AdvanceTo(new(51));
        model.AdvanceTo(new(101));
        ConstructionParticle child = Assert.Single(model.Particles);
        Assert.Equal(21, child.Definition.EffectElementId);
        Assert.Equal(10000, child.XRaw);
    }

    [Fact]
    public void ContactChildrenKeepTheirGivenHeadingAndDoNotInheritLaunchVelocityAgain()
    {
        var falling = Element() with { MinSpeedYz = -16000, MaxSpeedYz = -16000, Flags = 1,
            HitWaterElementId = 21 };
        var model = Model([Effect() with { Flags = 0x100 }], [falling, Element() with { EffectElementId = 21 }]);
        model.Request(Request() with { Position = Point(10000, 12000, 500), CarriedVelocity = Point(160, 0, 0) });
        model.AdvanceTo(new(51));
        model.AdvanceTo(new(101));
        ConstructionParticle child = Assert.Single(model.Particles);
        Assert.Equal((0, 0, 0), (child.VelocityX, child.VelocityY, child.VelocityZ));
        Assert.Equal(512, child.HeadingRaw);
    }

    [InstallFact]
    public void TheShippedInfernoCarpetUsesRandomnessThreeAndEightPasses()
    {
        GameDatabase database = DK2.Data.GameDatabaseLoader.Load(Dk2InstallGate.Path);
        EffectDef carpet = database.Effects[106];
        Assert.Equal(3, carpet.GenerateRandomness);
        Assert.Equal(8, carpet.ElementsPerTurn);
        Assert.Equal(112, carpet.MinHp);
        // Isolate the carpet's wave gate from its initial Explosion/Fire Ring and death mist.
        var isolated = new GameDatabase([], [], [], new GlobalVariables(),
            effects: [carpet with { NextEffectId = 0, DeathEffectId = 0 }], effectElements: database.EffectElements.Values);
        var model = new RoomConstructionParticles(isolated, 7, () => default, floorHeight: _ => default);
        model.Request(Request() with { EffectId = new(106) });
        int waves = 0;
        var ids = carpet.GenerateIds.ToHashSet();
        for (int step = 1; step <= 112; step++)
        {
            model.AdvanceTo(new(step * 50 + 1));
            if (model.Particles.Any(p => p.VisualUpdates == 0 && ids.Contains(p.Definition.EffectElementId))) waves++;
        }
        Assert.InRange(waves, 15, 45);
    }

    [InstallFact]
    public void SpecialGenerationRecordsHaveNoAuthoredRequestSourcesInTheGlobalDatabase()
    {
        GameDatabase database = DK2.Data.GameDatabaseLoader.Load(Dk2InstallGate.Path);
        Assert.Equal(6, database.Effects[27].GenerationType);
        Assert.Equal(3, database.Effects[182].GenerationType);
        var sources = new List<string>();
        IEnumerable<object> records = database.Shots.Values.Cast<object>().Concat(database.Creatures.Values)
            .Concat(database.Rooms.Values).Concat(database.Objects.Values).Concat(database.Traps.Values)
            .Concat(database.Doors.Values).Concat(database.KeeperSpells.Values).Concat(database.Effects.Values)
            .Concat(database.EffectElements.Values);
        foreach (object record in records)
            foreach (var property in record.GetType().GetProperties())
            {
                if (!property.Name.EndsWith("EffectId", StringComparison.Ordinal) || property.Name == "EffectId") continue;
                object? value = property.GetValue(record);
                if (value is not ushort && value is not byte) continue;
                int id = Convert.ToInt32(value);
                if (id is 27 or 182)
                    sources.Add($"{record.GetType().Name} {record.GetType().GetProperty("Name")?.GetValue(record)} {property.Name} -> {id}");
            }
        foreach (EffectDef effect in database.Effects.Values)
            if ((effect.Flags & 0x40) != 0 && effect.GenerateIds.Any(id => id is 27 or 182))
                sources.Add($"Effect {effect.EffectId} {effect.Name}: generates {string.Join(",", effect.GenerateIds)}");
        Assert.Empty(sources);
    }

    private static ViewEffectRequest Request() => new(new(10), PlayerIds.FirstKeeper, Point(10000, 12000, 14000), new(512));
    private static FpPosition3 Point(int x, int y, int z) => new(new(x), new(y), new(z));
    private static readonly KwdArtResource EmptyArt = new("", 0, new byte[12], 0, 0, 0, 0);
    private static EffectDef Effect() => new([])
    {
        Name = "effect", EffectId = 10, ArtResource = EmptyArt, Light = new(0, 0, 0, 0, 0, 0, 0, 0, 0),
        GenerateIds = [20], GenerationType = 4, ElementsPerTurn = 1, MinHp = 1, MaxHp = 1,
    };
    private static EffectElementDef Element() => new([])
    {
        Name = "element", EffectElementId = 20, ArtResource = new("spark", 0, new byte[12], 2, 0, 0, 0),
        MinHp = 20, MaxHp = 20, MinScale = 4096, MaxScale = 4096,
    };
    private static RoomConstructionParticles Model(EffectDef[] effects, EffectElementDef[]? elements = null,
        Func<TilePos, TerrainFlags>? terrain = null, Func<TilePos, bool>? solid = null) =>
        new(new GameDatabase([], [], [], new GlobalVariables(), effects: effects, effectElements: elements ?? [Element()]),
            7, () => default, floorHeight: _ => default, terrainFlags: terrain, solidTile: solid);
}
