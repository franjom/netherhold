// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Simulation;
using DK2.Data.Model;

namespace DK2.Presentation;

public static class CreatureBurningVisual
{
    public static bool ShowsRed(CreatureState creature, CreatureFlags flags) => !flags.HasFlag(CreatureFlags.IsHorny) && creature.BurningTicks != 0
        && creature.FrozenTicks == 0 && creature.PoisonTicks == 0 && creature.InvisibleTicks == 0;
}
