// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public sealed class BedlessSleepNeedTests
{
    [Fact]
    public void AHealthyUntiredCreatureHandsIdleToTheChooserBeforeAcquiringALair()
    {
        var (sim, creature, events) = Start(withChooser: true);
        uint seed = ((Dk2Lcg)sim.Rng.Algorithm).Seed;
        long x = creature.PosX, y = creature.PosY;

        sim.Tick();

        Assert.Equal(CreatureJobSystem.IdleChooserState, creature.Dk2StateId);
        Assert.Equal(x, creature.PosX);
        Assert.Equal(y, creature.PosY);
        Assert.Equal(seed, ((Dk2Lcg)sim.Rng.Algorithm).Seed);
        Assert.DoesNotContain(events.Events, e => e.Kind == "lair_sought");

        sim.Tick();

        Assert.Equal(80, creature.UtilityNewState);
        Assert.True(creature.HasRoute);
        Assert.Contains(events.Events, e => e.Kind == "lair_sought" && e.System == "CreatureJob");
    }

    [Fact]
    public void AHealthyUntiredBedlessCreatureDoesNotAskTheSleepNeedForALair()
    {
        var (sim, creature, events) = Start();
        creature.TimerTicks = 7;
        uint seed = ((Dk2Lcg)sim.Rng.Algorithm).Seed;

        sim.Run(12);

        Assert.Equal(CreatureStateMachine.Idle, creature.Dk2StateId);
        Assert.Equal(7, creature.TimerTicks);
        Assert.Equal(seed, ((Dk2Lcg)sim.Rng.Algorithm).Seed);
        Assert.Empty(events.Events);
    }

    [Fact]
    public void ATiredBedlessCreatureUsesTheSleepRetryRatherThanTheIdleCountdown()
    {
        var (sim, creature, _) = Start();
        creature.SleepDebtTicks = 8;
        creature.NextSleepTryTick = new GameTick(4);
        creature.TimerTicks = 99;

        sim.Run(4);
        Assert.Equal(CreatureStateMachine.Idle, creature.Dk2StateId);
        Assert.Equal(99, creature.TimerTicks);
        sim.Tick();

        Assert.True(creature.HasRoute);
        Assert.Equal(80, creature.UtilityNewState);
        Assert.Equal(new GameTick(8), creature.NextSleepTryTick);
    }

    [Fact]
    public void ABusyBedlessAttemptSpendsItsRetryBeforeTheCreatureBecomesFree()
    {
        var (sim, creature, _) = Start();
        creature.SleepDebtTicks = 8;
        creature.DroppedOnARoom = true;
        creature.PickupTick = -15;

        sim.Run(8);
        Assert.Equal(CreatureStateMachine.Idle, creature.Dk2StateId);
        Assert.Equal(new GameTick(8), creature.NextSleepTryTick);
        sim.Tick();

        Assert.True(creature.HasRoute);
        Assert.Equal(80, creature.UtilityNewState);
    }

    [Fact]
    public void AWoundedBedlessResearcherMaySeekALairWithoutDebtOrARetryWait()
    {
        var (sim, creature, _) = Start();
        StatePlacement.Place(creature, 56);
        creature.Health = 1;
        creature.NextSleepTryTick = new GameTick(100);

        sim.Tick();

        Assert.True(creature.HasRoute);
        Assert.Equal(80, creature.UtilityNewState);
        Assert.Equal(0, creature.SleepDebtTicks);
        Assert.Equal(new GameTick(100), creature.NextSleepTryTick);
    }

    private static (GameSimulation, CreatureState, ListEventSink) Start(bool withChooser = false)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [PlayerIds.Neutral, (byte)3]);
        for (int y = 1; y <= 3; y++)
            for (int x = 1; x <= 8; x++)
            {
                world.Tile(x, y).TerrainId = Dk2TerrainFixture.ClaimedPath;
                world.Tile(x, y).OwnerPlayerId = 3;
                if (x >= 6) RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Lair), 3);
            }
        CreatureState creature = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, 3, new TilePos(2, 2)).Unstaggered();
        var events = new ListEventSink();
        ICreatureUpdateStep[] steps = withChooser
            ? [new CreatureSleepSystem(), new CreatureJobSystem()]
            : [new CreatureSleepSystem()];
        var sim = new GameSimulation(db, world, new SimRandom(new Dk2Lcg(42)), [new CreatureUpdateSystem(steps)]);
        sim.ObserveWith(events);
        return (sim, creature, events);
    }
}
