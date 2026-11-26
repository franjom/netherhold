// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>A subtitle or title card text in one canvas item: <see cref="BitmapTextBlock"/>'s glyphs (the shared wrap and
/// placement), recorded again only when the block changes, so a text that stays costs nothing a frame. Each glyph is
/// blitted from the font's <see cref="BitmapGlyphAtlas"/>, or, with a <see cref="ReplacementFont"/> that has every
/// character of the text, drawn as that face's glyph centred on the bitmap glyph's ink and sized so the text's tallest
/// glyph fills its ink height, white, as the menu substitutes its labels' glyphs (<c>HudCanvas.Font.cs</c>).</summary>
internal sealed class BitmapTextItem : IDisposable
{
    private const int MeasureSize = 96, MinSize = 6, MaxSize = 256;
    private readonly BitmapTextBlock _block = new();
    private readonly Dictionary<BitmapGlyph, Rect2I> _ink = new(ReferenceEqualityComparer.Instance);
    private readonly List<long> _indices = new();
    private readonly List<Rect2> _targets = new();
    private Rid _item;
    private BitmapFont? _atlasFont;
    private FontFile? _face;
    private Rid _faceRid;
    private TextServer? _server;

    /// <param name="parent">The canvas or canvas item the text draws in, after the parent's own draws.</param>
    public BitmapTextItem(Rid parent)
    {
        _item = RenderingServer.CanvasItemCreate();
        RenderingServer.CanvasItemSetParent(_item, parent);
        RenderingServer.CanvasItemSetDefaultTextureFilter(_item, RenderingServer.CanvasItemTextureFilter.Nearest);
    }

    /// <summary>The replacement TrueType face (<see cref="ClassicFrontEndMenuAssets.LoadReplacementFont"/>), or null for the
    /// bitmap. Not owned.</summary>
    public FontFile? ReplacementFont
    {
        get => _face;
        set
        {
            if (_face == value)
            {
                return;
            }

            _server ??= TextServerManager.Singleton.GetPrimaryInterface();
            Godot.Collections.Array<Rid>? rids = value?.GetRids();
            _face = rids is { Count: > 0 } && _server != null ? value : null;
            _faceRid = _face != null ? rids![0] : default;
            Record();
        }
    }

    /// <summary>Whether the text shown is drawn in the replacement face.</summary>
    public bool UsingReplacementFont { get; private set; }

    /// <summary>Shows <paramref name="text"/> centred in <paramref name="box"/> (screen pixels) at <paramref name="scale"/>;
    /// a null font or text shows nothing.</summary>
    public void Show(BitmapFont? font, string? text, HudRect box, float scale)
    {
        if (_block.Update(font, text, box, scale))
        {
            Record();
        }
    }

    private void Record()
    {
        if (!_item.IsValid)
        {
            return;
        }

        RenderingServer.CanvasItemClear(_item);
        UsingReplacementFont = false;
        BitmapFont? font = _block.Font;
        if (font == null)
        {
            return;
        }

        if (_face != null && RecordFace()) { UsingReplacementFont = true; return; }
        if (font != _atlasFont)
        {
            BitmapGlyphAtlas.Acquire(font);
            if (_atlasFont != null)
            {
                BitmapGlyphAtlas.Release(_atlasFont);
            }

            _atlasFont = font;
        }
        var atlas = BitmapGlyphAtlas.Of(font);
        Rid texture = atlas.Texture;
        foreach (PlacedBitmapGlyph placed in _block.Glyphs)
        {
            BitmapGlyph glyph = placed.Glyph;
            Vector2I corner = atlas.Corner(glyph);
            RenderingServer.CanvasItemAddTextureRectRegion(_item,
                new Rect2(placed.Rect.X, placed.Rect.Y, placed.Rect.Width, placed.Rect.Height), texture,
                new Rect2(corner.X, corner.Y, glyph.Width, glyph.Height), Colors.White, false, false);
        }
    }

