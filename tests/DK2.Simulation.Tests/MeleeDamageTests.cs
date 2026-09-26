// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Simulation.Tests;

/// <summary>
/// The melee damage formula (Q8, docs/research/combat-damage.md).
///
/// Nothing in this simulation swings, so these are unit tests of a formula rather than of a
/// mechanic. They exist so that the attack state, when there is one, inherits arithmetic that
/// has been checked against the binary rather than a guess — and so that the two quirks the
/// original has here cannot be tidied away by a later reader who thinks they are bugs.
/// </summary>
public class MeleeDamageTests
{
    private static GameDatabase Db() => Dk2TerrainFixture.Database();

    private static CreatureDef Goblin() => Dk2TerrainFixture.Goblin();

    [Fact]
    public void LevelOneSwingsForTheRecordValue()
    {
        Assert.Equal(170, MeleeDamageRules.Swing(Db(), Goblin(), 1, MeleeDamageRules.Stance.None));
    }

    [Fact]
    public void LevelEightSwingsForThreeHundredPercent()
    {
        Assert.Equal(510, MeleeDamageRules.Swing(Db(), Goblin(), 8, MeleeDamageRules.Stance.None));
    }

    /// <summary>The shipped CombatPitMeleeDamageModifierPercentage is 100 and the original adds it
    /// to a ceiling of 100, so a pit fighter hits for double. The name reads like a scale; the
    /// arithmetic is an increment, and this pins which one the original means.</summary>
    [Fact]
    public void TheCombatPitDoublesDamageBecauseItsHundredIsAnIncrement()
    {
        Assert.Equal(
            340,
            MeleeDamageRules.Swing(Db(), Goblin(), 1, MeleeDamageRules.Stance.CombatPitFighter));
    }

    /// <summary>Influence gets half of possession's bonus, by dividing through 200 rather than 100
    /// — one shipped value serving two effects.</summary>
    [Fact]
    public void InfluenceGetsHalfOfPossessionsBonus()
    {
        GameDatabase db = Db();
        Assert.Equal(6553, MeleeDamageRules.StanceMultiplier(db, MeleeDamageRules.Stance.Possessed));
        Assert.Equal(
            5324,
            MeleeDamageRules.StanceMultiplier(db, MeleeDamageRules.Stance.InfluencedByPossessed));
    }

    [Fact]
    public void NoStanceIsExactlyOne()
    {
        Assert.Equal(MeleeDamageRules.One,
                     MeleeDamageRules.StanceMultiplier(Db(), MeleeDamageRules.Stance.None));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(300, 15)]
    [InlineData(320, 16)]
    [InlineData(1023, 51)]
    [InlineData(1024, 51)]   // reflected about half a turn: 0x7FE - 0x400 = 0x3FE
    [InlineData(2048, 0)]    // a whole turn wraps to zero
    public void BearingIsSymmetricAboutHalfATurn(int rawAngle, int expected)
    {
        Assert.Equal(expected, MeleeDamageRules.BearingPercent(rawAngle));
    }

    /// <summary>The original skips the bearing bonus entirely at 15 or below, so the bonus is a
    /// step at 16 rather than a ramp from zero.</summary>
    [Fact]
    public void BearingBelowSixteenDoesNothingAtAll()
    {
        Assert.Equal(-100, MeleeDamageRules.ApplyBearing(-100, 15));
        Assert.Equal(-116, MeleeDamageRules.ApplyBearing(-100, 16));
    }

    /// <summary>The damage is negated before the bonuses, so the divide truncates toward zero
    /// rather than flooring: a 16% bonus on -170 adds -27, not the -28 a floor would give, and the
    /// blow lands one point softer than a naive reading predicts. Reproduced, not fixed.</summary>
    [Fact]
    public void TheBonusTruncatesTowardZeroRatherThanFlooring()
    {
        Assert.Equal(-197, MeleeDamageRules.ApplyBearing(-170, 16));
        Assert.Equal(197, MeleeDamageRules.ApplyBearing(170, 16));
        Assert.Equal(-27, -170 * 16 / 100);
        Assert.Equal(-28, (int)Math.Floor(-170 * 16 / 100.0));
    }

    [Fact]
    public void PlayerModifiersAreSkippedWhenZeroOrLess()
    {
        Assert.Equal(-100, MeleeDamageRules.ApplyPlayerModifiers(-100, 0, 0));
        Assert.Equal(-100, MeleeDamageRules.ApplyPlayerModifiers(-100, -5, -5));
    }

    [Fact]
    public void PlayerModifiersAreTenPercentAndFifteenPercentAUnit()
    {
        Assert.Equal(-110, MeleeDamageRules.ApplyPlayerModifiers(-100, 1, 0));
        Assert.Equal(-115, MeleeDamageRules.ApplyPlayerModifiers(-100, 0, 1));
        Assert.Equal(-130, MeleeDamageRules.ApplyPlayerModifiers(-100, 0, 2));
    }

    /// <summary>A blow that rounds to nothing never reaches the health routine, which is a branch
    /// in the original and not an optimisation.</summary>
    [Fact]
    public void AZeroBlowStaysZeroThroughTheModifiers()
    {
        CreatureDef harmless = Goblin() with { MeleeDamage = 0 };
        Assert.Equal(0, MeleeDamageRules.Blow(Db(), harmless, 1, MeleeDamageRules.Stance.None, 0, 9, 9));
    }

    [Fact]
    public void TheWholeBlowReachesTheHealthRoutineNegative()
    {
        Assert.Equal(-170, MeleeDamageRules.Blow(Db(), Goblin(), 1, MeleeDamageRules.Stance.None, 0, 0, 0));
    }
}
