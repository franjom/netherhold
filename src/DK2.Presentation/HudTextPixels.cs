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
        byte[] pixels = new byte[width * height * 4];
        for (int row = 0; row < lines.Count; row++)
        {
            int pen = Indent(text, lines[row], widest);
            foreach (char c in lines[row].Text)
            {
                BitmapGlyph? glyph = font.Find(c); if (glyph == null)
                {
                    continue;
                }

                for (int y = 0; y < glyph.Height; y++)
                {
                    for (int x = 0; x < glyph.Width; x++)
                {
                    int px = pen + glyph.OffsetX + x, py = row * font.LineHeight + y + glyph.OffsetY - minY;
                    // Glyph boxes overlap (a j reaches back under the letter before it): keep the stronger
                    // ink instead of letting one glyph's blank pixels erase its neighbour.
                    if ((uint)px >= (uint)width || (uint)py >= (uint)height)
                        {
                            continue;
                        }

                        int at = (py * width + px) * 4;
                    byte alpha = (byte)(glyph.Pixels[y * glyph.Width + x] * 17);
                    if (alpha <= pixels[at + 3])
                        {
                            continue;
                        }

                        pixels[at] = pixels[at + 1] = pixels[at + 2] = 255; pixels[at + 3] = alpha;
                }
                }

                pen += glyph.Advance;
            }
        }
        return pixels;
    }

    /// <summary>The text as glyph pieces for a renderer that draws from one glyph atlas per font rather than a texture
    /// per text: each piece copies a part of a glyph (coverage x 17 as alpha) to its place in the <see cref="Render"/>
    /// image. No pixel is inked by two pieces, so drawing them in order, an uninked pixel leaving what is under it,
    /// gives <see cref="Render"/>'s image exactly: where inked boxes overlap, the stronger ink's glyph keeps the pixel
    /// (the earlier on a tie, as <see cref="Render"/> keeps it) and the other is drawn around it in one-row runs.</summary>
    public static void Pieces(HudText text, List<HudTextPiece> into, out int width, out int height)
    {
        into.Clear();
        Bounds(text, out IReadOnlyList<BitmapTextLine> lines, out width, out height, out int minY);
        BitmapFont font = text.Font;
        int widest = Widest(text, lines);
        for (int row = 0; row < lines.Count; row++)
        {
            int pen = Indent(text, lines[row], widest);
            foreach (char c in lines[row].Text)
            {
                BitmapGlyph? glyph = font.Find(c); if (glyph == null)
                {
                    continue;
                }

                int px = pen + glyph.OffsetX, py = row * font.LineHeight + glyph.OffsetY - minY;
                pen += glyph.Advance;
                // Render's clip to the image, then the box shrunk to its ink: a blank glyph adds no piece.
                int left = Math.Max(0, -px), top = Math.Max(0, -py);
                int right = Math.Min(glyph.Width, width - px), bottom = Math.Min(glyph.Height, height - py);
                if (!Ink(glyph, ref left, ref top, ref right, ref bottom))
                {
                    continue;
                }

                into.Add(new HudTextPiece(glyph, left, top, px + left, py + top, right - left, bottom - top));
            }
        }
        Resolve(into, width, height);
    }

    /// <summary>Shrinks a glyph-space box to the glyph's inked pixels inside it; false when there are none.</summary>
    private static bool Ink(BitmapGlyph glyph, ref int left, ref int top, ref int right, ref int bottom)
    {
        int l = int.MaxValue, t = int.MaxValue, r = int.MinValue, b = int.MinValue;
        for (int y = top; y < bottom; y++)
        {
            for (int x = left; x < right; x++)
            {
                if (glyph.Pixels[y * glyph.Width + x] == 0)
                {
                    continue;
                }

                l = Math.Min(l, x); r = Math.Max(r, x + 1); t = Math.Min(t, y); b = Math.Max(b, y + 1);
            }
        }

        if (r <= l)
        {
            return false;
        }

        left = l; top = t; right = r; bottom = b;
        return true;
    }

    /// <summary>Gives each doubly inked pixel to the glyph <see cref="Render"/> keeps there and redraws every other glyph
    /// inked there as one-row runs that skip it. Usually nothing to do: glyph ink rarely meets.</summary>
    private static void Resolve(List<HudTextPiece> pieces, int width, int height)
    {
        byte[] alpha = new byte[width * height];
        bool shared = false;
        foreach (HudTextPiece p in pieces)
        {
            for (int y = p.Y; y < p.Y + p.Height; y++)
            {
                for (int x = p.X; x < p.X + p.Width; x++)
                {
                    byte a = p.Alpha(x, y);
                    if (a == 0)
                    {
                        continue;
                    }

                    shared |= alpha[y * width + x] != 0;
                    alpha[y * width + x] = Math.Max(alpha[y * width + x], a);
                }
            }
        }

        if (!shared)
        {
            return;
        }

        Array.Clear(alpha, 0, alpha.Length);
        int[] owner = new int[width * height];
        for (int i = 0; i < pieces.Count; i++)
        {
            HudTextPiece p = pieces[i];
            for (int y = p.Y; y < p.Y + p.Height; y++)
            {
                for (int x = p.X; x < p.X + p.Width; x++)
                {
                    byte a = p.Alpha(x, y);
                    if (a <= alpha[y * width + x])
                    {
                        continue;
                    }

                    alpha[y * width + x] = a; owner[y * width + x] = i;
                }
            }
        }
        var resolved = new List<HudTextPiece>(pieces.Count);
        for (int i = 0; i < pieces.Count; i++)
        {
            HudTextPiece p = pieces[i];
            bool loses = false;
            for (int y = p.Y; y < p.Y + p.Height && !loses; y++)
            {
                for (int x = p.X; x < p.X + p.Width && !loses; x++)
                {
                    loses = p.Alpha(x, y) > 0 && owner[y * width + x] != i;
                }
            }

            if (!loses) { resolved.Add(p); continue; }
            for (int y = p.Y; y < p.Y + p.Height; y++)
            {
                int start = p.X;
                for (int x = p.X; x <= p.X + p.Width; x++)
                {
                    bool cut = x == p.X + p.Width || p.Alpha(x, y) > 0 && owner[y * width + x] != i;
                    if (!cut)
                    {
                        continue;
                    }

                    if (x > start)
                    {
                        resolved.Add(new HudTextPiece(p.Glyph, p.SourceX + start - p.X, p.SourceY + y - p.Y, start, y, x - start, 1));
                    }

                    start = x + 1;
                }
            }
        }
        pieces.Clear(); pieces.AddRange(resolved);
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
                BitmapGlyph? glyph = font.Find(c); if (glyph == null)
                {
                    continue;
                }

                width = Math.Max(width, pen + glyph.OffsetX + glyph.Width);
                minY = Math.Min(minY, glyph.OffsetY); maxY = Math.Max(maxY, glyph.OffsetY + glyph.Height);
                pen += glyph.Advance;
            }
        }
        height = Math.Max(1, (Math.Max(1, lines.Count) - 1) * font.LineHeight + maxY - minY);
    }

    private static int Widest(HudText text, IReadOnlyList<BitmapTextLine> lines)
    {
        if (!text.Centred)
        {
            return 0;
        }

        int widest = 0;
        foreach (BitmapTextLine line in lines)
        {
            widest = Math.Max(widest, line.Width);
        }

        return widest;
    }

    /// <summary>Where a line's pen starts: 0, or half the gap to the widest line when centred.</summary>
    private static int Indent(HudText text, BitmapTextLine line, int widest)
    {
        return text.Centred ? (widest - line.Width) / 2 : 0;
    }
}

/// <summary>A part of a glyph drawn into a <see cref="HudText"/> image: the glyph's pixels from
/// (<paramref name="SourceX"/>, <paramref name="SourceY"/>), <paramref name="Width"/> x <paramref name="Height"/> of them,
/// placed with their top-left at image pixel (<paramref name="X"/>, <paramref name="Y"/>).</summary>
public readonly record struct HudTextPiece(BitmapGlyph Glyph, int SourceX, int SourceY, int X, int Y, int Width, int Height)
{
    /// <summary>The piece's alpha at image pixel (x, y): the glyph's coverage x 17, 0 outside the piece.</summary>
    public byte Alpha(int x, int y)
    {
        return x < X || y < Y || x >= X + Width || y >= Y + Height ? (byte)0
            : (byte)(Glyph.Pixels[(SourceY + y - Y) * Glyph.Width + SourceX + x - X] * 17);
    }
}
