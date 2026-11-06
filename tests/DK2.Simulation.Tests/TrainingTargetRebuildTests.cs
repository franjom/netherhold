// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public sealed class TrainingTargetRebuildTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Target = 29;

    private sealed class Draws : IRandomAlgorithm
    {
        public List<int> Bounds { get; } = [];
        public int NextInt(int exclusiveMax) { Bounds.Add(exclusiveMax); return exclusiveMax - 1; }
        public void HashInto(ref Hash64 hash) { }
    }

    private static GameDatabase Database(bool specialWalls = true)
    {
        GameDatabase basis = Dk2TerrainFixture.Database();
        TerrainDef wall = basis.TerrainById(Dk2TerrainFixture.ReinforcedWall);
        RoomDef training = basis.Room(RoomTypeId.TrainingRoom);
        if (!specialWalls) training = training with { Flags = training.Flags & ~RoomFlags.SpecialWalls };
        return new GameDatabase(basis.Terrain.Values.Select(t => t.TerrainId == wall.TerrainId ? t with { Flags = t.Flags | TerrainFlags.AllowRoomWalls } : t),
            basis.Rooms.Values.Select(r => r.RoomId == training.RoomId ? training : r), basis.Creatures.Values,
            basis.Variables, objects: basis.Objects.Values);
    }

    private static void Paint(GameDatabase db, WorldState world, RoomTypeId type, int left, int top)
    {
        for (int y = top; y < top + 3; y++)
        for (int x = left; x < left + 3; x++)
            RoomRules.Place(db, ref world.Tile(x, y), db.Room(type), Keeper);
    }

    private static (GameDatabase Db, WorldState World, Draws Draws, GameSimulation Sim) Setup(bool specialWalls = true, bool targetWall = true)
    {
        GameDatabase db = Database(specialWalls);
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [0, PlayerIds.Neutral, Keeper]);
        Paint(db, world, RoomTypeId.TrainingRoom, 4, 4);
        for (int y = 4; y <= 6; y++)
            world.Tile(7, y).TerrainId = targetWall ? Dk2TerrainFixture.ReinforcedWall : Dk2TerrainFixture.Rock;
        Dk2TerrainFixture.FurnishRooms(db, world);
        var draws = new Draws();
        var sim = new GameSimulation(db, world, new SimRandom(draws), [new TileWorkSystem(), new AutoFurnitureSystem()]);
        draws.Bounds.Clear();
        return (db, world, draws, sim);
    }

    private static void Reinforce(WorldState world, TilePos at, int multiplier)
    {
        world.Tile(at).TerrainId = Dk2TerrainFixture.Rock;
        world.Tile(at).OwnerPlayerId = PlayerIds.Neutral;
        world.QueueWork(new TileWorkOrder(world.TileIndex(at), TileWorkKind.Reinforce, Keeper, Multiplier: multiplier));
    }

    [Fact]
    public void FinishingAnAdjacentWallReplacesDummiesAndTargetAndDrawsOnce()
    {
        var (_, world, draws, sim) = Setup();
        ObjectState oldTarget = Assert.Single(world.Objects, o => o.ObjectTypeId == Target);
        ObjectState oldDummy = Assert.Single(world.Objects, o => o.ObjectTypeId == Dk2TerrainFixture.TrainingDummyObjectId);
        Reinforce(world, new TilePos(3, 5), 1000);
        sim.Tick();
        Assert.Equal(Dk2TerrainFixture.ReinforcedWall, world.Tile(3, 5).TerrainId);
        Assert.DoesNotContain(oldTarget, world.Objects);
        Assert.DoesNotContain(oldDummy, world.Objects);
        Assert.Single(world.Objects, o => o.ObjectTypeId == Target);
        Assert.Single(world.Objects, o => o.ObjectTypeId == Dk2TerrainFixture.TrainingDummyObjectId);
        Assert.Equal(new[] { 4096 }, draws.Bounds);
        int[] ids = world.Objects.Select(o => o.Id).ToArray();
        draws.Bounds.Clear();
        sim.Tick();
        Assert.Equal(ids, world.Objects.Select(o => o.Id));
        Assert.Empty(draws.Bounds);
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 1000)]
    public void PartialWorkOrRoomWithoutSpecialWallsDoesNotRebuild(bool specialWalls, int multiplier)
    {
        var (_, world, draws, sim) = Setup(specialWalls);
        int[] ids = world.Objects.Select(o => o.Id).ToArray();
        Reinforce(world, new TilePos(3, 5), multiplier);
        sim.Tick();
        Assert.Equal(ids, world.Objects.Select(o => o.Id));
        Assert.Empty(draws.Bounds);
    }

    [Fact]
    public void ARefreshVisitsHatcheryNorthBeforeTrainingEast()
    {
        var (db, world, draws, _) = Setup();
        Paint(db, world, RoomTypeId.Hatchery, 2, 1);
        var sim = new GameSimulation(db, world, new SimRandom(draws), [new TileWorkSystem(), new AutoFurnitureSystem()]);
        draws.Bounds.Clear();
        Reinforce(world, new TilePos(3, 4), 1000);
        sim.Tick();
        Assert.Equal(new[] { 4, 4096 }, draws.Bounds);
    }

    [Fact]
    public void ARebuiltRoomWithNoWallTargetReplacesItsDummyWithoutDrawing()
    {
        var (_, world, draws, sim) = Setup(targetWall: false);
        Assert.DoesNotContain(world.Objects, o => o.ObjectTypeId == Target);
        ObjectState dummy = Assert.Single(world.Objects, o => o.ObjectTypeId == Dk2TerrainFixture.TrainingDummyObjectId);
        Reinforce(world, new TilePos(3, 5), 1000);
        sim.Tick();
        Assert.DoesNotContain(dummy, world.Objects);
        Assert.Single(world.Objects, o => o.ObjectTypeId == Dk2TerrainFixture.TrainingDummyObjectId);
        Assert.Empty(draws.Bounds);
    }

    [Fact]
    public void FourNeighborsInTheSameRoomCauseFourRebuilds()
    {
        var (_, world, draws, sim) = Setup();
        Reinforce(world, new TilePos(5, 5), 1000);
        sim.Tick();
        Assert.Equal(new[] { 4096, 4096, 4096, 4096 }, draws.Bounds);
        Assert.Single(world.Objects, o => o.ObjectTypeId == Target);
    }
}
