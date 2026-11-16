// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;

namespace DK2.Presentation;

/// <summary>A pointer ray's hit on a tile's top plane (<see cref="TileHeights.Solid"/> or <see cref="TileHeights.Open"/>),
/// in Unity's axes (x east, y up, z north negated). The adapter casts the ray from its camera; this is the plane math the
/// pointer's map position and the Hand's light share (migration plan step 2i).</summary>
public static class TileTopPlane
{
    /// <summary>The top plane's height of a solid or an open tile.</summary>
    public static float Height(bool solid) => solid ? TileHeights.Solid : TileHeights.Open;

    /// <summary>Where the ray meets the horizontal plane at <paramref name="height"/>, as Unity's <c>Plane.Raycast</c> and
    /// <c>Ray.GetPoint</c> compute it: false for a ray parallel to the plane or one that meets it behind its origin.</summary>
    public static bool TryHit(Vector3 origin, Vector3 direction, float height, out Vector3 point)
    {
        point = default;
        float along = direction.Y;
        // Mathf.Approximately(along, 0): nothing but a zero or a denormal this small passes.
        if (Math.Abs(along) < Math.Max(1e-6f * Math.Abs(along), float.Epsilon * 8)) return false;
        float distance = (-origin.Y - -height) / along;
        if (!(distance > 0)) return false;
        point = new Vector3(origin.X + direction.X * distance, origin.Y + direction.Y * distance, origin.Z + direction.Z * distance);
        return true;
    }

    /// <summary>The pointer's fixed-point map position on <paramref name="tile"/>'s top plane, or null when the ray misses
    /// the plane or the hit lies on another tile.</summary>
    public static FpPosition2? PositionOn(Vector3 origin, Vector3 direction, TilePos tile, bool solid)
    {
        if (!TryHit(origin, direction, Height(solid), out Vector3 point)) return null;
        var position = new FpPosition2(new Fp32((int)MathF.Floor(point.X * Fp32.One)), new Fp32((int)MathF.Floor(-point.Z * Fp32.One)));
        return position.Tile == tile ? position : null;
    }

    /// <summary>The Hand light's map point (x east, y south, tiles) on <paramref name="tile"/>: the ray's hit on its top
    /// plane, or the tile's centre when the ray misses.</summary>
    public static Vector2 HandLightPoint(Vector3 origin, Vector3 direction, TilePos tile, bool solid)
    {
        Vector3 ground = TryHit(origin, direction, Height(solid), out Vector3 point) ? point : new Vector3(tile.X + .5f, 0, -tile.Y - .5f);
        return new Vector2(ground.X, -ground.Z);
    }
}
