// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The Combat category's update <c>0x0047A870</c> (ADR-0366): combat experience in states
/// 199 and 200, for a creature that is not a worker, or is Good, fighting a live creature. The
/// owner saw a hero Dwarf gain levels in a fight (2026-10-05). Ours gave none.</summary>
public class CombatExperienceTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;

    [Theory]
    [InlineData((uint)0, 199, true, 160 / 4)]
    [InlineData((uint)0, 200, true, 160 / 4)]
    [InlineData((uint)0, 1, true, 0)]
    [InlineData((uint)0, 199, false, 0)]
    [InlineData((uint)CreatureFlags.IsWorker, 199, true, 0)]
    [InlineData((uint)(CreatureFlags.IsWorker | CreatureFlags.IsGood), 199, true, 160 / 4)]
    public void CombatExperienceIsPerTickInTheFightStates_ForANonWorkerOrAGoodOne(
        uint flags, int state, bool liveTarget, int expected) =>
        Assert.Equal(expected, Earned(flags, state, liveTarget, deadTarget: false));

    /// <summary>A target that is a body, not a live creature, earns nothing (the slot and class
    /// gate in <c>0x0047A870</c>).</summary>
    [Fact]
    public void ADeadTargetEarnsNothing() => Assert.Equal(0, Earned(0, 199, liveTarget: true, deadTarget: true));

    private static int Earned(uint flags, int state, bool liveTarget, bool deadTarget)
    {
        CreatureDef fighter = Dk2TerrainFixture.Goblin() with
        {
            Name = "Fighter", CreatureId = 61, ExpPerSecond = 160,
            Flags = (Dk2TerrainFixture.Goblin().Flags & ~(CreatureFlags.IsWorker | CreatureFlags.IsGood)) | (CreatureFlags)flags,
        };
        GameDatabase db = Dk2TerrainFixture.Database(fighter);
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper]);
        CreatureState me = world.SpawnCreature(db, 61, Keeper, new TilePos(2, 2));
        CreatureState them = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, PlayerIds.Good, new TilePos(3, 2));
        me.Dk2StateId = state;
        if (liveTarget)
        {
            me.FightTargetKind = FightTargetKind.Creature;
            me.FightTargetId = them.Id;
        }
        if (deadTarget) StatePlacement.As(them, CreatureActivity.Dead);

        return CreatureExperienceSystem.CombatExperiencePerOriginalTick(world, db, fighter, me);
    }
}
