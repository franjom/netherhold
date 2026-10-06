// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Data.Model;

/// <summary>What an availability row of a keeper's table is about: the kind field of KWD
/// Availability records (<c>AvailabilityKind</c> in the trigger action) and of the simulation's
/// <c>AvailabilityState</c>. 16 bits wide, as the simulation stores it. Not the research list's kind:
/// <c>ResearchTreeRules.AvailabilityKindOf</c> maps that one onto this.</summary>
public enum AvailabilityKind : ushort
{
    None = 0,
    Room = 1,
    Creature = 2,
    Trap = 3,
    Door = 4,
    Spell = 5,
}
