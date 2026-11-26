// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;

namespace DK2.Presentation;

/// <summary>Native whole-map ribbon in the HUD's 1024×768 reference coordinates;
/// corners are tile-space top-left, top-right, bottom-right, bottom-left.</summary>
public static class MinimapCameraFootprint
{
    public static bool TryBuild(ReadOnlySpan<Vector2> groundCorners, int mapWidth, int mapHeight,
        float headingRadians, Span<Vector2> vertices)
    {
        if (groundCorners.Length != 4 || vertices.Length < 8)
        {
            throw new ArgumentException("Four corners and eight output vertices required.");
        }

        if (mapWidth <= 0 || mapHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mapWidth));
        }

        if (!Finite(headingRadians))
        {
            return false;
        }

        float scale = .703125f * -82.4f / (MathF.Sin(1.5f * MathF.PI - .4266275f) * .5f)
            / Math.Max(mapWidth, mapHeight);
        float cosine = MathF.Cos(headingRadians), sine = MathF.Sin(headingRadians);
        Vector2 centre = Vector2.Zero;
        for (int i = 0; i < 4; i++)
        {
            Vector2 p = groundCorners[i] - new Vector2(mapWidth * .5f, mapHeight * .5f);
            p = new Vector2(cosine * p.X + sine * p.Y, -sine * p.X + cosine * p.Y) * scale
                + new Vector2(95.2f, 672);
            if (!Finite(p.X) || !Finite(p.Y))
            {
                return false;
            }

            vertices[i * 2] = p;
            centre += p * .25f;
        }
        if (!Finite(centre.X) || !Finite(centre.Y))
        {
            return false;
        }

        for (int i = 0; i < 4; i++)
        {
            Vector2 p = vertices[i * 2];
            p = new Vector2(Math.Clamp(p.X, 12.8f, 177.6f), Math.Clamp(p.Y, 588.8f, 755.2f));
            Vector2 direction = p - centre;
            float length = direction.Length();
            Vector2 offset = length > 0 ? direction * (2 / length) : Vector2.Zero;
            vertices[i * 2] = p + offset;
            vertices[i * 2 + 1] = p - offset;
        }
        return true;
    }

    /// <summary>The ribbon's index list (minimap-rendering.md, pairs in corner order, outward then inward).</summary>
    private static readonly byte[] Indices = { 0, 2, 1, 1, 2, 3, 2, 4, 3, 3, 4, 5, 5, 4, 6, 7, 5, 6, 0, 7, 6, 0, 1, 7 };

    /// <summary>The vertices of <see cref="Triangles"/>' list: eight triangles.</summary>
    public const int TriangleVertices = 24;

    /// <summary><see cref="TryBuild"/>'s eight vertices as the textured triangle list in virtual HUD units (one 1024x768
    /// unit is 2.5): each outward vertex at the image's top (V 0), each inward one at its bottom (V 1), U one half.</summary>
    public static void Triangles(ReadOnlySpan<Vector2> vertices, Span<HudVertex> triangles)
    {
        if (vertices.Length < 8 || triangles.Length < TriangleVertices)
        {
            throw new ArgumentException("Eight vertices and room for the triangle list required.");
        }

        for (int i = 0; i < TriangleVertices; i++)
        {
            int k = Indices[i];
            triangles[i] = new HudVertex(vertices[k].X * 2.5f, vertices[k].Y * 2.5f, .5f, k % 2);
        }
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
