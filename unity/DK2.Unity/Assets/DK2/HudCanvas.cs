// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.Text;
using DK2.Art.Textures;
using DK2.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace DK2.Unity
{
    /// <summary>Draws a <see cref="HudFrame"/> built in DK2.Presentation (ADR-0805) as one UI Toolkit mesh,
    /// rebuilt only when the frame changes. It owns the textures and picks only where
    /// <paramref name="hit"/> says the model has a widget, so pointer events elsewhere pass through.
    /// A <see cref="HudLabel"/> is drawn as its shared BF4 bitmap, or, once <see cref="ReplacementFont"/> is set, as
    /// that Unity font's glyphs in the label's glyph boxes; the Unity font atlas lives here because it is Unity's.</summary>
    public sealed class HudCanvas : VisualElement, IDisposable
    {
        private readonly Func<string, RgbaImage> _load;
        private readonly Func<float, float, bool> _hit;
        private readonly Dictionary<(HudImage, HudBlend), Texture2D> _textures = new Dictionary<(HudImage, HudBlend), Texture2D>();
        private readonly Dictionary<(HudImage, HudBlend), LiveTexture> _live = new Dictionary<(HudImage, HudBlend), LiveTexture>();
        private readonly HudFrame _shown = new HudFrame();
        private readonly HashSet<(HudImage, HudBlend)> _textInUse = new HashSet<(HudImage, HudBlend)>();
        private readonly List<(HudImage, HudBlend)> _stale = new List<(HudImage, HudBlend)>();
        private int _catalogueCount;
        private Texture2D _white;
        private Texture[] _resolved = new Texture[256];
        private float _scale = 1, _pixelsPerUnit = 1;
        // The replacement TrueType font: per label draw its glyphs' atlas cells, or null to draw the bitmap.
        private const int FontResolution = 96;
        private CharacterInfo[][] _glyphs = new CharacterInfo[256][];
        private readonly Dictionary<HudLabel, CharacterInfo[]> _fontInfo = new Dictionary<HudLabel, CharacterInfo[]>();
        private readonly HashSet<HudLabel> _labelsInUse = new HashSet<HudLabel>();
        /// <summary>Every label the shown frame draws in any form; its glow frames are kept while it is on the page.</summary>
        private readonly HashSet<HudLabel> _labelsDrawn = new HashSet<HudLabel>();
        /// <summary>Each glowing label's full-strength glow, computed once; the quarters are cut from it.</summary>
        private readonly Dictionary<HudLabel, RgbaImage> _glowBase = new Dictionary<HudLabel, RgbaImage>();
        private readonly List<HudLabel> _staleGlow = new List<HudLabel>();
        private readonly StringBuilder _characters = new StringBuilder();
        private Font _replacement;
        private RenderTexture _fontAtlas;
        private Material _fontMaterial;
        private bool _fontDirty, _usingFont, _invalid;

        /// <param name="load">Reads a catalogue image by name.</param>
        /// <param name="hit">Whether the model has a widget at a point in virtual units; null for a panel
        /// that never takes the pointer.</param>
        public HudCanvas(Func<string, RgbaImage> load, Func<float, float, bool> hit)
        {
            _load = load ?? throw new ArgumentNullException(nameof(load));
            _hit = hit ?? ((x, y) => false);
            pickingMode = hit == null ? PickingMode.Ignore : PickingMode.Position;
            style.position = Position.Absolute; style.left = 0; style.top = 0;
            style.width = Length.Percent(100); style.height = Length.Percent(100);
            generateVisualContent += Draw;
        }

        /// <summary>Local pixels per virtual unit (screen height / 1920).</summary>
        public float Scale => _scale;

        /// <summary>The TrueType font to draw labels with instead of their bitmaps, or null for the bitmaps. Each
        /// glyph goes in its own bitmap glyph box, so layout and colour stay the model's.</summary>
        public Font ReplacementFont
        {
            get => _replacement;
            set
            {
                if (_replacement == value) return;
                if (_replacement != null) Font.textureRebuilt -= FontRebuilt;
                _replacement = value;
                if (value != null) Font.textureRebuilt += FontRebuilt;
                _fontDirty = true;
            }
        }

        /// <summary>Whether the last presented frame drew any label with the replacement font.</summary>
        public bool UsingReplacementFont => _usingFont;

        private void FontRebuilt(Font font) { if (font == _replacement) _fontDirty = true; }

        /// <summary>A local point in virtual units.</summary>
        public Vector2 ToVirtual(Vector2 local) => local / _scale;

        /// <param name="scale">Local units per virtual unit.</param>
        /// <param name="pixelsPerUnit">Screen pixels per local unit (the panel's scale); edges snap to them.</param>
        public void Present(HudFrame frame, float scale, float pixelsPerUnit)
        {
            if (!_invalid && !_fontDirty && scale == _scale && pixelsPerUnit == _pixelsPerUnit && frame.SameAs(_shown)) return;
            if (_resolved.Length < frame.Count)
            {
                int size = Math.Max(frame.Count, _resolved.Length * 2);
                _resolved = new Texture[size]; _glyphs = new CharacterInfo[size][];
            }
            // Textures first: if loading throws, the frame is not taken and the next one tries again.
            ReadOnlySpan<HudDraw> draws = frame.Draws;
            _textInUse.Clear(); _labelsDrawn.Clear();
            for (int i = 0; i < draws.Length; i++) if (draws[i].Image.Label != null) _labelsDrawn.Add(draws[i].Image.Label);
            PrepareFont(draws);
            for (int i = 0; i < draws.Length; i++)
            {
                if (_glyphs[i] != null) { _resolved[i] = _fontAtlas; continue; }
                _resolved[i] = draws[i].Kind == HudDrawKind.Fill ? White : Texture(draws[i].Image, draws[i].Blend);
                if (draws[i].Image.IsGenerated) _textInUse.Add((draws[i].Image, draws[i].Blend));
            }
            _scale = scale; _pixelsPerUnit = pixelsPerUnit; _shown.CopyFrom(frame); _invalid = false;
            ReleaseUnusedText();
            MarkDirtyRepaint();
        }

        /// <summary>Draws the frame again at the next <see cref="Present"/> even if it is unchanged: a document that was
        /// disabled and enabled again has lost its mesh.</summary>
        public void Invalidate() { _invalid = true; MarkDirtyRepaint(); }

        /// <summary>Text changes with the message shown; a text texture no frame draws any more is destroyed,
        /// as the panels destroyed a replaced text texture.</summary>
        private void ReleaseUnusedText()
        {
            if (_glowBase.Count > 0)
            {
                _staleGlow.Clear();
                foreach (HudLabel label in _glowBase.Keys) if (!_labelsDrawn.Contains(label)) _staleGlow.Add(label);
                foreach (HudLabel label in _staleGlow) _glowBase.Remove(label);
            }
            if (_textures.Count == _textInUse.Count + _catalogueCount) return;
            _stale.Clear();
            foreach (KeyValuePair<(HudImage, HudBlend), Texture2D> entry in _textures)
            {
                HudImage image = entry.Key.Item1;
                if (!image.IsGenerated || _textInUse.Contains(entry.Key)) continue;
                // A label's glow frames stay while the label is on the page, as the menu built them once per page:
                // the fade in and out then makes no textures.
                if (image.Variant == HudImageVariant.Glow && image.Label != null && _labelsDrawn.Contains(image.Label)) continue;
                _stale.Add(entry.Key);
            }
            foreach ((HudImage, HudBlend) key in _stale) { UnityEngine.Object.Destroy(_textures[key]); _textures.Remove(key); }
        }

        /// <summary>Resolves each plain label draw's glyphs in the replacement font's atlas (the atlas is requested for
        /// the characters, then copied to an alpha-as-white texture), leaving a null for a draw that shows its bitmap: no
        /// font, a glow, or a glyph the font lacks.</summary>
        private void PrepareFont(ReadOnlySpan<HudDraw> draws)
        {
            for (int i = 0; i < draws.Length; i++) _glyphs[i] = null;
            _usingFont = false;
            if (_replacement == null) { _fontDirty = false; return; }
            _labelsInUse.Clear();
            bool missing = _fontDirty;
            for (int i = 0; i < draws.Length; i++)
            {
                HudLabel label = draws[i].Image.Label;
                if (label == null || draws[i].Image.Variant != HudImageVariant.None) continue;
                if (_labelsInUse.Add(label) && !_fontInfo.ContainsKey(label)) missing = true;
            }
            if (_fontInfo.Count > _labelsInUse.Count) missing = true;
            if (missing)
            {
                _characters.Clear();
                foreach (HudLabel label in _labelsInUse)
                    foreach (FrontEndGlyph glyph in label.Geometry.Glyphs) _characters.Append(glyph.Character);
                _replacement.RequestCharactersInTexture(_characters.ToString(), FontResolution, FontStyle.Normal);
                Texture source = _replacement.material.mainTexture;
                if (_fontAtlas == null || _fontAtlas.width != source.width || _fontAtlas.height != source.height)
                {
                    if (_fontAtlas != null) { _fontAtlas.Release(); UnityEngine.Object.Destroy(_fontAtlas); }
                    _fontAtlas = new RenderTexture(source.width, source.height, 0, RenderTextureFormat.ARGB32);
                    _fontAtlas.Create();
                }
                if (_fontMaterial == null) _fontMaterial = new Material(Resources.Load<Shader>("DK2FrontEndFont"));
                Graphics.Blit(source, _fontAtlas, _fontMaterial);
                _fontInfo.Clear();
                foreach (HudLabel label in _labelsInUse)
                {
                    var info = new CharacterInfo[label.Geometry.Glyphs.Count];
                    bool all = true;
                    for (int g = 0; g < info.Length && all; g++)
                        all = _replacement.GetCharacterInfo(label.Geometry.Glyphs[g].Character, out info[g], FontResolution, FontStyle.Normal);
                    _fontInfo[label] = all ? info : null;
                }
            }
            for (int i = 0; i < draws.Length; i++)
            {
                HudLabel label = draws[i].Image.Label;
                if (label == null || draws[i].Image.Variant != HudImageVariant.None) continue;
                _glyphs[i] = _fontInfo[label];
                if (_glyphs[i] != null) _usingFont = true;
            }
            _fontDirty = false;
        }

        private Texture2D White
        {
            get
            {
                if (_white != null) return _white;
                _white = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = "HUD fill" };
                _white.SetPixel(0, 0, Color.white); _white.Apply(false);
                return _white;
            }
        }

        /// <summary>A virtual rect in local units with its edges on whole screen pixels, as UI Toolkit's
        /// layout placed the retained images this canvas replaces.</summary>
        private Rect Snapped(HudRect r)
        {
            float left = Edge(r.X), top = Edge(r.Y), right = Edge(r.X + r.Width), bottom = Edge(r.Y + r.Height);
            return new Rect(left, top, right - left, bottom - top);
        }

        /// <summary>A virtual coordinate as a local one on the screen-pixel grid, halves rounded up like
        /// layout rounding. The local value is first cleaned to 1/1024 so the float round trip of a whole
        /// local pixel (pixels / scale * scale) cannot land a hair under a half and round the other way.</summary>
        private float Edge(float v)
        {
            float local = Mathf.Round(v * _scale * 1024f) / 1024f;
            return Mathf.Floor(local * _pixelsPerUnit + .5f) / _pixelsPerUnit;
        }

        public override bool ContainsPoint(Vector2 localPoint) => _hit(localPoint.x / _scale, localPoint.y / _scale);

        private void Draw(MeshGenerationContext context)
        {
            ReadOnlySpan<HudDraw> draws = _shown.Draws;
            Span<HudVertex> fan = stackalloc HudVertex[HudSector.MaxVertices];
            for (int i = 0; i < draws.Length; i++)
            {
                Texture texture = _resolved[i];
                if (texture == null) continue;
                HudDraw draw = draws[i];
                var tint = new Color32(draw.Tint.R, draw.Tint.G, draw.Tint.B, draw.Tint.A);
                if (_glyphs[i] != null) { DrawGlyphs(context, draw, _glyphs[i], texture, tint); continue; }
                if (draw.Kind != HudDrawKind.Sector)
                {
                    // A run of quads on one texture is one mesh: a full map's hundreds of creature squares are one
                    // allocation, not one each. Only neighbours join, so the back-to-front order is kept.
                    int end = i + 1;
                    while (end < draws.Length && end - i < MaxBatch && _resolved[end] == texture && _glyphs[end] == null && draws[end].Kind != HudDrawKind.Sector) end++;
                    if (_batch.Length < end - i) _batch = new BatchQuad[Math.Max(end - i, _batch.Length * 2)];
                    int count = 0;
                    for (int k = i; k < end; k++)
                        if (Quad(draws[k], out _batch[count].Rect, out _batch[count].Uv)) _batch[count++].Tint = new Color32(draws[k].Tint.R, draws[k].Tint.G, draws[k].Tint.B, draws[k].Tint.A);
                    i = end - 1;
                    if (count == 0) continue;
                    MeshWriteData quads = context.Allocate(count * 4, count * 6, texture);
                    for (int k = 0; k < count; k++)
                    {
                        Rect q = _batch[k].Rect; HudRect uv = _batch[k].Uv; Color32 colour = _batch[k].Tint;
                        // UV from the image's top-left, to Unity's bottom-up texture space.
                        float u0 = uv.X, u1 = uv.X + uv.Width, v0 = 1 - uv.Y, v1 = 1 - (uv.Y + uv.Height);
                        Vertex(quads, q.xMin, q.yMin, u0, v0, colour);
                        Vertex(quads, q.xMax, q.yMin, u1, v0, colour);
                        Vertex(quads, q.xMax, q.yMax, u1, v1, colour);
                        Vertex(quads, q.xMin, q.yMax, u0, v1, colour);
                        // Clockwise on screen: UI Toolkit's winding.
                        ushort first = (ushort)(k * 4);
                        quads.SetNextIndex(first); quads.SetNextIndex((ushort)(first + 1)); quads.SetNextIndex((ushort)(first + 2));
                        quads.SetNextIndex(first); quads.SetNextIndex((ushort)(first + 2)); quads.SetNextIndex((ushort)(first + 3));
                    }
                    continue;
                }
                Rect r = Snapped(draw.Rect);
                // The fan is laid over the snapped rect in local units, as the old sector element's content rect was.
                int n = HudSector.Fan(new HudRect(r.x, r.y, r.width, r.height), draw.Percent, fan);
                if (n == 0) continue;
                MeshWriteData mesh = context.Allocate(n, (n - 2) * 3, texture);
                for (int k = 0; k < n; k++) Vertex(mesh, fan[k].X, fan[k].Y, fan[k].U, 1 - fan[k].V, tint);
                for (int k = 1; k < n - 1; k++)
                { mesh.SetNextIndex(0); mesh.SetNextIndex((ushort)k); mesh.SetNextIndex((ushort)(k + 1)); }
            }
        }

        /// <summary>Quads in one mesh at most: its indices are 16-bit, four vertices a quad.</summary>
        private const int MaxBatch = 16383;
        private struct BatchQuad { public Rect Rect; public HudRect Uv; public Color32 Tint; }
        private BatchQuad[] _batch = new BatchQuad[16];

        /// <summary>A quad's snapped rect and image part, cut to its clip window; false when nothing is left.</summary>
        private bool Quad(HudDraw draw, out Rect r, out HudRect uv)
        {
            r = Snapped(draw.Rect);
            uv = draw.Uv ?? new HudRect(0, 0, 1, 1);
            if (r.width <= 0 || r.height <= 0) return false;
            if (draw.Clip is HudRect clip)
            {
                // Quad and window both on the pixel grid, then cut: the overflow-hidden box's own rule.
                Rect c = Snapped(clip);
                if (!HudQuads.Cut(new HudRect(r.x, r.y, r.width, r.height), uv, new HudRect(c.x, c.y, c.width, c.height),
                    out HudRect cut, out uv)) return false;
                r = new Rect(cut.X, cut.Y, cut.Width, cut.Height);
            }
            return true;
        }

        /// <summary>A label as its font's glyphs, each in the box the bitmap layout gives it: the glyph's ink box
        /// relative to the label's ink corner, scaled from the label's pixels to the draw's rect. A clipped label cuts every
        /// glyph at the window, the cell's uv following the cut.</summary>
        private void DrawGlyphs(MeshGenerationContext context, HudDraw draw, CharacterInfo[] info, Texture texture, Color32 tint)
        {
            FrontEndLabelGeometry geometry = draw.Image.Label.Geometry;
            if (geometry.InkBounds.Width <= 0 || geometry.InkBounds.Height <= 0) return;
            float sx = draw.Rect.Width / geometry.InkBounds.Width, sy = draw.Rect.Height / geometry.InkBounds.Height;
            Rect window = draw.Clip is HudRect clip ? Snapped(clip) : default;
            for (int i = 0; i < info.Length; i++)
            {
                FrontEndGlyph glyph = geometry.Glyphs[i];
                Rect r = Snapped(new HudRect(draw.Rect.X + (glyph.InkBounds.X - geometry.InkBounds.X) * sx,
                    draw.Rect.Y + (glyph.InkBounds.Y - geometry.InkBounds.Y) * sy, glyph.InkBounds.Width * sx, glyph.InkBounds.Height * sy));
                if (r.width <= 0 || r.height <= 0) continue;
                CharacterInfo c = info[i];
                Vector2 tl = c.uvTopLeft, tr = c.uvTopRight, br = c.uvBottomRight, bl = c.uvBottomLeft;
                if (draw.Clip != null)
                {
                    if (!HudQuads.Cut(new HudRect(r.x, r.y, r.width, r.height), new HudRect(0, 0, 1, 1),
                        new HudRect(window.x, window.y, window.width, window.height), out HudRect cut, out HudRect part)) continue;
                    float x0 = part.X, x1 = part.X + part.Width, y0 = part.Y, y1 = part.Y + part.Height;
                    Vector2 ntl = Across(tl, tr, bl, br, x0, y0), ntr = Across(tl, tr, bl, br, x1, y0);
                    Vector2 nbr = Across(tl, tr, bl, br, x1, y1), nbl = Across(tl, tr, bl, br, x0, y1);
                    r = new Rect(cut.X, cut.Y, cut.Width, cut.Height);
                    tl = ntl; tr = ntr; br = nbr; bl = nbl;
                }
                MeshWriteData quad = context.Allocate(4, 6, texture);
                Vertex(quad, r.xMin, r.yMin, tl.x, tl.y, tint);
                Vertex(quad, r.xMax, r.yMin, tr.x, tr.y, tint);
                Vertex(quad, r.xMax, r.yMax, br.x, br.y, tint);
                Vertex(quad, r.xMin, r.yMax, bl.x, bl.y, tint);
                quad.SetNextIndex(0); quad.SetNextIndex(1); quad.SetNextIndex(2);
                quad.SetNextIndex(0); quad.SetNextIndex(2); quad.SetNextIndex(3);
            }
        }

        /// <summary>The uv at fractions (x, y) of a quad whose corners carry the given uvs.</summary>
        private static Vector2 Across(Vector2 tl, Vector2 tr, Vector2 bl, Vector2 br, float x, float y) =>
            Vector2.Lerp(Vector2.Lerp(tl, tr, x), Vector2.Lerp(bl, br, x), y);

        private static void Vertex(MeshWriteData mesh, float x, float y, float u, float v, Color32 tint) =>
            mesh.SetNextVertex(new Vertex { position = new Vector3(x, y, UnityEngine.UIElements.Vertex.nearZ), tint = tint, uv = new Vector2(u, v) });

        /// <summary>Decodes an image now rather than at the first frame that draws it.</summary>
        public void Preload(HudImage image, HudBlend blend = HudBlend.Alpha) => Texture(image, blend);

        /// <summary>The image as a texture, its variant applied; UI Toolkit has no additive blending, so an
        /// additive draw gets <see cref="HudImagePixels.AdditiveOverAlpha"/>.</summary>
        private Texture2D Texture(HudImage image, HudBlend blend)
        {
            if (image.IsNone) return null;
            if (image.IsLive) return Live(image, blend);
            if (_textures.TryGetValue((image, blend), out Texture2D texture)) return texture;
            RgbaImage source;
            bool glow = image.Variant == HudImageVariant.Glow;
            if (image.Label != null && glow)
            {
                if (!_glowBase.TryGetValue(image.Label, out RgbaImage full))
                    _glowBase.Add(image.Label, full = HudLabelPixels.Glow(HudLabelPixels.Render(image.Label, HudLabel.GlowPadding)));
                source = HudLabelPixels.Quarter(full, image.Pieces);
                glow = false;
            }
            else if (image.Label != null) source = HudLabelPixels.Render(image.Label);
            else if (image.Text != null)
            {
                byte[] set = HudTextPixels.Render(image.Text, out int width, out int height);
                source = new RgbaImage(width, height, set);
            }
            else
            {
                source = _load(image.Path);
                _catalogueCount++;
                if (glow) source = HudLabelPixels.Pad(source, FrontEndScrollBar.GlowPadding);
            }
            if (glow) source = HudLabelPixels.GlowFrame(source, image.Pieces);
            byte[] pixels = HudImagePixels.Apply(source.Pixels, source.Width, source.Height, image);
            if (blend == HudBlend.Additive) pixels = HudImagePixels.AdditiveOverAlpha(pixels);
            texture = MinimapFrameVisual.Decode(new RgbaImage(source.Width, source.Height, pixels));
            // Set text keeps hard bitmap edges when the UI scale stretches it, as the panels' own text did.
            if (image.Text != null) texture.filterMode = FilterMode.Point;
            _textures.Add((image, blend), texture);
            return texture;
        }

        /// <summary>A live image keeps one texture per <see cref="HudImage.Slot"/>, refilled in place when its
        /// parameter changes (a gauge's fraction), so a moving gauge creates no textures.</summary>
        private Texture2D Live(HudImage image, HudBlend blend)
        {
            if (!_live.TryGetValue((image.Slot, blend), out LiveTexture live))
            {
                RgbaImage source = _load(image.Path);
                live = new LiveTexture
                {
                    Source = source, Pixels = new byte[source.Pixels.Length], Rows = new byte[source.Pixels.Length],
                    Texture = new Texture2D(source.Width, source.Height, TextureFormat.RGBA32, false)
                        { name = image.Path, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp },
                };
                _live.Add((image.Slot, blend), live);
            }
            else if (live.Current == image) return live.Texture;
            live.Current = image;
            RgbaImage s = live.Source;
            HudImagePixels.Fill(s.Pixels, s.Width, s.Height, image, live.Pixels);
            byte[] pixels = blend == HudBlend.Additive ? HudImagePixels.AdditiveOverAlpha(live.Pixels) : live.Pixels;
            // Unity textures run bottom-up.
            int row = s.Width * 4;
            for (int y = 0; y < s.Height; y++) Buffer.BlockCopy(pixels, y * row, live.Rows, (s.Height - 1 - y) * row, row);
            live.Texture.LoadRawTextureData(live.Rows); live.Texture.Apply(false);
            return live.Texture;
        }

        private sealed class LiveTexture
        {
            public RgbaImage Source;
            public byte[] Pixels, Rows;
            public Texture2D Texture;
            public HudImage Current;
        }

        public void Dispose()
        {
            foreach (Texture2D texture in _textures.Values) UnityEngine.Object.Destroy(texture);
            foreach (LiveTexture live in _live.Values) UnityEngine.Object.Destroy(live.Texture);
            if (_white != null) UnityEngine.Object.Destroy(_white);
            if (_replacement != null) Font.textureRebuilt -= FontRebuilt;
            _replacement = null;
            if (_fontAtlas != null) { _fontAtlas.Release(); UnityEngine.Object.Destroy(_fontAtlas); _fontAtlas = null; }
            if (_fontMaterial != null) { UnityEngine.Object.Destroy(_fontMaterial); _fontMaterial = null; }
            _textures.Clear(); _live.Clear(); _catalogueCount = 0; _fontInfo.Clear(); _glowBase.Clear();
        }
    }
}
