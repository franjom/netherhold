// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

/// <summary>Which shipped creatures play the Angry flourish in prison: those whose Angry animation
/// (block 9) carries flag bit 0, the bit <c>CreatureDef_HasArt(9)</c> tests (Q188).</summary>
public class PrisonFlourishOnRealDataTests
{
    [InstallFact]
    public void ThirteenShippedCreaturesHaveTheFlourishAndTheGoblinDoesNot()
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2InstallGate.Path);

        int[] flourishing = db.Creatures.Values
            .Where(PrisonRules.HasAngryFlourish)
            .Select(c => (int)c.CreatureId)
            .Order()
            .ToArray();

        Assert.Equal([2, 12, 15, 17, 20, 23, 26, 29, 30, 31, 32, 44, 45], flourishing);
        Assert.False(PrisonRules.HasAngryFlourish(db.CreatureById(7)));

        CreatureDef firefly = db.CreatureById(12);
        Assert.Equal((34, 30), (firefly.AngryFrames, firefly.AngryFramesPerSecond));
        Assert.Equal(14, PrisonRules.FlourishTicks(firefly, variant: 2));
    }
}
