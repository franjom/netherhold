// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>Flame Round Room (effect 20): its own gtorch sprite in its owner's colour grows from 0
/// to its MaxScale over half its life and shrinks back (<c>0x004347E0</c> flag 0x20,
/// <c>0x004B3B40</c>), and its Next effect, Room Flame Data (118), throws three sparks an update.</summary>
public sealed class RoomBorderFlameEffectTests
{
    private const ushort Flame = 20, Data = 118, Spark = 265;

    [Fact]
    public void TheFlameGrowsToItsMaxScaleThenShrinksBackAndItsNextEffectThrowsSparks()
    {
        long now = 0;
        var model = new RoomConstructionParticles(Database(), 1, () => new EffectTime(now));
        model.Request(new ViewEffectRequest(new EffectId(Flame), new PlayerId(3),
            new FpPosition3(new(8192 + 512), new(8192 + 2000), new(4096))));

        ConstructionParticle flame = Assert.Single(model.Particles);
        Assert.Equal("gtorch", flame.Definition.ArtResource.Name);
        Assert.Equal((230, 50, 50), (flame.Red, flame.Green, flame.Blue));   // palette 3
        Assert.Equal(0, flame.ScaleRaw);

        now = 1;   // an update is due once more than 50 ms have passed
        var sizes = new List<int>();
        for (int update = 1; update <= 19; update++)
        {
            now += 50;
            model.AdvanceTo(new EffectTime(now));
            sizes.Add(flame.ScaleRaw);
        }
        // 1228 / 10 = 122 an update: 1220 falls short, the eleventh reaches 1228 and turns back.
        Assert.Equal([122, 244, 366, 488, 610, 732, 854, 976, 1098, 1220, 1228,
            1106, 984, 862, 740, 618, 496, 374, 252], sizes);
        Assert.Contains(flame, model.Particles);
        Assert.Equal(3, model.Particles.Count(p => p.Definition.EffectElementId == Spark));

        now += 50;
        model.AdvanceTo(new EffectTime(now));
        Assert.DoesNotContain(flame, model.Particles);
    }

    [Fact]
    public void ANeutralFlameTakesThePaletteColourOfTheWallClocksTenthOfASecond()
    {
        long now = 250;
        var model = new RoomConstructionParticles(Database(), 1, () => new EffectTime(now));
        var at = new FpPosition3(new(8192), new(8192), new(4096));
        model.Request(new ViewEffectRequest(new EffectId(Flame), new PlayerId(PlayerIds.Neutral), at));
        now = 640;
        model.Request(new ViewEffectRequest(new EffectId(Flame), new PlayerId(PlayerIds.Neutral), at));

        ConstructionParticle[] flames = [.. model.Particles.Where(p => p.Definition.Name == "Flame Round Room")];
        Assert.Equal((230, 50, 50), (flames[0].Red, flames[0].Green, flames[0].Blue));   // 250 ms: palette 3
        Assert.Equal((230, 50, 212), (flames[1].Red, flames[1].Green, flames[1].Blue));  // 640 ms: palette 7
    }

    [InstallFact]
    public void ShippedFlameRoundRoomIsTheResearchedGrowingSpriteChainedToItsSparks()
    {
        var db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        EffectDef flame = db.Effects[Flame];
        Assert.Equal(("gtorch", 0x67u, 0, 0xA0u), (flame.ArtResource.Name, flame.ArtResource.Flags, flame.GenerationType, flame.Flags));
        Assert.Equal((0, 1228, 20), (flame.MinScale, flame.MaxScale, (int)flame.MinHp));
        Assert.Equal(Data, flame.NextEffectId);
        Assert.Equal([Spark], db.Effects[Data].GenerateIds.Where(id => id != 0));
        var model = new RoomConstructionParticles(db, 1, () => new EffectTime(0));
        model.Request(new ViewEffectRequest(new EffectId(Flame), new PlayerId(3),
            new FpPosition3(new(8192), new(8192), new(4096))));
        Assert.Single(model.Particles);
        model.AdvanceTo(new EffectTime(51));
        Assert.Equal(4, model.Particles.Count);
    }

    private static GameDatabase Database()
    {
        var none = new KwdLight(0, 0, 0, 0, 0, 0, 0, 0, 0);
        var flame = new EffectDef([])
        {
            EffectId = Flame, Name = "Flame Round Room", NextEffectId = Data,
            ArtResource = new KwdArtResource("gtorch", 0x67, new byte[12], 3, 0, 0, 0),
            Light = none, GenerateIds = [], GenerationType = 0, Flags = 0xA0,
            MinHp = 20, MaxHp = 20, MinScale = 0, MaxScale = 1228,
        };
        var data = new EffectDef([])
        {
            EffectId = Data, Name = "Room Flame Data", ArtResource = new KwdArtResource("", 0, new byte[12], 0, 0, 0, 0),
            Light = none, GenerateIds = [Spark], GenerationType = 4, ElementsPerTurn = 3, Flags = 0x280,
            MinHp = 1, MaxHp = 1,
        };
        var spark = new EffectElementDef([])
        {
            EffectElementId = Spark, Name = "Flame round room element 0",
            ArtResource = new KwdArtResource("gsparkle", 0x84, new byte[12], 3, 0, 0, 0),
            MinHp = 24, MaxHp = 24, MinScale = 245, MaxScale = 409, Red = 255, Green = 255, Blue = 255,
            MinSpeedYz = 1638, MaxSpeedYz = 3072,
        };
        return new GameDatabase([], [], [], new GlobalVariables(), effects: [flame, data], effectElements: [spark]);
    }
}
