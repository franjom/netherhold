// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using DK2.Data.Model;
using DK2.Art.Textures;
using DK2.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace DK2.Unity
{
    /// <summary>Front-end overlay over the native station view, with bitmap-matched replacement glyphs.</summary>
    public sealed class ClassicFrontEndMenu : IDisposable
    {
        private static readonly uint[] TitleIds = { 71, 1476, 1403, 2803, 96, 2802, 145, 73, 93, 2800,
            94, 95, 98, 168, 162, 141, 149, 2729, 1478, 2801, 372, 1675, 140, 0 };
        private sealed class LabelData
        {
            public FrontEndWidget Widget;
            public FrontEndLabelGeometry Geometry;
            public string Text;
            public Texture2D Bitmap;
            public Texture2D[] Glow;
            public CharacterInfo[] FontGlyphs;
            public bool UsesFont;
            public FrontEndTextAlignment Alignment;
            public FrontEndVerticalAlignment Vertical;
        }
        private sealed class Canvas : VisualElement
        {
            public Canvas(Action<MeshGenerationContext> draw) { generateVisualContent += draw; pickingMode = PickingMode.Ignore; }
        }
        private readonly IClassicFrontEndMenuAssets _assets;
        private readonly GameObject _owner;
        private readonly PanelSettings _panel;
        private readonly Canvas _canvas;
        private readonly Dictionary<FrontEndWidgetId, LabelData> _labels = new Dictionary<FrontEndWidgetId, LabelData>();
        private FrontEndPage _shown = (FrontEndPage)ushort.MaxValue;
        private bool _fontDirty = true, _useReplacement = true;
        private RenderTexture _fontAtlas;
        private Material _fontMaterial;
        private const int FontResolution = 96;
        public PanelSettings Panel => _panel;
        public FrontEndMenuController Model { get; }
        public bool UseReplacementFont
        {
            get => _useReplacement;
            set { if (_useReplacement != value) { _useReplacement = value; _fontDirty = true; } }
        }
        public bool UsingReplacementFont => _labels.Values.Any(label => label.UsesFont);

        public ClassicFrontEndMenu(Transform parent, FrontEndSession session, IClassicFrontEndMenuAssets assets)
        {
            _assets = assets ?? throw new ArgumentNullException(nameof(assets));
            Model = new FrontEndMenuController(session, HitBounds);
            _panel = ScriptableObject.CreateInstance<PanelSettings>();
            _panel.themeStyleSheet = Resources.Load<ThemeStyleSheet>("DK2Hud");
            _panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            _panel.referenceResolution = new Vector2Int(640, 480);
            _panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight; _panel.match = 1;
            _panel.sortingOrder = 20;
            _owner = new GameObject("Front-end menu"); _owner.transform.SetParent(parent, false);
            var document = _owner.AddComponent<UIDocument>(); document.panelSettings = _panel;
            VisualElement root = document.rootVisualElement; root.pickingMode = PickingMode.Ignore;
            root.style.width = Length.Percent(100); root.style.height = Length.Percent(100);
            _canvas = new Canvas(Draw);
            _canvas.style.position = Position.Absolute; _canvas.style.width = Length.Percent(100); _canvas.style.height = Length.Percent(100);
            root.Add(_canvas);
            Font.textureRebuilt += FontRebuilt;
        }

        public FrontEndRect HitBounds(FrontEndWidget widget)
        {
            FrontEndRect rect = LabelBox(widget);
            var hit = new FrontEndRect(rect.X + widget.HitRect.X * .25f, rect.Y + widget.HitRect.Y * .25f,
                widget.HitRect.Width * .25f, widget.HitRect.Height * .25f);
            return _labels.TryGetValue(widget.Id, out LabelData label) && widget.Draw == FrontEndDrawKind.Label
                ? label.Geometry.PlaceCanvas(rect, label.Alignment, label.Vertical).Inflate(8).Intersect(hit) : hit;
        }
        public FrontEndRect LabelInkBounds(FrontEndWidgetId id)
        {
            LabelData label = _labels[id];
            return label.Geometry.PlaceInk(LabelBox(label.Widget), label.Alignment, label.Vertical);
        }
        private FrontEndRect LabelBox(FrontEndWidget widget)
        {
            FrontEndRect rect = widget.ReferenceRect(FrontEndPages.Get(Model.Page).DrawRect);
            return widget.Draw == FrontEndDrawKind.Title ? new FrontEndRect(rect.X, rect.Y, rect.Width, 70) : rect;
        }
        public void Refresh(bool pollInput = true)
        {
            if (_shown != Model.Page) RebuildLabels();
            if (_fontDirty) PrepareFont();
            if (pollInput)
            {
                int width = _panel.targetTexture != null ? _panel.targetTexture.width : Screen.width;
                int height = _panel.targetTexture != null ? _panel.targetTexture.height : Screen.height;
                Vector3 mouse = Input.mousePosition;
                if (width > 0 && height > 0)
                    Model.UpdatePointer(new FrontEndPointer(mouse.x * 640 / width, (height - mouse.y) * 480 / height, Input.GetMouseButton(0)));
                Model.AdvanceFrame();
            }
            if (_shown != Model.Page) { RebuildLabels(); PrepareFont(); }
            _canvas.MarkDirtyRepaint();
        }
        private void RebuildLabels()
        {
            foreach (LabelData label in _labels.Values) DestroyLabel(label);
            _labels.Clear(); _shown = Model.Page;
            foreach (FrontEndWidget widget in Model.Widgets)
            {
                uint textId = widget.TextId;
                FrontEndFont fontSpec = FrontEndLabelStyles.FontFor(widget.Draw);
                if (widget.Draw == FrontEndDrawKind.Title)
                { textId = widget.TextId < TitleIds.Length ? TitleIds[widget.TextId] : 0; }
                else if (Model.Dialog != null)
                {
                    if (widget.Id.Value == 684) textId = Model.Dialog.TitleTextId;
                    else if (widget.Id.Value == 686) textId = Model.Dialog.BodyTextId;
                    else continue;
                }
                else if (!FrontEndLabelStyles.IsText(widget.Draw)) continue;
                if (textId == 0) continue;
                string text = _assets.Text(textId);
                BitmapFont font = _assets.Font(fontSpec);
                FrontEndLabelGeometry geometry = FrontEndLabelGeometry.Measure(font, text);
                if (widget.Draw == FrontEndDrawKind.Title && geometry.Advance > 629)
                { font = _assets.Font(new FrontEndFont(FrontEndFontFamily.FrontEnd, 28)); geometry = FrontEndLabelGeometry.Measure(font, text); }
                _labels.Add(widget.Id, new LabelData { Widget = widget, Text = text, Geometry = geometry, Bitmap = Bitmap(font, geometry),
                    Glow = widget.Draw == FrontEndDrawKind.Label ? Glow(font, geometry) : null,
                    Alignment = FrontEndLabelStyles.AlignmentFor(widget), Vertical = FrontEndLabelStyles.VerticalFor(widget) });
            }
            _fontDirty = true;
        }
        private void FontRebuilt(Font font) { if (font == _assets.ReplacementFont) _fontDirty = true; }
        private void PrepareFont()
        {
            Font font = _useReplacement ? _assets.ReplacementFont : null;
            if (font != null) font.RequestCharactersInTexture(string.Concat(_labels.Values.Select(label => label.Text)), FontResolution, FontStyle.Normal);
            if (font != null)
            {
                Texture source = font.material.mainTexture;
                if (_fontAtlas == null || _fontAtlas.width != source.width || _fontAtlas.height != source.height)
                {
                    if (_fontAtlas != null) { _fontAtlas.Release(); UnityEngine.Object.Destroy(_fontAtlas); }
                    _fontAtlas = new RenderTexture(source.width, source.height, 0, RenderTextureFormat.ARGB32);
                    _fontAtlas.Create();
                }
                if (_fontMaterial == null) _fontMaterial = new Material(Resources.Load<Shader>("DK2FrontEndFont"));
                Graphics.Blit(source, _fontAtlas, _fontMaterial);
            }
            foreach (LabelData label in _labels.Values)
            {
                label.UsesFont = font != null;
                if (font == null) continue;
                label.FontGlyphs = new CharacterInfo[label.Geometry.Glyphs.Count];
                for (int i = 0; i < label.FontGlyphs.Length; i++)
                    if (!font.GetCharacterInfo(label.Geometry.Glyphs[i].Character, out label.FontGlyphs[i], FontResolution, FontStyle.Normal))
                    { label.UsesFont = false; break; }
            }
            _fontDirty = false;
        }

        private void Draw(MeshGenerationContext context)
        {
            if (_shown == FrontEndPage.None) return;
            float sx = _canvas.contentRect.width / 640, sy = _canvas.contentRect.height / 480;
            if (sx <= 0 || sy <= 0) return;
            foreach (FrontEndWidget widget in Model.Widgets)
            {
                FrontEndWidgetState state = Model.State(widget);
                FrontEndRect rect = LabelBox(widget);
                if (_labels.TryGetValue(widget.Id, out LabelData label))
                {
                    bool decoration = widget.Draw != FrontEndDrawKind.Label && widget.Primary.Kind == FrontEndActionKind.None;
                    byte alpha = state.Enabled || decoration ? (byte)255 : (byte)90;
                    Color32 tint = LabelColour(widget, state.Hovered, alpha);
                    FrontEndRect ink = label.Geometry.PlaceInk(rect, label.Alignment, label.Vertical);
                    if (state.GlowFrame >= 0 && label.Glow != null)
                        Quad(context, label.Glow[state.GlowFrame], ink.Inflate(16), Color.white, sx, sy);
                    DrawLabel(context, label, ink, tint, sx, sy);
                }
                else if (widget.Draw == FrontEndDrawKind.Logo)
                {
                    Texture2D logo = _assets.Image("Logo");
                    Quad(context, logo, new FrontEndRect(rect.X, rect.Y, logo.width, logo.height), Color.white, sx, sy);
                }
                else if (widget.Draw == FrontEndDrawKind.Accept || widget.Draw == FrontEndDrawKind.Cancel)
                {
                    string name = widget.Draw == FrontEndDrawKind.Accept ? "Confirm" : "Cancel";
                    if (state.Pressed) name += "-Click";
                    else if (state.Hovered) name += "-Over";
                    Quad(context, _assets.Image(name), rect, state.Enabled ? Color.white : new Color(1, 1, 1, .35f), sx, sy);
                }
            }
        }
        private static Color32 LabelColour(FrontEndWidget widget, bool hovered, byte alpha)
        {
            if (widget.Draw == FrontEndDrawKind.Title) return new Color32(255, 176, 51, alpha);
            if (widget.Draw == FrontEndDrawKind.Label)
                return hovered ? new Color32(255, 255, 255, alpha) : new Color32(191, 191, 191, alpha);
            switch (widget.DrawArgument2 >> 16)
            {
                case 1: return new Color32(253, 242, 167, alpha);
                case 2: return new Color32(255, 176, 51, alpha);
                case 3: return new Color32(191, 191, 191, alpha);
                default: return new Color32(255, 255, 255, alpha);
            }
        }
        private void DrawLabel(MeshGenerationContext context, LabelData label, FrontEndRect ink, Color32 tint, float sx, float sy)
        {
            if (!label.UsesFont)
            { Quad(context, label.Bitmap, ink, tint, sx, sy); return; }
            Texture atlas = _fontAtlas;
            for (int i = 0; i < label.Geometry.Glyphs.Count; i++)
            {
                FrontEndGlyph glyph = label.Geometry.Glyphs[i]; CharacterInfo info = label.FontGlyphs[i];
                var rect = new FrontEndRect(ink.X + glyph.InkBounds.X - label.Geometry.InkBounds.X,
                    ink.Y + glyph.InkBounds.Y - label.Geometry.InkBounds.Y, glyph.InkBounds.Width, glyph.InkBounds.Height);
                Quad(context, atlas, rect, tint, sx, sy, info.uvTopLeft, info.uvTopRight, info.uvBottomRight, info.uvBottomLeft);
            }
        }
        private static void Quad(MeshGenerationContext context, Texture texture, FrontEndRect rect, Color32 tint, float sx, float sy)
            => Quad(context, texture, rect, tint, sx, sy, new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), Vector2.zero);
        private static void Quad(MeshGenerationContext context, Texture texture, FrontEndRect rect, Color32 tint, float sx, float sy,
            Vector2 tl, Vector2 tr, Vector2 br, Vector2 bl)
        {
            if (texture == null || rect.Width <= 0 || rect.Height <= 0) return;
            MeshWriteData mesh = context.Allocate(4, 6, texture);
            Vertex Write(float x, float y, Vector2 source) => new Vertex { position = new Vector3(x * sx, y * sy, Vertex.nearZ),
                tint = tint, uv = source };
            mesh.SetNextVertex(Write(rect.X, rect.Y, tl)); mesh.SetNextVertex(Write(rect.X + rect.Width, rect.Y, tr));
            mesh.SetNextVertex(Write(rect.X + rect.Width, rect.Y + rect.Height, br)); mesh.SetNextVertex(Write(rect.X, rect.Y + rect.Height, bl));
            mesh.SetNextIndex(0); mesh.SetNextIndex(1); mesh.SetNextIndex(2);
            mesh.SetNextIndex(2); mesh.SetNextIndex(3); mesh.SetNextIndex(0);
        }
        private static RgbaImage Raster(BitmapFont font, FrontEndLabelGeometry geometry, int padding)
        {
            int width = Math.Max(1, (int)geometry.InkBounds.Width) + 2 * padding;
            int height = Math.Max(1, (int)geometry.InkBounds.Height) + 2 * padding;
            var pixels = new byte[width * height * 4];
            foreach (FrontEndGlyph item in geometry.Glyphs)
            {
                BitmapGlyph glyph = font.Find(item.Character);
                for (int y = 0; y < item.InkBounds.Height; y++) for (int x = 0; x < item.InkBounds.Width; x++)
                {
                    int targetX = (int)(item.InkBounds.X - geometry.InkBounds.X) + x + padding;
                    int targetY = (int)(item.InkBounds.Y - geometry.InkBounds.Y) + y + padding;
                    byte alpha = (byte)(glyph.Pixels[((int)item.SourceInkBounds.Y + y) * glyph.Width + (int)item.SourceInkBounds.X + x] * 17);
                    int at = (targetY * width + targetX) * 4;
                    pixels[at] = 255; pixels[at + 1] = 255; pixels[at + 2] = 255; pixels[at + 3] = alpha;
                }
            }
            return new RgbaImage(width, height, pixels);
        }
        private static Texture2D Bitmap(BitmapFont font, FrontEndLabelGeometry geometry) => MinimapFrameVisual.Decode(Raster(font, geometry, 0));
        private static Texture2D[] Glow(BitmapFont font, FrontEndLabelGeometry geometry)
        {
            RgbaImage full = FrontEndLabelGlow.Create(Raster(font, geometry, 16));
            var frames = new Texture2D[4];
            for (int frame = 0; frame < frames.Length; frame++)
            {
                byte[] pixels = (byte[])full.Pixels.Clone();
                for (int at = 3; at < pixels.Length; at += 4) pixels[at] = (byte)(pixels[at] * (frame + 1) / 4);
                frames[frame] = MinimapFrameVisual.Decode(new RgbaImage(full.Width, full.Height, pixels));
            }
            return frames;
        }
        private static void DestroyLabel(LabelData label)
        {
            UnityEngine.Object.Destroy(label.Bitmap);
            if (label.Glow != null) foreach (Texture2D glow in label.Glow) UnityEngine.Object.Destroy(glow);
        }
        public void Dispose()
        {
            Font.textureRebuilt -= FontRebuilt;
            Model.Dispose();
            foreach (LabelData label in _labels.Values) DestroyLabel(label);
            if (_fontAtlas != null) { _fontAtlas.Release(); UnityEngine.Object.Destroy(_fontAtlas); }
            if (_fontMaterial != null) UnityEngine.Object.Destroy(_fontMaterial);
            _assets.Dispose(); UnityEngine.Object.Destroy(_owner); UnityEngine.Object.Destroy(_panel);
        }
    }
}
