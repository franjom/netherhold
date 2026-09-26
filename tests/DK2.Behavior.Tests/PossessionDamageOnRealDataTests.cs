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
/// possession for x1.3.</summary>
public class PossessionDamageOnRealDataTests
{
    [InstallFact]
    public void ThePossessionBonusIsSixtyPercent()
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2InstallGate.Path);

        Assert.Equal(60, MeleeDamageRules.PossessionIncreasePercent(db));
        Assert.Equal(6553, MeleeDamageRules.StanceMultiplier(db, MeleeDamageRules.Stance.Possessed));
        Assert.Equal(5324, MeleeDamageRules.StanceMultiplier(db, MeleeDamageRules.Stance.InfluencedByPossessed));
    }
}
