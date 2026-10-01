// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>State 89's fallback <c>0x00478680</c> and state 3 (ADR-0228, <c>idle-fallback.md</c>).</summary>
public sealed class IdleFallbackTests
{
    private const byte Keeper = 4;
    private const byte Rival = 5;
    private const byte Idler = 54;
    private const byte NoTrainer = 55;
    private const ushort RoomKind = 1;
    private const int Available = 3;
    private const int Engaging = 199;   // the fight state, category 22

    /// <summary>A Goblin whose whole happy table is Research, and no Library anywhere: its chooser
    /// always fails.</summary>
    private static GameDatabase Db()
    {
        CreatureDef idler = Dk2TerrainFixture.Goblin() with { CreatureId = Idler, Name = "Idle Goblin", HappyJobs = [new(JobType.Research, 0, 0, 100)] };
        return Dk2TerrainFixture.Database(idler,
            idler with { CreatureId = NoTrainer, Name = "Lazy Goblin", Flags = idler.Flags | CreatureFlags.NotTrainWhenIdle });
    }

    private static WorldState World(GameDatabase db)
    {
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper, Rival]);
        for (int y = 1; y <= 8; y++)
        for (int x = 1; x <= 12; x++)
        {
            ref TileState tile = ref world.Tile(x, y);
            tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
            tile.OwnerPlayerId = Keeper;
        }
        return world;
    }

    private static void Allow(WorldState world, RoomTypeId type) =>
        world.Player(Keeper).Availabilities.Add(new AvailabilityState(RoomKind, (int)type, Available));

    private static (GameSimulation, ListEventSink) Simulation(GameDatabase db, WorldState world)
    {
        var sim = new GameSimulation(db, world, seed: 42,
            [new CombatPitSystem(), new CreatureMovementSystem(), new CreatureJobSystem()]);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        return (sim, sink);
    }

    private static void TrainingRoom(GameDatabase db, WorldState world)
    {
        for (int y = 5; y <= 7; y++)
        for (int x = 9; x <= 11; x++)
            RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.TrainingRoom), Keeper);
        Dk2TerrainFixture.Furnish(db, world, RoomTypeId.TrainingRoom, new TilePos(10, 6), Keeper);
        ref TileState gold = ref world.Tile(2, 8);
        gold.GoldHeld = 10_000;
    }

    /// <summary>A 5x5 pit at (4..8, 2..6) with two creatures fighting on its floor.</summary>
    private static void PitWithAFight(GameDatabase db, WorldState world)
    {
        for (int y = 2; y <= 6; y++)
        for (int x = 4; x <= 8; x++)
            RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.CombatPit), Keeper);
        foreach ((TilePos at, byte owner) in new[] { (new TilePos(6, 4), Keeper), (new TilePos(6, 5), Rival) })
            Assert.True(CreatureStateMachine.TrySetState(new StateHookContext(world, db), world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, owner, at), Engaging));
    }

    [Fact]
    public void WithNothingToDoItWaitsInThreeForTwelveTurns_WanderingIn87OrPlayingAnIdleAction()
    {
        GameDatabase db = Db();
        WorldState world = World(db);
        (GameSimulation sim, ListEventSink sink) = Simulation(db, world);
        CreatureState goblin = world.SpawnCreature(db, Idler, Keeper, new TilePos(3, 3));

        var seen = new HashSet<int>();
        bool deadlineChecked = false;
        for (int i = 0; i < 400; i++)
        {
            long tick = sim.World.CurrentTick.Value;
            int before = goblin.Dk2StateId;
            sim.Tick();
            if (before != CreatureJobSystem.DoingNothingState && goblin.Dk2StateId == CreatureJobSystem.DoingNothingState)
            {
                Assert.Equal(tick + CreatureJobSystem.NothingToDoTicks, goblin.StateDeadlineTick);
                deadlineChecked = true;
            }
            seen.Add(goblin.Dk2StateId is 6 ? 1000 + goblin.UtilityNewState : goblin.Dk2StateId);
            Assert.Equal(CreatureActivity.Idle, goblin.Activity);
        }

        Assert.True(deadlineChecked);
        Assert.Superset(new HashSet<int> { CreatureStateMachine.Idle, CreatureJobSystem.DoingNothingState, 87, 1001 }, seen);
        Assert.Contains(sink.Events, e => e.Kind == "idle_action");
        Assert.DoesNotContain(sink.Events, e => e.Kind == "job_taken");
    }

    [Fact]
    public void TheFallbackTrains_WhenTheLevelMakesTheTrainingRoomAvailable()
    {
        GameDatabase db = Db();
        WorldState world = World(db);
        TrainingRoom(db, world);
        Allow(world, RoomTypeId.TrainingRoom);
        (GameSimulation sim, ListEventSink sink) = Simulation(db, world);
        CreatureState goblin = world.SpawnCreature(db, Idler, Keeper, new TilePos(3, 3));

        sim.Run(8);

        Assert.Contains(sink.Events, e => e.Kind == "job_taken" && e.Detail.Contains("job=Train") && e.Detail.Contains("how=idle"));
        Assert.Equal(JobType.Train, goblin.CurrentJob);
    }

    /// <summary>Without the availability row (<c>0x004BAB20</c> answers 1) the fallback does not
    /// try, even with a Training Room built; and <c>NotTrainWhenIdle</c> skips Train outright.</summary>
    [Theory]
    [InlineData(false, Idler)]
    [InlineData(true, NoTrainer)]
    public void TheFallbackDoesNotTrain_WithoutTheRoomAvailable_OrForACreatureThatNeverTrainsWhenIdle(bool available, byte type)
    {
        GameDatabase db = Db();
        WorldState world = World(db);
        TrainingRoom(db, world);
        if (available) Allow(world, RoomTypeId.TrainingRoom);
        (GameSimulation sim, ListEventSink sink) = Simulation(db, world);
        CreatureState goblin = world.SpawnCreature(db, type, Keeper, new TilePos(3, 3));

        sim.Run(80);

        Assert.DoesNotContain(sink.Events, e => e.Kind == "job_taken");
        Assert.Equal(JobType.None, goblin.CurrentJob);
    }

    [Fact]
    public void TheFallbackWatchesAFightInThePit_WhenTheCombatPitIsAvailable()
    {
        GameDatabase db = Db();
        WorldState world = World(db);
        PitWithAFight(db, world);
        Allow(world, RoomTypeId.CombatPit);
        (GameSimulation sim, ListEventSink sink) = Simulation(db, world);
        CreatureState goblin = world.SpawnCreature(db, Idler, Keeper, new TilePos(10, 4));

        sim.Run(8);

        Assert.Contains(sink.Events, e => e.Kind == "job_taken" && e.Detail.Contains("job=CombatPitSpectate") && e.Detail.Contains("how=idle"));
        Assert.Equal(JobType.CombatPitSpectate, goblin.CurrentJob);
    }

    /// <summary>Inside its five-second gate the happy chooser returns 0 untried, and 89 goes on to
    /// the fallback: a Goblin that loses its training within the gate is watching the fight well
    /// before the gate would let it choose again.</summary>
    [Fact]
    public void InsideTheChoosersGateTheFallbackStillRuns()
    {
        GameDatabase db = Db();
        WorldState world = World(db);
        PitWithAFight(db, world);
        TrainingRoom(db, world);
        Allow(world, RoomTypeId.CombatPit);
        (GameSimulation sim, ListEventSink sink) = Simulation(db, world);
        CreatureState goblin = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(10, 3));

        for (int i = 0; i < 200 && goblin.Dk2StateId != 108; i++) sim.Tick();
        Assert.Equal(108, goblin.Dk2StateId);   // training, in the room
        long chosen = goblin.LastHappyChoiceTick = sim.World.CurrentTick.Value;   // a choice just made: the gate is shut
        for (int y = 5; y <= 7; y++)
        for (int x = 9; x <= 11; x++)
            world.Tile(x, y).TerrainId = Dk2TerrainFixture.ClaimedPath;

        for (int i = 0; i < 40 && goblin.CurrentJob != JobType.CombatPitSpectate; i++) sim.Tick();

        Assert.Equal(JobType.CombatPitSpectate, goblin.CurrentJob);
        Assert.True(sim.World.CurrentTick.Value - chosen < JobRules.ReselectionGateTicks, "the gate had already run out");
    }

    [Fact]
    public void TheFallbackTriesTrainBeforeTheFight()
    {
        GameDatabase db = Db();
        WorldState world = World(db);
        PitWithAFight(db, world);
        TrainingRoom(db, world);
        Allow(world, RoomTypeId.CombatPit);
        Allow(world, RoomTypeId.TrainingRoom);
        (GameSimulation sim, ListEventSink sink) = Simulation(db, world);
        CreatureState goblin = world.SpawnCreature(db, Idler, Keeper, new TilePos(10, 3));

        sim.Run(8);

        Assert.Equal(JobType.Train, goblin.CurrentJob);
        Assert.DoesNotContain(sink.Events, e => e.Kind == "job_taken" && e.Detail.Contains("job=CombatPitSpectate"));
    }

    [Fact]
    public void TheFallbackGambles_WhenTheCasinoIsAvailableAndThePurseAllows()
    {
        GameDatabase db = Db();
        WorldState world = World(db);
        for (int y = 5; y <= 7; y++)
        for (int x = 9; x <= 11; x++)
            RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Casino), Keeper);
        Dk2TerrainFixture.Furnish(db, world, RoomTypeId.Casino, new TilePos(10, 6), Keeper);
        Allow(world, RoomTypeId.Casino);
        (GameSimulation sim, ListEventSink sink) = Simulation(db, world);
        CreatureState goblin = world.SpawnCreature(db, Idler, Keeper, new TilePos(3, 3));
        goblin.GoldHeld = 100;

        sim.Run(8);

        Assert.Contains(sink.Events, e => e.Kind == "job_taken" && e.Detail.Contains("job=Drink") && e.Detail.Contains("how=idle"));
    }
}
