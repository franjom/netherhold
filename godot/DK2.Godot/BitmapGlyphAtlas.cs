// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>One font's glyphs packed in rows into one luminance-alpha texture (white, coverage x 17 as alpha), shared
/// by every canvas and subtitle text (<see cref="BitmapTextItem"/>) that draws the font, and freed when the last lets go.</summary>
internal sealed class BitmapGlyphAtlas
{
    private const int Width = 1024;
    private static readonly Dictionary<BitmapFont, BitmapGlyphAtlas> Shared = new();
    private readonly Dictionary<BitmapGlyph, Vector2I> _corners = new(ReferenceEqualityComparer.Instance);
    private readonly ImageTexture _texture;
    private int _users;

    public Rid Texture => _texture.GetRid();
    /// <summary>The atlas's bytes on the GPU, for the HUD's texture budget.</summary>
    public long Bytes => (long)_texture.GetWidth() * _texture.GetHeight() * 2;

    private BitmapGlyphAtlas(BitmapFont font)
    {
        var order = new List<BitmapGlyph>(font.Glyphs);
        order.Sort((a, b) => b.Height.CompareTo(a.Height));
        int width = Width;
        foreach (BitmapGlyph glyph in order)
        {
            width = Math.Max(width, glyph.Width);
        }

        int x = 0, y = 0, row = 0;
        foreach (BitmapGlyph glyph in order)
        {
            if (x + glyph.Width > width) { x = 0; y += row; row = 0; }
            _corners.TryAdd(glyph, new Vector2I(x, y));
            x += glyph.Width; row = Math.Max(row, glyph.Height);
        }
        int height = Math.Max(1, y + row);
        byte[] pixels = new byte[width * height * 2];
        foreach (BitmapGlyph glyph in font.Glyphs)
        {
            Vector2I at = _corners[glyph];
            for (int gy = 0; gy < glyph.Height; gy++)
            {
                for (int gx = 0; gx < glyph.Width; gx++)
                {
                    byte coverage = glyph.Pixels[gy * glyph.Width + gx];
                    if (coverage == 0)
                    {
                        continue;
                    }

                    int i = ((at.Y + gy) * width + at.X + gx) * 2;
                    pixels[i] = 255; pixels[i + 1] = (byte)(coverage * 17);
                }
            }
        }
        using var image = Image.CreateFromData(width, height, false, Image.Format.La8, pixels);
        _texture = ImageTexture.CreateFromImage(image);
    }

    public Vector2I Corner(BitmapGlyph glyph) => _corners[glyph];

    public static BitmapGlyphAtlas Of(BitmapFont font) => Shared[font];

    public static void Acquire(BitmapFont font)
    {
        if (!Shared.TryGetValue(font, out BitmapGlyphAtlas? atlas))
        {
            Shared.Add(font, atlas = new BitmapGlyphAtlas(font));
        }

        atlas._users++;
    }

    public static void Release(BitmapFont font)
    {
        if (!Shared.TryGetValue(font, out BitmapGlyphAtlas? atlas) || --atlas._users > 0)
        {
            return;
        }

        atlas._texture.Dispose();
        Shared.Remove(font);
    }
}
