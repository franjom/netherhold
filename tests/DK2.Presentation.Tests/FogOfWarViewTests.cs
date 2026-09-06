// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation.Tests;

public sealed class FogOfWarViewTests
{
    [Fact]
    public void TwoKeepersReceiveDifferentTerrainWithoutLeakingHiddenState()
    {
        GameDatabase database = Database();
        var map = new LevelMap(2, 1,
        [
            new MapTile(1, 2, 0, 0),
            new MapTile(3, 2, 0, 0),
        ]);
        WorldState world = WorldState.FromMap(database, map, [1, 2], fogOfWarEnabled: true);
        world.Tile(0, 0).GoldHeld = 900;
        world.Tile(0, 0).SetDiscoveredBy(1);
        world.Tile(0, 0).SetTaggedBy(2, true);

        TileView keeperOne = new WorldView(database, 1).TileAt(world, 0, 0);
        TileView keeperTwo = new WorldView(database, 2).TileAt(world, 0, 0);

        Assert.Equal("terrain.secret.texture", keeperOne.Ground.Key);
        Assert.Equal((byte)2, keeperOne.OwnerPlayerId);
        Assert.Equal(900, keeperOne.GoldHeld);
        Assert.False(keeperOne.Tagged);

        Assert.Equal("terrain.fog_of_war.mesh", keeperTwo.Ground.Key);
        Assert.Equal((byte)0, keeperTwo.OwnerPlayerId);
        Assert.Equal(0, keeperTwo.GoldHeld);
        Assert.True(keeperTwo.Tagged);
    }

    [Fact]
    public void RevealThroughFogFlagShowsTheRealTerrainToEveryKeeper()
    {
        GameDatabase database = Database();
        var map = new LevelMap(1, 1, [new MapTile(3, 0, 0, 0)]);
        WorldState world = WorldState.FromMap(database, map, [1, 2], fogOfWarEnabled: true);

        TileView tile = new WorldView(database, 2).TileAt(world, 0, 0);

        Assert.Equal("terrain.gold.mesh", tile.Ground.Key);
    }

    private static GameDatabase Database() => new(
        [
            Terrain(1, "Secret", TerrainFlags.Ownable),
            Terrain(27, "Fog Of War", TerrainFlags.Solid | TerrainFlags.Impenetrable | TerrainFlags.Taggable),
            Terrain(3, "Gold", TerrainFlags.Solid | TerrainFlags.RevealThroughFogOfWar),
        ],
        rooms: [],
        creatures: [],
        variables: new GlobalVariables());

    private static TerrainDef Terrain(byte id, string name, TerrainFlags flags) => new()
    {
        Name = name,
        TerrainId = id,
        Flags = flags,
        StartingHealth = 1,
        MaxHealth = 1,
        GoldValue = 0,
        ManaGain = 0,
        MaxManaGain = 0,
        Damage = 0,
        MaxHealthTypeTerrainId = id,
        DestroyedTypeTerrainId = id,
        Depth = new Fp32(0),
        NameStringId = 0,
        TooltipStringId = 0,
    };
}
