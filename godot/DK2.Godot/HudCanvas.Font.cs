// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>Resolves the replacement face for labels. The canvas rasterizes each label at its drawn pixel size and keeps set text and glows in the bitmap font.</summary>
public partial class HudCanvas
{
    /// <summary>The size each glyph's ink is measured at once, to find the size that fills a box.</summary>
    private const int MeasureSize = 96;
    private const int MinSize = 6, MaxSize = 256;
    private FontFile? _replacement;
    private Rid _fontRid;
    private TextServer? _textServer;
    private FontGlyph[]?[] _glyphs = new FontGlyph[]?[256];
    /// <summary>Each label's glyph cells at the size last drawn, or null cells for a label the face cannot set.</summary>
    private readonly Dictionary<HudLabel, LabelFont> _fontInfo = new();
    private readonly List<HudLabel> _staleFont = new();
    private bool _usingFont;

    /// <summary>A glyph's cell: the text server's cache texture and the glyph's rectangle in it, in pixels.</summary>
    private readonly record struct FontGlyph(Rid Texture, Rect2 Uv);

    /// <summary>A label's cells rasterized for <see cref="PixelsPerLabel"/> screen pixels per label pixel; null cells
    /// when the face lacks a glyph.</summary>
    private readonly record struct LabelFont(float PixelsPerLabel, FontGlyph[]? Cells);

    /// <summary>The TrueType font to draw labels with instead of their bitmaps, or null for the bitmaps.</summary>
    public FontFile? ReplacementFont
    {
        get => _replacement;
        set
        {
            if (_replacement == value) return;
            _textServer ??= TextServerManager.Singleton.GetPrimaryInterface();
            // A face the text server cannot hold (no font RIDs, e.g. a dummy server) leaves the labels as the bitmap.
            Godot.Collections.Array<Rid>? rids = value?.GetRids();
            _replacement = rids is { Count: > 0 } && _textServer != null ? value : null;
            _fontRid = _replacement != null ? rids![0] : default;
            _fontInfo.Clear();
            _invalid = true;
        }
    }

    /// <summary>Whether the last presented frame drew any label with the replacement font.</summary>
    public bool UsingReplacementFont => _usingFont;

    /// <summary>Resolves each plain label draw's glyph cells at its on-screen size, leaving a null for a draw that shows
    /// its bitmap: no font, a glow, or a glyph the face lacks. Labels no longer drawn are forgotten.</summary>
    /// <param name="pixelsPerVirtual">Screen pixels per virtual unit.</param>
    private void PrepareFont(ReadOnlySpan<HudDraw> draws, float pixelsPerVirtual)
    {
        for (int i = 0; i < draws.Length; i++) _glyphs[i] = null;
        _usingFont = false;
        if (_replacement == null) return;
        if (_fontInfo.Count > 0)
        {
            _staleFont.Clear();
            foreach (HudLabel label in _fontInfo.Keys) if (!_labelsDrawn.Contains(label)) _staleFont.Add(label);
            foreach (HudLabel label in _staleFont) _fontInfo.Remove(label);
        }
        for (int i = 0; i < draws.Length; i++)
        {
            HudLabel? label = draws[i].Image.Label;
            if (label == null || draws[i].Image.Variant != HudImageVariant.None) continue;
            FrontEndLabelGeometry geometry = label.Geometry;
            if (geometry.InkBounds.Height <= 0) continue;
            float pixelsPerLabel = draws[i].Rect.Height * pixelsPerVirtual / geometry.InkBounds.Height;
            // Rasterized again only when the label's scale on screen changes (a window resize); a label the face cannot
            // set keeps its null cells.
            if (!_fontInfo.TryGetValue(label, out LabelFont font) || font.Cells != null && font.PixelsPerLabel != pixelsPerLabel)
                _fontInfo[label] = font = Rasterize(label, pixelsPerLabel);
            _glyphs[i] = font.Cells;
            if (font.Cells != null) _usingFont = true;
        }
    }

    /// <summary>The pixel size at which the label's tallest glyph's ink fills its box on screen.</summary>
    private int SizeFor(HudLabel label, float pixelsPerLabel)
    {
        IReadOnlyList<FrontEndGlyph> glyphs = label.Geometry.Glyphs;
        if (glyphs.Count == 0) return MinSize;
        int tallest = 0;
        for (int g = 1; g < glyphs.Count; g++) if (glyphs[g].InkBounds.Height > glyphs[tallest].InkBounds.Height) tallest = g;
        float box = glyphs[tallest].InkBounds.Height * pixelsPerLabel;
        float ink = CellHeight(glyphs[tallest].Character, MeasureSize);
        if (ink <= 0) return MinSize;
        int size = Math.Clamp((int)MathF.Round(MeasureSize * box / ink), MinSize, MaxSize);
        // One correction at the chosen size: a fixed margin in the cache cell weighs more at small sizes than at 96.
        float actual = CellHeight(glyphs[tallest].Character, size);
        return actual > 0 ? Math.Clamp((int)MathF.Round(size * box / actual), MinSize, MaxSize) : size;
    }

