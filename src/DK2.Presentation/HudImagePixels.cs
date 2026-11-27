// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;

namespace DK2.Presentation;

/// <summary>Pixel transforms behind <see cref="HudImageVariant"/>, and the additive stand-in for renderers
/// that cannot blend additively. Pixels are top-down RGBA, 4 bytes each; the source is never modified.
/// The angular masks measure rows from the bottom up, as they were first written against Unity's texture order.</summary>
public static class HudImagePixels
{
    /// <summary><paramref name="image"/>'s variant applied to its catalogue pixels; the source itself when there is none.
    /// <see cref="HudImageVariant.Glow"/> changes the size, so it is <see cref="HudLabelPixels"/>'s and passes through here.</summary>
    public static byte[] Apply(byte[] rgba, int width, int height, in HudImage image)
    {
        if (image.Variant is HudImageVariant.None or HudImageVariant.Glow)
        {
            return rgba;
        }

        byte[] into = new byte[rgba.Length];
        Fill(rgba, width, height, image, into);
        return into;
    }

    /// <summary>Writes the variant's pixels into <paramref name="into"/> (the source's length); a renderer
    /// refilling a live image reuses one buffer.</summary>
    public static void Fill(byte[] rgba, int width, int height, in HudImage image, byte[] into)
    {
        switch (image.Variant)
        {
            case HudImageVariant.None:
            case HudImageVariant.Glow: Buffer.BlockCopy(rgba, 0, into, 0, rgba.Length); break;
            case HudImageVariant.TalismanArc: Talisman(rgba, width, height, image.Pieces, false, into); break;
            case HudImageVariant.TalismanQuarter: Talisman(rgba, width, height, image.Pieces, true, into); break;
            case HudImageVariant.Gauge: Gauge(rgba, width, height, image.Gauge, into); break;
            case HudImageVariant.GreenKey:
                Buffer.BlockCopy(rgba, 0, into, 0, rgba.Length);
                KeyGreen(into.AsSpan(0, rgba.Length));
                break;
            default: throw new ArgumentOutOfRangeException(nameof(image), image.Variant, null);
        }
    }

    /// <summary>The hand cursor sheets' key, in place: a pure green pixel (0, 255, 0) turns transparent and keeps its
    /// colour. Every other pixel is left as it is.</summary>
    public static void KeyGreen(Span<byte> rgba)
    {
        for (int i = 0; i + 3 < rgba.Length; i += 4)
        {
            if (rgba[i] == 0 && rgba[i + 1] == 255 && rgba[i + 2] == 0)
            {
                rgba[i + 3] = 0;
            }
        }
    }

    /// <summary>Keeps the pixels within <paramref name="pieces"/> quarters swept clockwise on screen from
    /// 9 o'clock (top-left first), or only the last of them, clearing the rest to transparent.</summary>
    private static void Talisman(byte[] rgba, int w, int h, int pieces, bool lastQuarterOnly, byte[] into)
    {
        Buffer.BlockCopy(rgba, 0, into, 0, rgba.Length);
        for (int row = 0; row < h; row++)
        {
            int y = h - 1 - row;
            for (int x = 0; x < w; x++)
            {
                float theta = (float)Math.Atan2(x + .5f - w / 2f, h / 2f - y - .5f) * 57.29578f;
                float sweep = Repeat(270 - theta, 360);
                if (sweep > pieces * 90 || lastQuarterOnly && sweep <= (pieces - 1) * 90)
                {
                    into[(row * w + x) * 4 + 3] = 0;
                }
            }
        }
    }

    /// <summary>Pixels the fraction covers keep their colour; the rest turn black and keep their alpha.</summary>
    private static void Gauge(byte[] rgba, int w, int h, HudGaugeFraction fraction, byte[] into)
    {
        for (int row = 0; row < h; row++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = (row * w + x) * 4;
                bool lit = fraction.Contains(GaugeOffset(x, row, w, h));
                into[i] = lit ? rgba[i] : (byte)0;
                into[i + 1] = lit ? rgba[i + 1] : (byte)0;
                into[i + 2] = lit ? rgba[i + 2] : (byte)0;
                into[i + 3] = rgba[i + 3];
            }
        }
    }

    /// <summary>A gauge pixel's centre from the image's centre as the gauge mask measures it: <paramref name="row"/>
    /// counts from the top of the stored image and, as the class remarks, y from its bottom.</summary>
    public static Vector2 GaugeOffset(int x, int row, int width, int height)
    {
        return new(x + .5f - width / 2f, height / 2f - (height - 1 - row) - .5f);
    }

    private static float Repeat(float t, float length)
    {
        return Math.Min(Math.Max(t - (float)Math.Floor(t / length) * length, 0f), length);
    }

    /// <summary>For a renderer with only alpha blending: colour scaled to full brightness and the brightest
    /// channel as alpha, so over a dark ground the draw adds about what an additive draw would.</summary>
    public static byte[] AdditiveOverAlpha(byte[] rgba)
    {
        byte[] to = new byte[rgba.Length];
        for (int i = 0; i < rgba.Length; i += 4)
        {
            int r = rgba[i], g = rgba[i + 1], b = rgba[i + 2], a = rgba[i + 3];
            int peak = Math.Max(r, Math.Max(g, b));
            if (peak == 0)
            {
                continue;
            }

            to[i] = (byte)(r * 255 / peak); to[i + 1] = (byte)(g * 255 / peak); to[i + 2] = (byte)(b * 255 / peak);
            to[i + 3] = (byte)(peak * a / 255);
        }
        return to;
    }
}
