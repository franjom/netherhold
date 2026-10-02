// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

/// <summary>Who reveals traps and which traps start hidden (traps.md §11), as shipped.</summary>
public class TrapRevealOnRealDataTests
{
    [InstallFact]
    public void TheTrollThiefRogueAndGuardRevealTraps_AndNineTrapTypesStartHidden()
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        Assert.Equal(["Troll", "Thief", "Rogue", "Guard"],
            db.Creatures.Values.Where(c => c.Flags.HasFlag(CreatureFlags.RevealsAdjacentTraps)).OrderBy(c => c.CreatureId).Select(c => c.Name));
        Assert.Equal([1, 4, 5, 7, 9, 10, 12, 13, 15],
            db.Traps.Values.Where(t => (t.Flags & TrapRules.StartsHidden) != 0).OrderBy(t => t.TrapId).Select(t => (int)t.TrapId));
    }
}
