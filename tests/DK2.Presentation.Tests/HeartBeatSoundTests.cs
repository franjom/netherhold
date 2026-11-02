// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using DK2.Data.Model;

namespace DK2.Presentation.Tests;

/// <summary>The heart's beat, <c>0x150</c> at <c>0x0043CE85</c> in the per-tile presentation pass: handle 0, the heart
/// room's terrain category, at the heart object's position, whenever the pulse gate lets a pending beat
/// through (<see cref="HeartPulseGate"/>), whatever the game turn.</summary>
public sealed class HeartBeatSoundTests
{
    private static RoomDef HeartRoom() => new()
    {
        Name = "Dungeon Heart", RoomId = (byte)RoomTypeId.DungeonHeart, Flags = 0, Cost = 0, ReturnPercentage = 0, TerrainId = 14,
        Construction = TileConstruction.Normal, CreatedCreatureId = 0, ObjectIds = [], HealthGain = 0,
        CeilingHeight = new Fp32(0), RecommendedSizeX = 1, RecommendedSizeY = 1, OrderInEditor = 0, NameStringId = 0, TooltipStringId = 0,
    };

    private static TerrainDef HeartTerrain(string category) => new()
    {
        Name = "Dungeon Heart", TerrainId = 14, Flags = 0, StartingHealth = 1, MaxHealth = 1, GoldValue = 0, ManaGain = 0,
        MaxManaGain = 0, Damage = 0, MaxHealthTypeTerrainId = 14, DestroyedTypeTerrainId = 8, Depth = new Fp32(0),
        NameStringId = 0, TooltipStringId = 0, SoundCategory = category,
    };

    [Fact]
    public void TheBeatIs0x150InTheHeartRoomsTerrainCategoryAtTheObjectsPositionWithoutAVoice()
    {
        var database = new GameDatabase([HeartTerrain("ROOM_DUNGEON_HEART")], [HeartRoom()], [], new GlobalVariables());
        var pose = new ObjectView(1, 13, new PlayerId(3), new TilePos(5, 5), default, 0,
            5 * Fp32.One + 2048, 4 * Fp32.One + 1024, Fp32.One + 512, 0, Fp32.One);

        SoundRequest beat = HeartBeatSound.For(HeartBeatSound.CategoryOf(database), pose)!.Value;

        Assert.Equal("room_dungeon_heart", beat.Category.Name);
        Assert.Equal(0x150u, beat.Id.Value);
        Assert.Equal(new Vector3(5.5f, 1.125f, -4.25f), beat.World);   // (x, height, -y), in tiles
        Assert.Equal(SoundVoiceId.None, beat.Voice);
    }

    [Fact]
    public void ARoomWithNoCategoryMakesNoBeat()
    {
        var database = new GameDatabase([HeartTerrain("")], [HeartRoom()], [], new GlobalVariables());
        Assert.Equal("", HeartBeatSound.CategoryOf(database));
        var pose = new ObjectView(1, 13, new PlayerId(3), new TilePos(5, 5), default, 0, 0, 0, 0, 0, Fp32.One);
        Assert.Null(HeartBeatSound.For("", pose));
    }
}