    /// <summary>The text in the replacement face; false, drawing nothing, when the face lacks one of its characters. Every
    /// glyph is rasterized before any cache texture is read, so a texture that gained glyphs is uploaded once.</summary>
    private bool RecordFace()
    {
        TextServer server = _server!;
        BitmapGlyph? tallest = null;
        int tallestInk = 0;
        foreach (PlacedBitmapGlyph placed in _block.Glyphs)
        {
            if (!server.FontHasChar(_faceRid, placed.Glyph.Character))
            {
                return false;
            }

            int ink = Ink(placed.Glyph).Size.Y;
            if (ink > tallestInk) { tallest = placed.Glyph; tallestInk = ink; }
        }
        if (tallest == null)
        {
            return true;
        }

        int size = SizeFor(tallest.Character, tallestInk * _block.Scale);
        var key = new Vector2I(size, 0);
        _indices.Clear();
        _targets.Clear();
        foreach (PlacedBitmapGlyph placed in _block.Glyphs)
        {
            long index = server.FontGetGlyphIndex(_faceRid, size, placed.Glyph.Character, 0);
            Rect2 uv = server.FontGetGlyphUVRect(_faceRid, key, index);
            Rect2I ink = Ink(placed.Glyph);
            if (ink.Size.X <= 0 || ink.Size.Y <= 0 || uv.Size.X <= 0 || uv.Size.Y <= 0)
            {
                continue;
            }

            float scale = _block.Scale;
            float centreX = placed.Rect.X + (ink.Position.X + ink.Size.X / 2f) * scale;
            float centreY = placed.Rect.Y + (ink.Position.Y + ink.Size.Y / 2f) * scale;
            _indices.Add(index);
            _targets.Add(new Rect2(MathF.Round(centreX - uv.Size.X / 2), MathF.Round(centreY - uv.Size.Y / 2), uv.Size));
        }
        for (int i = 0; i < _indices.Count; i++)
        {
            RenderingServer.CanvasItemAddTextureRectRegion(_item, _targets[i], server.FontGetGlyphTextureRid(_faceRid, key, _indices[i]),
                server.FontGetGlyphUVRect(_faceRid, key, _indices[i]), Colors.White, false, true);
        }

        return true;
    }

    /// <summary>The pixel size at which the face's cell for <paramref name="character"/> is <paramref name="pixels"/> high,
    /// measured as <c>HudCanvas.SizeFor</c> measures a label's tallest glyph.</summary>
    private int SizeFor(char character, float pixels)
    {
        float ink = CellHeight(character, MeasureSize);
        if (ink <= 0)
        {
            return MinSize;
        }

        int size = Math.Clamp((int)MathF.Round(MeasureSize * pixels / ink), MinSize, MaxSize);
        float actual = CellHeight(character, size);
        return actual > 0 ? Math.Clamp((int)MathF.Round(size * pixels / actual), MinSize, MaxSize) : size;
    }

    private float CellHeight(char character, int size)
    {
        long index = _server!.FontGetGlyphIndex(_faceRid, size, character, 0);
        return _server.FontGetGlyphUVRect(_faceRid, new Vector2I(size, 0), index).Size.Y;
    }

    /// <summary>The bitmap glyph's inked pixels' bounds in its own pixels, found once.</summary>
    private Rect2I Ink(BitmapGlyph glyph)
    {
        if (_ink.TryGetValue(glyph, out Rect2I ink))
        {
            return ink;
        }

        int left = glyph.Width, top = glyph.Height, right = -1, bottom = -1;
        for (int y = 0; y < glyph.Height; y++)
        {
            for (int x = 0; x < glyph.Width; x++)
            {
                if (glyph.Pixels[y * glyph.Width + x] == 0)
                {
                    continue;
                }

                left = Math.Min(left, x); right = Math.Max(right, x);
                top = Math.Min(top, y); bottom = Math.Max(bottom, y);
            }
        }

        ink = right < 0 ? default : new Rect2I(left, top, right - left + 1, bottom - top + 1);
        _ink.Add(glyph, ink);
        return ink;
    }

    public void Dispose()
    {
        if (_item.IsValid)
        {
            RenderingServer.FreeRid(_item);
        }

        _item = default;
        if (_atlasFont != null)
        {
            BitmapGlyphAtlas.Release(_atlasFont);
        }

        _atlasFont = null;
        _ink.Clear();
    }
}
