// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation;

/// <summary>A triangle list (<see cref="HudDrawKind.Triangles"/>) cut to a round window, the shape an overflow-hidden box
/// with a full border radius shows through, so every renderer draws the same cut. The circle is taken as the regular
/// polygon of <see cref="CircleSides"/> sides whose edges touch it, at most 0.12% of the radius outside it.</summary>
public static class HudTriangles
{
    public const int CircleSides = 64;

    /// <summary>The most vertices one input triangle can become.</summary>
    public const int MaxVerticesPerTriangle = 3 * (CircleSides + 1);

    private static readonly float[] NormalX = new float[CircleSides], NormalY = new float[CircleSides];

    static HudTriangles()
    {
        for (int k = 0; k < CircleSides; k++)
        {
            double angle = 2 * Math.PI * k / CircleSides;
            NormalX[k] = (float)Math.Cos(angle); NormalY[k] = (float)Math.Sin(angle);
        }
    }

    /// <summary>Writes the parts of <paramref name="triangles"/> inside the circle as a triangle list, U and V
    /// interpolated along each cut edge, and returns its vertex count. A triangle with every corner in the circle is
    /// copied unchanged.</summary>
    public static int ClipToCircle(ReadOnlySpan<HudVertex> triangles, float centreX, float centreY, float radius,
        Span<HudVertex> into)
    {
        if (triangles.Length % 3 != 0) throw new ArgumentException("Whole triangles required.", nameof(triangles));
        Span<HudVertex> a = stackalloc HudVertex[CircleSides + 3], b = stackalloc HudVertex[CircleSides + 3];
        float squared = radius * radius;
        int written = 0;
        for (int t = 0; t < triangles.Length; t += 3)
        {
            if (Inside(triangles[t], centreX, centreY, squared) && Inside(triangles[t + 1], centreX, centreY, squared)
                && Inside(triangles[t + 2], centreX, centreY, squared))
            {
                triangles.Slice(t, 3).CopyTo(into.Slice(written));
                written += 3;
                continue;
            }
            triangles.Slice(t, 3).CopyTo(a);
            int count = 3;
            for (int k = 0; k < CircleSides && count > 0; k++)
            {
                count = ClipEdge(a.Slice(0, count), NormalX[k], NormalY[k], centreX, centreY, radius, b);
                Span<HudVertex> swap = a; a = b; b = swap;
            }
            for (int k = 1; k + 1 < count; k++)
            {
                into[written++] = a[0]; into[written++] = a[k]; into[written++] = a[k + 1];
            }
        }
        return written;
    }

    private static bool Inside(in HudVertex v, float centreX, float centreY, float squared)
    {
        float dx = v.X - centreX, dy = v.Y - centreY;
        return dx * dx + dy * dy <= squared;
    }

    /// <summary>One Sutherland-Hodgman pass: the polygon's part on the centre's side of the tangent with outward normal
    /// (<paramref name="nx"/>, <paramref name="ny"/>).</summary>
    private static int ClipEdge(ReadOnlySpan<HudVertex> polygon, float nx, float ny, float centreX, float centreY,
        float radius, Span<HudVertex> into)
    {
        int count = 0;
        HudVertex previous = polygon[polygon.Length - 1];
        float previousOut = (previous.X - centreX) * nx + (previous.Y - centreY) * ny - radius;
        foreach (HudVertex current in polygon)
        {
            float currentOut = (current.X - centreX) * nx + (current.Y - centreY) * ny - radius;
            if ((currentOut <= 0) != (previousOut <= 0))
            {
                float s = previousOut / (previousOut - currentOut);
                into[count++] = new HudVertex(previous.X + (current.X - previous.X) * s, previous.Y + (current.Y - previous.Y) * s,
                    previous.U + (current.U - previous.U) * s, previous.V + (current.V - previous.V) * s);
            }
            if (currentOut <= 0) into[count++] = current;
            previous = current; previousOut = currentOut;
        }
        return count;
    }
}
