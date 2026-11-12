// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Steering;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public sealed class ImpWithdrawalArrivalTests
{
    private const int WithdrawnState = 202;
    [Fact]
    public void LandingResumesWithdrawalBeforeTheFollowingIdleAndSelectionTurns()
    {
        var (db, world, imp) = Setup();
        Walk(imp, WithdrawnState, remaining: 1024);
        imp.TimerTicks = 2;
        var combat = new CombatSystem();
        var workers = new ImpTaskSystem();
        var sim = new GameSimulation(db, world, 1,
            [new CreatureUpdateSystem(combat.States, new CreatureMovementSystem(), workers)]);

        sim.Tick();
        Assert.Equal((WithdrawnState, CreatureStateMachine.Idle), (imp.Dk2StateId, imp.UtilityNewState));
        Assert.Equal(0, imp.Body.PathTotalCount);
        Assert.Equal(Fixed16Vector.Zero, imp.Body.Vel);
        sim.Tick();
        Assert.Equal(CreatureStateMachine.Idle, imp.Dk2StateId);
        sim.Tick();
        Assert.Equal(ImpStates.TaskSelection, imp.Dk2StateId);
        sim.Tick();
        Assert.Equal(ImpStates.Resting, imp.Dk2StateId);
    }

    /// <summary>State 6 tests an Imp's progress after integrating it (<c>0x00470C80</c>), for any
    /// resume: the alarm walk (251) ends on the turn of its last step. Level 5, Imp 446 on turn
    /// 1803: stopped on its point and in 251 that turn.</summary>
    [Fact]
    public void AnImpsAlarmWalkEndsOnTheTurnOfItsLastStep()
    {
        const int resume = 251;
        var (db, world, imp) = Setup();
        Walk(imp, resume, remaining: 1024);
        var combat = new CombatSystem();

        new GameSimulation(db, world, 1,
            [new CreatureUpdateSystem(combat.States, new CreatureMovementSystem())]).Tick();

        Assert.Equal((resume, CreatureStateMachine.Idle), (imp.Dk2StateId, imp.UtilityNewState));
        Assert.Equal(Fixed16Vector.Zero, imp.Body.Vel);
    }

    [Theory]
    [InlineData(6, 202, 32768)]
    [InlineData(6, 25, 1024)]
    public void OtherWalksDoNotTakeTheWithdrawalHandover(int state, int resume, int remaining)
    {
        var (db, world, imp) = Setup();
        Walk(imp, resume, remaining);
        var combat = new CombatSystem();

        new GameSimulation(db, world, 1,
            [new CreatureUpdateSystem(combat.States, new CreatureMovementSystem())]).Tick();

        Assert.Equal((state, resume), (imp.Dk2StateId, imp.UtilityNewState));
    }

    [Theory]
    [InlineData(1, 10)]
    [InlineData(10, 12)]
    public void IdleAndSelectionIgnoreAnEarlierWorkCountdown(int state, int expected)
    {
        var (db, world, imp) = Setup();
        StatePlacement.Place(imp, state);
        imp.TimerTicks = 100;

        new GameSimulation(db, world, 1, [new ImpTaskSystem()]).Tick();

        Assert.Equal(expected, imp.Dk2StateId);
    }

    private static (GameDatabase, WorldState, CreatureState) Setup()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20),
            [PlayerIds.Good, PlayerIds.Neutral, PlayerIds.FirstKeeper]);
        for (int y = 1; y < world.Height - 1; y++)
        for (int x = 1; x < world.Width - 1; x++)
        {
            world.Tile(x, y).TerrainId = Dk2TerrainFixture.ClaimedPath;
            world.Tile(x, y).OwnerPlayerId = PlayerIds.FirstKeeper;
        }
        return (db, world, world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId,
            PlayerIds.FirstKeeper, new TilePos(7, 6)));
    }

    private static void Walk(CreatureState creature, int resume, int remaining)
    {
        StatePlacement.Place(creature, 6);
        creature.UtilityNewState = resume;
        creature.Body.Pos = new Fixed16Vector(creature.PosX << 4, creature.PosY << 4);
        Fixed16Vector point = creature.Body.Pos + new Fixed16Vector(remaining, 0);
        RouteWindow.Load(creature.Body, [point], point, 0);
    }
}
