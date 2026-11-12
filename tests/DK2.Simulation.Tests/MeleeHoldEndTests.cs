// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>Action 17's hold after its impact ends only where DK2's end-action callers end it
/// (<c>0x00496D20</c>, <c>0x00496D80</c>; ADR-0762), never because the creature's state changed: the
/// flee state 203 (<c>0x004602E0</c>) never ends it, the pick-up (<c>0x004BFC44</c>) and the slap
/// (<c>0x0048B9BC</c>, <c>0x0046F20D</c>) do.</summary>
public class MeleeHoldEndTests
{
    private const byte Keeper = 3;
    private const int FightState = 199, FleeState = 203;

    private static (GameDatabase Db, WorldState World, CreatureState Fighter, CreatureDef Def) Fight()
    {
        var db = Dk2TerrainFixture.Database();
        var world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [Keeper, (byte)4]);
        CreatureState fighter = world.SpawnCreature(db, 7, Keeper, new TilePos(2, 2));
        CreatureState enemy = world.SpawnCreature(db, 7, 4, new TilePos(3, 2));
        Assert.True(CreatureStateMachine.TrySetState(new StateHookContext(world, db), fighter, FightState));
        fighter.FightTargetId = enemy.Id;
        fighter.FightTargetKind = FightTargetKind.Creature;
        return (db, world, fighter, db.CreatureById(fighter.CreatureTypeId));
    }

    /// <summary>The identity of a swing whose blow has landed, one turn after it began.</summary>
    private static GameTick Swung(CreatureState fighter, long start)
    {
        fighter.SwingInProgress = false;
        fighter.SwingKind = SpellRules.MeleeSwing;
        fighter.SwingTargetKind = FightTargetKind.Creature;
        fighter.SwingStartTick = start;
        fighter.TimeLastUsedMelee = start;
        fighter.MeleeStampDelay = 0;
        return new GameTick(start + 1);
    }

    [Fact]
    public void AMoveToTheFleeStateDoesNotEndTheHold()
    {
        (GameDatabase db, WorldState world, CreatureState fighter, CreatureDef def) = Fight();
        Assert.True(AnimationTimingRules.ActionHoldTicks(def.ArtSlot(5)) >= 3);
        GameTick now = Swung(fighter, 100);
        Assert.True(FightRules.MeleeActionIsRunning(fighter, def, now));

        // The old test took any state stamped after the swing's start as the end of the action.
        Assert.True(CreatureStateMachine.TrySetState(new StateHookContext(world, db), fighter, FleeState, now.Value));
        Assert.True(fighter.StateEnteredTick > fighter.SwingStartTick);

        Assert.True(FightRules.MeleeActionIsRunning(fighter, def, now));
        Assert.Equal(CreatureActionTable.MeleeSwing, CreatureCurrentAction.IndexOf(fighter, def, now));
    }

    [Fact]
    public void ThePickUpEndsTheHold()
    {
        (GameDatabase db, WorldState world, CreatureState fighter, CreatureDef def) = Fight();
        var sim = new GameSimulation(db, world, 1, [new HandSystem()]);
        GameTick now = Swung(fighter, sim.CurrentTick.Value);
        Assert.True(FightRules.MeleeActionIsRunning(fighter, def, now));

        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, fighter.Id)));

        Assert.Equal(HandRules.PreparationState, fighter.Dk2StateId);
        Assert.False(FightRules.MeleeActionIsRunning(fighter, def, now));
    }

    [Fact]
    public void ASlapEndsTheHoldWhenItsReactionStarts()
    {
        (GameDatabase db, WorldState world, CreatureState fighter, CreatureDef def) = Fight();
        // A creature in the slapper's own land that is no fighter reacts in 77 (slap-states.md).
        TilePos at = new(2, 2);
        ref TileState tile = ref world.Tile(at.X, at.Y);
        tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
        tile.OwnerPlayerId = Keeper;
        var sim = new GameSimulation(db, world, 1, [new HandSystem(), new CreatureStatusSystem()]);
        Assert.True(CreatureStateMachine.TrySetState(new StateHookContext(world, db), fighter, CreatureStateMachine.Idle, sim.CurrentTick.Value));
        GameTick now = Swung(fighter, sim.CurrentTick.Value);
        Assert.True(FightRules.MeleeActionIsRunning(fighter, def, now));

        sim.Tick(InputFrame.Of(new SlapCreatureCommand(Keeper, fighter.Id)));
        Assert.Equal(SlapRules.ReactionState, fighter.Dk2StateId);
        sim.Tick();   // age 1: the reaction ends the running action (0x0046F20D)

        Assert.False(FightRules.MeleeActionIsRunning(fighter, def, sim.CurrentTick));
        Assert.Equal(FightTargetKind.None, fighter.SwingTargetKind);
    }
}
