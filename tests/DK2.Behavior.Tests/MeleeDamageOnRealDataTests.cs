// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

/// <summary>
/// The melee damage formula against the shipped table rather than a transcription of it
/// (docs/research/combat-damage.md).
///
/// The formula's inputs are three numbers out of the install — the MeleeDamage level row and two
/// balance variables — and the synthetic fixture copies all three. A copy that quietly stopped
/// matching would make every unit test of the formula worthless, so this re-reads them.
///
/// Gated with [InstallFact], so it reports SKIPPED without an installation rather than passing
/// without proprietary assets.
/// </summary>
public class MeleeDamageOnRealDataTests
{
    private const byte GoblinId = 7;

    private static GameDatabase RealDatabase() => GameDatabaseLoader.Load(Dk2InstallGate.Path);

    [InstallFact]
    public void TheMeleeDamageRowIsWhatTheFixtureSaysItIs()
    {
        GameDatabase db = RealDatabase();

        int[] shipped = [125, 150, 175, 200, 225, 250, 300, 400, 600];
        for (int level = 2; level <= 10; level++)
            Assert.Equal(shipped[level - 2], db.StatPercent(CreatureStatId.MeleeDamage, level));

        Assert.Equal(100, db.StatPercent(CreatureStatId.MeleeDamage, 1));
    }

    /// <summary>The Combat Pit ships 100 against a ceiling of 100, so its "modifier percentage" is
    /// an increment and a pit fighter hits for double. Pinned here because the name argues for the
    /// other reading and only the binary settles it.</summary>
    [InstallFact]
    public void TheCombatPitVariableShipsAHundredAndDoublesTheBlow()
    {
        GameDatabase db = RealDatabase();
        Assert.Equal(100, db[VariableTypeId.CombatPitMeleeDamageModifierPercentage]);

        CreatureDef goblin = db.CreatureById(GoblinId);
        Assert.Equal(170, MeleeDamageRules.Swing(db, goblin, 1, MeleeDamageRules.Stance.None));
        Assert.Equal(340, MeleeDamageRules.Swing(db, goblin, 1, MeleeDamageRules.Stance.CombatPitFighter));
    }

    /// <summary>The two shipped possession percentages stand in the exact 2:1 ratio the original's
    /// two ceilings produce from one value, which is the whole evidence for Q337's hypothesis that
    /// the scalar it actually reads carries the first-person figure. If this pair ever stops being
    /// 12 and 6, the hypothesis loses its only support and the rule must go back to UNKNOWN.</summary>
    [InstallFact]
    public void TheTwoPossessionPercentagesAreTwelveAndSix()
    {
        GameDatabase db = RealDatabase();
        Assert.Equal(12, db[VariableTypeId.FirstPersonStatIncreasePercentage]);
        Assert.Equal(6, db[VariableTypeId.PossessionFriendlyStatIncreasePercentage]);
    }

    [InstallFact]
    public void ALevelTenGoblinSwingsForSixHundredPercent()
    {
        GameDatabase db = RealDatabase();
        CreatureDef goblin = db.CreatureById(GoblinId);

        Assert.Equal(1020, MeleeDamageRules.Swing(db, goblin, 10, MeleeDamageRules.Stance.None));
        Assert.Equal(-1020, MeleeDamageRules.Blow(db, goblin, 10, MeleeDamageRules.Stance.None, 0, 0, 0));
    }

    /// <summary>Every shipped creature's blow is representable, which is the cheap guard against a
    /// stat row or a record value that overflows the multiply the original does in 32 bits.</summary>
    [InstallFact]
    public void NoShippedCreatureOverflowsAtLevelTen()
    {
        GameDatabase db = RealDatabase();

        Assert.All(db.Creatures.Values, creature =>
        {
            int swing = MeleeDamageRules.Swing(db, creature, 10, MeleeDamageRules.Stance.CombatPitFighter);
            Assert.InRange(swing, 0, 1 << 20);
        });
    }
}
