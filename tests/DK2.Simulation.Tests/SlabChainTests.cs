// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>A room's slab chain: the build wave <c>0x004C7380</c>, the insert <c>0x004ED320</c>
/// (join at the tail, N/E/S/W, merge behind) and the sale's replay <c>0x004ED7B0</c>
/// (ADR-0240, <c>room-slab-chain.md</c>).</summary>
public sealed class SlabChainTests
{
    private const byte Keeper = 4;

    private static (GameDatabase, WorldState, GameSimulation) World()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, Keeper]);
        for (int y = 1; y <= 10; y++)
        for (int x = 1; x <= 14; x++)
        {
            ref TileState tile = ref world.Tile(x, y);
            tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
            tile.OwnerPlayerId = Keeper;
        }

        world.Tile(14, 10).GoldHeld = 1_000_000;
        return (db, world, new GameSimulation(db, world, 1, [new RoomBuildSystem(), new RoomSellSystem()]));
    }

    private static byte Prison(GameDatabase db) => db.Room(RoomTypeId.Prison).RoomId;

    private static void Run(GameSimulation sim, PlayerCommand command) => sim.Tick(new InputFrame([command]));

    private static void Click(GameDatabase db, GameSimulation sim, params TilePos[] tiles)
    {
        foreach (TilePos t in tiles) Run(sim, BuildRoomCommand.At(Keeper, Prison(db), t));
    }

    private static RoomInstance RoomOn(GameDatabase db, WorldState world, TilePos at) =>
        RoomIndex.RoomAt(world, db, (at.Y * world.Width) + at.X)!;

    private static TilePos[] Chain(GameDatabase db, WorldState world, TilePos at) =>
        [.. SlabChain.Order(world, RoomOn(db, world, at)).Select(i => new TilePos(i % world.Width, i / world.Width))];

    private static TilePos Door(GameDatabase db, WorldState world, TilePos at)
    {
        int door = JailBreakRules.DoorTile(world, RoomOn(db, world, at));
        return new TilePos(door % world.Width, door / world.Width);
    }

    [Fact]
    public void TheWaveRunsFromTheSecondCorner_ADiagonalAtATime_AlongEachFromItsXTowardTheFirst()
    {
        Assert.Equal(
            [new(2, 1), new(2, 0), new(1, 1), new(1, 0), new(0, 1), new(0, 0)],
            SlabChain.Wave(0, 0, 2, 1));

        (GameDatabase db, WorldState world, GameSimulation sim) = World();
        Run(sim, BuildRoomCommand.Rect(Keeper, Prison(db), 4, 2, 6, 3));
        Assert.Equal([new(6, 3), new(6, 2), new(5, 3), new(5, 2), new(4, 3), new(4, 2)], Chain(db, world, new TilePos(4, 2)));
    }

    /// <summary>The owner's two builds (2026-09-26): the same 5×5 at (4..8, 2..6), north up. Dragged
    /// top-left to bottom-right the door is in the east interior column, top-right to bottom-left in
    /// the west one. The released corner's x decides; its y does not.</summary>
    [Theory]
    [InlineData(4, 2, 8, 6, 7)]
    [InlineData(8, 2, 4, 6, 5)]
    [InlineData(4, 6, 8, 2, 7)]
    [InlineData(8, 6, 4, 2, 5)]
    public void ADraggedPrisonsDoorIsAtTheReleasedCornersEndOfTheTopInteriorRow(int x1, int y1, int x2, int y2, int doorX)
    {
        (GameDatabase db, WorldState world, GameSimulation sim) = World();
        Run(sim, BuildRoomCommand.Rect(Keeper, Prison(db), x1, y1, x2, y2));
        Assert.Equal(new TilePos(doorX, 2), Door(db, world, new TilePos(6, 4)));
    }

    [Fact]
    public void ARoomBuiltClickByClickIsChainedInClickOrder_SoItsDoorIsNorthOfTheFirstTopTileClicked()
    {
        foreach (bool eastFirst in new[] { true, false })
        {
            (GameDatabase db, WorldState world, GameSimulation sim) = World();
            var clicks = new List<TilePos>();
            for (int y = 6; y >= 2; y--)
                for (int i = 0; i < 5; i++)
                    clicks.Add(new TilePos(eastFirst ? 8 - i : 4 + i, y));
            Click(db, sim, [.. clicks]);

            Assert.Equal(clicks, Chain(db, world, new TilePos(6, 4)));
            Assert.Equal(new TilePos(eastFirst ? 7 : 5, 2), Door(db, world, new TilePos(6, 4)));
        }
    }

    [Fact]
    public void ATileJoiningTwoRoomsTakesTheNorthOnesChain_ThenItself_ThenTheSouthOnes_ThoughTheSouthIsOlder()
    {
        (GameDatabase db, WorldState world, GameSimulation sim) = World();
        Click(db, sim, new TilePos(5, 5), new TilePos(5, 6));   // the south room, first
        Click(db, sim, new TilePos(5, 2), new TilePos(5, 3));   // the north room
        Click(db, sim, new TilePos(5, 4));

        Assert.Equal([new(5, 2), new(5, 3), new(5, 4), new(5, 5), new(5, 6)], Chain(db, world, new TilePos(5, 4)));
    }

    /// <summary>A sale takes the room's other tiles in their old order and inserts them again. Here the
    /// replay starts two rooms and a later tile joins them, so the chain comes back reordered.</summary>
    [Fact]
    public void SellingATileReinsertsTheRestInTheirOldOrder_WhichCanReorderTheChain()
    {
        (GameDatabase db, WorldState world, GameSimulation sim) = World();
        Click(db, sim, new TilePos(6, 4), new TilePos(4, 4), new TilePos(5, 4), new TilePos(6, 5), new TilePos(4, 5), new TilePos(5, 5));
        Assert.Equal([new(6, 4), new(5, 4), new(4, 4), new(6, 5), new(4, 5), new(5, 5)], Chain(db, world, new TilePos(5, 5)));

        Run(sim, SellRoomCommand.At(Keeper, new TilePos(5, 4)));
        Assert.Equal([new(6, 4), new(6, 5), new(5, 5), new(4, 4), new(4, 5)], Chain(db, world, new TilePos(5, 5)));
    }

    [Fact]
    public void TheChainSurvivesASnapshot()
    {
        (GameDatabase db, WorldState world, GameSimulation sim) = World();
        Run(sim, BuildRoomCommand.Rect(Keeper, Prison(db), 8, 2, 4, 6));
        TilePos[] before = Chain(db, world, new TilePos(6, 4));

        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db);
        Assert.Equal(before, Chain(db, restored.World, new TilePos(6, 4)));
        Assert.Equal(sim.World.NextSlabOrder, restored.World.NextSlabOrder);
    }
}
