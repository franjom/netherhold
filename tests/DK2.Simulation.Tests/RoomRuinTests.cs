// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Tests;

/// <summary>The ruin is the room's (+0x9A bit 0), not the keeper's (ADR-0662): a captured ruined Heart
/// stays a ruin, so its captor stores no gold in it (0x00464400 skips it) and a third keeper may claim it
/// (0x00454040).</summary>
public sealed class RoomRuinTests
{
    private const byte A = PlayerIds.FirstKeeper, B = PlayerIds.FirstKeeper + 1, C = PlayerIds.FirstKeeper + 2;

    private static (GameDatabase Db, WorldState World, RoomInstance Heart) RuinedHeartOfA()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, A, B, C]);
        for (int y = 5; y <= 7; y++)
            for (int x = 5; x <= 7; x++)
                RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.DungeonHeart), A);
        world.Player(A).HeartRuined = true;
        RoomRules.RuinHearts(world, db, A);
        return (db, world, Heart(db, world));
    }

    private static RoomInstance Heart(GameDatabase db, WorldState world) =>
        RoomIndex.Build(world, db).Single(r => r.Definition.TypeId == RoomTypeId.DungeonHeart);

    private static void Capture(WorldState world, RoomInstance room, byte by)
    {
        foreach (int index in room.TileIndices) world.TileAtIndex(index).OwnerPlayerId = by;
    }

    [Fact]
    public void ACapturedRuinedHeartStaysARuinAndTakesNoGold()
    {
        (GameDatabase db, WorldState world, RoomInstance heart) = RuinedHeartOfA();
        Capture(world, heart, B);
        RoomInstance captured = Heart(db, world);

        Assert.Equal(B, captured.OwnerPlayerId);
        Assert.False(world.Player(B).HeartRuined);
        Assert.True(RoomRules.IsRuined(world, captured));
        Assert.False(GoldStore.Accepts(world, captured));
        Assert.Equal(0, GoldStore.Capacity(world, db, captured.TileIndices[0], captured));
    }

    [Fact]
    public void AThirdKeeperMayClaimTheCapturedRuin()
    {
        (GameDatabase db, WorldState world, RoomInstance heart) = RuinedHeartOfA();
        Capture(world, heart, B);
        TileState tile = world.TileAtIndex(heart.TileIndices[0]);

        Assert.True(TileWorkRules.Applies(world, db, tile, TileWorkKind.ConvertRoom, C));
    }

    [Fact]
    public void ALiveHeartIsNotClaimable()
    {
        (GameDatabase db, WorldState world, RoomInstance heart) = RuinedHeartOfA();
        foreach (int index in heart.TileIndices) world.TileAtIndex(index).RoomRuined = false;

        Assert.False(TileWorkRules.Applies(world, db, world.TileAtIndex(heart.TileIndices[0]), TileWorkKind.ConvertRoom, B));
    }

    /// <summary>A tile that joins a room takes the room's ruin, as the join keeps the room (0x004EC650).</summary>
    [Fact]
    public void ATileJoiningARuinedRoomIsRuinedToo()
    {
        (GameDatabase db, WorldState world, _) = RuinedHeartOfA();
        int index = (6 * world.Width) + 8;
        ref TileState tile = ref world.TileAtIndex(index);
        byte terrainBefore = tile.TerrainId, ownerBefore = tile.OwnerPlayerId;
        RoomRules.Place(db, ref tile, db.Room(RoomTypeId.DungeonHeart), A);
        SlabChain.Changed(world, db, index, terrainBefore, ownerBefore);

        Assert.True(tile.RoomRuined);
        Assert.True(RoomRules.IsRuined(world, Heart(db, world)));
    }

    /// <summary>Taking a tile out rebuilds what is left through Room_Construct, which clears the bit
    /// (0x004E392D): the pieces start unruined.</summary>
    [Fact]
    public void TakingATileOutLeavesUnruinedPieces()
    {
        (GameDatabase db, WorldState world, RoomInstance heart) = RuinedHeartOfA();
        int index = (6 * world.Width) + 6;
        ref TileState tile = ref world.TileAtIndex(index);
        byte terrainBefore = tile.TerrainId, ownerBefore = tile.OwnerPlayerId;
        RoomRules.Unmake(db, ref tile);
        SlabChain.Changed(world, db, index, terrainBefore, ownerBefore);

        Assert.All(heart.TileIndices.Where(i => i != index), i => Assert.False(world.TileAtIndex(i).RoomRuined));
    }
}
