// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The pick-up preparation <c>0x004BFBD0</c>, state 75, which the hand handler runs
/// before it tests its capacity (hand-of-evil.md §2, ADR-0284).</summary>
public class HandPreparationTests
{
    private const byte Keeper = 3;
    private const int FightState = 199;

    private static (GameDatabase Db, WorldState World, CreatureState Fighter) Fight()
    {
        var db = Dk2TerrainFixture.Database();
        var world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [Keeper, (byte)4]);
        CreatureState fighter = world.SpawnCreature(db, 7, Keeper, new TilePos(2, 2));
        CreatureState enemy = world.SpawnCreature(db, 7, 4, new TilePos(3, 2));
        Assert.True(CreatureStateMachine.TrySetState(fighter, FightState));
        fighter.FightTargetId = enemy.Id;
        fighter.FightTargetKind = Combat.FightTargetKind.Creature;
        return (db, world, fighter);
    }

    private static void Fill(GameDatabase db, WorldState world, CreatureState any)
    {
        List<HandItem> hand = world.Player(Keeper).Hand;
        while (hand.Count < HandRules.Capacity(db)) hand.Add(HandItem.Of(any));
    }

    [Fact]
    public void AFullHandStillTakesTheFighterOutOfItsFightThroughIdleIntoTheLiftingState()
    {
        (GameDatabase db, WorldState world, CreatureState fighter) = Fight();
        Fill(db, world, fighter);
        var sim = new GameSimulation(db, world, 1, [new HandSystem()]);

        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, fighter.Id)));

        // 75's entry mask lacks the combat category, so the preparation goes 199, 1, 75; the
        // capacity test comes after it and leaves the creature there, out of the hand.
        Assert.Equal(HandRules.PreparationState, fighter.Dk2StateId);
        Assert.Equal(CreatureActivity.BeingLifted, fighter.Activity);
        Assert.False(fighter.InHand);
        Assert.NotEqual(Data.Model.CreatureStateCategory.Combat, NeedRules.ResolvedCategory(fighter));
    }

    [Fact]
    public void ARefusedPermissionPreparesNothingAndAPreparedCreatureIsLiftedOnceThereIsRoom()
    {
        (GameDatabase db, WorldState world, CreatureState fighter) = Fight();
        CreatureState foreign = world.Creatures.Single(c => c.OwnerPlayerId == 4);
        var sim = new GameSimulation(db, world, 1, [new HandSystem()]);

        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, foreign.Id)));
        Assert.NotEqual(HandRules.PreparationState, foreign.Dk2StateId);

        Fill(db, world, fighter);
        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, fighter.Id)));
        Assert.Equal(HandRules.PreparationState, fighter.Dk2StateId);

        // 75 is none of the states the predicate refuses: with room in the hand the next pick-up lands.
        world.Player(Keeper).Hand.Clear();
        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, fighter.Id)));
        Assert.True(fighter.InHand);
        Assert.Single(world.Player(Keeper).Hand);
    }
}
