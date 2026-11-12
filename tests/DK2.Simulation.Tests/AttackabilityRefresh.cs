// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Simulation.Combat;

namespace DK2.Simulation.Tests;

internal static class AttackabilityRefresh
{
    /// <summary>Stores the cached attackable bit of every creature, as after each one's update.</summary>
    public static void RefreshAttackability(this WorldState world)
    {
        foreach (CreatureState creature in world.Creatures)
        {
            CombatRules.RefreshAttackable(creature);
        }
    }
}
