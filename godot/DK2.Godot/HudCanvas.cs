// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>Draws a <see cref="HudFrame"/> built in DK2.Presentation (ADR-0805). The canvas maps virtual layout boxes to screen pixels, applies the draw blend, and snaps edges to whole pixels.</summary>
public partial class HudCanvas : Node2D
{
    private readonly HudFrame _shown = new();
    private readonly List<Rid> _items = new();
    private CanvasItemMaterial? _additive;
    private Resolved[] _resolved = new Resolved[256];
    private readonly SectorBuffers?[] _fans = new SectorBuffers?[HudSector.MaxVertices + 1];
    /// <summary>Exact-size arrays for a triangle list of 3 (k + 1) vertices at index k, kept once made.</summary>
    private TriangleBuffers?[] _lists = new TriangleBuffers?[8];
    private Vector2 _origin;
    private float _scale = 1, _pixelsPerUnit = 1;
    private bool _invalid = true;

    /// <summary>A draw's texture as the rebuild uses it.</summary>
    private readonly record struct Resolved(Rid Texture, Vector2 Size, bool Nearest, Rid Material = default)
    {
        public bool IsValid
        {
            get
            {
                return Texture.IsValid;
            }
        }
    }

    /// <summary>Reads a catalogue image by name; set before the first <see cref="Present"/>.</summary>
    public Func<string, DK2.Art.Textures.RgbaImage>? Load { get; set; }

    /// <param name="origin">Screen pixel of virtual (0, 0).</param>
    /// <param name="scale">Local units per virtual unit.</param>
    /// <param name="pixelsPerUnit">Screen pixels per local unit; edges snap to them.</param>
    public void Present(HudFrame frame, Vector2 origin, float scale, float pixelsPerUnit)
    {
        if (!_invalid && origin == _origin && scale == _scale && pixelsPerUnit == _pixelsPerUnit && frame.SameAs(_shown))
        {
            return;
        }

        if (_resolved.Length < frame.Count)
        {
            int size = Math.Max(frame.Count, _resolved.Length * 2);
            _resolved = new Resolved[size]; _glyphs = new FontGlyph[]?[size]; _text = new TextLayout?[size];
        }
        // Textures first: if loading throws, the frame is not taken and the next one tries again.
        ReadOnlySpan<HudDraw> draws = frame.Draws;
        _textInUse.Clear(); _labelsDrawn.Clear(); _layoutsInUse.Clear();
        for (int i = 0; i < draws.Length; i++)
        {
            if (draws[i].Image.Label != null)
            {
                _labelsDrawn.Add(draws[i].Image.Label!);
            }
        }

        PrepareFont(draws, scale * pixelsPerUnit);
        // Placed first: a text is drawn from its font's atlas only when its snapped rect is its own size. Invalid until
        // the frame is taken, so a throw below cannot leave the new placement looking drawn.
        _invalid = true; _origin = origin; _scale = scale; _pixelsPerUnit = pixelsPerUnit;
        for (int i = 0; i < draws.Length; i++)
        {
            _text[i] = null;
            // A label set in the replacement font needs no bitmap; its bitmap texture is released below.
            if (draws[i].Kind == HudDrawKind.Fill || _glyphs[i] != null) { _resolved[i] = default; continue; }
            if (draws[i].Image.Text != null && TextFromAtlas(draws[i]) is TextLayout layout)
            {
                _text[i] = layout;
                _resolved[i] = new Resolved(layout.Atlas.Texture, default, true);
                continue;
            }
            _resolved[i] = Texture(draws[i].Image);
            if (draws[i].Image.IsGenerated && draws[i].Image.Variant != HudImageVariant.Glow)
            {
                _textInUse.Add(draws[i].Image);
            }
        }
        _shown.CopyFrom(frame); _invalid = false;
        ReleaseUnusedText(); ReleaseUnusedLayouts(); ReleaseUnusedGlows();
        Record();
    }

