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

    private static (GameDatabase, WorldState, GameSimulation) World(GameDatabase? definition = null)
    {
        GameDatabase db = definition ?? Dk2TerrainFixture.Database();
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

    private static RoomTypeId Prison(GameDatabase db) => RoomTypeId.Prison;

    private static RoomInstance Drag(GameDatabase db, WorldState world, GameSimulation sim, RoomTypeId type,
        int x1, int y1, int x2, int y2)
    {
        Run(sim, BuildRoomCommand.Rect(Keeper, type, x1, y1, x2, y2));
        return RoomOn(db, world, new TilePos(x1, y1));
    }

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

    /// <summary>The owner (2026-09-26): a Prison dragged so its door is in the east column, saved and
    /// reloaded, has its door in the west column. DK2's load re-chains every room row-major.</summary>
    [Fact]
    public void LoadingASavedGameReChainsEveryRoomRowMajor_SoADraggedPrisonsDoorMovesWest()
    {
        (GameDatabase db, WorldState world, GameSimulation sim) = World();
        Run(sim, BuildRoomCommand.Rect(Keeper, Prison(db), 4, 2, 8, 6));
        Assert.Equal(new TilePos(7, 2), Door(db, world, new TilePos(6, 4)));

        GameSimulation loaded = WorldSnapshot.LoadGame(WorldSnapshot.ToBytes(sim), db);
        Assert.Equal(new TilePos(5, 2), Door(db, loaded.World, new TilePos(6, 4)));
        Assert.Equal(new TilePos(4, 2), Chain(db, loaded.World, new TilePos(6, 4))[0]);
    }

    /// <summary>A checkpoint, unlike a loaded game, restores the chain exactly.</summary>
    [Fact]
    public void ACheckpointKeepsTheChain()
    {
        (GameDatabase db, WorldState world, GameSimulation sim) = World();
        Run(sim, BuildRoomCommand.Rect(Keeper, Prison(db), 8, 2, 4, 6));
        TilePos[] before = Chain(db, world, new TilePos(6, 4));

        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db);
        Assert.Equal(before, Chain(db, restored.World, new TilePos(6, 4)));
        Assert.Equal(sim.World.NextSlabOrder, restored.World.NextSlabOrder);
    }

    [Fact]
    public void ADraggedHatcheryChoosesTheChainFirstChickenRatherThanTheIndexFirstChickenOnATie()
    {
        (GameDatabase db, WorldState world, GameSimulation sim) = World();
        RoomInstance room = Drag(db, world, sim, RoomTypeId.Hatchery, 4, 2, 8, 6);
        ObjectState west = world.SpawnObject(db, HatcheryRules.ChickenObjectId, Keeper, 3 * world.Width + 5);
        ObjectState east = world.SpawnObject(db, HatcheryRules.ChickenObjectId, Keeper, 3 * world.Width + 7);

        Assert.True(west.TileIndex < east.TileIndex);
        Assert.Same(east, EatingRules.NearestChickenIn(world, room, new TilePos(6, 3)));
    }

    [Fact]
    public void ADraggedPrisonChoosesTheChainFirstFoodRatherThanTheIndexFirstFoodOnATie()
    {
        (GameDatabase db, WorldState world, GameSimulation sim) = World();
        RoomInstance room = Drag(db, world, sim, RoomTypeId.Prison, 4, 2, 8, 6);
        ObjectState west = world.SpawnObject(db, HatcheryRules.ChickenObjectId, Keeper, 3 * world.Width + 5);
        ObjectState east = world.SpawnObject(db, HatcheryRules.ChickenObjectId, Keeper, 3 * world.Width + 7);

        Assert.True(west.TileIndex < east.TileIndex);
        Assert.Same(east, EatingRules.NearestFoodIn(world, db, room, new TilePos(6, 3)));
    }

    [Fact]
    public void ADraggedWorkshopListsFreeCrateTilesInChainOrderRatherThanIndexOrder()
    {
        (GameDatabase db, WorldState world, GameSimulation sim) = World();
        RoomInstance room = Drag(db, world, sim, RoomTypeId.Workshop, 4, 3, 6, 3);
        var tiles = new List<int>();
        CrateRules.CrateTiles(world, db, room, tiles);

        Assert.Equal([3 * world.Width + 6, 3 * world.Width + 5, 3 * world.Width + 4], tiles);
    }

    [Fact]
    public void ADraggedLairSearchStartsAlongTheChainRatherThanIndexOrderWhenTheLocalRingIsFull()
    {
        (GameDatabase db, WorldState world, GameSimulation sim) = World();
        RoomInstance room = Drag(db, world, sim, RoomTypeId.Lair, 4, 4, 8, 4);
        foreach (int x in new[] { 5, 6, 7 })
            world.SpawnObject(db, Dk2TerrainFixture.GoblinBedObjectId, Keeper, 4 * world.Width + x);

        int[] chain = SlabChain.Order(world, room);
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var expectedRng = new SimRandom(seed);
            int start = expectedRng.NextInt(chain.Length);
            int expected = -1;
            for (int step = 0; step < chain.Length; step++)
            {
                int tile = chain[(start + step) % chain.Length];
                if (tile % world.Width is 4 or 8) { expected = tile; break; }
            }
            Assert.True(LairRules.TryFindFreeTile(world, db, room, new TilePos(6, 4), Keeper,
                new SimRandom(seed), out int found, out LairRules.TileFound how));
            Assert.Equal(LairRules.TileFound.Walk, how);
            Assert.Equal(expected, found);
        }
    }

    [Fact]
    public void ADraggedWorkshopsWallPhaseStartsAtTheChainFirstEligibleTileRatherThanTheIndexFirst()
    {
        GameDatabase basis = Dk2TerrainFixture.Database();
        TerrainDef wall = basis.TerrainById(Dk2TerrainFixture.ReinforcedWall) with
        {
            Flags = basis.TerrainById(Dk2TerrainFixture.ReinforcedWall).Flags | TerrainFlags.AllowRoomWalls,
        };
        GameDatabase db = new(
            basis.Terrain.Values.Select(t => t.TerrainId == wall.TerrainId ? wall : t),
            basis.Rooms.Values, basis.Creatures.Values, basis.Variables,
            objects: basis.Objects.Values);
        (_, WorldState world, GameSimulation sim) = World(db);
        RoomInstance room = Drag(db, world, sim, RoomTypeId.Workshop, 4, 3, 7, 5);
        for (int x = 4; x <= 7; x++) world.Tile(x, 2).TerrainId = Dk2TerrainFixture.ReinforcedWall;
        var slots = new HashSet<int>();
        AutoFurnitureRules.FurnitureSlots(world, db, room, slots);

        Assert.Contains(3 * world.Width + 6, slots);
        Assert.DoesNotContain(3 * world.Width + 5, slots);
    }

    [Fact]
    public void ADraggedPitRoutePointTakesTheChainFirstRimTileRatherThanTheIndexFirstRimTile()
    {
        (GameDatabase db, WorldState world, GameSimulation sim) = World();
        RoomInstance room = Drag(db, world, sim, RoomTypeId.CombatPit, 4, 3, 6, 5);
        int? point = StealGoldRules.FirstStandable(world, db,
            db.CreatureById(Dk2TerrainFixture.KnightCreatureId), room);

        Assert.Equal(5 * world.Width + 6, point);
    }

    [Fact]
    public void ADraggedRoomImpPointDrawsFromChainOrderRatherThanIndexOrder()
    {
        (GameDatabase db, WorldState world, GameSimulation sim) = World();
        RoomInstance room = Drag(db, world, sim, RoomTypeId.Treasury, 4, 3, 6, 3);
        int[] chain = SlabChain.Order(world, room);
        CreatureDef imp = db.CreatureById(Dk2TerrainFixture.ImpCreatureId);

        for (ulong seed = 1; seed <= 20; seed++)
        {
            int pick = new SimRandom(seed).NextInt(chain.Length);
            Assert.Equal(chain[pick], ImpTaskSystem.RoomPoint(world, db, imp, room, new SimRandom(seed)));
        }
    }

    [Fact]
    public void ADraggedTreasuryDepositWrapsAlongTheChainRatherThanIndexOrder()
    {
        (GameDatabase db, WorldState world, GameSimulation sim) = World();
        RoomInstance room = Drag(db, world, sim, RoomTypeId.Treasury, 4, 3, 6, 3);
        int middle = 3 * world.Width + 5;
        world.TileAtIndex(middle).GoldHeld = GoldStore.Capacity(world, db, middle);

        Assert.Equal(3 * world.Width + 4,
            GoldStore.DepositTileAlongChain(world, db, room, middle));
    }
}
