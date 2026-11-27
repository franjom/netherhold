// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.


using System.Numerics;

namespace DK2.Presentation;

/// <summary>Rasterizes the projected mesh into four coverage bits per texel, so overlapping faces darken once.</summary>
public static class ClassicShadowCoverage
{
    private const int Size = ClassicMeshShadow.TextureSize;
    private static readonly byte[] Opacity = [0, 63, 63, 127, 63, 127, 127, 191, 63, 127, 127, 191, 127, 191, 191, 255];

    /// <summary>Adds one triangle in texel coordinates to the 32-by-32 mask.</summary>
    public static void AddTriangle(Span<byte> samples, Vector2 a, Vector2 b, Vector2 c)
    {
        float area = Edge(a, b, c);
        if (area == 0)
        {
            return;
        }

        if (area < 0)
        {
            (b, c) = (c, b);
        }

        int left = Math.Max(0, (int)MathF.Floor(MathF.Min(a.X, MathF.Min(b.X, c.X))));
        int right = Math.Min(Size - 1, (int)MathF.Floor(MathF.Max(a.X, MathF.Max(b.X, c.X))));
        int top = Math.Max(0, (int)MathF.Floor(MathF.Min(a.Y, MathF.Min(b.Y, c.Y))));
        int bottom = Math.Min(Size - 1, (int)MathF.Floor(MathF.Max(a.Y, MathF.Max(b.Y, c.Y))));
        for (int y = top; y <= bottom; y++)
        {
            for (int x = left; x <= right; x++)
        {
            int at = y * Size + x;
            int covered = samples[at];
            if (covered == 15)
                {
                    continue;
                }

                if (Contains(a, b, c, new Vector2(x + .25f, y + .25f)))
                {
                    covered |= 1;
                }

                if (Contains(a, b, c, new Vector2(x + .75f, y + .25f)))
                {
                    covered |= 2;
                }

                if (Contains(a, b, c, new Vector2(x + .25f, y + .75f)))
                {
                    covered |= 4;
                }

                if (Contains(a, b, c, new Vector2(x + .75f, y + .75f)))
                {
                    covered |= 8;
                }

                samples[at] = (byte)covered;
        }
        }
    }

    /// <summary>Replaces each sample mask with its opacity for upload to a single-channel texture.</summary>
    public static void Resolve(Span<byte> samples)
    {
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = Opacity[samples[i]];
        }
    }

    private static bool Contains(Vector2 a, Vector2 b, Vector2 c, Vector2 p)
    {
        return Edge(a, b, p) >= 0 && Edge(b, c, p) >= 0 && Edge(c, a, p) >= 0;
    }

    private static float Edge(Vector2 a, Vector2 b, Vector2 p)
    {
        return (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
    }
}
