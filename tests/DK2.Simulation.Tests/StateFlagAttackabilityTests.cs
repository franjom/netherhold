// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;

namespace DK2.Simulation.Tests;

/// <summary>ADR-0585: <c>0x0045A8B0</c> clears IsAttackable for a creature whose current state carries
/// the table flag bit 2 (<c>0x0045A948</c>), read on the state held: a walk to bed is state 6 and is
/// attackable; the lair and sleep states, the Casino's states and joining are not.</summary>
public class StateFlagAttackabilityTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;

    [Theory]
    [InlineData(6, 81, true)]      // walking to bed
    [InlineData(1, 0, true)]       // idle
    [InlineData(79, 0, false)]     // choosing a bed
    [InlineData(82, 0, false)]     // lying down
    [InlineData(153, 0, false)]    // the Casino
    [InlineData(193, 0, false)]    // joining the dungeon
    public void ACreatureIsAttackableUnlessItsStateCarriesTheFlag(int state, int resume, bool attackable)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(10, 10), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper]);
        CreatureState goblin = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(5, 5));
        StatePlacement.Place(goblin, state);
        goblin.UtilityNewState = resume;
        Assert.Equal(state, goblin.Dk2StateId);

        Assert.Equal(attackable, CombatRules.IsAttackable(goblin));
    }
}
