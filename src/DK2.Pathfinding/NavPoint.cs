// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Pathfinding;

/// <summary>A position in the router's units: <see cref="NavMesh.UnitsPerTile"/> per tile, which
/// is a creature position in 1/4096 tiles shifted right by four, as the original's request does.</summary>
public readonly record struct NavPoint(int X, int Y)
{
    public static NavPoint FromTileCentre(int tileX, int tileY) =>
        new((tileX * NavMesh.UnitsPerTile) + (NavMesh.UnitsPerTile / 2),
            (tileY * NavMesh.UnitsPerTile) + (NavMesh.UnitsPerTile / 2));

    public int TileX => X / NavMesh.UnitsPerTile;
    public int TileY => Y / NavMesh.UnitsPerTile;
}

/// <summary>What the original's request setup (`0x004D0E60`) reads off the mover before routing:
/// the two definition flags that unlock water and lava (navigation-mesh-and-search.md §4). The
/// door-related request options are absent until doors exist in the navigation bytes.</summary>
public readonly record struct MoverProfile(
    bool CanWalkOnWater,
    bool CanWalkOnLava,
    byte Owner = 0,
    bool RespectDoors = false,
    bool PassOwnLockedDoors = false,
    bool IgnoreLockMismatch = false)
{
    public static readonly MoverProfile Walker = new(false, false);
}
