// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;
using DK2.Simulation.Navigation;
using DK2.Simulation.Steering;

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

    /// <summary>A whole <c>TileState</c> assigned over a tile copies the private fields without running the setters, so
    /// nothing counts it: the caches keep answering from the tile it replaced until some other counted write, or a
    /// <c>NoteBulkKeyWrite</c>, makes them look again. Nothing in <c>src/</c> may do it
    /// (<c>TileWriteGuardTests</c> in DK2.Tools.Tests reads the source for it), and the one bulk copy, the world's
    /// <c>CopyFrom</c>, calls <c>NoteBulkKeyWrite</c>. The staleness itself is not asserted, because the count is
    /// process-wide and a test running beside this one moves it; what is pinned is that the call makes the write seen.
    /// The tile assigned is a copy of one that exists elsewhere, so no setter runs for the new values.</summary>
    [Fact]
    public void RoomAt_SeesAWholeTileAssignedOver_OnceItIsCounted()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        RoomRules.Place(db, ref world.Tile(2, 2), db.Room(RoomTypeId.Treasury), Keeper);
        int index = Index(world, 2, 2);
        Assert.NotNull(RoomIndex.RoomAt(world, db, index));
        TileState elsewhere = world.Tile(5, 5);
        Assert.NotEqual(world.Tile(2, 2).TerrainId, elsewhere.TerrainId);

        world.Tile(2, 2) = elsewhere;   // the forbidden write: no setter, nothing counted
        NoteBulkKeyWrite();

        Assert.Null(RoomIndex.RoomAt(world, db, index));
    }

    // TileState.NoteBulkKeyWrite is internal to DK2.Simulation, which has no friend among the test projects.
    private static void NoteBulkKeyWrite() =>
        typeof(TileState).GetMethod("NoteBulkKeyWrite", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(null, null);

    [Fact]
    public void StructureBlocks_SeeAPortalBuiltAndSoldBetweenQueries()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        int centre = Index(world, 6, 6);
        Assert.False(StructureBlocks.For(world, db).Blocks(centre));

        for (int y = 5; y <= 7; y++)
        {
            for (int x = 5; x <= 7; x++)
            {
                RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Portal), Keeper);
            }
        }

        Assert.True(StructureBlocks.For(world, db).Blocks(centre));
        Assert.True(StructureBlocks.For(world, db).Blocks(centre));

        for (int x = 5; x <= 7; x++)
        {
            world.Tile(x, 7).TerrainId = Dk2TerrainFixture.ClaimedPath;   // a 3x2 no more
        }

        Assert.False(StructureBlocks.For(world, db).Blocks(centre));
    }

    [Fact]
    public void StructureBlocks_SeeARuinedHeart_ThroughTheRuinBitAlone()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        for (int y = 5; y <= 9; y++)
        {
            for (int x = 5; x <= 9; x++)
            {
                RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.DungeonHeart), Keeper);
            }
        }

        var standing = StructureBlocks.For(world, db);
        Assert.NotEqual(0, standing.Count);
        Assert.Same(standing, StructureBlocks.For(world, db));

        for (int y = 5; y <= 9; y++)
        {
            for (int x = 5; x <= 9; x++)
            {
                world.Tile(x, y).RoomRuined = true;
            }
        }

        Assert.Equal(0, StructureBlocks.For(world, db).Count);
    }

    // The navigation refresh skips everything while none of a tile's terrain, owner, ruin bit or Secret Door bit changed,
    // so each of the four has to be seen by the next refresh. Each test makes a tile's write change something the
    // refresh produces, and checks it did after the write and not before.

    [Fact]
    public void NavigationRefresh_SeesATerrainWrittenAfterAnEarlierRefresh()
    {
        GameSimulation simulation = TestWorld.Simulation();
        WorldState world = simulation.World;
        WorldSteering steering = simulation.Navigation;
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

    [Fact]
    public void NavigationRefresh_SeesAnOwnerWrittenOnAPortalTile()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        for (int y = 5; y <= 7; y++)
        {
            for (int x = 5; x <= 7; x++)
            {
                RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Portal), Keeper);
            }
        }

        var steering = new WorldSteering(db, world);
        steering.Refresh();
        Assert.Equal(NavBytes.PortalHole, steering.NavByte(6, 6));
        int rebuilds = steering.Rebuilds;
        steering.Refresh();
        Assert.Equal(rebuilds, steering.Rebuilds);

        for (int x = 5; x <= 7; x++)
        {
            world.Tile(x, 7).OwnerPlayerId = 4;   // a 3x2 portal no more
        }

        steering.Refresh();

        Assert.Equal(NavBytes.Floor, steering.NavByte(6, 6));
        Assert.Equal(rebuilds + 1, steering.Rebuilds);
    }

    [Fact]
    public void NavigationRefresh_SeesTheRuinBitOfAHeart()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        for (int y = 5; y <= 9; y++)
        {
            for (int x = 5; x <= 9; x++)
            {
                RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.DungeonHeart), Keeper);
            }
        }

        var steering = new WorldSteering(db, world);
        steering.Refresh();
        // A standing heart's core keeps a body outside it from walking in; a ruined one does not (HeartPlatformMovementTests).
        Fixed16Vector outside = At(3, 7), core = At(7, 7);
        Assert.False(steering.CanMove(outside, core));

        for (int y = 5; y <= 9; y++)
        {
            for (int x = 5; x <= 9; x++)
            {
                world.Tile(x, y).RoomRuined = true;
            }
        }

        steering.Refresh();

        Assert.True(steering.CanMove(outside, core));
    }

    [Fact]
    public void NavigationRefresh_SeesTheSecretDoorBitOfADoorTile()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        ThingProjection.Place(world, db, [new DoorThing(new byte[20]) { X = 4, Y = 4, PlayerId = Keeper, DoorId = 1, Unknown_0011 = new byte[3] }]);
        var steering = new WorldSteering(db, world);
        steering.Refresh();
        byte plain = steering.NavByte(4, 4);
        Assert.NotEqual(NavBytes.Floor, plain);
        int rebuilds = steering.Rebuilds;
        steering.Refresh();
        Assert.Equal(rebuilds, steering.Rebuilds);

        world.Tile(4, 4).SecretDoor = true;
        steering.Refresh();

        Assert.NotEqual(plain, steering.NavByte(4, 4));
        Assert.Equal(rebuilds + 1, steering.Rebuilds);
    }

    private static Fixed16Vector At(int x, int y) => new(CreatureState.CentreOf(x) << 4, CreatureState.CentreOf(y) << 4);
}
