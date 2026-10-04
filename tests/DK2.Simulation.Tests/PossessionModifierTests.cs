// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;

namespace DK2.Simulation.Tests;

public sealed class PossessionModifierTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(255)]
    [InlineData(278)]
    public void PossessionPreoccupiesBeforeInterpretingItsRetainedState(int state)
    {
        var creature = new CreatureState { Possessed = true, Dk2StateId = state };
        Assert.True(CombatRules.Preoccupied(creature));
    }

    private static GameDatabase Database(int stat = 25, int rechargePercent = 100)
    {
        GameDatabase basis = Dk2TerrainFixture.Database();
        var variables = new GlobalVariables();
        foreach (var entry in basis.Variables.Misc) variables.Misc[entry.Key] = entry.Value;
        variables.Misc[VariableTypeId.FirstPersonRechargeModifyPercentage] =
            new(VariableTypeId.FirstPersonRechargeModifyPercentage, rechargePercent, 0, 0);
        variables.CreatureStats.AddRange(basis.Variables.CreatureStats);
        variables.CreatureStats.Add(new((int)CreatureStatId.MeleeRecharge, stat, 9, true));
        return new(basis.Terrain.Values, basis.Rooms.Values, basis.Creatures.Values, variables,
            creatureSpells: basis.CreatureSpells.Values);
    }

    [Theory]
    [InlineData(true, false, 31)]
    [InlineData(false, true, 15)]
    [InlineData(true, true, 31)]
    public void SpellRechargeUsesPossessionBeforeWorkRate(bool possessed, bool influenced, int interval)
    {
        GameDatabase db = Database();
        CreatureDef definition = Dk2TerrainFixture.Warlock();
        var creature = new CreatureState { Possessed = possessed, InfluencedByPossessed = influenced };
        creature.SpellStamps[0] = 10;
        Assert.False(SpellRules.CanCast(db, creature, definition, 0, 10 + interval));
        Assert.True(SpellRules.CanCast(db, creature, definition, 0, 11 + interval));
    }

    [Fact]
    public void DamageTableStoresOnlyTheSignedLowWordAndLastRowIgnoresLevel()
    {
        GameDatabase db = Database();
        db.Variables.CreatureStats.Add(new((int)CreatureStatId.MeleeDamage, 65535, -1, true));
        Assert.Equal(-1, MeleeDamageRules.PossessionIncreasePercent(db));
    }

    [Fact]
    public void PossessedMeleeSkipsWindUpWhileInfluenceKeepsIt()
    {
        CreatureDef definition = Dk2TerrainFixture.Goblin() with { MeleeHitDelay = new Fp32(4096 * 10) };
        var creature = new CreatureState { Possessed = true, SwingStartTick = 50 };
        Assert.True(CombatRules.BlowLands(creature, definition, 50));
        creature.Possessed = false;
        creature.InfluencedByPossessed = true;
        Assert.False(CombatRules.BlowLands(creature, definition, 50));
    }

    [Fact]
    public void PossessedSpellSkipsWindUpWithoutBypassingAnAbsentAction()
    {
        CreatureDef definition = Dk2TerrainFixture.Warlock();
        var creature = new CreatureState
        {
            Possessed = true, SwingInProgress = true, SwingKind = SpellRules.SpellCast,
            SwingSpellSlot = 0, SwingStartTick = 50,
        };
        Assert.True(FightRules.CastFires(creature, definition, 50));
        creature.SwingInProgress = false;
        Assert.False(FightRules.CastFires(creature, definition, 50));
    }

    [Theory]
    [InlineData(true, false, 100, 5120)]
    [InlineData(true, true, 70, 3583)]
    [InlineData(false, true, 70, 2560)]
    [InlineData(false, false, 70, 4096)]
    [InlineData(true, false, 326, 3583)]
    public void RechargePreservesSeparateDivisionTruncationsAndByteStorage(
        bool possessed, bool influenced, int percent, int factor)
    {
        var creature = new CreatureState { Possessed = possessed, InfluencedByPossessed = influenced };
        Assert.Equal(factor, CombatRules.RechargeMultiplier(Database(rechargePercent: percent), creature).Raw);
    }

    [Theory]
    [InlineData(true, false, 31)]
    [InlineData(false, true, 15)]
    [InlineData(true, true, 31)]
    public void MeleeRechargeUsesTheSameModifierAndStrictBoundary(bool possessed, bool influenced, int interval)
    {
        GameDatabase db = Database();
        CreatureDef definition = Dk2TerrainFixture.Goblin() with { MeleeRecharge = new Fp32(5 * 4096) };
        var creature = new CreatureState
        {
            Possessed = possessed, InfluencedByPossessed = influenced, TimeLastUsedMelee = 10,
        };
        Assert.False(CombatRules.SwingMayBegin(db, creature, definition, 10 + interval));
        Assert.True(CombatRules.SwingMayBegin(db, creature, definition, 11 + interval));
    }

    [Fact]
    public void PossessionDamageWinsOverInfluenceAndCombatPit()
    {
        GameDatabase db = Database();
        var creature = new CreatureState { Possessed = true, InfluencedByPossessed = true, CombatPitFighter = true };
        Assert.Equal(6553, MeleeDamageRules.StanceMultiplier(db, MeleeDamageRules.StanceOf(creature)));
        creature.Possessed = false;
        Assert.Equal(3276, MeleeDamageRules.StanceMultiplier(db, MeleeDamageRules.StanceOf(creature)));
        creature.InfluencedByPossessed = false;
        Assert.Equal(8192, MeleeDamageRules.StanceMultiplier(db, MeleeDamageRules.StanceOf(creature)));
    }

    [Fact]
    public void NegativeDamageStatNarrowsTheFixedProductBeforeIntegerConversion_StronglyInferredFromBinary()
    {
        GameDatabase db = Database();
        db.Variables.CreatureStats.Add(new((int)CreatureStatId.MeleeDamage, -101, 0, true));
        CreatureDef definition = Dk2TerrainFixture.Goblin() with { MeleeDamage = 100 };
        Assert.Equal(-1, MeleeDamageRules.Swing(db, definition, 1, MeleeDamageRules.Stance.Possessed));
    }

    [Fact]
    public void NegativeRechargeStatNarrowsTheFixedProductBeforeIntegerConversion_StronglyInferredFromBinary()
    {
        var creature = new CreatureState { Possessed = true };
        Assert.Equal(-1, CombatRules.RechargeTicks(Database(stat: -101), creature, 100));
    }

    [Fact]
    public void FirstPersonWordIgnoresLevelKeepsLastRowAndRetainsInitializerDefault()
    {
        GameDatabase db = Database(65561);
        Assert.Equal(25, db.FirstPersonStat(CreatureStatId.MeleeRecharge));
        Assert.Equal(100, db.FirstPersonStat(CreatureStatId.Speed));
        db.Variables.CreatureStats.Add(new((int)CreatureStatId.MeleeRecharge, 65535, -1, true));
        Assert.Equal(-1, db.FirstPersonStat(CreatureStatId.MeleeRecharge));
        Assert.Equal(100, db.FirstPersonStat((CreatureStatId)32));
    }

    [Theory]
    [InlineData(-101, MeleeDamageRules.Stance.Possessed, 42949632)]
    [InlineData(-101, MeleeDamageRules.Stance.InfluencedByPossessed, 21474816)]
    public void NegativeDamageNumeratorUsesTheOriginalZeroExtendedFixedDivide(
        int value, MeleeDamageRules.Stance stance, int factor)
    {
        GameDatabase db = Database();
        db.Variables.CreatureStats.Add(new((int)CreatureStatId.MeleeDamage, value, 0, true));
        Assert.Equal(factor, MeleeDamageRules.StanceMultiplier(db, stance));
    }
}
