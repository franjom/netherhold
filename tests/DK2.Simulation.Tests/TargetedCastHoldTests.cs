// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Systems;
using DK2.Simulation.Steering;

namespace DK2.Simulation.Tests;

public sealed class TargetedCastHoldTests
{
    // 0045FB92 starts action16 count1; 0049558D/94 selects art6/mode3.
    // 00498870 finishes the shot handler; 00496BD3 waits for both completion bits.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReleasedShotKeepsTheFighterBusyThroughTheClipAndSnapshot(bool fallback)
    {
        var (db, world, caster, target, sim) = Setup(fallback);
        CreatureDef definition = db.CreatureById(caster.CreatureTypeId);
        int hold = AnimationTimingRules.CountedHoldTicks(definition, 6, 1);
        int fire = SpellRules.DelayTicks(definition, 0) + 1;
        Assert.True(hold > fire + 1);
        sim.Run(fire + 1);
        Assert.False(caster.SwingInProgress);
        Assert.Single(world.Shots);
        target.PosX = CreatureState.CentreOf(35);

        GameSimulation copy = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db, [new CombatSystem()]);
        while (world.CurrentTick.Value < hold)
        {
            sim.Tick(); copy.Tick();
            Assert.Equal(199, caster.Dk2StateId);
            Assert.Equal(target.Id, caster.FightTargetId);
            Assert.False(caster.HasRoute);
            Assert.Single(world.Shots);
            Assert.Equal(sim.StateHash(), copy.StateHash());
        }
        // 00496D00 becomes false on the completion poll, allowing ordinary re-selection.
        sim.Tick(); copy.Tick();
        Assert.Equal(1, caster.Dk2StateId);
        Assert.Equal(sim.StateHash(), copy.StateHash());
        Assert.Single(world.Shots);
    }

    // Travel_RouteToPoint ends the current action last (00471890 ->00496D20).
    [Fact]
    public void ATravelRequestCancelsTheReleasedCastHold()
    {
        var (db, world, caster, _, sim) = Setup(false);
        int fire = SpellRules.DelayTicks(db.CreatureById(caster.CreatureTypeId), 0) + 1;
        sim.Run(fire + 1);
        CreatureStateMachine.BeginTravel(new StateHookContext(world, db), caster, 6, 1);
        Assert.Equal(0, caster.ActionUntilTick);
        Assert.False(caster.SwingInProgress);
    }

    // Mode3 needs handler completion even after its animation finishes (00496AA0).
    [Fact]
    public void ALongShotDelayStillWaitsAfterTheClipCompletes()
    {
        var (db, world, caster, _, sim) = Setup(false, shortClip: true);
        CreatureDef definition = db.CreatureById(caster.CreatureTypeId);
        int hold = AnimationTimingRules.CountedHoldTicks(definition, 6, 1);
        int fire = SpellRules.DelayTicks(definition, 0) + 1;
        Assert.True(hold < fire);
        sim.Run(hold + 1);
        Assert.True(caster.SwingInProgress);
        Assert.Empty(world.Shots);
        sim.Run(fire - hold);
        Assert.False(caster.SwingInProgress);
        Assert.Single(world.Shots);
    }

    // 0045F241 validates the target before 00496D00's busy gate.
    [Fact]
    public void AnUnattackableTargetIsDroppedBeforeTheBusyGate()
    {
        var (_, world, caster, target, sim) = Setup(false);
        sim.Run(14);
        Assert.False(caster.SwingInProgress);
        Assert.Single(world.Shots);
        target.InHand = true;
        sim.Tick();
        // Step 5 clears the target; step 7 then returns for the action still running (0x0045F632),
        // and the state ends on the first turn with none.
        Assert.Equal(FightTargetKind.None, caster.FightTargetKind);
        Assert.Equal(199, caster.Dk2StateId);
        for (int i = 0; i < 40 && caster.Dk2StateId == 199; i++) sim.Tick();
        Assert.Equal(1, caster.Dk2StateId);
    }

    // 00496700 ends a permitted previous action before installing its replacement.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ReplacementActionsDiscardThePreviousCastDeadline(int replacement)
    {
        var (db, world, caster, target, sim) = Setup(false);
        sim.Run(14);
        Assert.True(caster.ActionUntilTick > world.CurrentTick.Value);
        new GameSimulation(db, world, 1, [new RunRule(context =>
        {
            if (replacement == 0)
            {
                context.Navigation.Refresh();
                FightRules.StartSwing(context, new WorldSight(context.Navigation, world, db), caster,
                    db.CreatureById(caster.CreatureTypeId), []);
            }
            else if (replacement == 1)
                FightRules.BeginPossessedAction(context, caster, SpellRules.MeleeSwing,
                    slot: 0, targetKind: FightTargetKind.Creature, targetId: target.Id);
            else
                FightRules.BeginFreeAim(context, caster, slot: 0,
                    x: target.PosX, y: target.PosY, z: 0, pitch: 0, range: 16384);
        })]).Tick();
        Assert.Equal(0, caster.ActionUntilTick);
    }

    private sealed class RunRule(Action<SimulationContext> rule) : ISimulationSystem
    {
        public string Name => nameof(RunRule);
        public void Execute(SimulationContext context) => rule(context);
    }

    private sealed class StartOnce(CreatureState caster) : ISimulationSystem
    {
        private bool _started;
        public string Name => nameof(StartOnce);
        public void Execute(SimulationContext context)
        {
            if (_started) return;
            _started = true;
            FightRules.StartCast(context, caster, context.Database.CreatureById(caster.CreatureTypeId), 0);
        }
    }

    private static (GameDatabase Db, WorldState World, CreatureState Caster, CreatureState Target, GameSimulation Sim)
        Setup(bool fallback, bool shortClip = false)
    {
        GameDatabase basis = Dk2TerrainFixture.Database();
        var slots = new CreatureAnimationTiming[CreatureDef.ArtSlotCount];
        slots[fallback ? 5 : 6] = new CreatureAnimationTiming
            { ResourceType = 6, FrameCount = (ushort)(shortClip ? 1 : 60), FramesPerSecond = 16 };
        var db = new GameDatabase(basis.Terrain.Values, basis.Rooms.Values,
            basis.Creatures.Values.Select(c => c.CreatureId == Dk2TerrainFixture.WarlockCreatureId
                ? c with { ArtSlots = slots } : c), basis.Variables,
            creatureSpells: basis.CreatureSpells.Values, shots: basis.Shots.Values);
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(40, 40),
            [(byte)0, PlayerIds.Good, PlayerIds.Neutral, PlayerIds.FirstKeeper, (byte)(PlayerIds.FirstKeeper + 1)]);
        foreach (int y in Enumerable.Range(0, world.Height))
        foreach (int x in Enumerable.Range(0, world.Width))
            world.Tile(new TilePos(x, y)).TerrainId = Dk2TerrainFixture.DirtPath;
        CreatureState caster = world.SpawnCreature(db, Dk2TerrainFixture.WarlockCreatureId,
            PlayerIds.FirstKeeper, new TilePos(5, 10));
        CreatureState target = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId,
            PlayerIds.FirstKeeper + 1, new TilePos(8, 10));
        target.FrozenTicks = 1000;
        StatePlacement.As(target, CreatureActivity.Frozen);
        Assert.True(CreatureStateMachine.TrySetState(new StateHookContext(world, db), caster, 199, 0));
        caster.FightTargetKind = FightTargetKind.Creature;
        caster.FightTargetId = target.Id;
        caster.AttackType = CombatRules.AttackMode.SpellAtCreature;
        caster.AttackSpellSlot = 0;
        caster.AttackRangeSquared = SpellRules.RangeSquared(db.CreatureSpells[Dk2TerrainFixture.FireballSpellId]);
        Assert.True(CombatRules.Register(target, caster.Id, ranged: true));
        return (db, world, caster, target, new GameSimulation(db, world, 1, [new StartOnce(caster), new CombatSystem()]));
    }
}
