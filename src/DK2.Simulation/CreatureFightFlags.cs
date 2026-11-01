// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Simulation;

/// <summary>The two bits of <c>creature+0x46F</c> that combat reads (combat-acquisition.md §2, §4).
/// A creature is made with both set (the byte is 9).</summary>
[Flags]
public enum CreatureFightFlags : byte
{
    None = 0,

    /// <summary>Bit 0: without it the fight gate <c>0x0045A6D0</c> refuses unless forced.</summary>
    MayLookForAFight = 0x01,

    /// <summary>Bit 3: without it <c>IsAttackable</c> (<c>0x0045A8B0</c>) is cleared.</summary>
    Attackable = 0x08,
}
