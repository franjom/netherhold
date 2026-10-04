// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>How hard one melee blow lands, from the attacker's record to the health routine.</summary>
/// <remarks>The fixed-point stance multiplier scales level-adjusted damage before negation,
/// bearing and per-player bonuses. Negating first makes each bonus division truncate toward zero;
/// see docs/research/combat-damage.md.</remarks>
[Dk2Behavior(Confidence.Confirmed, "docs/research/combat-damage.md")]
public static class MeleeDamageRules
{
    /// <summary>1.0 in the original's 12-bit fixed point, and the multiplier when no stance applies.</summary>
    public const int One = 1 << 12;

    /// <summary>The attacker states that scale a blow. Exactly one applies, in this order — the
    /// original tests them as <c>if / else if / else if</c>, not as factors that compose.</summary>
    public enum Stance
    {
        /// <summary>No modifier: the multiplier is <see cref="One"/>.</summary>
        None = 0,

        /// <summary>Attacker flags bit 7, <c>Possessed</c>: the keeper is driving it.</summary>
        Possessed = 1,

        /// <summary>Attacker flags bit 3 of the second word, <c>InfluencedByPossessed</c>. Gets
        /// half the possession bonus, by dividing through 200 rather than 100.</summary>
        InfluencedByPossessed = 2,

        /// <summary>Attacker flags bit 15, <c>CombatPitFighter</c>.</summary>
        CombatPitFighter = 3,
    }

    /// <summary>
    /// The percentage the possession arms add: <c>global+0x31B</c> (<c>0x006F286B</c>), the
    /// <c>MeleeDamage</c> slot of the first-person stat table. Variable 224's handler
    /// <c>0x0051DC70</c> stores each record's value at <c>0x006F2863 + 2 × statId</c>, ignoring its
    /// level, so the last stat-4 signed low word wins; the shipped one is 60 (Q337, ADR-0237). Without one
    /// the table keeps the world initialiser's 100 (<c>0x005099A0</c> fills all 32 words with
    /// <c>0x64</c> at <c>0x00509A88</c>, before the load). Variable 98, once guessed here, has no
    /// reader.
    /// </summary>
    [Dk2Behavior(Confidence.Confirmed, "docs/research/combat-damage.md")]
    public static int PossessionIncreasePercent(GameDatabase database)
        => database.FirstPersonStat(CreatureStatId.MeleeDamage);

    public static Stance StanceOf(CreatureState creature) =>
        creature.Possessed ? Stance.Possessed
        : creature.InfluencedByPossessed ? Stance.InfluencedByPossessed
        : creature.CombatPitFighter ? Stance.CombatPitFighter : Stance.None;

    /// <summary>The stance multiplier in 12.12 fixed point: <c>(ceiling + value) / ceiling</c>,
    /// built by a fixed-point add and divide, so it truncates.</summary>
    public static int StanceMultiplier(GameDatabase database, Stance stance) => stance switch
    {
        Stance.Possessed => Ratio(100, PossessionIncreasePercent(database)),
        Stance.InfluencedByPossessed => Ratio(200, PossessionIncreasePercent(database)),
        Stance.CombatPitFighter =>
            Ratio(100, database[VariableTypeId.CombatPitMeleeDamageModifierPercentage]),
        _ => One,
    };

    private static int Ratio(int ceiling, int add)
    {
        int numerator = unchecked((ceiling + add) << 12);
        // The fixed-divide helper zero-extends its 32-bit numerator before the 64-bit shift.
        return unchecked((int)(((ulong)(uint)numerator << 12) / (uint)(ceiling << 12)));
    }

    /// <summary>The record's <c>MeleeDamage</c> at this level, before any stance applies.</summary>
    public static int BaseDamage(GameDatabase database, CreatureDef attacker, int level) =>
        CreatureLevelRules.Scale(
            attacker.MeleeDamage,
            CreatureLevelRules.Percent(database, CreatureStatId.MeleeDamage, level));

    /// <summary>What the melee-damage accessor returns: a <b>positive</b> figure, which the swing
    /// then negates.</summary>
    public static int Swing(GameDatabase database, CreatureDef attacker, int level, Stance stance) =>
        (int)(((long)StanceMultiplier(database, stance) * BaseDamage(database, attacker, level)) >> 12);

    /// <summary>The bearing bonus's percentage, 0 to 51, from the raw angle between the two things.
    /// A turn is 2048, and the angle is reflected about half a turn before the divide, so the bonus
    /// is symmetric. Purpose UNKNOWN (Q325): nothing in it reads either thing's facing.</summary>
    public static int BearingPercent(int rawAngle)
    {
        int a = rawAngle & 0x7FF;
        if (a > 0x3FF) a = 0x7FE - a;
        return (a & 0x3FF) / 20;
    }

    /// <summary>Applies the bearing bonus, which the original skips entirely at 15 or below.</summary>
    public static int ApplyBearing(int damage, int bearingPercent) =>
        bearingPercent > 15 ? damage + damage * bearingPercent / 100 : damage;

    /// <summary>The two per-player modifiers, each skipped when its signed byte is zero or less.
    /// What they are is UNKNOWN — a keeper spell, a handicap and a difficulty setting would all
    /// look like this.</summary>
    public static int ApplyPlayerModifiers(int damage, int first, int second)
    {
        if (first > 0) damage = damage * (first + 10) * 10 / 100;
        if (second > 0) damage = damage * (second * 15 + 100) / 100;
        return damage;
    }

    /// <summary>The whole blow as the health routine receives it: negative, or zero when the swing
    /// rounds away — and a zero never reaches the health routine at all.</summary>
    public static int Blow(
        GameDatabase database, CreatureDef attacker, int level, Stance stance,
        int rawAngle, int playerModifierFirst, int playerModifierSecond)
    {
        int damage = ApplyBearing(-Swing(database, attacker, level, stance), BearingPercent(rawAngle));
        return damage == 0 ? 0 : ApplyPlayerModifiers(damage, playerModifierFirst, playerModifierSecond);
    }
}
