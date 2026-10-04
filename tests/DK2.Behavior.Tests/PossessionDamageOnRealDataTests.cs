// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

/// <summary>The possession bonus the shipped data gives (Q337, ADR-0237): variable 224's
/// <c>MeleeDamage</c> record, 60, so a possessed creature hits for x1.6 and one influenced by
/// possession for x0.8 before fixed-point truncation.</summary>
public class PossessionDamageOnRealDataTests
{
    [InstallFact]
    public void ThePossessionBonusIsSixtyPercent()
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2InstallGate.Path);

        Assert.Equal(60, MeleeDamageRules.PossessionIncreasePercent(db));
        Assert.Equal(6553, MeleeDamageRules.StanceMultiplier(db, MeleeDamageRules.Stance.Possessed));
        Assert.Equal(3276, MeleeDamageRules.StanceMultiplier(db, MeleeDamageRules.Stance.InfluencedByPossessed));
    }

    /// <summary>DK2 writes variable 224 record by record, so a level's own records override only
    /// the stats they name. Ours replaces the list when a level has any, which would lose the
    /// global 60 on a level that lists other first-person stats. No shipped level does.</summary>
    [InstallFact]
    public void EveryShippedLevelKeepsTheSixtyPercent()
    {
        string maps = System.IO.Path.Combine(Dk2InstallGate.Path, "Data", "editor", "maps");
        string[] levels = System.IO.Directory.GetFiles(maps, "*Variables.kld")
            .Select(f => System.IO.Path.GetFileName(f)[..^"Variables.kld".Length]).ToArray();
        Assert.True(levels.Length >= 60, $"found {levels.Length} level variable files");

        foreach (string level in levels)
            Assert.True(
                MeleeDamageRules.PossessionIncreasePercent(GameDatabaseLoader.LoadForCampaignLevel(Dk2InstallGate.Path, level)) == 60,
                level);
    }
}