    /// <summary>Records the shown frame into the canvas items, back to front.</summary>
    private void Record()
    {
        foreach (Rid item in _items)
        {
            RenderingServer.CanvasItemClear(item);
        }

        ReadOnlySpan<HudDraw> draws = _shown.Draws;
        Span<HudVertex> fan = stackalloc HudVertex[HudSector.MaxVertices];
        int run = -1;
        HudBlend blend = HudBlend.Alpha;
        bool nearest = false;
        Rid material = default;
        for (int i = 0; i < draws.Length; i++)
        {
            HudDraw draw = draws[i];
            Resolved texture = _resolved[i];
            FontGlyph[]? glyphs = _glyphs[i];
            if (glyphs == null && draw.Kind != HudDrawKind.Fill && !texture.IsValid)
            {
                continue;
            }

            if (run < 0 || draw.Blend != blend || texture.Nearest != nearest || texture.Material != material)
            {
                run++; blend = draw.Blend; nearest = texture.Nearest; material = texture.Material;
                Begin(run, blend, nearest, material);
            }
            Rid item = _items[run];
            var tint = new Color(draw.Tint.R / 255f, draw.Tint.G / 255f, draw.Tint.B / 255f, draw.Tint.A / 255f);
            if (glyphs != null) { RecordGlyphs(item, draw, glyphs, tint); continue; }
            if (_text[i] is TextLayout text) { RecordText(item, draw, text, tint); continue; }
            if (draw.Kind == HudDrawKind.Triangles) { RecordTriangles(item, _shown.Vertices(draw), texture.Texture, tint); continue; }
            Rect2 r = Snapped(draw.Rect);
            if (draw.Kind == HudDrawKind.Sector)
            {
                // Lay the fan over the snapped rectangle.
                int n = HudSector.Fan(new HudRect(r.Position.X, r.Position.Y, r.Size.X, r.Size.Y), draw.Percent, fan);
                if (n == 0)
                {
                    continue;
                }

                SectorBuffers buffers = _fans[n] ??= new SectorBuffers(n);
                for (int k = 0; k < n; k++)
                {
                    buffers.Points[k] = new Vector2(fan[k].X, fan[k].Y);
                    buffers.Uvs[k] = new Vector2(fan[k].U, fan[k].V);
                    buffers.Colors[k] = tint;
                }
                // An explicit fan (0, k, k + 1), not CanvasItemAddPolygon: that triangulates the outline and drops the
                // whole draw when a thin wedge or collinear rim points defeat the triangulation.
                RenderingServer.CanvasItemAddTriangleArray(item, buffers.Indices, buffers.Points, buffers.Colors, buffers.Uvs,
                    Array.Empty<int>(), Array.Empty<float>(), texture.Texture, -1);
                continue;
            }
            if (r.Size.X <= 0 || r.Size.Y <= 0)
            {
                continue;
            }

            HudRect uv = draw.Uv ?? new HudRect(0, 0, 1, 1);
            if (draw.Clip is HudRect clip)
            {
                // Quad and window both on the pixel grid, then cut: the overflow-hidden box's own rule.
                Rect2 c = Snapped(clip);
                if (!HudQuads.Cut(new HudRect(r.Position.X, r.Position.Y, r.Size.X, r.Size.Y), uv,
                    new HudRect(c.Position.X, c.Position.Y, c.Size.X, c.Size.Y), out HudRect cut, out uv))
                {
                    continue;
                }

                r = new Rect2(cut.X, cut.Y, cut.Width, cut.Height);
            }
            if (draw.Kind == HudDrawKind.Fill) { RenderingServer.CanvasItemAddRect(item, r, tint); continue; }
            var region = new Rect2(uv.X * texture.Size.X, uv.Y * texture.Size.Y, uv.Width * texture.Size.X, uv.Height * texture.Size.Y);
            RenderingServer.CanvasItemAddTextureRectRegion(item, r, texture.Texture, region, tint, false, true);
        }
    }

    /// <summary>A triangle list in unsnapped screen pixels.</summary>
    private void RecordTriangles(Rid item, ReadOnlySpan<HudVertex> vertices, Rid texture, Color tint)
    {
        int slot = vertices.Length / 3 - 1;
        if (slot < 0)
        {
            return;
        }

        if (slot >= _lists.Length)
        {
            Array.Resize(ref _lists, Math.Max(slot + 1, _lists.Length * 2));
        }

        TriangleBuffers buffers = _lists[slot] ??= new TriangleBuffers(vertices.Length);
        float unit = _scale * _pixelsPerUnit;
        for (int k = 0; k < vertices.Length; k++)
        {
            buffers.Points[k] = new Vector2(_origin.X + vertices[k].X * unit, _origin.Y + vertices[k].Y * unit);
            buffers.Uvs[k] = new Vector2(vertices[k].U, vertices[k].V);
            buffers.Colors[k] = tint;
        }
        RenderingServer.CanvasItemAddTriangleArray(item, buffers.Indices, buffers.Points, buffers.Colors, buffers.Uvs,
            Array.Empty<int>(), Array.Empty<float>(), texture, -1);
    }

