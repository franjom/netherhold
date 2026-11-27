// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public sealed class SleepDebtTests
{
    private static readonly PlayerId Keeper = PlayerIds.FirstKeeper;
    private static readonly TilePos Bed = new TilePos(2, 2);

    [Fact]
    public void AHealthyManualSleeperWithNoDebtLeavesOnItsFirstUpdate()
    {
        (GameSimulation? sim, CreatureState? creature) = Start();
        StatePlacement.Place(creature, LairRules.SleepingState);

        sim.Tick();

        Assert.Equal(CreatureActivity.Idle, creature.Activity);
    }

    [Fact]
    public void InterruptedSleepPaysOnlyTheRemainingDebtAfterReentry()
    {
        (GameSimulation? sim, CreatureState? creature) = Start();
        MakeOverdue(creature);
        for (int i = 0; i < 16 && creature.Dk2StateId != LairRules.SleepingState; i++)
        {
            sim.Tick();
        }

        Assert.Equal(LairRules.SleepingState, creature.Dk2StateId);
        sim.Run(3);
        Assert.Equal(CreatureActivity.Sleeping, creature.Activity);

        StatePlacement.Place(creature, 108);
        sim.Tick();
        StatePlacement.Place(creature, LairRules.LyingDownState);
        sim.Tick();
        Assert.Equal(LairRules.SleepingState, creature.Dk2StateId);
        sim.Run(8);
        Assert.Equal(CreatureActivity.Sleeping, creature.Activity);
        sim.Tick();

        Assert.Equal(CreatureActivity.Idle, creature.Activity);
    }

    [Fact]
    public void CaptivityBlocksTheBedAttemptButStillLoadsOverdueSleepDebt()
    {
        (GameSimulation? sim, CreatureState? creature) = Start();
        StatePlacement.Place(creature, PrisonRules.InTheCellState);
        creature.PrisonOwnerPlayerId = 4;

        sim.Run(9);

        Assert.True(NeedRules.IsTired(sim.Database.CreatureById(creature.CreatureTypeId), creature));
        Assert.Equal(PrisonRules.InTheCellState, creature.Dk2StateId);
    }

    [Theory]
    [InlineData(79)]
    [InlineData(80)]
    [InlineData(83)]
    [InlineData(93)]
    [InlineData(187)]
    public void NeedsStatesDeferHungerButAnOverdueHelpingLandsOnTheFirstUpdateAfterLeaving(int state)
    {
        (GameSimulation? sim, CreatureState? creature) = Start(healthOnly: true);
        StatePlacement.Place(creature, state);
        sim.Run(8);
        Assert.Equal(0, creature.HungerNeed);

        StatePlacement.Place(creature, CreatureStateMachine.Idle);
        sim.Tick();

        Assert.Equal(1, creature.HungerNeed);
    }

    [Fact]
    public void ABusyTiredAttemptSpendsItsFourTurnRetryBeforeTheCreatureBecomesFree()
    {
        (GameSimulation? sim, CreatureState? creature) = Start();
        MakeOverdue(creature);
        creature.DroppedOnARoom = true;
        creature.PickupTick = -15; // The five-second grace expires at turn 5.

        sim.Run(8);
        Assert.Equal(CreatureActivity.Idle, creature.Activity);
        sim.Tick();

        Assert.Equal(CreatureActivity.GoingToSleep, creature.Activity);
    }

    [Fact]
    public void SleepDebtAndItsClockAndRetrySurviveSaveAndContinueIdentically()
    {
        (GameSimulation? sim, CreatureState? creature) = Start();
        creature.SleepDebtTicks = 9;
        creature.LastSleptTick = new GameTick(3);
        creature.NextSleepTryTick = new GameTick(7);
        StatePlacement.Place(creature, LairRules.SleepingState);
        ulong savedHash = sim.StateHash();

        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), sim.Database,
            [new CreatureSleepSystem()]);
        CreatureState copy = Assert.Single(restored.World.Creatures);
        Assert.Equal(9, copy.SleepDebtTicks);
        Assert.Equal(new GameTick(3), copy.LastSleptTick);
        Assert.Equal(new GameTick(7), copy.NextSleepTryTick);
        Assert.Equal(savedHash, restored.StateHash());
        for (int i = 0; i < 12; i++)
        {
            sim.Tick();
            restored.Tick();
            Assert.Equal(sim.StateHash(), restored.StateHash());
        }
    }

    [Theory]
    [InlineData(79)]
    [InlineData(80)]
    [InlineData(93)]
    [InlineData(145)]
    [InlineData(187)]
    public void SuppressedSleepUpdatesLeaveTheStampSoDebtLoadsOnLeaving(int state)
    {
        (GameSimulation? sim, CreatureState? creature) = Start();
        for (int i = 0; i < 9; i++)
        {
            StatePlacement.Place(creature, state);
            sim.Tick();
        }
        Assert.Equal(0, creature.SleepDebtTicks);
        Assert.Equal(GameTick.Zero, creature.LastSleptTick);

        StatePlacement.Place(creature, CreatureStateMachine.Idle);
        sim.Tick();

        Assert.Equal(12, creature.SleepDebtTicks);
        Assert.Equal(new GameTick(9), creature.LastSleptTick);
    }

    [Fact]
    public void ARescuedSleeperPaysDebtWhileStillAtZeroHealth()
    {
        (GameSimulation? sim, CreatureState? creature) = Start();
        creature.SleepDebtTicks = 9;
        creature.Health = 0;
        StatePlacement.Place(creature, LairRules.SleepingState);

        sim.Run(3);

        Assert.Equal(6, creature.SleepDebtTicks);
        Assert.Equal(new GameTick(2), creature.LastSleptTick);
        Assert.Equal(CreatureActivity.Sleeping, creature.Activity);
    }

    [Fact]
    public void AWoundedTiredAttemptStillStampsTheRetryBeforeItsBusyRefusal()
    {
        (GameSimulation? sim, CreatureState? creature) = Start();
        creature.SleepDebtTicks = 12;
        creature.NextSleepTryTick = GameTick.Zero;
        creature.Health = 300;
        creature.DroppedOnARoom = true;
        creature.PickupTick = -19;
        sim.Tick();
        creature.Health = 1500;
        sim.Tick();

        Assert.Equal(new GameTick(4), creature.NextSleepTryTick);
        Assert.Equal(CreatureActivity.Idle, creature.Activity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void EachPersistentSleepFieldContributesToTheStateHash(int field)
    {
        (GameSimulation? sim, CreatureState? creature) = Start();
        ulong before = sim.StateHash();
        switch (field)
        {
            case 0: creature.SleepDebtTicks = 5; break;
            case 1: creature.LastSleptTick = new GameTick(7); break;
            case 2: creature.NextSleepTryTick = new GameTick(11); break;
        }
        Assert.NotEqual(before, sim.StateHash());
    }

    /// <summary>The hunger clock alone: the eat step would also move a creature out of state 93.</summary>
    private sealed class HungerClockStep : ICreatureUpdateStep
    {
        public string Name
        {
            get
            {
                return "HungerClock";
            }
        }

        public void Execute(SimulationContext context)
        {
            CreatureUpdateSystem.RunAlone(context, this);
        }

        public void Update(SimulationContext context, CreatureState creature)
        {
            CreatureEatSystem.Clock(context, context.Database.CreatureById(creature.CreatureTypeId), creature);
        }
    }

    private static void MakeOverdue(CreatureState creature)
    {
        creature.LastSleptTick = new GameTick(-8);
    }

    private static (GameSimulation Sim, CreatureState Creature) Start(bool healthOnly = false)
    {
        CreatureDef definition = Dk2TerrainFixture.Goblin() with
            { CreatureId = 60, Name = "Sleep Test Goblin", TimeAwake = 2, TimeSleep = 3, HungerRate = new Fp32(Fp32.One) };
        GameDatabase database = Dk2TerrainFixture.Database(definition);
        var world = WorldState.FromMap(database, Dk2TerrainFixture.Map(),
            [PlayerIds.Unowned, PlayerIds.Neutral, Keeper.Value]);
        RoomRules.Place(database, ref world.Tile(Bed), database.Room(RoomTypeId.Lair), Keeper);
        CreatureState creature = world.SpawnCreature(database, definition.CreatureId, Keeper, Bed).Unstaggered();
        LairRules.PlaceBed(world, database, creature, world.TileIndex(Bed));
        ISimulationSystem[] systems = healthOnly
            ? [new CreatureUpdateSystem(new HungerClockStep())]
            : [new CreatureSleepSystem()];
        return (new GameSimulation(database, world, 42, systems), creature);
    }
}
