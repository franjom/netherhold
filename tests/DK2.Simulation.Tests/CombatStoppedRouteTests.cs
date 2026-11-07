// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Navigation;
using DK2.Simulation.Steering;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public sealed class CombatStoppedRouteTests
{
    private const int ApproachState = 8, FightState = 199;

    [Fact]
    public void FailedReacquisitionPreservesTheFinishedPointAndFacing()
    {
        var (db, world, caster, target) = Setup();
        target.OwnerPlayerId = caster.OwnerPlayerId;
        caster.FightTargetKind = FightTargetKind.None;
        caster.FightTargetId = CombatRules.NoTarget;
        Load(caster, finished: true);
        caster.Heading = 1612;

        new GameSimulation(db, world, 1, [new CombatSystem(), new CreatureMovementSystem()]).Tick();

        Assert.Equal(CreatureStateMachine.Idle, caster.Dk2StateId);
        Assert.Equal(1612, caster.Heading);
        Assert.Equal(1, caster.Body.PathTotalCount);
        Assert.True(caster.Body.PathFinished);
    }

    [Fact]
    public void AnApproachStopTurnsOnceBeforeTwoFacingTicksCanStartTheCast()
    {
        var (db, world, caster, _) = Setup();
        StatePlacement.Place(caster, ApproachState);
        caster.UtilityNewState = FightState;
        caster.Heading = 1941;
        new GameSimulation(db, world, 1,
            [new RunRule(c => ApproachRules.End(caster, "in_reach", c)), new CreatureMovementSystem()]).Tick();

        Assert.Equal(0, caster.Heading);
        Assert.Equal(1, caster.Body.PathTotalCount);
        Assert.True(caster.Body.PathFinished);
        var sim = new GameSimulation(db, world, 1, [new CombatSystem(), new CreatureMovementSystem()]);
        sim.Tick();
        Assert.Equal(1707, caster.Heading);
        Assert.False(caster.SwingInProgress);
        sim.Tick();
        Assert.Equal(1536, caster.Heading);
        Assert.True(caster.SwingInProgress);
        Assert.Equal(2, caster.SwingStartTick);
    }

    [Fact]
    public void AWaitingSupportKeepsItsFinishedPointWhileTurning()
    {
        var (db, world, caster, _) = Setup();
        Load(caster, finished: true);
        caster.Heading = 0;
        Fixed16Vector point = caster.Body.PathTargetPos;

        new GameSimulation(db, world, 1, [new CombatSystem(), new CreatureMovementSystem()]).Tick();

        Assert.Equal(1707, caster.Heading);
        Assert.False(caster.SwingInProgress);
        Assert.Equal(point, caster.Body.PathTargetPos);
        Assert.Equal(1, caster.Body.PathTotalCount);
        Assert.True(caster.Body.PathFinished);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BeginningAnAttackPreservesTheLoadedRouteAndVelocity(bool spell)
    {
        var (db, world, caster, _) = Setup();
        Load(caster, finished: false);
        Fixed16Vector point = caster.Body.PathTargetPos;
        caster.Body.Vel = new Fixed16Vector(1234, -17);
        var velocity = caster.Body.Vel;

        new GameSimulation(db, world, 1, [new RunRule(c =>
        {
            if (spell) FightRules.StartCast(c, caster, db.CreatureById(caster.CreatureTypeId), 0);
            else FightRules.StartSwing(c, new WorldSight(c.Navigation, world, db), caster,
                db.CreatureById(caster.CreatureTypeId), []);
        })]).Tick();

        Assert.True(caster.SwingInProgress);
        Assert.Equal(point, caster.Body.PathTargetPos);
        Assert.Equal(1, caster.Body.PathTotalCount);
        Assert.False(caster.Body.PathFinished);
        Assert.Equal(velocity, caster.Body.Vel);
    }

    [Fact]
    public void SupportReacquisitionPreservesAnExistingRoute()
    {
        var (db, world, caster, target) = Setup();
        Load(caster, finished: false);
        Fixed16Vector point = caster.Body.PathTargetPos;

        new GameSimulation(db, world, 1, [new RunRule(c =>
            Assert.True(FightRules.Reacquire(c, new WorldSight(c.Navigation, world, db),
                new HeartSites(), new RoomCache(), caster, db.CreatureById(caster.CreatureTypeId), [])))]).Tick();

        Assert.Equal(target.Id, caster.FightTargetId);
        Assert.Equal(point, caster.Body.PathTargetPos);
        Assert.Equal(1, caster.Body.PathTotalCount);
        Assert.False(caster.Body.PathFinished);
    }

    [Theory]
    [InlineData(Dk2TerrainFixture.ImpCreatureId, FightState)]
    [InlineData(Dk2TerrainFixture.GoblinCreatureId, CreatureStateMachine.Idle)]
    [InlineData(Dk2TerrainFixture.GoblinCreatureId, 259)]
    [InlineData(Dk2TerrainFixture.GoblinCreatureId, 260)]
    public void OtherCallerFamiliesRetainTheirEmptyRouteStopAdapter(byte type, int state)
    {
        var (db, world, _, _) = Setup();
        var creature = world.SpawnCreature(db,
            type,
            PlayerIds.FirstKeeper, new TilePos(10, 10));
        StatePlacement.Place(creature, state);
        creature.StopMoving();
        Assert.Equal(0, creature.Body.PathTotalCount);
    }

    private static void Load(CreatureState caster, bool finished)
    {
        caster.Body.Pos = new Fixed16Vector(caster.PosX << 4, caster.PosY << 4);
        Fixed16Vector point = caster.Body.Pos + new Fixed16Vector(finished ? 0 : Fixed16.One, 0);
        RouteWindow.Load(caster.Body, [point], point, 0);
        caster.Body.PathFinished = finished;
    }

    private static (GameDatabase, WorldState, CreatureState, CreatureState) Setup()
    {
        GameDatabase basis = Dk2TerrainFixture.Database();
        var db = new GameDatabase(basis.Terrain.Values, basis.Rooms.Values,
            basis.Creatures.Values.Select(c => c.CreatureId == Dk2TerrainFixture.WarlockCreatureId
                ? c with { TurnRate = 1365 } : c), basis.Variables,
            creatureSpells: basis.CreatureSpells.Values, shots: basis.Shots.Values);
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20),
            [(byte)0, PlayerIds.Good, PlayerIds.Neutral, PlayerIds.FirstKeeper, (byte)(PlayerIds.FirstKeeper + 1)]);
        for (int y = 0; y < world.Height; y++)
        for (int x = 0; x < world.Width; x++)
            world.Tile(new TilePos(x, y)).TerrainId = Dk2TerrainFixture.DirtPath;
        CreatureState caster = world.SpawnCreature(db, Dk2TerrainFixture.WarlockCreatureId,
            PlayerIds.FirstKeeper, new TilePos(8, 10));
        CreatureState target = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId,
            PlayerIds.FirstKeeper + 1, new TilePos(5, 10));
        target.FrozenTicks = 1000;
        StatePlacement.As(target, CreatureActivity.Frozen);
        StatePlacement.Place(caster, FightState);
        caster.FightTargetKind = FightTargetKind.Creature;
        caster.FightTargetId = target.Id;
        caster.AttackType = CombatRules.AttackMode.SpellAtCreature;
        caster.AttackSpellSlot = 0;
        caster.AttackRangeSquared = SpellRules.RangeSquared(db.CreatureSpells[Dk2TerrainFixture.FireballSpellId]);
        Assert.True(CombatRules.Register(target, caster.Id, ranged: true));
        return (db, world, caster, target);
    }

    private sealed class RunRule(Action<SimulationContext> rule) : ISimulationSystem
    {
        public string Name => nameof(RunRule);
        public void Execute(SimulationContext context) => rule(context);
    }
}
