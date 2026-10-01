// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

/// <summary>Which shipped creatures play the Angry flourish in prison: those whose Angry animation
/// (block 9) has a resource type of 1..8, which sets the runtime bit <c>CreatureDef_HasArt(9)</c> tests (Q188).</summary>
public class PrisonFlourishOnRealDataTests
{
    [InstallFact]
    public void AngryFlourish_FollowsLoadedArtNotFileBit0_Q188()
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2InstallGate.Path);

        int[] flourishing = db.Creatures.Values
            .Where(PrisonRules.HasAngryFlourish)
            .Select(c => (int)c.CreatureId)
            .Order()
            .ToArray();

        Assert.Equal(39, flourishing.Length);
        Assert.All(db.Creatures.Values, c => Assert.Equal(c.AngryArtType != 0, PrisonRules.HasAngryFlourish(c)));
        // The Bile Demon has no file flag and flourishes; Imp, a Skeleton and Jack have no Angry art.
        CreatureDef bile = db.Creatures.Values.Single(c => c.Name == "Bile Demon");
        Assert.Equal(0u, bile.AngryArtFlags & 1);
        Assert.True(PrisonRules.HasAngryFlourish(bile));
        foreach (string none in new[] { "Imp", "Skeleton", "Jack" })
            Assert.False(PrisonRules.HasAngryFlourish(db.Creatures.Values.Single(c => c.Name == none)));

        CreatureDef firefly = db.CreatureById(12);
        Assert.Equal((34, 30), (firefly.AngryFrames, firefly.AngryFramesPerSecond));
        Assert.Equal(14, PrisonRules.FlourishTicks(firefly, variant: 2));
    }
}
