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
    public void SightAddsTheOriginalFogSideMeshOnlyOnUnexploredOpenGround()
    {
        GameDatabase database = Database();
        var tiles = Enumerable.Repeat(new MapTile(1, 2, 0, 0), 400).ToArray();
        WorldState world = WorldState.FromMap(database, new LevelMap(20, 20, tiles), [1, 2], true);
        var simulation = new GameSimulation(database, world, 1, []);
        simulation.Run(24);
        var view = new WorldView(database, 1);
        view.SetSightRegion(new SightRegion(GameTick.Zero, new TilePos(10, 10), 64, 5, 20, 20));

        TileView centre = view.TileAt(world, 10, 10);
        Assert.Equal("terrain.secret.texture", centre.Ground.Key);
        Assert.Equal(DK2.Assets.AssetCatalogue.TerrainPiece("Fog Of War", "side"), centre.SightFogOverlay);
        Assert.Equal(63, centre.SightFogOpacity);
        Assert.True(view.CanShowTile(world, new TilePos(10, 10)));
        Assert.False(view.CanShowTile(world, new TilePos(-1, 10)));
        Assert.False(world.Tile(10, 10).IsExploredBy(1));
        Assert.False(world.Tile(10, 10).IsDiscoveredBy(1));

        world.Tile(10, 10).SetExploredBy(1);
        Assert.True(view.TileAt(world, 10, 10).SightFogOverlay.IsEmpty);
        Assert.False(view.TileAt(world, 11, 10).SightFogOverlay.IsEmpty);
        world.Tile(11, 10).TerrainId = 3;
        Assert.True(view.TileAt(world, 11, 10).SightFogOverlay.IsEmpty);
        view.SetPossessionView(true);
        Assert.True(view.TileAt(world, 12, 10).SightFogOverlay.IsEmpty);
        view.SetPossessionView(false);
        simulation.Run(41);
        Assert.True(view.TileAt(world, 12, 10).SightFogOverlay.IsEmpty);
        Assert.False(view.CanShowTile(world, new TilePos(12, 10)));
    }

    [Fact]
    public void TwoKeepersReceiveDifferentTerrainWithoutLeakingHiddenState()
    {
        GameDatabase database = Database();
        var map = new LevelMap(2, 1,
        [
            new MapTile(1, 3, 0, 0),
            new MapTile(3, 2, 0, 0),
        ]);
        WorldState world = WorldState.FromMap(database, map, [1, 2], fogOfWarEnabled: true);
        world.Tile(0, 0).GoldHeld = 900;
        world.Tile(0, 0).SetExploredBy(1);
        world.Tile(0, 0).SetTaggedBy(2, true);

        TileView keeperOne = new WorldView(database, 1).TileAt(world, 0, 0);
        TileView keeperTwo = new WorldView(database, 2).TileAt(world, 0, 0);

        Assert.Equal("terrain.secret.texture", keeperOne.Ground.Key);
        Assert.Equal((byte)3, keeperOne.OwnerPlayerId);
        Assert.Equal(900, keeperOne.GoldHeld);
        Assert.False(keeperOne.Tagged);

        Assert.Equal("terrain.fog_of_war.mesh", keeperTwo.Ground.Key);
        Assert.Equal((byte)0, keeperTwo.OwnerPlayerId);
        Assert.Equal(0, keeperTwo.GoldHeld);
        Assert.True(keeperTwo.Tagged);
    }

    [Fact]
    public void RevealThroughFogFlagRequiresScanningBeforeShowingTheRealTerrain()
    {
        GameDatabase database = Database();
        var map = new LevelMap(1, 1, [new MapTile(3, 0, 0, 0)]);
        WorldState world = WorldState.FromMap(database, map, [1, 2], fogOfWarEnabled: true);

        var view = new WorldView(database, 1);
        Assert.Equal("terrain.fog_of_war.mesh", view.TileAt(world, 0, 0).Ground.Key);
        world.Tile(0, 0).SetDiscoveredBy(1);
        TileView tile = view.TileAt(world, 0, 0);

        Assert.Equal("terrain.gold.mesh", tile.Ground.Key);
        Assert.False(tile.Explored);
    }

    [Fact]
    public void ScannedOrdinaryTerrainUsesClaimScanWithoutLeakingOwnershipOrGold()
    {
        GameDatabase database = Database();
        var map = new LevelMap(1, 1, [new MapTile(1, 2, 0, 0)]);
        WorldState world = WorldState.FromMap(database, map, [1, 2], fogOfWarEnabled: true);
        world.Tile(0, 0).SetDiscoveredBy(1);
        world.Tile(0, 0).GoldHeld = 900;

        TileView tile = new WorldView(database, 1).TileAt(world, 0, 0);

        Assert.Equal("terrain.claim_scan.mesh", tile.Ground.Key);
        Assert.False(tile.Explored);
        Assert.Equal((byte)0, tile.OwnerPlayerId);
        Assert.Equal(0, tile.GoldHeld);
        Assert.True(tile.Furniture.IsEmpty);
    }

    private static GameDatabase Database() => new(
        [
            Terrain(1, "Secret", TerrainFlags.Ownable),
            Terrain(27, "Fog Of War", TerrainFlags.Solid | TerrainFlags.Impenetrable | TerrainFlags.Taggable),
            Terrain(38, "Claim Scan", TerrainFlags.Solid),
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
