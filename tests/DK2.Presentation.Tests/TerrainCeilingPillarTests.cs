// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
namespace DK2.Presentation.Tests;
public sealed class TerrainCeilingPillarTests
{
    private const int Size = 12;
    private static (WorldState World, GameDatabase Database) Room(RoomTypeId type, int width = 3, int height = 3)
    {
        TerrainDef Terrain(byte id) => new()
        {
            Name = "Open", TerrainId = id, Flags = TerrainFlags.Ownable, StartingHealth = 1, MaxHealth = 1,
            GoldValue = 0, ManaGain = 0, MaxManaGain = 0, Damage = 0, MaxHealthTypeTerrainId = id,
            DestroyedTypeTerrainId = id, Depth = new Fp32(0), NameStringId = 0, TooltipStringId = 0,
        };
        var room = new RoomDef
        {
            Name = "Room", RoomId = (byte)type, Flags = RoomFlags.Buildable, Cost = 0, ReturnPercentage = 0,
            TerrainId = 2, Construction = TileConstruction.Normal, CreatedCreatureId = 0, ObjectIds = [],
            HealthGain = 0, CeilingHeight = new Fp32(0), RecommendedSizeX = 0, RecommendedSizeY = 0,
            OrderInEditor = 0, NameStringId = 0, TooltipStringId = 0,
        };
        var variables = new GlobalVariables();
        variables.Misc[VariableTypeId.DungeonHeartObjectHealth] = new(VariableTypeId.DungeonHeartObjectHealth, 1000, 0, 0);
        var database = new GameDatabase([Terrain(1), Terrain(2)], [room], [], variables);
        MapTile[] tiles = Enumerable.Repeat(new MapTile(1, 3, 0, 0), Size * Size).ToArray();
        for (int y = 4; y < 4 + height; y++)
            for (int x = 4; x < 4 + width; x++) tiles[(y * Size) + x] = new MapTile(2, 3, 0, 0);
        return (WorldState.FromMap(database, new LevelMap(Size, Size, tiles), [3]), database);
    }
    [Theory]
    [InlineData(RoomTypeId.Treasury)]
    [InlineData(RoomTypeId.Hatchery)]
    [InlineData(RoomTypeId.TortureChamber)]
    [InlineData(RoomTypeId.Graveyard)]
    [InlineData(RoomTypeId.Casino)]
    [InlineData(RoomTypeId.CombatPit)]
    [InlineData(RoomTypeId.Crypt)]
    public void VirtualRoomPillarsCloseTheirFourCornerTilesAndLowerTheVault(RoomTypeId type)
    {
        (WorldState world, GameDatabase database) = Room(type);
        Assert.Empty(world.Objects);
        var closed = new bool[Size * Size];
        TerrainCeiling.MarkClosed(world, database, closed);
        Assert.Equal(4, closed.Count(value => value));
        foreach ((int x, int y) in new[] { (4, 4), (6, 4), (4, 6), (6, 6) })
        {
            Assert.False(TerrainCeiling.Has(Size, Size, closed, x, y));
            Assert.Equal(0, TerrainCeiling.Nibble(Size, Size, closed, x, y));
        }
        Assert.True(TerrainCeiling.Has(Size, Size, closed, 5, 5));
        // Removing a corner breaks every 3x3 candidate; an old pillar closure must not survive the rebuild.
        world.Tile(4, 4).TerrainId = 1;
        TerrainCeiling.MarkClosed(world, database, closed);
        Assert.DoesNotContain(true, closed);
    }
    [Theory]
    [InlineData(RoomTypeId.Lair)]
    [InlineData(RoomTypeId.Workshop)]
    [InlineData(RoomTypeId.Library)]
    public void RoomsWithoutTheCommonPillarPassKeepTheirCornerRoofs(RoomTypeId type)
    {
        (WorldState world, GameDatabase database) = Room(type);
        var closed = new bool[Size * Size];
        TerrainCeiling.MarkClosed(world, database, closed);
        Assert.DoesNotContain(true, closed);
    }
    [Fact]
    public void ARoomNarrowerThanThreeTilesHasNoVirtualPillars()
    {
        (WorldState world, GameDatabase database) = Room(RoomTypeId.Hatchery, width: 2);
        var closed = new bool[Size * Size];
        TerrainCeiling.MarkClosed(world, database, closed);
        Assert.DoesNotContain(true, closed);
    }
}
