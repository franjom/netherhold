// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>Set text drawn from one glyph atlas per BF4 font (audit item 11): a text drawn one texel per screen pixel, as
/// every HUD text is, becomes its <see cref="HudTextPixels.Pieces"/> copied out of the font's atlas, so a new hover string
/// or counter makes no texture. Pieces never ink one pixel twice and sample texel centres with nearest filtering, so the
/// pixels are <see cref="HudTextPixels.Render"/>'s. A text stretched over a rect of another size keeps its own texture.</summary>
public partial class HudCanvas
{
    private readonly Dictionary<HudText, TextLayout> _layouts = new();
    private readonly HashSet<HudText> _layoutsInUse = new();
    private readonly List<HudText> _staleLayouts = new();
    private readonly List<HudTextPiece> _pieceScratch = new();
    private readonly HashSet<BitmapFont> _atlasesHeld = new();
    private TextLayout?[] _text = new TextLayout?[256];

    /// <summary>A text's size in font pixels and its pieces as atlas regions and offsets from the text's corner.</summary>
    private sealed class TextLayout(int width, int height, BitmapGlyphAtlas atlas, Rect2[] sources, Vector2[] offsets)
    {
        public readonly int Width = width, Height = height;
        public readonly BitmapGlyphAtlas Atlas = atlas;
        public readonly Rect2[] Sources = sources;
        public readonly Vector2[] Offsets = offsets;
    }

    /// <summary>The layout when the draw shows the whole text one texel per pixel; null when it must be stretched.</summary>
    private TextLayout? TextFromAtlas(in HudDraw draw)
    {
        HudText text = draw.Image.Text!;
        if (draw.Kind != HudDrawKind.Quad || draw.Uv is HudRect uv && uv != new HudRect(0, 0, 1, 1))
        {
            return null;
        }

        if (!_layouts.TryGetValue(text, out TextLayout? layout))
        {
            _layouts.Add(text, layout = Layout(text));
        }
        // Kept while drawn either way, so a stretched text is not laid out again each frame.
        _layoutsInUse.Add(text);
        Rect2 r = Snapped(draw.Rect);
        return r.Size.X == layout.Width && r.Size.Y == layout.Height ? layout : null;
    }

    private TextLayout Layout(HudText text)
    {
        if (_atlasesHeld.Add(text.Font))
        {
            BitmapGlyphAtlas.Acquire(text.Font);
        }

        var atlas = BitmapGlyphAtlas.Of(text.Font);
        HudTextPixels.Pieces(text, _pieceScratch, out int width, out int height);
        var sources = new Rect2[_pieceScratch.Count];
        var offsets = new Vector2[_pieceScratch.Count];
        for (int k = 0; k < sources.Length; k++)
        {
            HudTextPiece piece = _pieceScratch[k];
            Vector2I corner = atlas.Corner(piece.Glyph);
            sources[k] = new Rect2(corner.X + piece.SourceX, corner.Y + piece.SourceY, piece.Width, piece.Height);
            offsets[k] = new Vector2(piece.X, piece.Y);
        }
        return new TextLayout(width, height, atlas, sources, offsets);
    }

    /// <summary>A text's pieces at the draw's snapped corner, each cut by the draw's window on whole pixels. No UV clamp:
    /// the pieces sample texel centres, and the clamp works on a half-precision copy of the region.</summary>
    private void RecordText(Rid item, in HudDraw draw, TextLayout layout, Color tint)
    {
        Vector2 corner = Snapped(draw.Rect).Position;
        bool clipped = draw.Clip is HudRect clip;
        Rect2 window = clipped ? Snapped(draw.Clip!.Value) : default;
        Rid texture = layout.Atlas.Texture;
        for (int k = 0; k < layout.Sources.Length; k++)
        {
            Rect2 source = layout.Sources[k];
            var target = new Rect2(corner + layout.Offsets[k], source.Size);
            if (clipped)
            {
                float left = MathF.Max(target.Position.X, window.Position.X), top = MathF.Max(target.Position.Y, window.Position.Y);
                float right = MathF.Min(target.End.X, window.End.X), bottom = MathF.Min(target.End.Y, window.End.Y);
                if (right <= left || bottom <= top)
                {
                    continue;
                }

                source = new Rect2(source.Position.X + left - target.Position.X, source.Position.Y + top - target.Position.Y,
                    right - left, bottom - top);
                target = new Rect2(left, top, right - left, bottom - top);
            }
            RenderingServer.CanvasItemAddTextureRectRegion(item, target, texture, source, tint, false, false);
        }
    }

    /// <summary>Forgets the layouts no frame draws any more; a font's atlas lives until the canvas leaves the tree.</summary>
    private void ReleaseUnusedLayouts()
    {
        if (_layouts.Count == _layoutsInUse.Count)
        {
            return;
        }

        _staleLayouts.Clear();
        foreach (HudText text in _layouts.Keys)
        {
            if (!_layoutsInUse.Contains(text))
            {
                _staleLayouts.Add(text);
            }
        }

        foreach (HudText text in _staleLayouts)
        {
            _layouts.Remove(text);
        }
    }

    private void ReleaseAtlases()
    {
        foreach (BitmapFont font in _atlasesHeld)
        {
            BitmapGlyphAtlas.Release(font);
        }

        _atlasesHeld.Clear(); _layouts.Clear();
    }
}
