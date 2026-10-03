// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The job a creature is dropped on (ADR-0215): the hand's drop on a job room starts its
/// work state and remembers the job (<c>0x004E8DA0</c>), and idle's chooser 89 tries that job
/// first until it is refused.</summary>
public class DroppedToDoJobTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte MakerId = 61;
    private const int Second = SimulationTiming.TicksPerSecond;
    private static readonly TilePos Bench = new(2, 2);
    private static readonly TilePos Aside = new(6, 2);

    /// <summary>A manufacturer whose content table is Train alone, so only the dropped job can
    /// send it back to the Workshop.</summary>
    private static GameDatabase Db(params JobType[] hates) => Dk2TerrainFixture.Database(
        Dk2TerrainFixture.Goblin() with
        {
            CreatureId = MakerId, Name = "Maker", ManufacturePerSecond = 40,
            HappyJobs = [new(JobType.Train, 0, 0, 100)], HateJobs = hates,
        });

    private static WorldState World(GameDatabase db)
    {
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, (byte)1, PlayerIds.Neutral, Keeper]);
        for (int y = 1; y <= 3; y++)
        for (int x = 1; x <= 3; x++)
            RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Workshop), Keeper);
        for (int x = 4; x <= 8; x++)
        {
            world.Tile(x, 2).TerrainId = Dk2TerrainFixture.ClaimedPath;
            world.Tile(x, 2).OwnerPlayerId = Keeper;
        }

        Dk2TerrainFixture.FurnishRooms(db, world);
        world.Player(Keeper).ManufactureQueue.Add(new ManufactureOrder(CrateRules.DoorKind, 1, 1000));
        world.Player(Keeper).ManufactureQueue.Add(new ManufactureOrder(CrateRules.DoorKind, 1, 1000));
        return world;
    }

    private static GameSimulation Simulation(GameDatabase db, WorldState world, ListEventSink sink)
    {
        var sim = new GameSimulation(db, world, seed: 42,
            [new HandSystem(), new CreatureStatusSystem(), new CreatureMovementSystem(), new CreatureJobSystem()]);
        sim.ObserveWith(sink);
        return sim;
    }

    /// <summary>The drop, then the stun it lands in (76 to 255, ADR-0226), then 257's room drop.</summary>
    private static void DropOnTheBench(GameSimulation sim, CreatureState creature)
    {
        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, creature.Id)));
        sim.Tick(InputFrame.Of(new DropCommand(Keeper, Bench)));
        Assert.Equal(255, creature.Dk2StateId);
        for (int t = 0; t < 400 && creature.Dk2StateId is 255 or 257; t++) sim.Tick();
    }

    [Fact]
    public void ACreatureDroppedInTheWorkshop_IsAtWorkThereOnceItGetsUp_AndRemembersTheJob()
    {
        GameDatabase db = Db();
        WorldState world = World(db);
        CreatureState maker = world.SpawnCreature(db, MakerId, Keeper, Aside);
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);

        DropOnTheBench(sim, maker);

        Assert.Contains(sink.Events, e => e.Kind == "dropped_to_job");
        Assert.Equal(63, maker.Dk2StateId);
        Assert.Equal(JobType.Manufacture, maker.CurrentJob);
        Assert.Equal(JobType.Manufacture, maker.DroppedToDoJob);
        Assert.Equal(EfficiencyRules.BasePercent + EfficiencyRules.BonusPercent, EfficiencyRules.Percent(maker, db.CreatureById(MakerId)));
    }

    [Fact]
    public void ACreatureThatHatesTheRoomsJobIsDroppedThereLikeAnywhereElse()
    {
        GameDatabase db = Db(JobType.Manufacture);
        WorldState world = World(db);
        CreatureState maker = world.SpawnCreature(db, MakerId, Keeper, Aside);
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);

        DropOnTheBench(sim, maker);

        Assert.DoesNotContain(sink.Events, e => e.Kind == "dropped_to_job");
        Assert.Equal(JobType.None, maker.DroppedToDoJob);
    }

    [Fact]
    public void AfterACrateTheDroppedJobIsTriedBeforeTheTable_AndIsForgottenOnceRefused()
    {
        // Its table says Train, and there is no Training Room: 89 takes Manufacture again while
        // the queue holds an order, and once the queue is empty the refusal clears the job.
        GameDatabase db = Db();
        WorldState world = World(db);
        CreatureState maker = world.SpawnCreature(db, MakerId, Keeper, Aside);
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);
        DropOnTheBench(sim, maker);

        sim.Run(Second * 30);
        Assert.Equal(1, world.Player(Keeper).ItemsManufactured);
        Assert.Equal(JobType.Manufacture, maker.CurrentJob);
        Assert.Contains(sink.Events, e => e.Kind == "job_taken" && e.Detail.Contains("how=dropped"));

        sim.Run(Second * 30);
        Assert.Equal(2, world.Player(Keeper).ItemsManufactured);
        Assert.Equal(JobType.None, maker.DroppedToDoJob);
    }
}
