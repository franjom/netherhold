// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Tests;

/// <summary>The caches that read a tile's terrain, owner, ruin bit and Secret Door bit (the room sweep, the structure
/// mask, the navigation bytes) trust <c>TileState.KeyWriteCount</c> instead of reading every tile on every query, so
/// each change to one of those fields has to be seen by the next query, however it was written.</summary>
public sealed class TileKeyCacheTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;

    private static GameDatabase Db => Dk2TerrainFixture.Database();

    private static WorldState World(GameDatabase db) =>
        WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper, (byte)4]);

    private static int Index(WorldState world, int x, int y) => (y * world.Width) + x;

    [Fact]
    public void RoomAt_SeesATerrainWrittenAfterAnEarlierQuery()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        RoomRules.Place(db, ref world.Tile(2, 2), db.Room(RoomTypeId.Treasury), Keeper);
        int index = Index(world, 2, 2);
        Assert.NotNull(RoomIndex.RoomAt(world, db, index));

        world.Tile(2, 2).TerrainId = Dk2TerrainFixture.ClaimedPath;

        Assert.Null(RoomIndex.RoomAt(world, db, index));
    }

    [Fact]
    public void RoomAt_SeesAnOwnerWrittenAfterAnEarlierQuery()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        RoomRules.Place(db, ref world.Tile(2, 2), db.Room(RoomTypeId.Treasury), Keeper);
        RoomRules.Place(db, ref world.Tile(3, 2), db.Room(RoomTypeId.Treasury), Keeper);
        int index = Index(world, 2, 2);
        Assert.Equal(2, RoomIndex.RoomAt(world, db, index)!.TileIndices.Count);

        world.Tile(3, 2).OwnerPlayerId = 4;

        Assert.Single(RoomIndex.RoomAt(world, db, index)!.TileIndices);
    }

    [Fact]
    public void RoomAt_ReusesTheSweepAfterWritesThatChangeNothingItReads()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        RoomRules.Place(db, ref world.Tile(2, 2), db.Room(RoomTypeId.Treasury), Keeper);
        int index = Index(world, 2, 2);
        RoomInstance? before = RoomIndex.RoomAt(world, db, index);

        world.Tile(2, 2).TerrainId = world.Tile(2, 2).TerrainId;   // no change: nothing to look at again
        world.Tile(5, 5).Health += 1;                               // a field no cache reads

        Assert.Same(before, RoomIndex.RoomAt(world, db, index));
    }

    [Fact]
    public void RoomAt_AnswersEachWorldFromItsOwnTiles()
    {
        GameDatabase db = Db;
        WorldState one = World(db), two = World(db);
        RoomRules.Place(db, ref one.Tile(2, 2), db.Room(RoomTypeId.Treasury), Keeper);
        RoomRules.Place(db, ref two.Tile(2, 2), db.Room(RoomTypeId.Treasury), Keeper);
        int index = Index(one, 2, 2);
        RoomInstance? first = RoomIndex.RoomAt(one, db, index);
        Assert.NotNull(RoomIndex.RoomAt(two, db, index));

        two.Tile(2, 2).TerrainId = Dk2TerrainFixture.ClaimedPath;

        Assert.Same(first, RoomIndex.RoomAt(one, db, index));
        Assert.Null(RoomIndex.RoomAt(two, db, index));
    }

    [Fact]
    public void RoomAt_SeesATileArrayCopiedOverTheWorld()
    {
        GameDatabase db = Db;
        WorldState target = World(db), source = World(db);
        RoomRules.Place(db, ref target.Tile(2, 2), db.Room(RoomTypeId.Treasury), Keeper);
        RoomRules.Place(db, ref source.Tile(4, 4), db.Room(RoomTypeId.Treasury), Keeper);
        int old = Index(target, 2, 2), added = Index(target, 4, 4);
        Assert.NotNull(RoomIndex.RoomAt(target, db, old));
        Assert.Null(RoomIndex.RoomAt(target, db, added));

        target.CopyFrom(source);

        Assert.Null(RoomIndex.RoomAt(target, db, old));
        Assert.NotNull(RoomIndex.RoomAt(target, db, added));
    }

    [Fact]
    public void RoomAt_SeesAWholeTileAssignedOver()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        RoomRules.Place(db, ref world.Tile(2, 2), db.Room(RoomTypeId.Treasury), Keeper);
        int index = Index(world, 2, 2);
        Assert.NotNull(RoomIndex.RoomAt(world, db, index));

        world.Tile(2, 2) = new TileState { TerrainId = Dk2TerrainFixture.ClaimedPath, OwnerPlayerId = Keeper };

        Assert.Null(RoomIndex.RoomAt(world, db, index));
    }

    [Fact]
    public void StructureBlocks_SeeAPortalBuiltAndSoldBetweenQueries()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        int centre = Index(world, 6, 6);
        Assert.False(StructureBlocks.For(world, db).Blocks(centre));

        for (int y = 5; y <= 7; y++)
            for (int x = 5; x <= 7; x++) RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Portal), Keeper);
        Assert.True(StructureBlocks.For(world, db).Blocks(centre));
        Assert.True(StructureBlocks.For(world, db).Blocks(centre));

        for (int x = 5; x <= 7; x++) world.Tile(x, 7).TerrainId = Dk2TerrainFixture.ClaimedPath;   // a 3x2 no more

        Assert.False(StructureBlocks.For(world, db).Blocks(centre));
    }

    [Fact]
    public void StructureBlocks_SeeARuinedHeart_ThroughTheRuinBitAlone()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        for (int y = 5; y <= 9; y++)
            for (int x = 5; x <= 9; x++) RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.DungeonHeart), Keeper);
        StructureBlocks standing = StructureBlocks.For(world, db);
        Assert.NotEqual(0, standing.Count);
        Assert.Same(standing, StructureBlocks.For(world, db));

        for (int y = 5; y <= 9; y++)
            for (int x = 5; x <= 9; x++) world.Tile(x, y).RoomRuined = true;

        Assert.Equal(0, StructureBlocks.For(world, db).Count);
    }

    [Fact]
    public void NavigationRefresh_SeesEachOfTheFourFieldsItReads()
    {
        GameSimulation simulation = TestWorld.Simulation();
        WorldState world = simulation.World;
        var steering = simulation.Navigation;
        steering.Refresh();
        int rebuilds = steering.Rebuilds;
        steering.Refresh();
        Assert.Equal(rebuilds, steering.Rebuilds);

        world.Tile(3, 3).TerrainId = TestWorld.Rock;
        steering.Refresh();
        Assert.Equal(rebuilds + 1, steering.Rebuilds);

        world.Tile(3, 3).TerrainId = TestWorld.DirtPath;
        steering.Refresh();
        Assert.Equal(rebuilds + 2, steering.Rebuilds);
        steering.Refresh();
        Assert.Equal(rebuilds + 2, steering.Rebuilds);
    }
}
