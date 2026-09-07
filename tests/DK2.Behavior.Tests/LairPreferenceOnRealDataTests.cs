// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

/// <summary>
/// The data behind the original's lair preference (Q151): a creature prefers the
/// nearest lair holding no bed whose owner is of the <i>other</i> alignment, and
/// alignment is the pair <see cref="CreatureFlags.IsEvil"/> /
/// <see cref="CreatureFlags.IsGood"/>. The binary reads the two bits as a pair;
/// this holds the shipped data to being one.
/// </summary>
public class LairPreferenceOnRealDataTests
{
    [InstallFact]
    public void EveryCreatureIsExactlyOneOfEvilAndGood()
    {
        string install = Dk2InstallGate.Path;

        GameDatabase db = GameDatabaseLoader.Load(install);
        foreach (CreatureDef c in db.Creatures.Values)
        {
            bool evil = c.Flags.HasFlag(CreatureFlags.IsEvil);
            bool good = c.Flags.HasFlag(CreatureFlags.IsGood);
            Assert.True(evil != good, $"{c.Name} is evil={evil} good={good}; the two bits must be a complement");
        }
    }

    /// <summary>The named heroes are the good side, and the portal never delivers one.</summary>
    [InstallFact]
    public void TheHeroesAreGoodAndNotAvailableViaPortal()
    {
        string install = Dk2InstallGate.Path;

        GameDatabase db = GameDatabaseLoader.Load(install);
        foreach (string name in new[] { "Knight", "Dwarf", "Giant", "Wizard", "Elven Archer", "Thief", "Monk", "Fairy", "Guard" })
        {
            CreatureDef c = db.CreatureByName(name)!;
            Assert.True(c.Flags.HasFlag(CreatureFlags.IsGood), name);
            Assert.False(c.Flags.HasFlag(CreatureFlags.AvailableViaPortal), name);
        }
    }
}
