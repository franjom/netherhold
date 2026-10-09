// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Tests;

/// <summary>A Casino's party (room <c>+0x41</c>, <c>+0x3D</c>, <c>+0x45</c>) through a room's rebuilds:
/// a growing room keeps it (<c>Room_AddTile</c>), a merge keeps the joined room's
/// (<c>Room_Absorb</c> writes no room field), and a lost tile ends it in every piece
/// (<c>Room_Construct</c> zeroes it and <c>Room_RemoveTile</c> restores only <c>+0x53</c>).</summary>
public class CasinoPartyRebuildTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private static readonly GameDatabase Db = Dk2TerrainFixture.Database();

    private static WorldState World()
    {
        WorldState world = WorldState.FromMap(Db, Dk2TerrainFixture.Map(), [(byte)0, 1, PlayerIds.Neutral, Keeper]);
        for (int x = 1; x <= 7; x++)
        {
            world.Tile(x, 2).TerrainId = Dk2TerrainFixture.ClaimedPath;
            world.Tile(x, 2).OwnerPlayerId = Keeper;
        }

        return world;
    }

    private static void Build(WorldState world, int x)
    {
        ref TileState tile = ref world.Tile(x, 2);
        byte before = tile.TerrainId;
        PlayerId owner = tile.OwnerPlayerId;
        RoomRules.Place(Db, ref tile, Db.Room(RoomTypeId.Casino), Keeper);
        SlabChain.Changed(world, Db, world.TileIndex(new TilePos(x, 2)), before, owner);
    }

    private static void Sell(WorldState world, int x)
    {
        ref TileState tile = ref world.Tile(x, 2);
        byte before = tile.TerrainId;
        tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
        SlabChain.Changed(world, Db, world.TileIndex(new TilePos(x, 2)), before, tile.OwnerPlayerId);
    }

    private static RoomInstance CasinoAt(WorldState world, int x) =>
        RoomIndex.Build(world, Db).Single(r => r.Definition.TypeId == RoomTypeId.Casino && r.Contains(world.TileIndex(new TilePos(x, 2))));

    private static void StartParty(WorldState world, int x, long tick)
    {
        ref TileState first = ref world.TileAtIndex(CasinoAt(world, x).TileIndices[0]);
        first.CasinoParty = CasinoJackpotRules.PartyBit | CasinoJackpotRules.MusicBit;
        first.CasinoPartyStartTick = tick;
    }

    private static int PartyTiles(WorldState world)
    {
        int count = 0;
        for (int i = 0; i < world.TileCount; i++)
            if (world.TileAtIndex(i).CasinoParty != 0) count++;
        return count;
    }

    [Fact]
    public void ACasinoKeepsItsPartyAndItsStartWhenALowerTileJoins()
    {
        WorldState world = World();
        Build(world, 5);
        Build(world, 6);
        StartParty(world, 5, 77);

        Build(world, 4);   // the new lowest tile

        RoomInstance room = CasinoAt(world, 4);
        Assert.True(CasinoJackpotRules.IsPartyOn(world, room));
        Assert.Equal(77, world.TileAtIndex(room.TileIndices[0]).CasinoPartyStartTick);
        Assert.Equal(1, PartyTiles(world));
    }

    [Fact]
    public void AMergeKeepsTheJoinedRoomsPartyAndDropsTheAbsorbedOnes()
    {
        // (3,2) joins its first neighbour in N, E, S, W order: the room to its east, (4..5,2),
        // and the room to its west, (1..2,2), is absorbed behind it.
        WorldState world = World();
        Build(world, 1);
        Build(world, 2);
        Build(world, 4);
        Build(world, 5);
        StartParty(world, 4, 10);
        Build(world, 3);
        Assert.True(CasinoJackpotRules.IsPartyOn(world, CasinoAt(world, 1)));

        WorldState other = World();
        Build(other, 1);
        Build(other, 2);
        Build(other, 4);
        Build(other, 5);
        StartParty(other, 1, 10);
        Build(other, 3);
        Assert.False(CasinoJackpotRules.IsPartyOn(other, CasinoAt(other, 1)));
        Assert.Equal(0, PartyTiles(other));
    }

    [Fact]
    public void ASaleEndsThePartyInEveryPieceAndLeavesTheKeepersCountRaised_ADk2Quirk()
    {
        WorldState world = World();
        for (int x = 3; x <= 5; x++) Build(world, x);
        StartParty(world, 3, 10);
        world.Player(Keeper).CasinoParties = 1;

        Sell(world, 4);

        Assert.False(CasinoJackpotRules.IsPartyOn(world, CasinoAt(world, 3)));
        Assert.False(CasinoJackpotRules.IsPartyOn(world, CasinoAt(world, 5)));
        Assert.Equal(0, PartyTiles(world));
        Assert.Equal(1, world.Player(Keeper).CasinoParties);
    }

    [Fact]
    public void SellingTheLastTileLeavesNoPartyOnTheFloor()
    {
        WorldState world = World();
        Build(world, 3);
        StartParty(world, 3, 10);

        Sell(world, 3);

        Assert.Equal(0, PartyTiles(world));
    }
}
