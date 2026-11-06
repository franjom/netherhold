// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Simulation.Tests;

public sealed class LibraryCapacityTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private static readonly GameDatabase Db = Dk2TerrainFixture.Database();

    private sealed class Draws : IRandomAlgorithm
    {
        public List<int> Bounds { get; } = [];
        public int NextInt(int exclusiveMax) { Bounds.Add(exclusiveMax); return exclusiveMax - 1; }
        public void HashInto(ref Hash64 hash) { }
    }

    private sealed class Probe(Draws draws, Action<SimulationContext> run) : ISimulationSystem
    {
        public string Name => "Probe";
        public void Execute(SimulationContext context) { draws.Bounds.Clear(); run(context); }
    }

    private static (WorldState World, RoomInstance Room, CreatureState Seeker) Setup(RoomTypeId type)
    {
        WorldState world = WorldState.FromMap(Db, Dk2TerrainFixture.Map(), [Keeper, PlayerIds.Neutral]);
        for (int y = 1; y <= 3; y++)
        for (int x = 1; x <= 3; x++)
            RoomRules.Place(Db, ref world.Tile(x, y), Db.Room(type), Keeper);
        Dk2TerrainFixture.FurnishRooms(Db, world);
        RoomInstance room = RoomIndex.RoomAt(world, Db, world.TileIndex(new TilePos(2, 2)))!;
        CreatureState seeker = world.SpawnCreature(Db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(2, 2));
        return (world, room, seeker);
    }

    private static void FillShelves(WorldState world, RoomInstance room)
    {
        foreach (int tile in AutoFurnitureRules.FloorSlotSet(world, room))
        for (int i = 0; i < ResearchTreeRules.ShelfCapacity; i++)
            world.SpawnObject(Db, ResearchTreeRules.BookObjectTypeId, Keeper, tile);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RoomSearchChecksTheShelfBeforeRejectingNumericFullness(bool full)
    {
        var (world, room, seeker) = Setup(RoomTypeId.Library);
        Assert.True(AutoFurnitureRules.TryTotalCapacity(world, room, out int capacity));
        Assert.True(capacity > 0);
        if (full)
        {
            CreatureState holder = world.SpawnCreature(Db, seeker.CreatureTypeId, Keeper, seeker.Tile);
            holder.HeldPlace = new RoomPlace(world.TileIndex(seeker.Tile), capacity);
        }
        var draws = new Draws();
        new GameSimulation(Db, world, new SimRandom(draws), [new Probe(draws, context =>
        {
            Assert.Equal(!full, RoomCapacityRules.FindRoomWithPlace(context, RoomTypeId.Library, seeker) is not null);
        })]).Tick();
        Assert.Equal(new[] { 9 }, draws.Bounds);
    }

    [Fact]
    public void NoShelfRejectsTheSearchDespiteUnusedNumericPlaces()
    {
        var (world, room, seeker) = Setup(RoomTypeId.Library);
        FillShelves(world, room);
        Assert.True(RoomCapacityRules.HasRoom(world, Db, room));
        var draws = new Draws();
        new GameSimulation(Db, world, new SimRandom(draws), [new Probe(draws, context =>
            Assert.Null(RoomCapacityRules.FindRoomWithPlace(context, RoomTypeId.Library, seeker)))]).Tick();
        Assert.Equal(new[] { 9 }, draws.Bounds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EnterHookChecksShelfAndFailureSearchesCapacityAgain(bool shelvesFull)
    {
        var (world, room, seeker) = Setup(RoomTypeId.Library);
        if (shelvesFull) FillShelves(world, room);
        var draws = new Draws();
        new GameSimulation(Db, world, new SimRandom(draws), [new Probe(draws, context =>
            Assert.Equal(!shelvesFull, CreatureStateMachine.TrySetState(context.Hooks, seeker, JobRules.StateFor(JobType.Research)!.Value)))]).Tick();
        Assert.Equal(shelvesFull ? new[] { 9, 9 } : new[] { 9 }, draws.Bounds);
        Assert.Equal(shelvesFull ? 0 : RoomCapacityRules.Units, RoomCapacityRules.Occupants(world, room));
    }

    [Fact]
    public void NonLibraryCapacityConsumesNoShelfDraw()
    {
        var (world, room, seeker) = Setup(RoomTypeId.TrainingRoom);
        var draws = new Draws();
        new GameSimulation(Db, world, new SimRandom(draws), [new Probe(draws, context =>
            Assert.NotNull(RoomCapacityRules.FindRoomWithPlace(context, RoomTypeId.TrainingRoom, seeker)))]).Tick();
        Assert.Empty(draws.Bounds);
        Assert.Equal(0, RoomCapacityRules.Occupants(world, room));
    }

    [Fact]
    public void RefusedClaimChecksEveryLibraryEvenAfterFindingAReachableOne()
    {
        var (world, room, seeker) = Setup(RoomTypeId.Library);
        FillShelves(world, room);
        for (int y = 1; y <= 3; y++)
        for (int x = 4; x <= 11; x++)
        {
            ref TileState tile = ref world.Tile(x, y);
            tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
            tile.OwnerPlayerId = Keeper;
        }
        foreach (int left in new[] { 5, 9 })
        for (int y = 1; y <= 3; y++)
        for (int x = left; x < left + 3; x++)
            RoomRules.Place(Db, ref world.Tile(x, y), Db.Room(RoomTypeId.Library), Keeper);
        Dk2TerrainFixture.FurnishRooms(Db, world);
        var draws = new Draws();
        new GameSimulation(Db, world, new SimRandom(draws), [new Probe(draws, context =>
            Assert.False(CreatureStateMachine.TrySetState(context.Hooks, seeker, JobRules.StateFor(JobType.Research)!.Value)))]).Tick();
        Assert.Equal(new[] { 9, 9, 9, 9 }, draws.Bounds);
    }

    [Fact]
    public void DetachedEnterStillRejectsFullShelves()
    {
        var (world, room, seeker) = Setup(RoomTypeId.Library);
        FillShelves(world, room);
        Assert.False(CreatureStateMachine.TrySetState(new StateHookContext(world, Db), seeker, JobRules.StateFor(JobType.Research)!.Value));
        Assert.Equal(0, RoomCapacityRules.Occupants(world, room));
    }
}
