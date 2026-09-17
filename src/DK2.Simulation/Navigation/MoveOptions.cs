// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation.Navigation;

/// <summary>The option word a creature state hands the route request (<c>0x004D0F60</c>;
/// docs/research/steering-behaviours.md §7.6). Bits 1, 2 and 7 are door rules for the cost
/// function, parked in <c>creature+0x3E6</c>; bits 3–6 become the body's <c>path_flags</c>:
/// bit 3 → tight arrival, bit 4 → no final approach, bit 5 or 6 → snap and separate until
/// arrival. Bit 0 is the state helper's own: keep the state on a failed request. The named
/// words are the ones the state handlers push; most push <see cref="Loose"/>.</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/steering-behaviours.md")]
public static class MoveOptions
{
    /// <summary>Imps en route (states 8 and 9), attacking, patrol offsets: no door rule, no flags.</summary>
    public const int Plain = 0x00;

    /// <summary>The common word (45 of 134 read sites): respect every door, arrive loosely.
    /// Room jobs, prisoners held, wandering, the walk to a hatchery.</summary>
    public const int Loose = 0x80;

    /// <summary>Making a home (state 80): respect doors, snap onto the spot.</summary>
    public const int Spot = 0xE0;

    /// <summary>Going to bed (81), the chicken (92), wages (187), gambling, celebrating:
    /// respect doors, snap onto the spot, arrive within a tenth of the radius.</summary>
    public const int ExactSpot = 0xE8;

    /// <summary>A captive's chicken (124), freeing comrades (121): the exact spot with door
    /// bit 2 ignored and no door rule.</summary>
    public const int CaptiveSpot = 0x6C;

    /// <summary>Leaving the dungeon (195): the exact spot, own locked doors ignored too.</summary>
    public const int Leaving = 0x6E;

    /// <summary>What <c>0x004D0F60</c> stores in <c>path_flags</c> for an option word.</summary>
    public static byte PathFlagsOf(int options)
    {
        int flags = 0;
        if ((options & 0x08) != 0) flags |= 1;
        if ((options & 0x10) != 0) flags |= 2;
        if ((options & 0x60) != 0) flags |= 0xC;
        return (byte)flags;
    }
}
