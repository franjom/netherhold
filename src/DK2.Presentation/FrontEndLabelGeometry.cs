// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Presentation;

public sealed record FrontEndGlyph(char Character, FrontEndRect InkBounds, FrontEndRect SourceInkBounds);

/// <summary>Native advance/line-height layout, retaining original ink rectangles for replacement glyphs.</summary>
public sealed class FrontEndLabelGeometry
{
    private FrontEndLabelGeometry(FrontEndRect ink, int advance, int lineHeight, IReadOnlyList<FrontEndGlyph> glyphs)
        => (InkBounds, Advance, LineHeight, Glyphs) = (ink, advance, lineHeight, glyphs);
    public FrontEndRect InkBounds { get; }
    public int Advance { get; }
    public int LineHeight { get; }
    public IReadOnlyList<FrontEndGlyph> Glyphs { get; }

    public static FrontEndLabelGeometry Measure(BitmapFont font, string text)
    {
        if (font is null) throw new ArgumentNullException(nameof(font));
        if (text is null) throw new ArgumentNullException(nameof(text));
        var glyphs = new List<FrontEndGlyph>();
        int pen = 0, left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
        foreach (char character in text)
        {
            BitmapGlyph? glyph = font.Find(character);
            if (glyph is null) continue;
            int x0 = glyph.Width, y0 = glyph.Height, x1 = -1, y1 = -1;
            for (int y = 0; y < glyph.Height; y++)
                for (int x = 0; x < glyph.Width; x++)
                    if (glyph.Pixels[y * glyph.Width + x] != 0)
                    { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
            if (x1 >= x0)
            {
                var ink = new FrontEndRect(pen + glyph.OffsetX + x0, glyph.OffsetY + y0, x1 - x0 + 1, y1 - y0 + 1);
                glyphs.Add(new(character, ink, new(x0, y0, ink.Width, ink.Height)));
                left = Math.Min(left, (int)ink.X); top = Math.Min(top, (int)ink.Y);
                right = Math.Max(right, (int)(ink.X + ink.Width)); bottom = Math.Max(bottom, (int)(ink.Y + ink.Height));
            }
            pen += glyph.Advance;
        }
        var bounds = glyphs.Count == 0 ? default : new FrontEndRect(left, top, right - left, bottom - top);
        return new(bounds, pen, font.LineHeight, glyphs.AsReadOnly());
    }

    public FrontEndRect PlaceInk(FrontEndRect box, FrontEndTextAlignment alignment,
        FrontEndVerticalAlignment vertical = FrontEndVerticalAlignment.Centre)
    {
        FrontEndRect canvas = PlaceCanvas(box, alignment, vertical);
        return new(canvas.X + InkBounds.X, canvas.Y + InkBounds.Y, InkBounds.Width, InkBounds.Height);
    }

    public FrontEndRect PlaceCanvas(FrontEndRect box, FrontEndTextAlignment alignment,
        FrontEndVerticalAlignment vertical = FrontEndVerticalAlignment.Centre)
    {
        float x = alignment switch
        {
            FrontEndTextAlignment.Left => box.X,
            FrontEndTextAlignment.Right => box.X + box.Width - Advance,
            _ => box.X + MathF.Truncate((box.Width - Advance) / 2),
        };
        return new(x, box.Y + (vertical == FrontEndVerticalAlignment.Top ? 0 : MathF.Truncate((box.Height - LineHeight) / 2)), Advance, LineHeight);
    }
}
