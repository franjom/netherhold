// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Tests;

/// <summary>A captured Treasury's slot +0x0D (<c>0x004F3C40</c>, ADR-0668): its gold, and the loose
/// gold on its tiles, is banked for the captor.</summary>
public class TreasuryCaptureTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = PlayerIds.FirstKeeper + 1;

    private static (GameDatabase, WorldState, RoomInstance) Treasury()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper, Rival]);
        for (int y = 2; y < 4; y++)
            for (int x = 2; x < 4; x++)
                RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Treasury), Keeper);
        RoomInstance room = RoomIndex.RoomAt(world, db, 2 * world.Width + 2)!;
        world.Tile(2, 2).GoldHeld = 500;
        world.Tile(3, 3).GoldHeld = 200;
        return (db, world, room);
    }

    [Fact]
    public void TheGoldAndTheLooseGoldOnItsTilesGoToTheCaptor()
    {
        (GameDatabase db, WorldState world, RoomInstance room) = Treasury();
        FloorGold.Drop(world, db, 2 * world.Width + 3, 50);
        int before = GoldStore.Total(world, new PlayerId(Keeper));

        RoomHealthRules.Apply(world, db, room, -1_000_000, Rival);

        Assert.Equal(700, before);
        Assert.Equal(0, GoldStore.Total(world, new PlayerId(Keeper)));
        Assert.Equal(750, GoldStore.Total(world, new PlayerId(Rival)));
        Assert.DoesNotContain(world.Objects, o => o.ObjectTypeId == FloorGold.FloorGoldObjectId);
        Assert.Equal(500, world.Tile(2, 2).GoldHeld);   // each tile's gold goes back from its own tile
        Assert.Equal(200, world.Tile(3, 3).GoldHeld);
    }

    [Fact]
    public void AnotherRoomsCaptureLeavesItsFloorGoldAlone()
    {
        // Only the Treasury's slot +0x0D banks (0x004F3C40); a Lair has none of it.
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper, Rival]);
        RoomRules.Place(db, ref world.Tile(2, 2), db.Room(RoomTypeId.Lair), Keeper);
        FloorGold.Drop(world, db, 2 * world.Width + 2, 50);

        RoomHealthRules.Apply(world, db, RoomIndex.RoomAt(world, db, 2 * world.Width + 2)!, -1_000_000, Rival);

        Assert.Single(world.Objects, o => o.ObjectTypeId == FloorGold.FloorGoldObjectId && o.GoldHeld == 50);
    }

    [Fact]
    public void EachRefilledTileDrawsAFreshPileAngle()
    {
        // Room_DepositGold draws rand(0x800) for every store pile it creates (line 0x14F).
        (GameDatabase db, WorldState world, RoomInstance room) = Treasury();
        var rng = new SimRandom(7);
        var mirror = new SimRandom(7);
        GoldStore.Recapture(world, db, room, rng);

        ushort first = (ushort)mirror.NextInt(0x800), second = (ushort)mirror.NextInt(0x800);
        Assert.Equal(new ushort?[] { first, second },
            SlabChain.Order(world, room).Where(i => world.TileAtIndex(i).GoldHeld > 0)
                .Select(i => world.TileAtIndex(i).StoreGoldHeading).ToArray());
    }
}
