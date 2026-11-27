// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;

namespace DK2.Simulation.Tests;

public sealed class CombatPitShotTests
{
    [Theory]
    [InlineData(false, PlayerIds.FirstKeeper)]
    [InlineData(true, PlayerIds.Good)]
    public void ATargetedPitSpellUsesTheGoodPlayersShotOwnership(bool pit, byte expectedOwner)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        var world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [PlayerIds.Neutral, PlayerIds.Good, PlayerIds.FirstKeeper]);
        CreatureState caster = world.SpawnCreature(db, Dk2TerrainFixture.WarlockCreatureId, PlayerIds.FirstKeeper, new TilePos(4, 4));
        CreatureState target = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, PlayerIds.FirstKeeper, new TilePos(5, 4));
        caster.CombatPitFighter = pit;
        caster.SwingSpellSlot = 0;
        caster.SwingTargetKind = FightTargetKind.Creature;
        caster.SwingTargetId = target.Id;
        new GameSimulation(db, world, 42, [new Cast(caster)]).Tick();

        Assert.Equal(expectedOwner, Assert.Single(world.Shots).OwnerPlayerId);
    }

    private sealed class Cast(CreatureState caster) : ISimulationSystem
    {
        public string Name
        {
            get
            {
                return nameof(Cast);
            }
        }

        public void Execute(SimulationContext context)
        {
            FightRules.FireSpell(context, caster, context.Database.CreatureById(caster.CreatureTypeId));
        }
    }
}
