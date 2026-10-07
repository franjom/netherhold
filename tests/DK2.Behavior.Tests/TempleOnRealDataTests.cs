// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

/// <summary>The Temple's states against the shipped v1.70 data (temple-states.md, ADR-0398).
/// Gated with [InstallFact].</summary>
public class TempleOnRealDataTests
{
    private static GameDatabase Db() => GameDatabaseLoader.LoadForCampaignLevel(Dk2InstallGate.Path, "level3");

    /// <summary>The definition's vector 1 (file <c>0x127B</c>) is the step 105 takes when a prayer
    /// ends: −y ahead, and x 0 for every creature.</summary>
    [InstallFact]
    public void TheShippedPrayerStepIsWhatTheRecordsHold()
    {
        GameDatabase db = Db();
        CreatureDef Named(string name) => db.Creatures.Values.Single(c => c.Name == name);
        Assert.Equal(-1257, Named("Goblin").PrayerStepY);
        Assert.Equal(-1257, Named("Vampire").PrayerStepY);
        Assert.Equal(-1675, Named("Troll").PrayerStepY);
        Assert.Equal(-843, Named("Rogue").PrayerStepY);
        Assert.Equal(0, Named("Bile Demon").PrayerStepY);
        Assert.All(db.Creatures.Values, c => Assert.Equal(0, c.PrayerStepX));
    }
}
