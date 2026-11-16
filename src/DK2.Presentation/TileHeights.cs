// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation;

/// <summary>Tile heights in the renderers' tile units (one unit is DK2's 0x1000), shared by the scene that draws the tiles
/// and <see cref="TilePicker"/>, which must agree with it on what the pointer is over.</summary>
public static class TileHeights
{
    /// <summary>DK2's ordinary floor height, 0x1000 (<c>0x004545C0</c>; pits, pools and water lie lower). Heights here are
    /// DK2's own, so a torch's 1.5 is mid-wall.</summary>
    public const float Floor = 1f;

    /// <summary>The top of a tile whose terrain fills its cell: every solid terrain KMF in the install spans heights 1 to 2,
    /// a wall one tile above the floor.</summary>
    public const float Solid = 2f;

    /// <summary>The top of a tile you could stand on, the floor.</summary>
    public const float Open = Floor;
}
