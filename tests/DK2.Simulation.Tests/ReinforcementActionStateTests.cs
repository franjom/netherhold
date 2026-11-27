// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public sealed class ReinforcementActionStateTests
{
    [Fact]
    public void AnotherCreaturesArt12IsNotAReinforcementAction()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        var world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)3]);
        CreatureState creature = world.SpawnCreature(db, Dk2TerrainFixture.Goblin().CreatureId, 3, new TilePos(3, 3));
        creature.ActionArtSlot = 12;
        creature.ActionAnimationPhase = 123;
        var sim = new GameSimulation(db, world, 99, [new ImpTaskSystem()]);
        sim.Tick();
        Assert.Equal(12, creature.ActionArtSlot);
        Assert.Equal(123, creature.ActionAnimationPhase);
    }
    [Fact]
    public void AReplacementPossessedActionCancelsTheIndependentWorkPoint()
    {
        GameSimulation sim = ReinforcementActionTests.Begin(ActionAnimationFlags.Loop);
        CreatureState imp = sim.World.Creatures[0];
        imp.Possessed = true;
        var commands = (List<ISimulationSystem>)sim.Systems;
        commands[0] = new Once(commands[0], context => FightRules.BeginPossessedAction(context, imp,
            SpellRules.MeleeSwing, 0, FightTargetKind.None, CombatRules.NoTarget));
        sim.Tick();
        Assert.False(imp.ReinforcementAction.Active);
        Assert.Equal(0, imp.ExperiencePoints);
    }

    [Fact]
    public void AReplacementArtActionCancelsTheIndependentWorkPoint()
    {
        GameSimulation sim = ReinforcementActionTests.Begin(ActionAnimationFlags.Loop);
        CreatureState imp = sim.World.Creatures[0];
        var commands = (List<ISimulationSystem>)sim.Systems;
        commands[0] = new Once(commands[0], context =>
        {
            ActionAnimationClock.BeginCounted(imp, context.Database.CreatureById(imp.CreatureTypeId), 6, 1, context.Rng);
            imp.ActionUntilTick = 100;
        });
        sim.Tick();
        Assert.Equal(0, imp.ExperiencePoints);
        Assert.Equal(0, imp.ReinforcementAction.Target.X);
        Assert.False(imp.ReinforcementAction.Active);
    }

    [Fact]
    public void State22DoesNotReplaceAnUnrelatedBusyAction()
    {
        GameSimulation sim = ReinforcementActionTests.Start();
        CreatureState imp = sim.World.Creatures[0];
        imp.ActionUntilTick = 100;
        sim.Run(4);
        Assert.False(imp.ReinforcementAction.Active);
        Assert.Equal(0, imp.ExperiencePoints);
        Assert.Equal(100, imp.ActionUntilTick);
    }

    [Fact]
    public void StickyAnimationCompletionSurvivesASaveWhileTheHandlerRemainsBusy()
    {
        GameSimulation original = ReinforcementActionTests.Begin(ActionAnimationFlags.Loop);
        CreatureState imp = original.World.Creatures[0];
        imp.ActionAnimationRate = 0;
        original.World.Tile(4, 3).Health = 659;
        original.Tick();
        Assert.True(imp.ReinforcementAction.AnimationComplete);
        Assert.False(imp.ActionHandlerDone);
        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(original), original.Database, [new ImpTaskSystem()]);
        Assert.Equal(original.StateHash(), restored.StateHash());
        original.Run(3);
        restored.Run(3);
        Assert.Equal(original.StateHash(), restored.StateHash());
        Assert.Equal(22, restored.World.Creatures[0].Dk2StateId);
    }

    [Fact]
    public void CountOneKeepsTheStickyCompletionGateUntilTheAnimationRateStops()
    {
        GameSimulation sim = ReinforcementActionTests.Begin(ActionAnimationFlags.Loop | ActionAnimationFlags.Wrapped);
        CreatureState imp = sim.World.Creatures[0];
        imp.ActionCount = 2;
        sim.Tick();
        Assert.True(imp.ReinforcementAction.Active);
        Assert.False(imp.ReinforcementAction.AnimationComplete);
        Assert.Equal(1, imp.ActionCount);
        imp.ActionAnimationFlags |= ActionAnimationFlags.Complete;
        sim.Tick();
        Assert.Equal(byte.MaxValue, imp.ActionCount);
        Assert.True(imp.ReinforcementAction.Active);
        Assert.False(imp.ReinforcementAction.AnimationComplete);
        sim.Tick();
        Assert.False(imp.ReinforcementAction.AnimationComplete);
        Assert.False(imp.ActionHandlerDone);
    }

    [Fact]
    public void GoldPickupPrecedesTheReinforcementApplication()
    {
        GameSimulation sim = ReinforcementActionTests.Start(defaultCreaturePass: true);
        sim.Run(2);
        var events = new ListEventSink();
        sim.ObserveWith(events);
        CreatureState imp = sim.World.Creatures[0];
        ObjectState pile = sim.World.SpawnObject(sim.Database, FloorGold.FloorGoldObjectId, PlayerIds.Neutral, 3 * sim.World.Width + 3);
        pile.GoldHeld = 100;
        pile.Cell = MiningSlots.AnyCell;
        sim.World.Tile(4, 3).Health = 659;
        sim.Tick();
        SimEvent[] sequence = [.. events.Events];
        int pickup = Array.FindIndex(sequence, e => e.Kind == "gold_picked_up_in_passing");
        int work = Array.FindIndex(sequence, e => e.System == "TileWork");
        Assert.True(pickup >= 0);
        Assert.True(work > pickup);
        Assert.Equal(100, imp.GoldHeld);
    }

    private sealed class Once : ISimulationSystem
    {
        private readonly ISimulationSystem _next;
        private readonly Action<SimulationContext> _apply;

        public Once(ISimulationSystem next, Action<SimulationContext> apply)
        {
            _next = next;
            _apply = apply;
        }

        private bool _ran;
        public string Name
        {
            get
            {
                return "action replacement fixture";
            }
        }

        public void Execute(SimulationContext context)
        {
            if (!_ran)
            {
                _ran = true;
                _apply(context);
            }
            _next.Execute(context);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void EveryPersistentActionFieldChangesTheHash(int field)
    {
        GameSimulation sim = TestWorld.Simulation(seed: 17);
        CreatureState creature = sim.World.Creatures[0];
        ulong before = sim.StateHash();
        switch (field)
        {
            case 0: creature.ReinforcementAction.Active = true; break;
            case 1: creature.ReinforcementAction.Target = new TilePos(1, 0); break;
            case 2: creature.ReinforcementAction.Target = new TilePos(0, 1); break;
            case 3: creature.ReinforcementAction.AnimationComplete = true; break;
        }
        Assert.NotEqual(before, sim.StateHash());
    }
}
