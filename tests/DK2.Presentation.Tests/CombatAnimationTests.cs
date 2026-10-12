// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Assets;
using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;
using DK2.Simulation.Combat;
using DK2.Simulation.Steering;
using DK2.Simulation.Systems;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

public class CombatAnimationTests
{
    [InstallTheory]
    [InlineData("Guard")]
    [InlineData("Dwarf")]
    [InlineData("Salamander")]
    public void ABlowDoesNotReleaseTheMeleeActionBeforeItsAuthoredAnimationEnds(string name)
    {
        var (database, world, attacker, target) = Fighters(name);
        CreatureDef definition = database.CreatureById(attacker.CreatureTypeId);
        var simulation = new GameSimulation(database, world, 7, [new CombatSystem()]);
        var view = new WorldView(database);
        for (int i = 0; i < 20 && !attacker.SwingInProgress; i++) simulation.Tick();
        Assert.True(attacker.SwingInProgress);
        long started = attacker.SwingStartTick;
        int hold = AnimationTimingRules.ActionHoldTicks(definition.ArtSlot(5));
        int delay = CombatRules.TicksOf(definition.MeleeHitDelay);
        simulation.Run(delay);
        Assert.False(attacker.SwingInProgress);
        Assert.True(target.Health < 30000);
        target.PosX += 3 * Fp32.One;
        while (world.CurrentTick.Value < started + hold - 1)
        {
            simulation.Tick();
            Assert.False(attacker.HasRoute);
            Assert.Equal(started, attacker.SwingStartTick);
            CreatureView pose = view.Creatures(world).Single(c => c.Id == attacker.Id);
            Assert.Equal(AssetRef.ForCreature(name, AssetKind.Animation, "fighting"), pose.Animation);
            Assert.Equal(new GameTick(started), pose.AnimationStartedAt);
        }
        target.PosX = attacker.PosX - 1300;
        simulation.Tick();
        Assert.NotEqual(0x200, attacker.Heading);
    }

    [InstallTheory]
    [InlineData("Guard")]
    [InlineData("Dwarf")]
    [InlineData("Salamander")]
    public void AStationaryFighterTurnsToItsTargetBeforeStartingMelee(string name)
    {
        var (database, world, attacker, _) = Fighters(name);
        CreatureDef definition = database.CreatureById(attacker.CreatureTypeId);
        attacker.Heading = 0x600;
        var simulation = new GameSimulation(database, world, 7, [new CombatSystem()]);
        int expected = 0x600;
        for (int i = 0; i < 8; i++)
        {
            int left = Bearing.TurnToward(ref expected, 0x200, definition.TurnRate);
            simulation.Tick();
            Assert.Equal(expected, attacker.Heading);
            if (left != 0) Assert.False(attacker.SwingInProgress);
            if (attacker.SwingInProgress) break;
        }
        Assert.True(attacker.SwingInProgress);
        Assert.Equal(0x200, attacker.Heading);
    }

    private static (GameDatabase, WorldState, CreatureState, CreatureState) Fighters(string name)
    {
        GameDatabase database = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        byte path = database.Terrain.Values.Single(t => t.Name.Trim() == "Dirt Path").TerrainId;
        var map = new LevelMap(12, 12, Enumerable.Repeat(new MapTile(path, 1, 0, 0), 144).ToArray());
        WorldState world = WorldState.FromMap(database, map, [0, 1]);
        new GameSimulation(database, world, 7, []).Tick();
        CreatureState attacker = world.SpawnCreature(database, database.CreatureByName(name)!.CreatureId,
            1, new TilePos(5, 5));
        CreatureState target = world.SpawnCreature(database, database.CreatureByName("Guard")!.CreatureId,
            0, new TilePos(5, 5));
        target.PosX = attacker.PosX + 1300;
        target.Health = 30000;
        target.Possessed = true;
        attacker.Heading = 0x200;
        CreatureStateMachine.TrySetState(new StateHookContext(world, database), attacker, 200, currentTick: 0);
        attacker.FightTargetKind = FightTargetKind.Creature;
        attacker.FightTargetId = target.Id;
        attacker.AttackType = CombatRules.AttackMode.MeleeAtCreature;
        attacker.AttackAbnormalTargets = true;
        attacker.AttackRangeSquared = CombatRules.MeleeReachSquared(database.CreatureById(attacker.CreatureTypeId),
            database.CreatureById(target.CreatureTypeId));
        return (database, world, attacker, target);
    }
}
