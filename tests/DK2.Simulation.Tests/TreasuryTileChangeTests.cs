// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Tests;

/// <summary>A Treasury's slot +0x09 (<c>0x004F3B60</c>, run by <c>Room_OnTilesChanged</c>, ADR-0669).</summary>
public class TreasuryTileChangeTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;

    private static (GameDatabase, WorldState) World()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        return (db, WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]));
    }

    private static void Build(GameDatabase db, WorldState world, int x, int y, SimRandom? rng = null)
    {
        int index = (y * world.Width) + x;
        ref TileState tile = ref world.TileAtIndex(index);
        byte terrain = tile.TerrainId, owner = tile.OwnerPlayerId;
        RoomRules.Place(db, ref tile, db.Room(RoomTypeId.Treasury), Keeper);
        SlabChain.Changed(world, db, index, terrain, owner, rng);
    }

    [Fact]
    public void ATreasuryBuiltOverLooseGoldBanksItAtOnce()
    {
        (GameDatabase db, WorldState world) = World();
        FloorGold.Drop(world, db, (3 * world.Width) + 3, 120);

        Build(db, world, 3, 3);

        Assert.Equal(120, world.Tile(3, 3).GoldHeld);
        Assert.DoesNotContain(world.Objects, o => o.ObjectTypeId == FloorGold.FloorGoldObjectId);
    }

    [Fact]
    public void SellingATileReBanksThePiecesWithAFreshAngleEach()
    {
        // Room_RemoveTile frees the old room, whose slot +0x05 empties the store (0x004D9670); each
        // piece's slot +0x09 banks it back, one rand(0x800) per store pile it creates (line 0x14F).
        (GameDatabase db, WorldState world) = World();
        for (int x = 2; x < 5; x++) Build(db, world, x, 3);
        world.Tile(2, 3).GoldHeld = 100;
        world.Tile(4, 3).GoldHeld = 200;
        var rng = new SimRandom(11);
        var mirror = new SimRandom(11);

        int index = (3 * world.Width) + 3;
        ref TileState middle = ref world.TileAtIndex(index);
        byte terrain = middle.TerrainId, owner = middle.OwnerPlayerId;
        RoomRules.Unmake(db, ref middle);
        SlabChain.Changed(world, db, index, terrain, owner, rng);

        Assert.Equal(100, world.Tile(2, 3).GoldHeld);
        Assert.Equal(200, world.Tile(4, 3).GoldHeld);
        ushort first = (ushort)mirror.NextInt(0x800), second = (ushort)mirror.NextInt(0x800);
        Assert.Equal(new ushort?[] { first, second }.Order(), new[] { world.Tile(2, 3).StoreGoldHeading, world.Tile(4, 3).StoreGoldHeading }.Order());
    }

    [Fact]
    public void AFirstTileOverLooseGoldDrawsItsPileAngle()
    {
        (GameDatabase db, WorldState world) = World();
        FloorGold.Drop(world, db, (3 * world.Width) + 3, 120);
        var mirror = new SimRandom(5);

        Build(db, world, 3, 3, new SimRandom(5));

        Assert.Equal((ushort)mirror.NextInt(0x800), world.Tile(3, 3).StoreGoldHeading);
    }

    [Fact]
    public void AJoinReBanksOnlyTheAbsorbedRoomsStore()
    {
        // Room_Absorb frees the absorbed room (slot +0x05 empties it); the kept room's piles stay.
        (GameDatabase db, WorldState world) = World();
        Build(db, world, 2, 3);
        Build(db, world, 4, 3);
        world.Tile(2, 3).GoldHeld = 100;
        world.Tile(4, 3).GoldHeld = 200;
        world.Tile(2, 3).StoreGoldHeading = 5;
        world.Tile(4, 3).StoreGoldHeading = 5;
        var mirror = new SimRandom(9);

        Build(db, world, 3, 3, new SimRandom(9));

        Assert.Equal(100, world.Tile(2, 3).GoldHeld);
        Assert.Equal(200, world.Tile(4, 3).GoldHeld);
        ushort?[] headings = [world.Tile(2, 3).StoreGoldHeading, world.Tile(4, 3).StoreGoldHeading];
        Assert.Single(headings, h => h == 5);
        Assert.Single(headings, h => h == (ushort)mirror.NextInt(0x800));
    }

    [Fact]
    public void AnotherRoomsTilesAreLeftAlone()
    {
        (GameDatabase db, WorldState world) = World();
        FloorGold.Drop(world, db, (3 * world.Width) + 3, 120);
        int index = (3 * world.Width) + 3;
        ref TileState tile = ref world.TileAtIndex(index);
        byte terrain = tile.TerrainId, owner = tile.OwnerPlayerId;
        RoomRules.Place(db, ref tile, db.Room(RoomTypeId.Lair), Keeper);
        SlabChain.Changed(world, db, index, terrain, owner);

        Assert.Single(world.Objects, o => o.ObjectTypeId == FloorGold.FloorGoldObjectId);
    }
}
