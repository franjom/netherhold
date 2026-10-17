// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Data.Model;

/// <summary>
/// One glyph of a DK2 <c>.bf4</c> font. Its pixels are row-major, one per byte, from 0
/// (clear) to 15 (full). The glyph is drawn at the pen plus (<see cref="OffsetX"/>,
/// <see cref="OffsetY"/>), and then the pen moves on by <see cref="Advance"/>
/// (<c>0x006337A0</c>).
/// </summary>
public sealed record BitmapGlyph(
    char Character, int Width, int Height, int OffsetX, int OffsetY, int Advance, byte[] Pixels);

/// <summary>A DK2 <c>.bf4</c> font. <see cref="LineHeight"/> is header byte +5 (<c>0x00633630</c>).</summary>
public sealed class BitmapFont
{
    private readonly Dictionary<char, BitmapGlyph> byCharacter;

    public BitmapFont(int lineHeight, IReadOnlyList<BitmapGlyph> glyphs)
    {
        LineHeight = lineHeight;
        Glyphs = glyphs;
        byCharacter = new Dictionary<char, BitmapGlyph>(glyphs.Count);
        // The files repeat a few characters (spaces); the first copy wins.
        foreach (BitmapGlyph glyph in glyphs) byCharacter.TryAdd(glyph.Character, glyph);
    }

    public int LineHeight { get; }
    public IReadOnlyList<BitmapGlyph> Glyphs { get; }

    public BitmapGlyph? Find(char character) =>
        byCharacter.TryGetValue(character, out BitmapGlyph? glyph) ? glyph : null;
}
