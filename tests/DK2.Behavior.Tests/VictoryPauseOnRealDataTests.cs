// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

/// <summary>Q376: the victory pause is one play of art slot 35, or slot 8 when 35 is absent, by the
/// action clock (combat-approach-and-victory.md §7). A slot counts as present when its type is
/// non-zero (<c>0x00504D80</c>), whatever its file flag. Predicted from the binary and the shipped
/// clips; no capture times a celebration.</summary>
public class VictoryPauseOnRealDataTests
{
    [InstallTheory]
    [InlineData("Thief", 22)]
    [InlineData("Bile Demon", 19)]
    [InlineData("Horny", 18)]
    [InlineData("Vampire", 15)]
    [InlineData("Goblin", 11)]
    [InlineData("Knight", 7)]
    [InlineData("Warlock", 7)]
    [InlineData("Imp", 5)]
    [InlineData("Dwarf", 4)]
    [InlineData("Stone Knight", 1)]
    public void TheVictoryPauseIsOnePlayOfTheCreaturesClip_PredictedNotObserved(string creature, int turns)
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        CreatureDef definition = db.Creatures.Values.First(c => c.Name == creature);

        Assert.Equal(turns, AnimationTimingRules.VictoryHoldTicks(definition));
    }
}
