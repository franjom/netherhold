// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>A creature the hand takes keeps its stored IsAttackable bit until its own next update
/// refreshes it: Hand_PickUp (<c>0x004BC500</c>) never calls <c>0x0045A8B0</c>, whose first refusal is
/// <c>+0x16 == 2</c> (<c>0x0045A8B3</c>), and the bit readers (<c>0x0045B140</c> and on, <c>0x0045C690</c>)
/// do not test it; <c>spFighting</c> does, first (<c>0x0045F2BE</c>) (ADR-0763).</summary>
public class HeldAttackableTests
{
    private const byte Keeper = 3;

    private static (GameDatabase Db, WorldState World, CreatureState Victim, CreatureState Hunter) Pair()
    {
        var db = Dk2TerrainFixture.Database();
        var world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [Keeper, (byte)4]);
        CreatureState victim = world.SpawnCreature(db, 7, Keeper, new TilePos(2, 2));
        CreatureState hunter = world.SpawnCreature(db, 7, 4, new TilePos(3, 2));
        CombatRules.RefreshAttackable(victim);
        Assert.True(victim.Attackable);
        hunter.FightTargetKind = FightTargetKind.Creature;
        hunter.FightTargetId = victim.Id;
        return (db, world, victim, hunter);
    }

    [Fact]
    public void AHeldCreatureKeepsItsStoredBitUntilItsOwnUpdateButSpFightingRefusesItAtOnce()
    {
        (GameDatabase db, WorldState world, CreatureState victim, CreatureState hunter) = Pair();
        var hand = new GameSimulation(db, world, 1, [new HandSystem()]);
        var sweep = new GameSimulation(db, world, 1, [new CreatureUpdateSystem()]);

        hand.Tick(InputFrame.Of(new TakeCreatureCommand(Keeper, victim.Id)));
        Assert.True(victim.InHand);

        Assert.True(victim.Attackable);                              // the pick-up did not recompute it
        Assert.True(CombatRules.IsAttackable(victim));               // what the bit readers see
        Assert.False(CombatRules.IsAttackableOnMap(victim));
        Assert.False(FightRules.TargetIsValid(world, db, hunter));   // spFighting: off the map first

        sweep.Tick();                                                // its own update refreshes the bit
        Assert.False(victim.Attackable);
        Assert.False(CombatRules.IsAttackable(victim));
    }
}
