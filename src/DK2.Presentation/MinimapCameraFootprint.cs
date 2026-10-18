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
        if (groundCorners.Length != 4 || vertices.Length < 8) throw new ArgumentException("Four corners and eight output vertices required.");
        if (mapWidth <= 0 || mapHeight <= 0) throw new ArgumentOutOfRangeException(nameof(mapWidth));
        if (!Finite(headingRadians)) return false;
        float scale = .703125f * -82.4f / (MathF.Sin(1.5f * MathF.PI - .4266275f) * .5f)
            / Math.Max(mapWidth, mapHeight);
        float cosine = MathF.Cos(headingRadians), sine = MathF.Sin(headingRadians);
        Vector2 centre = Vector2.Zero;
        for (int i = 0; i < 4; i++)
        {
            Vector2 p = groundCorners[i] - new Vector2(mapWidth * .5f, mapHeight * .5f);
            p = new Vector2(cosine * p.X + sine * p.Y, -sine * p.X + cosine * p.Y) * scale
                + new Vector2(95.2f, 672);
            if (!Finite(p.X) || !Finite(p.Y)) return false;
            vertices[i * 2] = p;
            centre += p * .25f;
        }
        if (!Finite(centre.X) || !Finite(centre.Y)) return false;
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

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
