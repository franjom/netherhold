// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Presentation;

/// <summary>Sets <see cref="HudText"/> into pixels: lines wrapped by <see cref="BitmapTextLayout"/>, white
/// with each glyph's 4-bit coverage as alpha. Panels measure with it to lay out; renderers rasterize with it,
/// so every engine gets the same text image.</summary>
public static class HudTextPixels
{
    /// <summary>The image size in font pixels; at least 1 x 1, as an empty text still gets an image.</summary>
    public static (int Width, int Height) Measure(HudText text)
    {
        Bounds(text, out _, out int width, out int height, out _);
        return (width, height);
    }

    /// <summary>Top-down RGBA, 4 bytes a pixel.</summary>
    public static byte[] Render(HudText text, out int width, out int height)
    {
        Bounds(text, out IReadOnlyList<BitmapTextLine> lines, out width, out height, out int minY);
        BitmapFont font = text.Font;
        int widest = Widest(text, lines);
        var pixels = new byte[width * height * 4];
        for (int row = 0; row < lines.Count; row++)
        {
            int pen = Indent(text, lines[row], widest);
            foreach (char c in lines[row].Text)
            {
                BitmapGlyph? glyph = font.Find(c); if (glyph == null) continue;
                for (int y = 0; y < glyph.Height; y++)
                for (int x = 0; x < glyph.Width; x++)
                {
                    int px = pen + glyph.OffsetX + x, py = row * font.LineHeight + y + glyph.OffsetY - minY;
                    // Glyph boxes overlap (a j reaches back under the letter before it): keep the stronger
                    // ink instead of letting one glyph's blank pixels erase its neighbour.
                    if ((uint)px >= (uint)width || (uint)py >= (uint)height) continue;
                    int at = (py * width + px) * 4;
                    byte alpha = (byte)(glyph.Pixels[y * glyph.Width + x] * 17);
                    if (alpha <= pixels[at + 3]) continue;
                    pixels[at] = pixels[at + 1] = pixels[at + 2] = 255; pixels[at + 3] = alpha;
                }
                pen += glyph.Advance;
            }
        }
        return pixels;
    }

    private static void Bounds(HudText text, out IReadOnlyList<BitmapTextLine> lines, out int width, out int height, out int minY)
    {
        BitmapFont font = text.Font;
        lines = BitmapTextLayout.Wrap(font, text.Text, text.WrapWidth > 0 ? text.WrapWidth : 32767);
        width = 1; minY = 0;
        int maxY = font.LineHeight, widest = Widest(text, lines);
        foreach (BitmapTextLine line in lines)
        {
            int pen = Indent(text, line, widest);
            foreach (char c in line.Text)
            {
                BitmapGlyph? glyph = font.Find(c); if (glyph == null) continue;
                width = Math.Max(width, pen + glyph.OffsetX + glyph.Width);
                minY = Math.Min(minY, glyph.OffsetY); maxY = Math.Max(maxY, glyph.OffsetY + glyph.Height);
                pen += glyph.Advance;
            }
        }
        height = Math.Max(1, (Math.Max(1, lines.Count) - 1) * font.LineHeight + maxY - minY);
    }

    private static int Widest(HudText text, IReadOnlyList<BitmapTextLine> lines)
    {
        if (!text.Centred) return 0;
        int widest = 0;
        foreach (BitmapTextLine line in lines) widest = Math.Max(widest, line.Width);
        return widest;
    }

    /// <summary>Where a line's pen starts: 0, or half the gap to the widest line when centred.</summary>
    private static int Indent(HudText text, BitmapTextLine line, int widest) => text.Centred ? (widest - line.Width) / 2 : 0;
}
