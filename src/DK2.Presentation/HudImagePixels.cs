// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation;

/// <summary>Pixel transforms behind <see cref="HudImageVariant"/>, and the additive stand-in for renderers
/// that cannot blend additively. Pixels are top-down RGBA, 4 bytes each; the source is never modified.</summary>
public static class HudImagePixels
{
    public static byte[] Apply(byte[] rgba, int width, int height, HudImageVariant variant, int pieces) => variant switch
    {
        HudImageVariant.None => rgba,
        HudImageVariant.TalismanArc => Talisman(rgba, width, height, pieces, lastQuarterOnly: false),
        HudImageVariant.TalismanQuarter => Talisman(rgba, width, height, pieces, lastQuarterOnly: true),
        _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, null),
    };

    /// <summary>Keeps the pixels within <paramref name="pieces"/> quarters swept clockwise on screen from
    /// 9 o'clock (top-left first), or only the last of them, clearing the rest to transparent.</summary>
    private static byte[] Talisman(byte[] rgba, int w, int h, int pieces, bool lastQuarterOnly)
    {
        byte[] pixels = (byte[])rgba.Clone();
        for (int row = 0; row < h; row++)
        {
            // Measured from the bottom row up, as the arcs were first written against Unity's texture order.
            int y = h - 1 - row;
            for (int x = 0; x < w; x++)
            {
                float theta = (float)Math.Atan2(x + .5f - w / 2f, h / 2f - y - .5f) * 57.29578f;
                float sweep = Repeat(270 - theta, 360);
                if (sweep > pieces * 90 || lastQuarterOnly && sweep <= (pieces - 1) * 90)
                    pixels[(row * w + x) * 4 + 3] = 0;
            }
        }
        return pixels;
    }

    private static float Repeat(float t, float length) =>
        Math.Min(Math.Max(t - (float)Math.Floor(t / length) * length, 0f), length);

    /// <summary>For a renderer with only alpha blending: colour scaled to full brightness and the brightest
    /// channel as alpha, so over a dark ground the draw adds about what an additive draw would.</summary>
    public static byte[] AdditiveOverAlpha(byte[] rgba)
    {
        var to = new byte[rgba.Length];
        for (int i = 0; i < rgba.Length; i += 4)
        {
            int r = rgba[i], g = rgba[i + 1], b = rgba[i + 2], a = rgba[i + 3];
            int peak = Math.Max(r, Math.Max(g, b));
            if (peak == 0) continue;
            to[i] = (byte)(r * 255 / peak); to[i + 1] = (byte)(g * 255 / peak); to[i + 2] = (byte)(b * 255 / peak);
            to[i + 3] = (byte)(peak * a / 255);
        }
        return to;
    }
}
