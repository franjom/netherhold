// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The Lair drop and the Sleep job. The hand's Lair drop (<c>0x004E8DA0</c>) takes the Lair
/// record's job, 0, so it remembers no job and sends the creature through 79; the Sleep job itself,
/// Job_Sleep (<c>0x0046F440</c>), routes a bedded creature to bed only while its sleep latch is set, with
/// the room helper resuming 81, and 81 walks on to 82 and 83. Ours once remembered Sleep on the drop and
/// stood the creature in 81 as an "at work" job that no system advanced: it never slept and never took wages.</summary>
public sealed class SleepJobTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private static readonly TilePos BedTile = new(1, 1);
    private static readonly TilePos Adjacent = new(3, 3);
    private static readonly TilePos OtherLair = new(7, 2);
    private static readonly TilePos Vault = new(5, 2);

    [Fact]
    public void ATiredCreatureDroppedInItsOwnLairSleepsAndWakesAndIsNotLeftInState81()
    {
        (GameSimulation sim, CreatureState goblin, ListEventSink sink) = Start(withPayday: false);
        goblin.SleepDebtTicks = SimulationTiming.TicksFromSeconds(3);

        Drop(sim, goblin, Adjacent);
        sim.Run(SimulationTiming.TicksPerSecond * 30);

        Assert.Contains(sink.Events, e => e.Kind == "fell_asleep");
        Assert.Contains(sink.Events, e => e.Kind == "woke_up");
        AssertOutOfBed(goblin, sink);
        Assert.Equal(0, goblin.SleepDebtTicks);
    }

    [Fact]
    public void ARestedCreatureDroppedInItsOwnLairLiesDownAndGetsStraightUp()
    {
        // 79 reuses the bed in this room (81, 82, 83) and 83 leaves at once on zero debt and full health.
        (GameSimulation sim, CreatureState goblin, ListEventSink sink) = Start(withPayday: false);

        Drop(sim, goblin, Adjacent);
        sim.Run(SimulationTiming.TicksPerSecond * 30);

        Assert.Contains(sink.Events, e => e.Kind == "woke_up");
        AssertOutOfBed(goblin, sink);
        Assert.DoesNotContain(sink.Events, e => e.Kind == "job_taken");
    }

    [Fact]
    public void TheSleepJobRoutesATiredBeddedCreatureToItsBedAndItSleepsAndWakes()
    {
        // Job_Sleep with a bed and the latch set: the tired attempt's own route (7 resuming 81).
        (GameSimulation sim, CreatureState goblin, ListEventSink sink) = Start(withPayday: false);
        goblin.StopMoving();
        goblin.PosX = CreatureState.CentreOf(Adjacent.X);
        goblin.PosY = CreatureState.CentreOf(Adjacent.Y);
        goblin.SleepDebtTicks = SimulationTiming.TicksFromSeconds(3);
        goblin.NextSleepTryTick = new GameTick(1_000_000); // the sleep system's own attempt stays out of it
        goblin.DroppedToDoJob = JobType.Sleep;

        sim.Run(SimulationTiming.TicksPerSecond * 30);

        Assert.Contains(sink.Events, e => e.Kind == "job_taken" && e.Detail.Contains("job=Sleep") && e.Detail.Contains("how=dropped"));
        Assert.Contains(sink.Events, e => e.Kind == "fell_asleep");
        Assert.Contains(sink.Events, e => e.Kind == "woke_up");
        AssertOutOfBed(goblin, sink);
    }

    [Fact]
    public void TheSleepJobIsRefusedForARestedBeddedCreatureAndForgotten()
    {
        // Job_Sleep returns 0 without the latch (0x0046F440); idle's chooser forgets a refused dropped job.
        (GameSimulation sim, CreatureState goblin, ListEventSink sink) = Start(withPayday: false);
        goblin.DroppedToDoJob = JobType.Sleep;

        sim.Run(SimulationTiming.TicksPerSecond * 10);

        Assert.DoesNotContain(sink.Events, e => e.Kind == "job_taken");
        Assert.DoesNotContain(sink.Events, e => e.Kind == "going_to_sleep");
        AssertOutOfBed(goblin, sink);
    }

    [Fact]
    public void ATiredCreatureDroppedInAnotherLairMakesABedThereAndSleeps()
    {
        // 79 builds the bed and ends idle; the tired attempt then sends it to that bed.
        (GameSimulation sim, CreatureState goblin, ListEventSink sink) = Start(withPayday: false);
        goblin.SleepDebtTicks = SimulationTiming.TicksFromSeconds(3);

        Drop(sim, goblin, OtherLair);
        sim.Run(SimulationTiming.TicksPerSecond * 30);

        Assert.Contains(sink.Events, e => e.Kind == "bed_claimed");
        Assert.Contains(sink.Events, e => e.Kind == "fell_asleep");
        Assert.Contains(sink.Events, e => e.Kind == "woke_up");
        AssertOutOfBed(goblin, sink);
    }

    [Fact]
    public void ACreatureDroppedInItsLairCollectsItsWagesAtTheNextPaydayWhenGoldIsInTheTreasury()
    {
        (GameSimulation sim, CreatureState goblin, ListEventSink sink) = Start(withPayday: true);
        int firstPayday = PaydayRules.FirstPaydayTicks(sim.Database);

        // Dropped long enough before the clock fires for the job to start (old code: parked in 81 at payday).
        const int leadTicks = SimulationTiming.TicksPerSecond * 25;
        sim.Run(firstPayday - leadTicks);
        Drop(sim, goblin, Adjacent);
        sim.Run(leadTicks + SimulationTiming.TicksPerSecond * 60);

        Assert.Equal(0, goblin.WageOwed);
        Assert.Contains(sink.Events, e => e.Kind == "paid");
        AssertOutOfBed(goblin, sink);
    }

    /// <summary>Not parked in 81, 82 or 83 and not holding the Sleep job.</summary>
    private static void AssertOutOfBed(CreatureState goblin, ListEventSink sink)
    {
        Assert.DoesNotContain(goblin.Dk2StateId, new[] { LairRules.GoingToBedState, LairRules.LyingDownState, LairRules.SleepingState });
        Assert.NotEqual(JobType.Sleep, goblin.CurrentJob);
        Assert.Equal(JobType.None, goblin.DroppedToDoJob);
        Assert.DoesNotContain(sink.Events, e => e.Kind == "job_started" && e.Detail.Contains("job=Sleep"));
    }

    /// <summary>State 83's counter (<c>0x0046FDF3</c>): loaded with <c>0x30</c> on the first call, so the
    /// Lair's own effect (67 in the shipped data) is asked for on every 49th sleeping tick, at the
    /// sleeper, for its owner.</summary>
    [Fact]
    public void ASleeperAsksForTheLairsEffectOnEveryFortyNinthSleepingTick()
    {
        (GameSimulation sim, CreatureState goblin, ListEventSink sink) = Start(withPayday: false, lairEffect: 67);
        goblin.SleepDebtTicks = 120;

        Drop(sim, goblin, Adjacent);
        var asked = new List<int>();
        for (int t = 0; t < 400 && !sink.Events.Any(e => e.Kind == "woke_up"); t++)
        {
            int before = sink.Events.Count(e => e.EffectRequest?.EffectId == new EffectId(67));
            sim.Tick();
            if (sink.Events.Count(e => e.EffectRequest?.EffectId == new EffectId(67)) != before)
            {
                asked.Add(goblin.SleepTicks);
            }
        }

        Assert.Equal([49, 98], asked);
        ViewEffectRequest request = sink.Events.First(e => e.EffectRequest?.EffectId == new EffectId(67)).EffectRequest!.Value;
        Assert.Equal(Keeper, request.Owner.Value);
    }

    private static void Drop(GameSimulation sim, CreatureState creature, TilePos at)
    {
        sim.Tick(InputFrame.Of(new TakeCreatureCommand(Keeper, creature.Id)));
        sim.Tick(InputFrame.Of(new DropCommand(Keeper, at)));
    }

    private static (GameSimulation, CreatureState, ListEventSink) Start(bool withPayday, ushort lairEffect = 0)
    {
        GameDatabase fixture = Dk2TerrainFixture.Database();
        GameDatabase database = lairEffect == 0 ? fixture : new GameDatabase(fixture.Terrain.Values,
            fixture.Rooms.Values.Select(r => r.TypeId == RoomTypeId.Lair ? r with { RoomEffect425Id = lairEffect } : r),
            fixture.Creatures.Values, fixture.Variables, objects: fixture.Objects.Values);
        var world = WorldState.FromMap(database, Dk2TerrainFixture.Map(),
            [PlayerIds.Unowned, PlayerIds.Neutral, Keeper]);
        // A 3x3 Lair whose room point (the centre) is not the bed, so the job's walk has somewhere to go.
        for (int y = 1; y <= 3; y++)
        {
            for (int x = 1; x <= 3; x++)
            {
                RoomRules.Place(database, ref world.Tile(x, y), database.Room(RoomTypeId.Lair), Keeper);
            }
        }

        RoomRules.Place(database, ref world.Tile(OtherLair), database.Room(RoomTypeId.Lair), Keeper);
        for (int x = 4; x <= 6; x++)
        {
            world.Tile(x, 2).TerrainId = Dk2TerrainFixture.ClaimedPath;
            world.Tile(x, 2).OwnerPlayerId = Keeper;
        }
        CreatureState goblin = world.SpawnCreature(database, Dk2TerrainFixture.GoblinCreatureId, Keeper, BedTile);
        LairRules.PlaceBed(world, database, goblin, world.TileIndex(BedTile));

        var systems = new List<ISimulationSystem>
        {
            new HandSystem(), new CreatureStatusSystem(), new CreatureMovementSystem(),
            new CreatureSleepSystem(), new CreatureJobSystem(),
        };
        if (withPayday)
        {
            ref TileState vault = ref world.Tile(Vault.X, Vault.Y);
            vault.TerrainId = Dk2TerrainFixture.TreasuryTerrain;
            vault.OwnerPlayerId = Keeper;
            vault.GoldHeld = 5000;
            systems.Add(new PaydaySystem());
        }

        var sim = new GameSimulation(database, world, 42, systems);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        return (sim, goblin, sink);
    }
}
