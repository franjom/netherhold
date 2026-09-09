// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>
/// A ray in world space, in tile units: one unit is one tile, y is up, and a tile
/// <c>(x, z)</c> occupies <c>[x, x+1) × [z, z+1)</c> at ground level.
/// </summary>
public readonly record struct PickRay(
    float OriginX, float OriginY, float OriginZ,
    float DirectionX, float DirectionY, float DirectionZ);

/// <summary>
/// Which tile a pointer is over, given a ray and how tall the renderer draws things.
/// <para>Renderer-independent on purpose. Casting the ray at the ground plane instead —
/// the obvious thing, and what the Unity adapter did first — reads the tile <i>behind</i>
/// whatever wall the pointer is on, because the ray passes over the wall top and carries
/// on to y=0. At a 55° camera pitch and a wall one tile tall that is 0.7 tiles of error,
/// which is a whole tile as often as not.</para>
/// </summary>
public static class TilePicker
{
    /// <summary>Enough steps to cross the grid diagonally from outside it, whatever the
    /// angle. A ray that finds nothing in this many steps is pointing away from the
    /// dungeon.</summary>
    private const int StepLimit = 4;

    /// <summary>
    /// The first tile whose column the ray meets, or null if it meets none.
    /// </summary>
    /// <param name="view">Decides each tile's solidity, so a tile hidden by fog is picked
    /// as the fog the viewer can see rather than as what is really there.</param>
    /// <param name="solidHeight">How tall the renderer draws a tile that fills its cell.</param>
    /// <param name="openHeight">How tall it draws one you could stand on.</param>
    /// <remarks>Floating point, deliberately: this is a pointer over a picture, it reaches
    /// no state and no hash, and ADR-0001 bans float in <i>gameplay</i> maths.</remarks>
    public static TilePos? Pick(
        in PickRay ray,
        WorldView view,
        WorldState world,
        float solidHeight,
        float openHeight)
    {
        if (view is null) throw new ArgumentNullException(nameof(view));
        if (world is null) throw new ArgumentNullException(nameof(world));

        // Only a ray on its way down can land on the dungeon floor.
        if (!(ray.DirectionY < 0f)) return null;

        int x = (int)MathF.Floor(ray.OriginX);
        int z = (int)MathF.Floor(ray.OriginZ);
        int stepX = ray.DirectionX < 0f ? -1 : 1;
        int stepZ = ray.DirectionZ < 0f ? -1 : 1;

        // Distance along the ray to the next grid line on each axis, and the distance
        // between successive ones — the standard grid march, one tile per iteration.
        float nextX = Boundary(ray.OriginX, ray.DirectionX, x, stepX);
        float nextZ = Boundary(ray.OriginZ, ray.DirectionZ, z, stepZ);
        float spanX = Span(ray.DirectionX);
        float spanZ = Span(ray.DirectionZ);

        int limit = StepLimit * (world.Width + world.Height);
        for (int step = 0; step < limit; step++)
        {
            float exit = MathF.Min(nextX, nextZ);
            float exitY = ray.OriginY + (ray.DirectionY * exit);

            if (world.Contains(x, z))
            {
                float height = view.TileAt(world, x, z).Solid ? solidHeight : openHeight;
                if (exitY <= height) return new TilePos(x, z);
            }

            // Below the lowest thing it could have hit, and still falling.
            if (exitY < 0f) return null;

            if (nextX <= nextZ) { x += stepX; nextX += spanX; }
            else { z += stepZ; nextZ += spanZ; }
        }

        return null;
    }

    private static float Boundary(float origin, float direction, int tile, int step) =>
        direction == 0f
            ? float.PositiveInfinity
            : (step > 0 ? tile + 1 - origin : tile - origin) / direction;

    private static float Span(float direction) =>
        direction == 0f ? float.PositiveInfinity : MathF.Abs(1f / direction);
}
