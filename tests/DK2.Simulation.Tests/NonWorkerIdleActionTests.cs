// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;
using DK2.Simulation.Combat;

namespace DK2.Simulation.Tests;

public class NonWorkerIdleActionTests
{
    private static (GameSimulation Sim, CreatureState Creature, GameDatabase Db, ListEventSink Sink) Start(
        int lowerRoll = 75, bool artPresent = true, bool separateHosts = false,
        ICreatureUpdateStep? observer = null, Action<CreatureAnimationTiming[]>? shapeArt = null,
        TriggerTree? triggers = null, ICreatureUpdateStep[]? orderedSteps = null)
    {
        var slots = new CreatureAnimationTiming[CreatureDef.ArtSlotCount];
        if (artPresent)
            slots[20] = slots[31] = new CreatureAnimationTiming
            { ResourceType = 6, FrameCount = 100, FramesPerSecond = 20 };
        shapeArt?.Invoke(slots);
        CreatureDef goblin = Dk2TerrainFixture.Goblin() with { CreatureId = 54, ArtSlots = slots };
        GameDatabase db = Dk2TerrainFixture.Database(goblin);
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [0, PlayerIds.Neutral, PlayerIds.FirstKeeper]);
        CreatureState c = world.SpawnCreature(db, goblin.CreatureId, PlayerIds.Neutral, new TilePos(2, 2));
        StatePlacement.Place(c, CreatureJobSystem.NeutralIdleState);
        c.WanderStamp = 1;
        uint seed = 1;
        while (new Dk2Lcg(seed).NextInt(100) is var roll && (roll < lowerRoll || roll >= lowerRoll + 25)) seed++;
        var rng = new Dk2Lcg(seed);
        ISimulationSystem[] systems = orderedSteps is not null ? [new CreatureUpdateSystem(orderedSteps)] : separateHosts
            ? [new CreatureJobSystem(), new CreatureMovementSystem()]
            : observer is null ? [new CreatureJobSystem()]
                : [new CreatureUpdateSystem(observer, new CreatureJobSystem())];
        if (triggers is not null) systems = [.. systems, new ActionPointTriggerSystem(), new PlayerTriggerSystem()];
        var sim = new GameSimulation(db, world, new SimRandom(rng), systems, triggers);
        rng.Reseed(seed);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        return (sim, c, db, sink);
    }

    [Theory]
    [InlineData(25, 20, 43)]
    [InlineData(75, 31, 44)]
    public void AnIdleChoiceStartsItsCountedClip(int lowerRoll, byte slot, int action)
    {
        var (sim, c, db, _) = Start(lowerRoll);
        sim.Tick();
        Assert.Equal(slot, c.ActionArtSlot);
        Assert.Equal(204, c.ActionAnimationRate);
        Assert.Equal(204, c.ActionAnimationPhase);
        Assert.Equal(2, c.ActionCount);
        Assert.Equal(action, CreatureCurrentAction.IndexOf(c, db.CreatureById(c.CreatureTypeId), sim.CurrentTick));
        Assert.Equal(0, c.ActionUntilTick);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARunningClipStillBlocksWanderingAfterEightTicks(bool separateHosts)
    {
        var (sim, c, _, _) = Start(separateHosts: separateHosts);
        sim.Tick();
        uint seed = ((Dk2Lcg)sim.Rng.Algorithm).Seed;
        sim.Run(12);
        Assert.Equal(ImpStates.Wandering, c.Dk2StateId);
        Assert.Equal(13 * 204, c.ActionAnimationPhase);
        Assert.Equal(seed, ((Dk2Lcg)sim.Rng.Algorithm).Seed);
        Assert.True(CreatureCurrentAction.IsRunning(c, sim.CurrentTick));
    }

    [Fact]
    public void AnUnavailableIdleFallsThroughToWanderingWithoutStartingAnAction()
    {
        var (sim, c, _, sink) = Start(artPresent: false);
        sim.Tick();
        Assert.Equal(ImpStates.Wandering, c.Dk2StateId);
        Assert.Equal(132, c.WanderStamp);
        Assert.Equal(0, c.ActionUntilTick);
        Assert.DoesNotContain(sink.Events, e => e.Kind == "idle_action");
    }

    [Fact]
    public void ATravelHelperCancelsTheIdleAction()
    {
        var (sim, c, db, _) = Start();
        sim.Tick();
        CreatureStateMachine.BeginTravel(new StateHookContext(sim.World, db), c, 6, 1);
        Assert.False(CreatureCurrentAction.IsRunning(c, sim.CurrentTick));
        Assert.Equal(0, c.ActionArtSlot);
        Assert.Equal(0, c.ActionCount);
    }

    [Fact]
    public void SaveAndRestoreKeepTheIdleClockAndItsFutureDraws()
    {
        var (sim, c, db, _) = Start();
        sim.Run(9);
        Assert.True(CreatureCurrentAction.IsRunning(c, sim.CurrentTick));
        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db, [new CreatureJobSystem()]);
        Assert.Equal(sim.StateHash(), restored.StateHash());
        for (int i = 0; i < 25; i++)
        {
            sim.Tick(); restored.Tick();
            Assert.Equal(sim.StateHash(), restored.StateHash());
        }
    }

    [Fact]
    public void TheRunnerClearsACompletedActionBeforeTheFirstStateStep()
    {
        var observer = new ActionObserver();
        var (sim, c, _, _) = Start(observer: observer);
        sim.Tick();
        Assert.Equal(IdleActionId.Second, c.IdleAction);
        c.ActionAnimationRate = 0;
        c.ActionAnimationFlags = ActionAnimationFlags.Complete;
        observer.Busy = null;

        sim.Tick();

        Assert.False(observer.Busy);
        Assert.Equal(IdleActionId.None, c.IdleAction);
    }

    [Theory]
    [InlineData(25, 20, 1, 43)]
    [InlineData(75, 31, 20, 44)]
    public void AResolvedFallbackKeepsTheRequestedSlotAndActionIdentity(int roll, byte requested, byte resolved, int action)
    {
        var (sim, c, db, _) = Start(roll, shapeArt: slots =>
        {
            slots[resolved] = slots[requested];
            slots[requested] = default;
        });
        sim.Tick();
        Assert.Equal(requested, c.ActionArtSlot);
        Assert.Equal(204, c.ActionAnimationRate);
        Assert.Equal(action, CreatureCurrentAction.IndexOf(c, db.CreatureById(c.CreatureTypeId), sim.CurrentTick));
    }

    [Fact]
    public void ChangingTheRequestedSlotReinstallsEvenWhenTheFallbackResourceIsTheSame()
    {
        var (sim, _, db, _) = Start(shapeArt: slots => slots[31] = default);
        CreatureDef definition = db.CreatureById(54);
        var c = new CreatureState { ActionArtSlot = 20, ActionAnimationPhase = 1234, ActionAnimationRate = 204 };
        ActionAnimationClock.BeginCounted(c, definition, 31, 1, sim.Rng);
        Assert.Equal(31, c.ActionArtSlot);
        Assert.Equal(204, c.ActionAnimationRate);
        Assert.Equal(0, c.ActionAnimationPhase);
    }

    [Fact]
    public void RepeatingTheSameRequestedFallbackSlotKeepsItsPhase()
    {
        var (sim, _, db, _) = Start(shapeArt: slots => slots[31] = default);
        var c = new CreatureState { ActionArtSlot = 31, ActionAnimationPhase = 1234, ActionAnimationRate = 204 };
        ActionAnimationClock.BeginCounted(c, db.CreatureById(54), 31, 1, sim.Rng);
        Assert.Equal(1234, c.ActionAnimationPhase);
        Assert.Equal(2, c.ActionCount);
    }

    [Fact]
    public void PresentArtWithNoTimingEndsOnTheNextPoll()
    {
        var (sim, c, _, _) = Start(shapeArt: slots => slots[31] = slots[31] with { FrameCount = 0 });
        sim.Tick();
        Assert.True(CreatureCurrentAction.IsRunning(c, sim.CurrentTick));
        Assert.Equal(0, c.ActionAnimationRate);
        sim.Tick();
        Assert.Equal(IdleActionId.None, c.IdleAction);
    }

    [Fact]
    public void TheActionIdentityChangesTheHashEvenWithTheSameClip()
    {
        var (sim, c, _, _) = Start();
        sim.Tick();
        ulong second = sim.StateHash();
        c.IdleAction = IdleActionId.First;
        Assert.NotEqual(second, sim.StateHash());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StatusReplacementCancelsAnIdleAction(bool freeze)
    {
        var replacement = new ActionObserver
        {
            BeforeObserve = (context, c) =>
            {
                if (c.IdleAction == IdleActionId.None) return;
                CreatureDef definition = context.Database.CreatureById(c.CreatureTypeId);
                Assert.True(freeze ? StatusEffectRules.Freeze(context, c, definition, 40)
                    : StatusEffectRules.StunOnLanding(context, c, definition));
            },
        };
        var (sim, c, _, _) = Start(observer: replacement);
        sim.Tick();
        Assert.Equal(IdleActionId.Second, c.IdleAction);

        sim.Tick();

        Assert.Equal(IdleActionId.None, c.IdleAction);
        Assert.Equal(0, c.ActionArtSlot);
    }

    [Fact]
    public void ActionPollingFollowsTimedEffectsAndPrecedesCombat()
    {
        var before = new ActionObserver();
        var after = new ActionObserver();
        var combat = new CombatSystem();
        var (sim, c, _, _) = Start(orderedSteps:
            [new CreatureStatusSystem(), before, combat, after, combat.States, new CreatureJobSystem()]);
        sim.Tick();
        c.ActionAnimationRate = 0;
        before.Busy = after.Busy = null;

        sim.Tick();

        Assert.True(before.Busy);
        Assert.False(after.Busy);
    }

    [Fact]
    public void CreatureTriggersReadTheClipBeforeItsAdvance()
    {
        CreatureState? target = null;
        var phases = new List<int>();
        var parameters = new ObservedParameters(() => phases.Add(target?.ActionAnimationPhase ?? -1));
        var condition = new GenericTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = 1, Type = 34, RepeatCount = 255, ChildTriggerId = ushort.MaxValue,
            Parameters = new Dictionary<string, long> { ["Comparison"] = 5, ["Value"] = 0 },
        };
        var action = new ActionTriggerRecord(new byte[16])
        { Body = new byte[8], TriggerId = ushort.MaxValue, Type = 7, Parameters = parameters };
        var (sim, c, _, _) = Start(triggers: new TriggerTree([condition, action], 0),
            orderedSteps: [new CreatureJobSystem()]);
        target = c;
        c.TriggerId = 1;
        c.TriggerRepeat = 255;
        phases.Clear();

        sim.Tick();

        Assert.NotEmpty(phases);
        Assert.All(phases, phase => Assert.Equal(0, phase));
        Assert.Equal(204, c.ActionAnimationPhase);
    }

    private sealed class ObservedParameters(Action observe) : IReadOnlyDictionary<string, long>
    {
        private readonly Dictionary<string, long> _values = new()
        { ["FlagId"] = 0, ["Flag"] = 10, ["Value"] = 1 };
        public long this[string key] { get { observe(); return _values[key]; } }
        public int Count => _values.Count;
        public IEnumerable<string> Keys => _values.Keys;
        public IEnumerable<long> Values => _values.Values;
        public bool ContainsKey(string key) => _values.ContainsKey(key);
        public bool TryGetValue(string key, out long value) { observe(); return _values.TryGetValue(key, out value); }
        public IEnumerator<KeyValuePair<string, long>> GetEnumerator() => _values.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Fact]
    public void ACountOfTwoKeepsTheAnimationLoopEnabled()
    {
        var (sim, _, db, _) = Start();
        var c = new CreatureState();
        ActionAnimationClock.BeginCounted(c, db.CreatureById(54), 31, 2, sim.Rng);
        Assert.Equal(3, c.ActionCount);
        Assert.True((c.ActionAnimationFlags & ActionAnimationFlags.Loop) != 0);
    }

    [Theory]
    [InlineData(128, 129)]
    [InlineData(255, 0)]
    public void AHighBitCycleCountUsesTheSignedByteGateAndWraps(byte plays, byte stored)
    {
        var (sim, _, db, _) = Start();
        var c = new CreatureState();
        ActionAnimationClock.BeginCounted(c, db.CreatureById(54), 31, plays, sim.Rng);
        Assert.Equal(stored, c.ActionCount);
        Assert.False((c.ActionAnimationFlags & ActionAnimationFlags.Loop) != 0);
    }

    private sealed class ActionObserver : ICreatureUpdateStep
    {
        public string Name => "IdleActionObserver";
        public bool? Busy { get; set; }
        public Action<SimulationContext, CreatureState>? BeforeObserve { get; init; }
        public void Execute(SimulationContext context) => CreatureUpdateSystem.RunAlone(context, this);
        public void Update(SimulationContext context, CreatureState creature)
        {
            BeforeObserve?.Invoke(context, creature);
            Busy = CreatureCurrentAction.IsRunning(creature, context.Tick);
        }
    }

    [Theory]
    [InlineData(1, ActionAnimationFlags.Wrapped, 1, ActionAnimationFlags.Wrapped)]
    [InlineData(1, ActionAnimationFlags.Complete, 255, ActionAnimationFlags.Complete)]
    [InlineData(3, ActionAnimationFlags.Loop | ActionAnimationFlags.Wrapped, 2, ActionAnimationFlags.Loop)]
    public void CountedPollingPreservesTheNativeCycleBranches(
        byte count, ActionAnimationFlags flags, byte expectedCount, ActionAnimationFlags expectedFlags)
    {
        var c = new CreatureState { ActionCount = count, ActionAnimationRate = 100, ActionAnimationFlags = flags };
        Assert.False(ActionAnimationClock.CountedActionEnded(c));
        Assert.Equal(expectedCount, c.ActionCount);
        Assert.Equal(expectedFlags, c.ActionAnimationFlags);
    }
}
