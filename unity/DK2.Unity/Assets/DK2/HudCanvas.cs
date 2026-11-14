// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using DK2.Art.Textures;
using DK2.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace DK2.Unity
{
    /// <summary>Draws a <see cref="HudFrame"/> built in DK2.Presentation (ADR-0805) as one UI Toolkit mesh,
    /// rebuilt only when the frame changes. It owns the textures and picks only where
    /// <paramref name="hit"/> says the model has a widget, so pointer events elsewhere pass through.</summary>
    public sealed class HudCanvas : VisualElement, IDisposable
    {
        private readonly Func<string, RgbaImage> _load;
        private readonly Func<float, float, bool> _hit;
        private readonly Dictionary<(HudImage, HudBlend), Texture2D> _textures = new Dictionary<(HudImage, HudBlend), Texture2D>();
        private readonly Dictionary<(HudImage, HudBlend), LiveTexture> _live = new Dictionary<(HudImage, HudBlend), LiveTexture>();
        private readonly HudFrame _shown = new HudFrame();
        private Texture2D[] _resolved = new Texture2D[256];
        private float _scale = 1, _pixelsPerUnit = 1;

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

        /// <summary>A local point in virtual units.</summary>
        public Vector2 ToVirtual(Vector2 local) => local / _scale;

        /// <param name="scale">Local units per virtual unit.</param>
        /// <param name="pixelsPerUnit">Screen pixels per local unit (the panel's scale); edges snap to them.</param>
        public void Present(HudFrame frame, float scale, float pixelsPerUnit)
        {
            if (scale == _scale && pixelsPerUnit == _pixelsPerUnit && frame.SameAs(_shown)) return;
            if (_resolved.Length < frame.Count) _resolved = new Texture2D[Math.Max(frame.Count, _resolved.Length * 2)];
            // Textures first: if loading throws, the frame is not taken and the next one tries again.
            ReadOnlySpan<HudDraw> draws = frame.Draws;
            for (int i = 0; i < draws.Length; i++) _resolved[i] = Texture(draws[i].Image, draws[i].Blend);
            _scale = scale; _pixelsPerUnit = pixelsPerUnit; _shown.CopyFrom(frame);
            MarkDirtyRepaint();
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
                Texture2D texture = _resolved[i];
                if (texture == null) continue;
                HudDraw draw = draws[i];
                var tint = new Color32(draw.Tint.R, draw.Tint.G, draw.Tint.B, draw.Tint.A);
                Rect r = Snapped(draw.Rect);
                if (draw.Kind == HudDrawKind.Quad)
                {
                    if (r.width <= 0 || r.height <= 0) continue;
                    MeshWriteData quad = context.Allocate(4, 6, texture);
                    Vertex(quad, r.xMin, r.yMin, 0, 1, tint);
                    Vertex(quad, r.xMax, r.yMin, 1, 1, tint);
                    Vertex(quad, r.xMax, r.yMax, 1, 0, tint);
                    Vertex(quad, r.xMin, r.yMax, 0, 0, tint);
                    // Clockwise on screen: UI Toolkit's winding.
                    quad.SetNextIndex(0); quad.SetNextIndex(1); quad.SetNextIndex(2);
                    quad.SetNextIndex(0); quad.SetNextIndex(2); quad.SetNextIndex(3);
                    continue;
                }
                // The fan is laid over the snapped rect in local units, as the old sector element's content rect was.
                int n = HudSector.Fan(new HudRect(r.x, r.y, r.width, r.height), draw.Percent, fan);
                if (n == 0) continue;
                MeshWriteData mesh = context.Allocate(n, (n - 2) * 3, texture);
                for (int k = 0; k < n; k++) Vertex(mesh, fan[k].X, fan[k].Y, fan[k].U, 1 - fan[k].V, tint);
                for (int k = 1; k < n - 1; k++)
                { mesh.SetNextIndex(0); mesh.SetNextIndex((ushort)k); mesh.SetNextIndex((ushort)(k + 1)); }
            }
        }

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
            RgbaImage source = _load(image.Path);
            byte[] pixels = HudImagePixels.Apply(source.Pixels, source.Width, source.Height, image);
            if (blend == HudBlend.Additive) pixels = HudImagePixels.AdditiveOverAlpha(pixels);
            texture = MinimapFrameVisual.Decode(new RgbaImage(source.Width, source.Height, pixels));
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
            _textures.Clear(); _live.Clear();
        }
    }
}
