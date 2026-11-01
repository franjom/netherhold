// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Tests;

public sealed class RoomPositionTests
{
    // 004E4759/60 divide slab sums; 004E47A5 rejects a centroid outside this room.
    // Captured native-room-centre-case.json: Training1118, fourteen identical tiles.
    [Fact]
    public void CapturedTrainingRoomChoosesItsOwnedTileBesideTheHole()
    {
        TilePos[] points = (from y in Enumerable.Range(29, 4)
                            from x in Enumerable.Range(14, 4)
                            where x != 15 || (y != 30 && y != 31)
                            select new TilePos(x, y)).ToArray();
        var (_, _, room) = Setup(points);
        Assert.Equal(14, room.TileCount);
        Assert.Equal(new TilePos(16, 30), Position(room));
    }

    [Fact]
    public void AnOwnedSlabCentroidWinsOverTheBoundingBoxMidpoint()
    {
        var (_, _, room) = Setup([new(5, 5), new(6, 5), new(7, 5), new(5, 6), new(5, 7)]);
        Assert.Equal(new TilePos(5, 5), Position(room));
    }

    // 004E484C..004E4954 expand east/south/west/north, rather than nearest distance.
    [Theory]
    [InlineData(5, 14)]
    [InlineData(7, 16)]
    public void ExpandingSpiralFindsTheFirstOwnedSlabRatherThanTheNearest(int side, int expectedX)
    {
        var (_, _, room) = Setup(Ring(side));
        Assert.Equal(new TilePos(expectedX, 11), Position(room));
    }

    // 004E47B0 compares room tag, not merely terrain type or player ownership.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ASeparateRoomAtTheCentroidDoesNotCountAsThisRoom(bool otherOwner)
    {
        var (db, world, _) = Setup(Ring(5));
        Place(world, db.Room(RoomTypeId.TrainingRoom),
            otherOwner ? new PlayerId(4) : new PlayerId(3), [new(12, 12)]);
        RoomInstance room = RoomIndex.Build(world, db).Single(r => r.TileCount == 16);
        Assert.Equal(new TilePos(14, 11), Position(room));
    }

    [Fact]
    public void SnapshotRebuildsTheSameOwnedRoomPosition()
    {
        var (db, world, _) = Setup(Ring(5));
        var sim = new GameSimulation(db, world, 1, []);
        GameSimulation copy = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db, []);
        Assert.Equal(new TilePos(14, 11), Position(Assert.Single(RoomIndex.Build(copy.World, db))));
        Assert.Equal(sim.StateHash(), copy.StateHash());
    }

    [Theory]
    [InlineData(1, 1, 5, 5)]
    [InlineData(3, 4, 6, 6)]
    public void SingletonAndRectangularOrdinaryRoomsKeepTheirCentroid(int width, int height, int x, int y)
    {
        var (_, _, room) = Setup(Rectangle(new TilePos(5, 5), width, height));
        Assert.Equal(new TilePos(x, y), Position(room));
    }

    // 004E45D0 Heart first slab+3; 004E45E9 Portal first slab+1.
    [Theory]
    [InlineData(RoomTypeId.DungeonHeart, 5, 17, 26)]
    [InlineData(RoomTypeId.Portal, 3, 15, 24)]
    public void ExistingRectangularHeartAndPortalAdaptersKeepTheirPosition(RoomTypeId kind, int side, int x, int y)
    {
        var (_, _, room) = Setup(Rectangle(new TilePos(14, 23), side, side), kind);
        Assert.Equal(new TilePos(x, y), Position(room));
    }

    private static TilePos Position(RoomInstance room) => new(room.CentreFieldX, room.CentreFieldY);

    private static TilePos[] Rectangle(TilePos first, int width, int height) =>
        (from y in Enumerable.Range(first.Y, height)
         from x in Enumerable.Range(first.X, width) select new TilePos(x, y)).ToArray();

    private static TilePos[] Ring(int side) =>
        (from y in Enumerable.Range(10, side)
         from x in Enumerable.Range(10, side)
         where x == 10 || y == 10 || x == 9 + side || y == 9 + side
         select new TilePos(x, y)).ToArray();

    private static void Place(WorldState world, RoomDef definition, PlayerId owner, IEnumerable<TilePos> points)
    {
        foreach (TilePos point in points)
        {
            ref TileState tile = ref world.Tile(point);
            tile.TerrainId = definition.TerrainId;
            tile.OwnerPlayerId = owner.Value;
        }
    }

    private static (GameDatabase Db, WorldState World, RoomInstance Room) Setup(
        TilePos[] points, RoomTypeId kind = RoomTypeId.TrainingRoom)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(40, 40), [(byte)0, (byte)1, (byte)2, (byte)3, (byte)4]);
        for (int y = 0; y < world.Height; y++)
        for (int x = 0; x < world.Width; x++)
        {
            ref TileState tile = ref world.Tile(new TilePos(x, y));
            tile.TerrainId = Dk2TerrainFixture.DirtPath;
            tile.OwnerPlayerId = 0;
        }
        Place(world, db.Room(kind), new PlayerId(3), points);
        return (db, world, Assert.Single(RoomIndex.Build(world, db)));
    }
}
