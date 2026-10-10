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
[Flags]
public enum MoveOptions
{
    /// <summary>Imps en route (states 8 and 9), attacking, patrol offsets: no door rule, no flags.</summary>
    Plain = 0x00,

    /// <summary>The common word (45 of 134 read sites): respect every door, arrive loosely.
    /// Room jobs, prisoners held, wandering, the walk to a hatchery.</summary>
    Loose = 0x80,

    /// <summary>Making a home (state 80): respect doors, snap onto the spot.</summary>
    Spot = 0xE0,

    /// <summary>Going to bed (81), the chicken (92), wages (187), gambling, celebrating:
    /// respect doors, snap onto the spot, arrive within a tenth of the radius.</summary>
    ExactSpot = 0xE8,

    /// <summary>Cross a walled room interior while retaining the other route rules.</summary>
    CrossRoomInterior = 0x04,

    /// <summary>State 44's delivery into a Prison cell: respect doors and snap to the spot.</summary>
    PrisonDelivery = 0xE4,

    /// <summary>A captive's chicken (124), freeing comrades (121): the exact spot with door
    /// bit 2 ignored and no door rule.</summary>
    CaptiveSpot = 0x6C,

    /// <summary>Leaving the dungeon (195): the exact spot, own locked doors ignored too.</summary>
    Leaving = 0x6E,
}

public static class MoveOptionsExtensions
{

    /// <summary>What <c>0x004D0F60</c> stores in <c>path_flags</c> for an option word.</summary>
    public static byte PathFlags(this MoveOptions options)
    {
        int word = (int)options;
        int flags = 0;
        if ((word & 0x08) != 0) flags |= 1;
        if ((word & 0x10) != 0) flags |= 2;
        if ((word & 0x60) != 0) flags |= 0xC;
        return (byte)flags;
    }
}
