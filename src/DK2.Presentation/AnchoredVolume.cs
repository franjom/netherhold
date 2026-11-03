// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;

namespace DK2.Presentation;

/// <summary>The volume <c>Casino_RoomUpdate</c> gives its party music (<c>0x004D63E6..0x004D6432</c>): the larger of the two axis distances between the
/// camera's point and the room's tile, capped at <c>0x2D000</c> (45 tiles), scales a base volume down linearly to zero. The base is a global byte
/// (<c>0x0075AD17</c>) read here as full; DK2 sets the volume to 1 when the tile is not explored, which ours does not test.</summary>
public static class AnchoredVolume
{
    public const float Tiles = 45f;

    /// <param name="anchor">The tile's point in the view's space.</param>
    /// <param name="focus">The camera's point in the same space, or none, which gives full volume.</param>
    public static float Of(Vector3 anchor, Vector3? focus)
    {
        if (focus is not { } at) return 1f;
        float distance = Math.Min(Math.Max(Math.Abs(anchor.X - at.X), Math.Abs(anchor.Z - at.Z)), Tiles);
        return 1f - (distance / Tiles);
    }
}
