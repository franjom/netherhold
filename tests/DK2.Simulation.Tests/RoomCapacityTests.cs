// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>
/// Rooms filling up (ADR-0205): a room's places are its furniture, the job finders pass a full
/// room over, and a creature that arrives at a room filled on its way goes back to idle.
/// </summary>
public sealed class RoomCapacityTests
{
    private const byte Keeper = 4;
    private const byte GoblinId = 7;

    private static int Second => SimulationTiming.TicksPerSecond;

    /// <summary>Paved ground at (1..12, 1..8) the keeper owns, and a treasury tile at
    /// (12, 8) to pay for training.</summary>
    private static WorldState World(GameDatabase db)
    {
        WorldState world = WorldState.FromMap(
            db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);
        for (int y = 1; y <= 8; y++)
        for (int x = 1; x <= 12; x++)
        {
            ref TileState tile = ref world.Tile(x, y);
            tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
            tile.OwnerPlayerId = Keeper;
        }

        ref TileState treasury = ref world.Tile(12, 8);
        treasury.TerrainId = Dk2TerrainFixture.TreasuryTerrain;
        treasury.GoldHeld = 100_000;
        return world;
    }

    private static void Room(GameDatabase db, WorldState world, RoomTypeId type, int x0, int y0, int size = 3)
    {
        for (int y = y0; y < y0 + size; y++)
        for (int x = x0; x < x0 + size; x++)
            RoomRules.Place(db, ref world.Tile(x, y), db.Room(type), Keeper);
    }

    private static GameSimulation Simulation(GameDatabase db, WorldState world, ListEventSink sink)
    {
        var sim = new GameSimulation(db, world, seed: 42,
            [new CreatureMovementSystem(), new CreatureJobSystem()]);
        sim.ObserveWith(sink);
        return sim;
    }

    private static RoomInstance RoomAt(GameDatabase db, WorldState world, int x, int y) =>
        RoomIndex.RoomAt(world, db, (y * world.Width) + x)!;

    private static bool Training(CreatureState c) =>
        c.CurrentJob == JobType.Train && c.Activity == CreatureActivity.AtWork;

    [Fact]
    public void ATrainingRoomTooSmallForADummyTrainsNobody()
    {
        // A 2x2 room has no tile surrounded by room, so no Training Dummy, so no places.
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = World(db);
        Room(db, world, RoomTypeId.TrainingRoom, 5, 1, size: 2);
        Dk2TerrainFixture.FurnishRooms(db, world);
        Assert.Empty(world.Objects);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(1, 7));
        var sink = new ListEventSink();
        Simulation(db, world, sink).Run(Second * 10);