    /// <summary>Reused exact-size arrays for a triangle list, for the same reason as <see cref="SectorBuffers"/>.</summary>
    private sealed class TriangleBuffers
    {
        public readonly Vector2[] Points, Uvs;
        public readonly Color[] Colors;
        public readonly int[] Indices;

        public TriangleBuffers(int vertices)
        {
            Points = new Vector2[vertices]; Uvs = new Vector2[vertices]; Colors = new Color[vertices]; Indices = new int[vertices];
            for (int k = 0; k < vertices; k++)
            {
                Indices[k] = k;
            }
        }
    }

    /// <summary>Reused arrays for a sector fan of one vertex count, exact-sized: the triangle-array call takes whole
    /// arrays (4.8 beta 3's span overload rejected its own index count), so a fan allocates no managed memory after its
    /// first draw. The call still copies them into native packed arrays each time a changed frame is recorded.</summary>
    private sealed class SectorBuffers
    {
        public readonly Vector2[] Points, Uvs;
        public readonly Color[] Colors;
        public readonly int[] Indices;

        public SectorBuffers(int vertices)
        {
            Points = new Vector2[vertices]; Uvs = new Vector2[vertices]; Colors = new Color[vertices];
            Indices = new int[3 * (vertices - 2)];
            for (int k = 1, i = 0; k + 1 < vertices; k++) { Indices[i++] = 0; Indices[i++] = k; Indices[i++] = k + 1; }
        }
    }

    /// <summary>Readies pooled canvas item <paramref name="run"/> for a run of draws with one blend, filter and material
    /// (a partial glow's; glows are alpha-blended, so a material never meets the additive blend).</summary>
    private void Begin(int run, HudBlend blend, bool nearest, Rid material)
    {
        while (_items.Count <= run)
        {
            Rid created = RenderingServer.CanvasItemCreate();
            RenderingServer.CanvasItemSetParent(created, GetCanvasItem());
            _items.Add(created);
        }
        Rid item = _items[run];
        _additive ??= new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        if (material.IsValid && blend == HudBlend.Additive)
        {
            GD.PushError("HudCanvas: an additive glow is drawn alpha-blended.");
        }

        RenderingServer.CanvasItemSetMaterial(item, material.IsValid ? material : blend == HudBlend.Additive ? _additive.GetRid() : default);
        RenderingServer.CanvasItemSetDefaultTextureFilter(item, nearest
            ? RenderingServer.CanvasItemTextureFilter.Nearest : RenderingServer.CanvasItemTextureFilter.Linear);
        RenderingServer.CanvasItemSetDrawIndex(item, run);
    }

    /// <summary>A virtual rect in screen pixels as the last <see cref="Present"/> placed its draws, for a host that draws
    /// beside the canvas on the same grid (the minimap's disc).</summary>
    public Rect2 ScreenRect(HudRect rect)
    {
        return Snapped(rect);
    }

    /// <summary>A virtual rectangle in screen pixels with edges on whole pixels.</summary>
    private Rect2 Snapped(HudRect r)
    {
        float left = Edge(r.X), top = Edge(r.Y), right = Edge(r.X + r.Width), bottom = Edge(r.Y + r.Height);
        return new Rect2(_origin.X + left, _origin.Y + top, right - left, bottom - top);
    }

    /// <summary>A virtual coordinate in whole screen pixels, halves rounded up like layout rounding. The local value
    /// is first cleaned to 1/1024 so a float round trip cannot land a hair under a half.</summary>
    private float Edge(float v)
    {
        float local = MathF.Round(v * _scale * 1024f) / 1024f;
        return MathF.Floor(local * _pixelsPerUnit + .5f);
    }

    public override void _ExitTree()
    {
        foreach (Rid item in _items)
        {
            RenderingServer.FreeRid(item);
        }

        _items.Clear();
        _additive?.Dispose(); _additive = null;
        ReleaseTextures(); ReleaseAtlases(); ReleaseGauges(); ReleaseGlows();
        _fontInfo.Clear();
        _shown.Clear(); _invalid = true;
    }
}
