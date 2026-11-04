// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary><c>Hatchery_AdoptChickens</c> (<c>0x004DEB70</c>), which the Hatchery's slot +0x09 runs after its
/// tiles change, and <c>Room_OnFreed</c> (<c>0x004E39C0</c>) releasing a freed room's birds (ADR-0652).</summary>
public class HatcheryAdoptionTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;

    private static (GameDatabase Db, WorldState World) Start()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [0, PlayerIds.Neutral, Keeper]);
        return (db, world);
    }

    private static ObjectState FreeBird(GameDatabase db, WorldState world, int x, int y, byte owner = Keeper)
    {
        ObjectState bird = world.SpawnObject(db, HatcheryRules.ChickenObjectId, owner, (y * world.Width) + x);
        bird.ChickenCanEscape = true;
        return bird;
    }

    /// <summary>Turns the keeper's claimed tile into a Hatchery tile through the slab chain, as building does.</summary>
    private static void Build(GameDatabase db, WorldState world, int x, int y, RoomTypeId type = RoomTypeId.Hatchery)
    {
        ref TileState tile = ref world.Tile(x, y);
        byte before = tile.TerrainId;
        PlayerId owner = tile.OwnerPlayerId;
        RoomRules.Place(db, ref tile, db.Room(type), Keeper);
        SlabChain.Changed(world, db, (y * world.Width) + x, before, owner);
    }

    private static void Sell(GameDatabase db, WorldState world, int x, int y)
    {
        ref TileState tile = ref world.Tile(x, y);
        byte before = tile.TerrainId;
        PlayerId owner = tile.OwnerPlayerId;
        tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
        SlabChain.Changed(world, db, (y * world.Width) + x, before, owner);
    }

    [Fact]
    public void AHatcheryBuiltUnderAFreeRangeBirdTakesItInAndItStopsDecaying()
    {
        var (db, world) = Start();
        ObjectState bird = FreeBird(db, world, 2, 2);
        Build(db, world, 2, 2);
        Assert.True(bird.HatcheryRoomAnchorTileIndex >= 0);
        Assert.False(bird.ChickenCanEscape);

        var sim = new GameSimulation(db, world, new SimRandom(new Dk2Lcg(1)), [new ChickenMovementSystem()]);
        sim.Tick();
        Assert.Equal(1000, bird.Health);
    }

    [Fact]
    public void AnotherOwnersBirdTakesTheRoomsOwner()
    {
        var (db, world) = Start();
        ObjectState bird = FreeBird(db, world, 2, 2, PlayerIds.Neutral);
        Build(db, world, 2, 2);
        Assert.Equal(Keeper, bird.OwnerPlayerId);
        Assert.True(bird.HatcheryRoomAnchorTileIndex >= 0);
    }

    /// <summary>A one-tile Hatchery holds one bird; the tile's thing list is newest first, so the newer is taken.</summary>
    [Fact]
    public void CapacityTakesTheHeadOfTheTilesThingListFirst()
    {
        var (db, world) = Start();
        ObjectState older = FreeBird(db, world, 2, 2);
        ObjectState newer = FreeBird(db, world, 2, 2);
        Build(db, world, 2, 2);
        Assert.True(newer.HatcheryRoomAnchorTileIndex >= 0);
        Assert.Equal(-1, older.HatcheryRoomAnchorTileIndex);
        Assert.True(older.ChickenCanEscape);
    }

    /// <summary>Cutting a room frees it: every bird it held leaves, and each piece takes back those on its tiles.
    /// The bird on the sold tile stays free.</summary>
    [Fact]
    public void ACutRoomsPiecesTakeBackOnlyTheBirdsOnTheirTiles()
    {
        var (db, world) = Start();
        ObjectState west = FreeBird(db, world, 1, 2);
        ObjectState middle = FreeBird(db, world, 2, 2);
        ObjectState east = FreeBird(db, world, 3, 2);
        Build(db, world, 1, 2);
        Build(db, world, 2, 2);
        Build(db, world, 3, 2);
        Assert.All([west, middle, east], b => Assert.True(b.HatcheryRoomAnchorTileIndex >= 0));

        Sell(db, world, 2, 2);
        Assert.Equal((2 * world.Width) + 1, west.HatcheryRoomAnchorTileIndex);
        Assert.Equal((2 * world.Width) + 3, east.HatcheryRoomAnchorTileIndex);
        Assert.Equal(-1, middle.HatcheryRoomAnchorTileIndex);
        Assert.True(middle.ChickenCanEscape);
    }

    [Fact]
    public void AnotherRoomTakesNoBird()
    {
        var (db, world) = Start();
        ObjectState bird = FreeBird(db, world, 2, 2);
        Build(db, world, 2, 2, RoomTypeId.Lair);
        Assert.Equal(-1, bird.HatcheryRoomAnchorTileIndex);
        Assert.True(bird.ChickenCanEscape);
    }

    /// <summary>Object_SetOwner unlinks a bird from its old room before the new owner's Hatchery admits it.</summary>
    [Fact]
    public void ABirdHeldForAnotherOwnerIsReleasedAndTakenIn()
    {
        var (db, world) = Start();
        ObjectState bird = FreeBird(db, world, 2, 2, PlayerIds.Neutral);
        (bird.HatcheryRoomAnchorTileIndex, bird.ChickenCanEscape) = ((5 * world.Width) + 7, false);
        Build(db, world, 2, 2);
        Assert.Equal(Keeper, bird.OwnerPlayerId);
        Assert.Equal((2 * world.Width) + 2, bird.HatcheryRoomAnchorTileIndex);
    }

    /// <summary>A merge frees the room absorbed behind the first neighbour: its bird standing off the room's
    /// tiles leaves, though the merged room still holds its old anchor tile.</summary>
    [Fact]
    public void AMergeReleasesTheAbsorbedRoomsBirdsThatStandOffItsTiles()
    {
        var (db, world) = Start();
        Build(db, world, 1, 2);
        Build(db, world, 3, 2);
        ObjectState away = FreeBird(db, world, 2, 1);
        (away.HatcheryRoomAnchorTileIndex, away.ChickenCanEscape) = ((2 * world.Width) + 1, false);
        // North of (2, 2) is a claimed path, so its east neighbour's room is joined and the west one absorbed.
        Build(db, world, 2, 2);
        Assert.Equal(-1, away.HatcheryRoomAnchorTileIndex);
        Assert.True(away.ChickenCanEscape);
    }
}