        Assert.Equal(JobType.None, goblin.CurrentJob);
        Assert.Contains(sink.Events, e => e.Kind == "job_refused"
            && e.Detail.Contains("job=Train") && e.Detail.Contains("reason=no_room"));
    }

    [Fact]
    public void OneDummyIsTwoPlacesAndTheThirdGoblinIsTurnedAway()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = World(db);
        Room(db, world, RoomTypeId.TrainingRoom, 5, 1);
        Dk2TerrainFixture.FurnishRooms(db, world);
        Assert.True(AutoFurnitureRules.TryTotalCapacity(world, RoomAt(db, world, 6, 2), out int capacity));
        Assert.Equal(2, capacity);

        CreatureState[] goblins =
        [
            world.SpawnCreature(db, GoblinId, Keeper, new TilePos(1, 5)),
            world.SpawnCreature(db, GoblinId, Keeper, new TilePos(1, 6)),
            world.SpawnCreature(db, GoblinId, Keeper, new TilePos(1, 7)),
        ];
        var sink = new ListEventSink();
        Simulation(db, world, sink).Run(Second * 30);

        Assert.Equal(2, goblins.Count(Training));
        CreatureState away = goblins.Single(g => !Training(g));
        Assert.Contains(sink.Events, e => e.Kind == "job_refused"
            && e.Detail.Contains($"creature={away.Id} ") && e.Detail.Contains("reason=no_room"));
        Assert.Equal(2, RoomCapacityRules.Occupants(world, RoomAt(db, world, 6, 2)));
    }

    [Fact]
    public void TheGoblinTurnedAwayGoesToTheKeepersOtherTrainingRoom()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = World(db);
        Room(db, world, RoomTypeId.TrainingRoom, 5, 1);
        Room(db, world, RoomTypeId.TrainingRoom, 9, 5);
        Dk2TerrainFixture.FurnishRooms(db, world);

        CreatureState[] goblins =
        [
            world.SpawnCreature(db, GoblinId, Keeper, new TilePos(1, 5)),
            world.SpawnCreature(db, GoblinId, Keeper, new TilePos(1, 6)),
            world.SpawnCreature(db, GoblinId, Keeper, new TilePos(1, 7)),
        ];
        var sink = new ListEventSink();
        Simulation(db, world, sink).Run(Second * 40);

        Assert.All(goblins, g => Assert.True(Training(g)));
        Assert.Equal(2, RoomCapacityRules.Occupants(world, RoomAt(db, world, 6, 2)));
        Assert.Equal(1, RoomCapacityRules.Occupants(world, RoomAt(db, world, 10, 6)));
    }

    [Theory]
    [InlineData(false, "message=0x2A")]
    [InlineData(true, "message=none")]
    public void AGoblinArrivingAtARoomFilledOnItsWayGoesIdle(bool anotherRoom, string message)
    {
        // The claim 0x004E5A70 fails; 0x004C6AE0 looks for another room of the type with a
        // place, and message 0x2A is raised only when there is none. State 1 either way.
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = World(db);
        Room(db, world, RoomTypeId.TrainingRoom, 5, 1);
        if (anotherRoom) Room(db, world, RoomTypeId.TrainingRoom, 9, 5);
        Dk2TerrainFixture.FurnishRooms(db, world);

        CreatureState first = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(6, 2));
        CreatureState walker = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(1, 7));
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);
        for (int i = 0; i < Second * 10 && walker.Activity != CreatureActivity.GoingToJob; i++) sim.Tick();
        Assert.True(Training(first));
        Assert.Equal(CreatureActivity.GoingToJob, walker.Activity);

        // Its last place goes while it walks: the dummy is taken away, which leaves none. (A
        // third goblin walking in would fill it too, but three bodies on the one arrival tile
        // never settle, our walks all ending on the room's centre.)
        world.RemoveObject(world.Objects.Single(o => o.ObjectTypeId == Dk2TerrainFixture.TrainingDummyObjectId
            && EatingRules.Contains(RoomAt(db, world, 6, 2), o.TileIndex)).Id);

        for (int i = 0; i < Second * 20 && walker.Activity == CreatureActivity.GoingToJob; i++) sim.Tick();

        Assert.Contains(sink.Events, e => e.Kind == "job_lost" && e.Detail.Contains($"creature={walker.Id} ")
            && e.Detail.Contains("reason=room_full") && e.Detail.Contains(message));
        Assert.Equal(CreatureActivity.Idle, walker.Activity);
        Assert.Equal(CreatureStateMachine.Idle, walker.Dk2StateId);
        Assert.True(Training(first));
    }

    [Fact]
    public void ACreatureInTheRoomTakesAPlaceOnlyWhileItsStateClaimsOne()
    {
        // A goblin merely standing in the room holds no state and takes no place.
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = World(db);
        Room(db, world, RoomTypeId.TrainingRoom, 5, 1);
        Dk2TerrainFixture.FurnishRooms(db, world);
        RoomInstance room = RoomAt(db, world, 6, 2);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(6, 2));
        Assert.Equal(0, RoomCapacityRules.Occupants(world, room));

        var sink = new ListEventSink();
        Simulation(db, world, sink).Run(Second * 10);
        Assert.True(Training(goblin));
        Assert.Equal(1, RoomCapacityRules.Occupants(world, room));
        Assert.Equal(0, RoomCapacityRules.Occupants(world, room, exceptId: goblin.Id));
    }

    // ---- held places: claimed by the enter hook, released by the leave hook (ADR-0309)

    private static int TrainState => JobRules.StateFor(JobType.Train)!.Value;

    private static StateHookContext Hooks(GameDatabase db, WorldState world) => new(world, db);

    [Fact]
    public void APlaceIsHeldWhereverTheClaimantStandsUntilItLeavesTheCategory()
    {
        // The derived count dropped a trainee the moment it stepped off the room; DK2's +0x6B
        // moves only at the claim and the release.
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = World(db);
        Room(db, world, RoomTypeId.TrainingRoom, 5, 1);
        Dk2TerrainFixture.FurnishRooms(db, world);
        RoomInstance room = RoomAt(db, world, 6, 2);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(6, 2));

        Assert.True(CreatureStateMachine.TrySetState(Hooks(db, world), goblin, TrainState));
        goblin.PosX = CreatureState.CentreOf(1);
        goblin.PosY = CreatureState.CentreOf(7);
        Assert.Equal(1, RoomCapacityRules.Occupants(world, room));

        Assert.True(CreatureStateMachine.TrySetState(Hooks(db, world), goblin, CreatureStateMachine.Idle));
        Assert.Equal(RoomPlace.None, goblin.HeldPlace);
        Assert.Equal(0, RoomCapacityRules.Occupants(world, room));
    }

    [Fact]
    public void AClaimingStateWrittenWithoutTheEnterHookHoldsNoPlace()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = World(db);
        Room(db, world, RoomTypeId.TrainingRoom, 5, 1);
        Dk2TerrainFixture.FurnishRooms(db, world);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(6, 2));

        goblin.Dk2StateId = TrainState;
        Assert.Equal(0, RoomCapacityRules.Occupants(world, RoomAt(db, world, 6, 2)));
    }

    [Fact]
    public void AFullRoomRefusesTheEnterAndLeavesTheCreatureInStateOne()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = World(db);
        Room(db, world, RoomTypeId.TrainingRoom, 5, 1);
        Dk2TerrainFixture.FurnishRooms(db, world);
        CreatureState[] goblins = [.. Enumerable.Range(0, 3).Select(_ => world.SpawnCreature(db, GoblinId, Keeper, new TilePos(6, 2)))];

        Assert.True(CreatureStateMachine.TrySetState(Hooks(db, world), goblins[0], TrainState));
        Assert.True(CreatureStateMachine.TrySetState(Hooks(db, world), goblins[1], TrainState));
        Assert.False(CreatureStateMachine.TrySetState(Hooks(db, world), goblins[2], TrainState));

        Assert.Equal(CreatureStateMachine.Idle, goblins[2].Dk2StateId);
        Assert.False(goblins[2].HeldPlace.IsHeld);
        Assert.Equal(2, RoomCapacityRules.Occupants(world, RoomAt(db, world, 6, 2)));
    }

    [Theory]
    [InlineData(RoomTypeId.TrainingRoom, false)]
    [InlineData(RoomTypeId.Temple, true)]
    public void AnotherKeepersRoomIsAdmittedOnlyForTheTypes0x004E7F80Names(RoomTypeId type, bool admitted)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = World(db);
        Room(db, world, type, 5, 1, size: 5);
        Dk2TerrainFixture.FurnishRooms(db, world);
        CreatureState stranger = world.SpawnCreature(db, GoblinId, 0, new TilePos(6, 2));
        int state = type == RoomTypeId.Temple ? JobRules.StateFor(JobType.Praying)!.Value : TrainState;

        Assert.Equal(admitted, CreatureStateMachine.TrySetState(Hooks(db, world), stranger, state));
        Assert.Equal(admitted, stranger.HeldPlace.IsHeld);
    }

    [Fact]
    public void NoRoomUnderfootRefusesTheClaim()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = World(db);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(1, 7));

        Assert.False(CreatureStateMachine.TrySetState(Hooks(db, world), goblin, TrainState));
        Assert.Equal(CreatureStateMachine.Idle, goblin.Dk2StateId);
    }

    [Fact]
    public void ACorpseHoldsNoPlace()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = World(db);
        Room(db, world, RoomTypeId.TrainingRoom, 5, 1);
        Dk2TerrainFixture.FurnishRooms(db, world);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(6, 2));
        Assert.True(CreatureStateMachine.TrySetState(Hooks(db, world), goblin, TrainState));

        CreatureStateMachine.Release(goblin);
        Assert.Equal(0, RoomCapacityRules.Occupants(world, RoomAt(db, world, 6, 2)));
    }

    [Fact]
    public void APlaceSurvivesASnapshot()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = World(db);
        Room(db, world, RoomTypeId.TrainingRoom, 5, 1);
        Dk2TerrainFixture.FurnishRooms(db, world);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(6, 2));
        Assert.True(CreatureStateMachine.TrySetState(Hooks(db, world), goblin, TrainState));

        var simulation = new GameSimulation(db, world, 1, []);
        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(simulation), db, []);
        Assert.Equal(goblin.HeldPlace, restored.World.CreatureById(goblin.Id)!.HeldPlace);
        Assert.Equal(simulation.StateHash(), restored.StateHash());
    }
}