    /// <summary>The height in pixels of a character's cache cell at a size.</summary>
    private float CellHeight(char character, int size)
    {
        TextServer server = _textServer!;
        long index = server.FontGetGlyphIndex(_fontRid, size, character, 0);
        return server.FontGetGlyphUVRect(_fontRid, new Vector2I(size, 0), index).Size.Y;
    }

    /// <summary>The face's cells for a label's glyphs at its on-screen size, or null cells when it lacks one. Every glyph
    /// is rasterized before any cache texture is read, so a texture that gained glyphs is uploaded once with all of them.</summary>
    private LabelFont Rasterize(HudLabel label, float pixelsPerLabel)
    {
        TextServer server = _textServer!;
        IReadOnlyList<FrontEndGlyph> glyphs = label.Geometry.Glyphs;
        for (int g = 0; g < glyphs.Count; g++) if (!server.FontHasChar(_fontRid, glyphs[g].Character)) return new LabelFont(pixelsPerLabel, null);
        int size = SizeFor(label, pixelsPerLabel);
        var key = new Vector2I(size, 0);
        var info = new FontGlyph[glyphs.Count];
        var index = new long[glyphs.Count];
        for (int g = 0; g < glyphs.Count; g++)
        {
            index[g] = server.FontGetGlyphIndex(_fontRid, size, glyphs[g].Character, 0);
            info[g] = new FontGlyph(default, server.FontGetGlyphUVRect(_fontRid, key, index[g]));
        }
        for (int g = 0; g < glyphs.Count; g++)
            info[g] = info[g] with { Texture = server.FontGetGlyphTextureRid(_fontRid, key, index[g]) };
        return new LabelFont(pixelsPerLabel, info);
    }

    /// <summary>A label as the face's glyphs, each drawn at its rasterized size on whole pixels, centred in the box the
    /// bitmap layout gives it (the glyph's ink box relative to the label's ink corner, scaled to the draw's rect). A
    /// clipped label cuts every glyph at the window, the cell following the cut.</summary>
    private void RecordGlyphs(Rid item, in HudDraw draw, FontGlyph[] info, Color tint)
    {
        FrontEndLabelGeometry geometry = draw.Image.Label!.Geometry;
        if (geometry.InkBounds.Width <= 0 || geometry.InkBounds.Height <= 0) return;
        float sx = draw.Rect.Width / geometry.InkBounds.Width, sy = draw.Rect.Height / geometry.InkBounds.Height;
        Rect2 window = draw.Clip is HudRect clip ? Snapped(clip) : default;
        for (int g = 0; g < info.Length; g++)
        {
            FrontEndGlyph glyph = geometry.Glyphs[g];
            Rect2 box = Snapped(new HudRect(draw.Rect.X + (glyph.InkBounds.X - geometry.InkBounds.X) * sx,
                draw.Rect.Y + (glyph.InkBounds.Y - geometry.InkBounds.Y) * sy, glyph.InkBounds.Width * sx, glyph.InkBounds.Height * sy));
            Rect2 uv = info[g].Uv;
            if (box.Size.X <= 0 || box.Size.Y <= 0 || uv.Size.X <= 0 || uv.Size.Y <= 0) continue;
            Vector2 centre = box.Position + box.Size / 2;
            var r = new Rect2(MathF.Round(centre.X - uv.Size.X / 2), MathF.Round(centre.Y - uv.Size.Y / 2), uv.Size);
            if (draw.Clip != null)
            {
                if (!HudQuads.Cut(new HudRect(r.Position.X, r.Position.Y, r.Size.X, r.Size.Y), new HudRect(0, 0, 1, 1),
                    new HudRect(window.Position.X, window.Position.Y, window.Size.X, window.Size.Y), out HudRect cut, out HudRect part)) continue;
                uv = new Rect2(uv.Position.X + part.X * uv.Size.X, uv.Position.Y + part.Y * uv.Size.Y, part.Width * uv.Size.X, part.Height * uv.Size.Y);
                r = new Rect2(cut.X, cut.Y, cut.Width, cut.Height);
            }
            RenderingServer.CanvasItemAddTextureRectRegion(item, r, info[g].Texture, uv, tint, false, true);
        }
    }
}
