// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Simulation.Combat;

namespace DK2.Simulation;

/// <summary>The reasons a creature system passes a creature by for the tick. Each system names the
/// set it applies, so its eligibility reads in one place; which reasons a system applies is that
/// system's rule, and the sets differ on purpose.</summary>
[Flags]
public enum CreatureGuards
{
    None = 0,

    /// <summary>A keeper is possessing it: <see cref="CreatureState.Possessed"/>.</summary>
    Possessed = 1 << 0,

    /// <summary>In the keeper's hand: <see cref="HandRules.IsHeld(CreatureState)"/>.</summary>
    Held = 1 << 1,

    /// <summary>Somebody's prisoner: <see cref="PrisonRules.IsImprisoned"/>.</summary>
    Imprisoned = 1 << 2,

    /// <summary>Collapsed or dying: <see cref="CreatureDeathRules.IsDown"/>.</summary>
    Down = 1 << 3,

    /// <summary>Fighting, possessed or incapacitated: <see cref="CombatRules.Preoccupied"/>.</summary>
    Preoccupied = 1 << 4,

    /// <summary>Not acting on its own at all: possessed, held or down.</summary>
    Sidelined = Possessed | Held | Down,
}

/// <summary>Applies a <see cref="CreatureGuards"/> set. Every guard is a pure read of the creature, so
/// the order they are tested in changes nothing but how soon the test stops.</summary>
public static class CreatureEligibility
{
    public static bool IsExcluded(CreatureState creature, CreatureGuards guards) =>
        ((guards & CreatureGuards.Possessed) != 0 && creature.Possessed)
        || ((guards & CreatureGuards.Held) != 0 && HandRules.IsHeld(creature))
        || ((guards & CreatureGuards.Imprisoned) != 0 && PrisonRules.IsImprisoned(creature))
        || ((guards & CreatureGuards.Down) != 0 && CreatureDeathRules.IsDown(creature))
        || ((guards & CreatureGuards.Preoccupied) != 0 && CombatRules.Preoccupied(creature));
}
