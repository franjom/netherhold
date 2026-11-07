// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Steering;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public sealed class LairHomeHandoverTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ACompletedWalkEntersHomeWithoutRunningItsHandler(bool finished)
    {
        var (db, world, creature) = Home(7);
        creature.UtilityNewState = LairRules.MakingAHomeState;
        RouteWindow.Load(creature.Body, [creature.Body.Pos], creature.Body.Pos, 0);
        creature.Body.PathFinished = finished;
        var sim = new GameSimulation(db, world, 1, [new CreatureUpdateSystem(
            new TravelArrivalSystem(), new CreatureSleepSystem(), new CreatureJobSystem(), new CreatureMovementSystem())]);
        creature.Heading = 1111;

        sim.Tick();

        Assert.Equal(LairRules.MakingAHomeState, creature.Dk2StateId);
        Assert.False(LairRules.HasBed(world, db, creature));
        Assert.Equal(1452, creature.Heading);
        Assert.Equal(1, creature.Body.PathTotalCount);
    }

    [Fact]
    public void HomeTurnsBeforeCreatingTheBedAndTheMoverAlsoTurns()
    {
        var (db, world, creature) = Home(LairRules.MakingAHomeState);
        creature.Heading = 1452;
        creature.StateEnteredTick = -1;
        var sim = new GameSimulation(db, world, 1, [new CreatureUpdateSystem(
            new CreatureSleepSystem(), new CreatureJobSystem(), new CreatureMovementSystem())]);

        sim.Tick();

        Assert.Equal(LairRules.MakingAHomeState, creature.Dk2StateId);
        Assert.Equal(0, creature.Heading);
        Assert.False(LairRules.HasBed(world, db, creature));
        Assert.Equal(1, creature.Body.PathTotalCount);
        sim.Tick();
        Assert.True(LairRules.HasBed(world, db, creature));
        Assert.Equal(CreatureStateMachine.Idle, creature.Dk2StateId);
        Assert.Equal(0, creature.Heading);
        sim.Tick();
        Assert.Equal(CreatureJobSystem.IdleChooserState, creature.Dk2StateId);
    }

    [Fact]
    public void TheIdleHandoverAlsoSurvivesSeparateSystemSweeps()
    {
        var (db, world, creature) = Home(LairRules.MakingAHomeState);
        creature.Heading = 0;
        creature.StateEnteredTick = -1;
        var sim = new GameSimulation(db, world, 1, [new CreatureSleepSystem(), new CreatureJobSystem(), new CreatureMovementSystem()]);

        sim.Tick();

        Assert.True(LairRules.HasBed(world, db, creature));
        Assert.Equal(CreatureStateMachine.Idle, creature.Dk2StateId);
        Assert.False(creature.Body.PathFinished);
        sim.Tick();
        Assert.Equal(CreatureJobSystem.IdleChooserState, creature.Dk2StateId);
    }

    [Fact]
    public void AnIncompleteWalkKeepsTravelling()
    {
        var (db, world, creature) = Home(7);
        creature.UtilityNewState = LairRules.MakingAHomeState;
        Fixed16Vector target = creature.Body.Pos + new Fixed16Vector(Fixed16.One, 0);
        RouteWindow.Load(creature.Body, [target], target, 0);
        creature.Body.PathFinished = false;
        new GameSimulation(db, world, 1, [new CreatureSleepSystem()]).Tick();
        Assert.Equal(7, creature.Dk2StateId);
        Assert.False(LairRules.HasBed(world, db, creature));
    }

    [Fact]
    public void AStopOnTheHomeWalkRetainsTheQuantizedSelfRoute()
    {
        var (_, _, creature) = Home(7);
        creature.UtilityNewState = LairRules.MakingAHomeState;
        creature.PosX += 11;
        creature.PosY += 5;

        creature.StopMoving();

        Assert.Equal(1, creature.Body.PathTotalCount);
        Assert.Equal(new Fixed16Vector(creature.Body.Pos.X & ~0xFF, creature.Body.Pos.Y & ~0xFF),
            creature.Body.CurrentPoint);
        Assert.Equal(creature.Body.Pos, creature.Body.PathTargetPos);
    }

    private static (GameDatabase, WorldState, CreatureState) Home(int state)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(),
            [PlayerIds.Good, PlayerIds.Neutral, PlayerIds.FirstKeeper]);
        RoomRules.Place(db, ref world.Tile(2, 2), db.Room(RoomTypeId.Lair), PlayerIds.FirstKeeper);
        CreatureState creature = world.SpawnCreature(db, Dk2TerrainFixture.VampireCreatureId,
            PlayerIds.FirstKeeper, new TilePos(2, 2));
        StatePlacement.Place(creature, state);
        creature.Body.Pos = new Fixed16Vector(creature.PosX << 4, creature.PosY << 4);
        return (db, world, creature);
    }
}
