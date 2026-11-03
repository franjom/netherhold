// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public sealed class CombatExperienceOrderTests
{
    // 00476FC0 calls the category at 004770E0 before the handler at 004772AA.
    [Theory]
    [InlineData(199)]
    [InlineData(200)]
    public void TheExitHandlerSeesItsFinalCategoryAward(int state)
    {
        var (db, world, fighter, target) = Setup();
        Fight(fighter, target, state);
        var sim = WithHandler(db, world, (context, creature) =>
        {
            if (creature != fighter) return;
            Assert.Equal(40, fighter.ExperiencePoints);
            Assert.True(CreatureStateMachine.TrySetState(context.Hooks, fighter, 1));
        });
        sim.Tick();
        Assert.Equal(1, fighter.Dk2StateId);
        Assert.Equal(40, fighter.ExperiencePoints);
    }

    // 00489AC7 acquisition precedes 00476FC0, so a newly acquired 199 earns now.
    [Fact]
    public void AcquisitionPaysBeforeTheFirstFightHandler()
    {
        var (db, world, fighter, target) = Setup();
        fighter.FightProcessCounter = 10;
        var sim = WithHandler(db, world, (_, creature) =>
        {
            if (creature != fighter) return;
            Assert.Equal(199, fighter.Dk2StateId);
            Assert.Equal(target.Id, fighter.FightTargetId);
            Assert.Equal(40, fighter.ExperiencePoints);
        });
        sim.Tick();
        Assert.Equal(40, fighter.ExperiencePoints);
    }

    // 0047A870 rejects state8 even when its handler resumes199 in this update.
    [Fact]
    public void AnApproachArrivalDoesNotBackdateItsFirstFightAward()
    {
        var (db, world, fighter, target) = Setup();
        Fight(fighter, target, 8);
        fighter.StateBeforeUtilityState = 199;
        fighter.UtilityNewState = 199;
        var sim = WithHandler(db, world, (context, creature) =>
        {
            if (creature != fighter) return;
            Assert.Equal(8, fighter.Dk2StateId);
            Assert.Equal(0, fighter.ExperiencePoints);
            Assert.True(CreatureStateMachine.TrySetState(context.Hooks, fighter, 199));
        });
        sim.Tick();
        Assert.Equal(199, fighter.Dk2StateId);
        Assert.Equal(0, fighter.ExperiencePoints);
    }

    // 0047A870 -> 00457880 promotes before the same creature's handler can use its level.
    [Fact]
    public void PromotionIsVisibleToTheCurrentHandler()
    {
        var (db, world, fighter, target) = Setup();
        Fight(fighter, target, 199);
        fighter.ExperiencePoints = CreatureLevelRules.ExperienceForNextLevel(db, db.CreatureById(61), 1) - 40;
        var sim = WithHandler(db, world, (_, creature) =>
        {
            if (creature != fighter) return;
            Assert.Equal(2, fighter.Level);
            Assert.Equal(0, fighter.ExperiencePoints);
        });
        sim.Tick();
        Assert.Equal(2, fighter.Level);
    }

    // 004B7830 completes the newer creature, including category XP, before the older one.
    [Fact]
    public void AnOlderCreaturesHandlerSeesTheNewerCreaturesAward()
    {
        var (db, world, older, newer) = Setup();
        Fight(newer, older, 199);
        newer.Possessed = false;
        var visited = new List<int>();
        var sim = WithHandler(db, world, (_, creature) =>
        {
            visited.Add(creature.Id);
            if (creature == older) Assert.Equal(40, newer.ExperiencePoints);
        });
        sim.Tick();
        Assert.Equal(new[] { newer.Id, older.Id }, visited);
    }

    [Fact]
    public void TheCreatureTargetGateIsStillRequired()
    {
        var (db, world, fighter, target) = Setup();
        Fight(fighter, target, 199);
        fighter.FightTargetKind = FightTargetKind.Heart;
        WithHandler(db, world, (_, _) => { }).Tick();
        Assert.Equal(0, fighter.ExperiencePoints);
    }

    [Fact]
    public void PossessionStillSkipsCategoryExperience()
    {
        var (db, world, fighter, target) = Setup();
        Fight(fighter, target, 199);
        fighter.Possessed = true;
        WithHandler(db, world, (_, _) => { }).Tick();
        Assert.Equal(0, fighter.ExperiencePoints);
    }

    private static void Fight(CreatureState fighter, CreatureState target, int state)
    {
        fighter.Dk2StateId = state;
        fighter.FightTargetKind = FightTargetKind.Creature;
        fighter.FightTargetId = target.Id;
    }

    private static (GameDatabase, WorldState, CreatureState, CreatureState) Setup()
    {
        CreatureDef definition = Dk2TerrainFixture.Goblin() with
        {
            CreatureId = 61, Name = "Category experience fixture", ExpPerSecond = 160,
            Flags = CreatureFlags.IsEvil | CreatureFlags.IsFearless,
        };
        GameDatabase db = Dk2TerrainFixture.Database(definition);
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(),
            [(byte)0, PlayerIds.Good, PlayerIds.Neutral, (byte)3]);
        CreatureState fighter = world.SpawnCreature(db, 61, 3, new TilePos(2, 2));
        CreatureState target = world.SpawnCreature(db, 61, PlayerIds.Good, new TilePos(3, 2));
        target.Possessed = true;
        return (db, world, fighter, target);
    }

    private static GameSimulation WithHandler(GameDatabase db, WorldState world,
        Action<SimulationContext, CreatureState> handler)
    {
        ISimulationSystem[] declared = SimulationSystems.Default();
        var update = declared.OfType<CreatureUpdateSystem>().Single();
        // Keep the actual default pre-dispatch steps and any category award there; replace
        // state handlers with a controlled callback. Retain an old global award if present.
        ICreatureUpdateStep[] prefix = update.Steps.TakeWhile(s => s is not TravelArrivalSystem).ToArray();
        ISimulationSystem[] systems = [new CreatureUpdateSystem([.. prefix, new Handler(handler)]),
            .. declared.OfType<CreatureExperienceSystem>()];
        return new GameSimulation(db, world, 42, systems);
    }

    private sealed class Handler(Action<SimulationContext, CreatureState> run) : ICreatureUpdateStep
    {
        public string Name => "controlled state handler";
        public void Execute(SimulationContext context) => CreatureUpdateSystem.RunAlone(context, this);
        public void Update(SimulationContext context, CreatureState creature) => run(context, creature);
    }
}
