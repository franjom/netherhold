// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public class AutoFurnitureTests
{
    private const byte Keeper = 2;

    private static GameDatabase Database() => Dk2TerrainFixture.Database();

    private static WorldState World(GameDatabase database) => WorldState.FromMap(
        database, Dk2TerrainFixture.Map(), [(byte)0, (byte)1, Keeper]);

    private static void PaintRectangle(
        GameDatabase database, WorldState world, RoomTypeId type,
        int x, int y, int width, int height)
    {
        RoomDef room = database.Room(type);
        for (int dy = 0; dy < height; dy++)
        for (int dx = 0; dx < width; dx++)
            RoomRules.Place(database, ref world.Tile(x + dx, y + dy), room, Keeper);
    }

    [Fact]
    public void LibraryFurnitureUsesOnlyTilesSurroundedOnAllEightSides()
    {
        GameDatabase database = Database();
        WorldState world = World(database);
        PaintRectangle(database, world, RoomTypeId.Library, 4, 4, 4, 4);

        new GameSimulation(database, world, 1, [new AutoFurnitureSystem()]).Tick();

        Assert.Equal(
            [Index(world, 5, 5), Index(world, 6, 5), Index(world, 5, 6), Index(world, 6, 6)],
            world.Objects.Select(o => o.TileIndex).Order());
        Assert.All(world.Objects, o =>
        {
            Assert.True(o.AutoPlaced);
            Assert.Contains(o.ObjectTypeId, new byte[]
            {
                Dk2TerrainFixture.BookCaseObjectId,
                Dk2TerrainFixture.LibraryBookObjectId,
            });
        });
    }

    [Fact]
    public void FurnitureIsReconciledAfterTheRoomShapeChanges()
    {
        GameDatabase database = Database();
        WorldState world = World(database);
        PaintRectangle(database, world, RoomTypeId.Library, 4, 4, 3, 3);
        var simulation = new GameSimulation(database, world, 1, [new AutoFurnitureSystem()]);
        simulation.Tick();

        ObjectState placedByKeeper = world.SpawnObject(
            database, Dk2TerrainFixture.BookCaseObjectId, Keeper, Index(world, 5, 5));
        RoomRules.Unmake(database, ref world.Tile(4, 4));
        simulation.Tick();

        Assert.Same(placedByKeeper, Assert.Single(world.Objects));
        Assert.False(placedByKeeper.AutoPlaced);
    }

    [Fact]
    public void FurnitureCountIsTheLibraryCapacityPlusOne()
    {
        GameDatabase database = Database();
        WorldState world = World(database);
        PaintRectangle(database, world, RoomTypeId.Library, 4, 4, 4, 4);
        new GameSimulation(database, world, 1, [new AutoFurnitureSystem()]).Tick();
        RoomInstance room = Assert.Single(RoomIndex.Build(world, database));

        Assert.True(AutoFurnitureRules.TryTotalCapacity(world, room, out int capacity));
        Assert.Equal(5, capacity);
    }

    [Fact]
    public void TempleHandNeedsTwoCompleteRingsOfTempleTiles()
    {
        GameDatabase database = Database();
        WorldState world = World(database);
        var desired = new List<(int TileIndex, byte ObjectTypeId)>();

        PaintRectangle(database, world, RoomTypeId.Temple, 3, 3, 3, 5);
        AutoFurnitureRules.DesiredFurniture(
            world, database, Assert.Single(RoomIndex.Build(world, database)), desired);
        Assert.Empty(desired);

        PaintRectangle(database, world, RoomTypeId.Temple, 8, 3, 5, 5);
        RoomInstance large = RoomIndex.Build(world, database).Single(r => r.TileIndices.Count == 25);
        AutoFurnitureRules.DesiredFurniture(world, database, large, desired);

        Assert.Equal((Index(world, 10, 5), AutoFurnitureRules.TempleHandObjectId),
            Assert.Single(desired));
    }

    [Fact]
    public void AutoPlacedStateSurvivesSnapshotAndHashing()
    {
        GameDatabase database = Database();
        WorldState world = World(database);
        PaintRectangle(database, world, RoomTypeId.Library, 4, 4, 3, 3);
        var simulation = new GameSimulation(database, world, 1, [new AutoFurnitureSystem()]);
        simulation.Tick();

        GameSimulation restored = WorldSnapshot.FromBytes(
            WorldSnapshot.ToBytes(simulation), database, [new AutoFurnitureSystem()]);

        Assert.True(Assert.Single(restored.World.Objects).AutoPlaced);
        Assert.Equal(simulation.StateHash(), restored.StateHash());
    }

    [Fact]
    public void WallFurnitureNeedsAThreeTileStraightWallAndUsesTheFourthObjectId()
    {
        GameDatabase basis = Database();
        RoomDef casino = basis.Room(RoomTypeId.Casino) with { ObjectIds = [87, 59, 67, 68] };
        ObjectDef template = basis.ObjectById(Dk2TerrainFixture.BookCaseObjectId)!;
        TerrainDef wall = basis.TerrainById(Dk2TerrainFixture.ReinforcedWall) with
        {
            Flags = basis.TerrainById(Dk2TerrainFixture.ReinforcedWall).Flags
                | TerrainFlags.AllowRoomWalls,
        };
        var database = new GameDatabase(
            basis.Terrain.Values.Select(t => t.TerrainId == wall.TerrainId ? wall : t),
            basis.Rooms.Values.Select(r => r.RoomId == casino.RoomId ? casino : r),
            basis.Creatures.Values,
            basis.Variables,
            objects:
            [
                .. basis.Objects.Values,
                template with { Name = "Roulette Table", ObjectId = 87 },
                template with { Name = "Barrel", ObjectId = 59 },
                template with { Name = "Bar Table", ObjectId = 67 },
                template with { Name = "Alcove Barrels", ObjectId = 68 },
            ]);
        WorldState world = World(database);
        PaintRectangle(database, world, RoomTypeId.Casino, 4, 4, 5, 3);
        for (int x = 4; x <= 8; x++)
            world.Tile(x, 3).TerrainId = Dk2TerrainFixture.ReinforcedWall;

        new GameSimulation(database, world, 1, [new AutoFurnitureSystem()]).Tick();

        ObjectState[] wallObjects = world.Objects.Where(o => o.ObjectTypeId == 68).ToArray();
        Assert.Equal([Index(world, 5, 4), Index(world, 7, 4)],
            wallObjects.Select(o => o.TileIndex));
        Assert.All(wallObjects, o => Assert.True(o.AutoPlaced));
    }

    private static int Index(WorldState world, int x, int y) => (y * world.Width) + x;
}
