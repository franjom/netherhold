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
        ManufactureRules.Enqueue(ref world.Player(Keeper), new ManufactureOrder(CrateRules.DoorKind, 1, 1000));
        ManufactureRules.Enqueue(ref world.Player(Keeper), new ManufactureOrder(CrateRules.DoorKind, 1, 1000));
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
        // In 63, or already on 63's walk to a machine, resuming 64 (ADR-0404).
        Assert.True(maker.Dk2StateId == WorkshopStep.ChoosingState
            || (maker.Dk2StateId == 6 && maker.UtilityNewState == WorkshopStep.AtTheMachineState),
            $"state {maker.Dk2StateId} resume {maker.UtilityNewState}");
        Assert.Equal(JobType.Manufacture, maker.CurrentJob);
        Assert.Equal(JobType.Manufacture, maker.DroppedToDoJob);
        Assert.Equal(EfficiencyRules.BasePercent + EfficiencyRules.BonusPercent, EfficiencyRules.Percent(maker, db.CreatureById(MakerId)));
    }

    /// <summary>The Manufacture update <c>0x0047B190</c> returns 0 on a zero credit, and the
    /// dispatcher idles the creature (ADR-0573): shipped, the Unique Skeleton, which neither
    /// manufactures nor hates it, and the hand can lift. Idle retries the dropped job, which only a
    /// refusal forgets, so with orders queued it goes back and is idled again (Q672).</summary>
    [Fact]
    public void ACreatureThatCannotManufactureIsDroppedInTheWorkshop_NeverEarns_AndIsIdledByEveryHook()
    {
        GameDatabase db = Dk2TerrainFixture.Database(Dk2TerrainFixture.Goblin() with
        {
            CreatureId = MakerId, Name = "Idler", ManufacturePerSecond = 0, ResearchPerSecond = 0,
            HappyJobs = [new(JobType.Train, 0, 0, 100)], HateJobs = [],
        });
        WorldState world = World(db);
        CreatureState idler = world.SpawnCreature(db, MakerId, Keeper, Aside);
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);

        DropOnTheBench(sim, idler);
        int idleTurns = 0;
        for (int t = 0; t < 2 * Second; t++)
        {
            sim.Tick();
            Assert.Equal(0, world.Player(Keeper).ManufacturePoints);
            Assert.InRange(world.Player(Keeper).ManufacturingCreatures, 0, 1);
            if (idler.CurrentJob == JobType.None) idleTurns++;
        }

        Assert.Contains(sink.Events, e => e.Kind == "dropped_to_job");
        Assert.True(sink.Events.Count(e => e.Kind == "job_lost"
            && e.Detail.Contains("reason=cannot_manufacture", StringComparison.Ordinal)) >= 2);
        Assert.True(idleTurns > 0);
        Assert.Equal(JobType.Manufacture, idler.DroppedToDoJob);
        Assert.Equal(0, world.Player(Keeper).ItemsManufactured);
    }

    /// <summary>The Research update <c>0x0047B000</c> credits the scaled rate, zero included, and
    /// returns 1: a dropped creature that cannot research stays at work and earns nothing.</summary>
    [Fact]
    public void ACreatureThatCannotResearchIsDroppedInTheLibraryAndStaysEarningNothing()
    {
        var level = new GlobalVariables();
        level.Research.Add(new ResearchEntry(ResearchTreeRules.KindSpell, Dk2TerrainFixture.CreateImpSpellId, 1_000_000));
        GameDatabase db = Dk2TerrainFixture.Database(Dk2TerrainFixture.Goblin() with
        {
            CreatureId = MakerId, Name = "Idler", ManufacturePerSecond = 0, ResearchPerSecond = 0,
            HappyJobs = [new(JobType.Train, 0, 0, 100)], HateJobs = [],
        }).ForLevel(level);
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, (byte)1, PlayerIds.Neutral, Keeper]);
        for (int y = 1; y <= 3; y++)
        for (int x = 1; x <= 3; x++)
            RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Library), Keeper);
        world.Player(Keeper).Availabilities.Add(new AvailabilityState(AvailabilityKind.Spell, Dk2TerrainFixture.CreateImpSpellId, 3));
        world.Player(Keeper).ResearchTarget = 1;
        Dk2TerrainFixture.FurnishRooms(db, world);
        CreatureState idler = world.SpawnCreature(db, MakerId, Keeper, Aside);
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);

        DropOnTheBench(sim, idler);
        sim.Run(5 * Second);

        Assert.Contains(sink.Events, e => e.Kind == "dropped_to_job");
        Assert.DoesNotContain(sink.Events, e => e.Kind == "job_lost");
        Assert.Equal(JobType.Research, idler.CurrentJob);
        Assert.Equal(JobType.Research, idler.DroppedToDoJob);
        Assert.Equal(0, world.Player(Keeper).ResearchPoints);
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
