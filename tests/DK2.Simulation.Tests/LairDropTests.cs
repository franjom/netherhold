// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public sealed class LairDropTests
{
    private static readonly TilePos OldTile = new(1, 1);
    private static readonly TilePos NewTile = new(3, 3);

    [Fact]
    public void DefaultSimulationRelocatesDroppedCreaturesBed()
    {
        var (sim, creature, oldBed) = Start(defaultPipeline: true);
        Drop(sim, creature, NewTile);
        sim.Run(160);
        Assert.Equal(sim.World.TileIndex(NewTile), LairRules.BedTileIndex(sim.World, sim.Database, creature));
        Assert.DoesNotContain(sim.World.Objects, o => o.Id == oldBed.Id);
    }

    [Fact]
    public void CreatureWithoutABedDefinitionDoesNotGetStuckSelectingABed()
    {
        var database = Dk2TerrainFixture.Database();
        var world = WorldState.FromMap(database, Dk2TerrainFixture.Map(),
            [PlayerIds.Unowned, PlayerIds.Neutral, PlayerIds.FirstKeeper]);
        RoomRules.Place(database, ref world.Tile(NewTile), database.Room(RoomTypeId.Lair), PlayerIds.FirstKeeper);
        CreatureState skeleton = world.SpawnCreature(database,
            Dk2TerrainFixture.SkeletonCreatureId, PlayerIds.FirstKeeper, OldTile);
        var simulation = new GameSimulation(database, world, 42,
            [new HandSystem(), new CreatureStatusSystem(), new CreatureMovementSystem(), new CreatureSleepSystem()]);
        Drop(simulation, skeleton, NewTile);
        simulation.Run(160);
        Assert.Equal(CreatureActivity.Idle, skeleton.Activity);
        Assert.Null(LairRules.BedOf(world, database, skeleton));
    }

    [Fact]
    public void NonSleepingBedUserCanBeDirectedToAnotherLair()
    {
        // Isolates the zero-sleep field used by the original Dwarf and Stone Knight.
        CreatureDef definition = Dk2TerrainFixture.Goblin() with
            { CreatureId = 60, Name = "Zero Sleep Bed User", TimeSleep = 0 };
        var (sim, creature, oldBed) = Start(definition);
        Drop(sim, creature, NewTile);
        sim.Run(160);
        Assert.Equal(sim.World.TileIndex(NewTile), LairRules.BedTileIndex(sim.World, sim.Database, creature));
        Assert.DoesNotContain(sim.World.Objects, o => o.Id == oldBed.Id);
        Assert.NotEqual(CreatureActivity.SeekingLair, creature.Activity);
    }

    [Fact]
    public void DropIntoAnotherLairMovesBedAndMakesOldTileAvailableToAnotherCreature()
    {
        var (sim, creature, oldBed) = Start();
        Drop(sim, creature, NewTile);
        sim.Run(160);

        ObjectState bed = Assert.IsType<ObjectState>(LairRules.BedOf(sim.World, sim.Database, creature));
        Assert.NotEqual(oldBed.Id, bed.Id);
        Assert.Equal(sim.World.TileIndex(NewTile), bed.TileIndex);
        Assert.DoesNotContain(sim.World.Objects, o => o.Id == oldBed.Id);
        Assert.True(LairRules.IsFreeLairTile(sim.World, sim.Database,
            sim.World.TileIndex(OldTile), PlayerIds.FirstKeeper));
        CreatureState next = sim.World.SpawnCreature(sim.Database,
            Dk2TerrainFixture.GoblinCreatureId, PlayerIds.FirstKeeper, OldTile);
        sim.Run(20);
        Assert.Equal(sim.World.TileIndex(OldTile), LairRules.BedTileIndex(sim.World, sim.Database, next));
    }

    [Fact]
    public void SuccessfulSelectionFreesOldBedBeforeWalkingAndSurvivesSaveRestore()
    {
        var (sim, creature, oldBed) = Start();
        Drop(sim, creature, NewTile);
        for (int i = 0; i < 120 && LairRules.BedOf(sim.World, sim.Database, creature) is not null; i++) sim.Tick();
        Assert.Null(LairRules.BedOf(sim.World, sim.Database, creature));
        Assert.DoesNotContain(sim.World.Objects, o => o.Id == oldBed.Id);
        Assert.True(creature.HasRoute);
        Assert.Equal(LairRules.MakingAHomeState, creature.UtilityNewState);
        Assert.Equal(0xD, creature.Body.PathFlags);
        long x = (creature.Body.PathTargetPos.X >> 4) - CreatureState.CentreOf(NewTile.X);
        long y = (creature.Body.PathTargetPos.Y >> 4) - CreatureState.CentreOf(NewTile.Y);
        Assert.InRange(x * x + y * y, 1363L * 1363, 1367L * 1367);
        var restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), sim.Database,
            [new HandSystem(), new CreatureStatusSystem(), new CreatureMovementSystem(), new CreatureSleepSystem()]);
        for (int i = 0; i < 80; i++)
        {
            sim.Tick();
            restored.Tick();
            Assert.Equal(sim.StateHash(), restored.StateHash());
        }
        Assert.Equal(sim.World.TileIndex(NewTile), LairRules.BedTileIndex(sim.World, sim.Database, creature));
    }

    [Fact]
    public void FullDestinationLairKeepsExistingBed()
    {
        var (sim, creature, oldBed) = Start();
        CreatureState other = sim.World.SpawnCreature(sim.Database,
            Dk2TerrainFixture.GoblinCreatureId, PlayerIds.FirstKeeper, NewTile);
        LairRules.PlaceBed(sim.World, sim.Database, other, sim.World.TileIndex(NewTile));
        Drop(sim, creature, NewTile);
        sim.Run(160);

        Assert.Same(oldBed, LairRules.BedOf(sim.World, sim.Database, creature));
        Assert.False(LairRules.IsFreeLairTile(sim.World, sim.Database,
            sim.World.TileIndex(OldTile), PlayerIds.FirstKeeper));
    }

    [Fact]
    public void DropIntoSameLairUsesExistingBedWithoutMovingIt()
    {
        var (sim, creature, oldBed) = Start();
        TilePos adjacent = new(1, 2);
        RoomRules.Place(sim.Database, ref sim.World.Tile(adjacent),
            sim.Database.Room(RoomTypeId.Lair), PlayerIds.FirstKeeper);
        Drop(sim, creature, adjacent);
        sim.Run(160);

        Assert.Same(oldBed, LairRules.BedOf(sim.World, sim.Database, creature));
        Assert.Equal(OldTile, creature.Tile);
        Assert.True(LairRules.IsFreeLairTile(sim.World, sim.Database,
            sim.World.TileIndex(adjacent), PlayerIds.FirstKeeper));
    }

    [Fact]
    public void OccupiedDropTileUsesAnotherFreeTileInTheDestinationLair()
    {
        var (sim, creature, oldBed) = Start();
        TilePos adjacent = new(3, 2);
        RoomRules.Place(sim.Database, ref sim.World.Tile(adjacent),
            sim.Database.Room(RoomTypeId.Lair), PlayerIds.FirstKeeper);
        CreatureState other = sim.World.SpawnCreature(sim.Database,
            Dk2TerrainFixture.GoblinCreatureId, PlayerIds.FirstKeeper, NewTile);
        LairRules.PlaceBed(sim.World, sim.Database, other, sim.World.TileIndex(NewTile));
        Drop(sim, creature, NewTile);
        sim.Run(200);

        Assert.Equal(sim.World.TileIndex(adjacent), LairRules.BedTileIndex(sim.World, sim.Database, creature));
        Assert.DoesNotContain(sim.World.Objects, o => o.Id == oldBed.Id);
    }

    [Fact]
    public void RelocationMovesOccupancyAndTheBedStaysInTheNewLair()
    {
        var (sim, creature, _) = Start();
        RoomInstance oldLair = LairAt(sim, OldTile), newLair = LairAt(sim, NewTile);
        Assert.Equal((1, 0), (LairRules.Occupied(sim.World, sim.Database, oldLair),
            LairRules.Occupied(sim.World, sim.Database, newLair)));
        Drop(sim, creature, NewTile);
        sim.Run(160);
        ObjectState bed = Assert.IsType<ObjectState>(LairRules.BedOf(sim.World, sim.Database, creature));

        // Long enough to tire and sleep: it sleeps in the new bed, never makes another.
        bool slept = false;
        for (int i = 0; i < 2000; i++)
        {
            sim.Tick();
            Assert.Same(bed, LairRules.BedOf(sim.World, sim.Database, creature));
            Assert.Single(sim.World.Objects, o => LairRules.IsBed(sim.Database, o));
            slept |= LairRules.AsleepInOwnBed(sim.World, sim.Database, creature);
        }
        Assert.True(slept);
        Assert.Equal((0, 1), (LairRules.Occupied(sim.World, sim.Database, oldLair),
            LairRules.Occupied(sim.World, sim.Database, newLair)));
    }

    [Fact]
    public void KeepersCreatureThatHatesSleepIsRefusedAndKeepsItsBed()
    {
        // 0x004E8DA0 refuses a keeper's creature that hates the room's job (hand-drop-landing.md §4).
        CreatureDef definition = Dk2TerrainFixture.Goblin() with
            { CreatureId = 60, Name = "Sleep Hater", HateJobs = [JobType.Sleep] };
        var (sim, creature, oldBed) = Start(definition);
        Drop(sim, creature, NewTile);
        for (int i = 0; i < 160; i++)
        {
            sim.Tick();
            Assert.NotEqual(LairRules.SelectingBedState, creature.Dk2StateId);
        }
        Assert.Same(oldBed, LairRules.BedOf(sim.World, sim.Database, creature));
        Assert.NotEqual(JobType.Sleep, creature.DroppedToDoJob);
        Assert.True(LairRules.IsFreeLairTile(sim.World, sim.Database,
            sim.World.TileIndex(NewTile), PlayerIds.FirstKeeper));
    }

    [Fact]
    public void RelocationIsDeterministicAcrossRuns()
    {
        var (first, a, _) = Start(defaultPipeline: true);
        var (second, b, _) = Start(defaultPipeline: true);
        Drop(first, a, NewTile);
        Drop(second, b, NewTile);
        for (int i = 0; i < 200; i++)
        {
            first.Tick();
            second.Tick();
            Assert.Equal(first.StateHash(), second.StateHash());
        }
        Assert.Equal(first.World.TileIndex(NewTile), LairRules.BedTileIndex(first.World, first.Database, a));
    }

    private static RoomInstance LairAt(GameSimulation sim, TilePos at) =>
        Assert.IsType<RoomInstance>(RoomIndex.RoomAt(sim.World, sim.Database, sim.World.TileIndex(at)));

    private static void Drop(GameSimulation sim, CreatureState creature, TilePos at)
    {
        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(PlayerIds.FirstKeeper, creature.Id)));
        sim.Tick(InputFrame.Of(new DropCommand(PlayerIds.FirstKeeper, at)));
    }

    private static (GameSimulation, CreatureState, ObjectState) Start(CreatureDef? definition = null, bool defaultPipeline = false)
    {
        var database = definition is null ? Dk2TerrainFixture.Database() : Dk2TerrainFixture.Database(definition);
        var world = WorldState.FromMap(database, Dk2TerrainFixture.Map(),
            [PlayerIds.Unowned, PlayerIds.Neutral, PlayerIds.FirstKeeper]);
        foreach (TilePos at in new[] { OldTile, NewTile })
            RoomRules.Place(database, ref world.Tile(at), database.Room(RoomTypeId.Lair), PlayerIds.FirstKeeper);
        CreatureState creature = world.SpawnCreature(database,
            definition?.CreatureId ?? Dk2TerrainFixture.GoblinCreatureId, PlayerIds.FirstKeeper, OldTile);
        ObjectState bed = LairRules.PlaceBed(world, database, creature, world.TileIndex(OldTile));
        var simulation = defaultPipeline ? new GameSimulation(database, world, 42, SimulationSystems.Default())
            : new GameSimulation(database, world, 42,
                [new HandSystem(), new CreatureStatusSystem(), new CreatureMovementSystem(), new CreatureSleepSystem()]);
        return (simulation, creature, bed);
    }
}
