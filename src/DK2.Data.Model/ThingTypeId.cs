// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Data.Model;

/// <summary>
/// DK2's entity ("thing") categories.
/// CONFIRMED: Flame `src/dk2/entities/entities_type.h`.
/// </summary>
public enum ThingTypeId : byte
{
    Creature = 0,
    Shot = 1,
    Object = 2,
    Trap = 3,
    Door = 4,
    ActionPoint = 5,
    DeadBody = 6,
    EffectGenerator = 7,
}
