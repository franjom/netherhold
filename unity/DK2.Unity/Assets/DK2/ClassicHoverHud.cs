// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.IO;
using DK2.Art.Textures;
using DK2.Data.Model;
using DK2.Data.Text;
using DK2.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace DK2.Unity
{
    public interface IClassicHoverAssets
    {
        BitmapFont Font(string fileName);
        RgbaImage StatusBar { get; }
        RgbaImage StatusEnd { get; }
        RgbaImage TooltipPiece(int index);
    }

    public sealed class InstallClassicHoverAssets : IClassicHoverAssets
    {
        private readonly string _textDirectory;
        private readonly Dictionary<string, BitmapFont> _fonts = new Dictionary<string, BitmapFont>();
        private readonly RgbaImage[] _pieces = new RgbaImage[9];
        public RgbaImage StatusBar { get; }
        public RgbaImage StatusEnd { get; }
        public InstallClassicHoverAssets(string install)
        {
            _textDirectory = Path.Combine(install, "Data", "Text", "Default");
            using (EngineTextureArchive textures = EngineTextureArchive.OpenInstall(install))
            {
                StatusBar = textures.Decode("GUI\\Windows\\Bar-00");
                StatusEnd = textures.Decode("GUI\\Windows\\Bar-01");
                string[] suffixes = { "tl", "t", "tr", "l", "m", "r", "bl", "b", "br" };
                for (int i = 0; i < 9; i++) _pieces[i] = textures.Decode("GUI\\Windows\\text_box-" + suffixes[i]);
            }
        }
        public RgbaImage TooltipPiece(int index) => _pieces[index];
        public BitmapFont Font(string file)
        {
            if (!_fonts.TryGetValue(file, out BitmapFont font))
            { font = Bf4FontReader.Open(Path.Combine(_textDirectory, file)); _fonts.Add(file, font); }
            return font;
        }
    }

    /// <summary>Retained UI Toolkit hover/status visuals from immutable settled snapshots.</summary>
    public sealed class ClassicHoverHud : IDisposable
    {
        private readonly IClassicHoverAssets _assets;
        private readonly Texture2D _status, _end;
        private readonly Texture2D[] _pieces = new Texture2D[9];
        private readonly HoverTooltipDelay _delay = new HoverTooltipDelay();
        private readonly GameObject _owner;
        private readonly PanelSettings _settings;
        private readonly VisualElement _root, _strip, _clip, _popup;
        private readonly Image _endImage, _statusImage, _tooltipImage;
        private readonly Image[] _popupPieces = new Image[9];
        private readonly List<Image> _stripTiles = new List<Image>();
        private BitmapFont _font, _tooltipFont;
        private Texture2D _statusTextTexture, _tooltipTextTexture;
        private string _statusText, _tooltipText;
        private float _scroll;
        private bool _scrollForward = true, _visible = true, _allowInput = true;
        private int _screenWidth, _screenHeight;
        public bool ShowCreatureTooltip { get; set; } = true;
        public PanelSettings Panel => _settings;
        public Rect StatusBounds => new Rect(ResourceWidth, 8 * Screen.height / 768f,
            Mathf.Max(0, Screen.width - ResourceWidth), StatusHeight);
        private float ResourceWidth => Mathf.Min(Screen.width, Mathf.Floor(1020 * Screen.height / 1920f));
        private float StatusHeight => Mathf.Max(22 * Screen.height / 480f, (_font?.LineHeight ?? 12) + 12);

        public ClassicHoverHud(IClassicHoverAssets assets) : this(null, assets) { }

        public ClassicHoverHud(Transform parent, IClassicHoverAssets assets)
        {
            _assets = assets ?? throw new ArgumentNullException(nameof(assets));
            _status = MinimapFrameVisual.Decode(assets.StatusBar);
            _end = MinimapFrameVisual.Decode(assets.StatusEnd);
            for (int i = 0; i < 9; i++) _pieces[i] = MinimapFrameVisual.Decode(assets.TooltipPiece(i));
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("DK2Hud");
            _settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            _settings.scale = 1;
            _settings.sortingOrder = 10;
            _owner = new GameObject("Dungeon hover HUD");
            if (parent != null) _owner.transform.SetParent(parent, false);
            var document = _owner.AddComponent<UIDocument>();
            document.panelSettings = _settings;
            _root = document.rootVisualElement;
            _root.pickingMode = PickingMode.Ignore;
            _root.style.width = Length.Percent(100); _root.style.height = Length.Percent(100);
            _strip = Element(_root);
            _strip.style.overflow = Overflow.Hidden;
            _endImage = Picture(_strip, _end);
            _clip = Element(_strip);
            _clip.style.overflow = Overflow.Hidden;
            _statusImage = Picture(_clip, null);
            _popup = Element(_root);
            for (int i = 0; i < 9; i++) _popupPieces[i] = Picture(_popup, _pieces[i]);
            _tooltipImage = Picture(_popup, null);
            _popup.style.display = DisplayStyle.None;
        }

        public void SetVisibility(HudVisibility visibility)
        {
            _visible = visibility.ShowNormalHud; _allowInput = visibility.AllowOrdinaryInput;
            _root.style.display = _visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!_visible) _delay.Visible(HoverTextSnapshot.Empty, (long)(Time.unscaledTimeAsDouble * 1000));
        }

        public bool ContainsPointer(Vector3 screenPosition) => _visible && _allowInput
            && StatusBounds.Contains(new Vector2(screenPosition.x, Screen.height - screenPosition.y));

        /// <summary>Refreshes retained visuals in Update, before worker simulation dispatch.</summary>
        public void Draw(HoverTextSnapshot snapshot, Vector2 cursorGui)
        {
            if (!_visible) return;
            BitmapFont font = _font, tooltipFont = _tooltipFont;
            bool sizeChanged = _screenWidth != Screen.width || _screenHeight != Screen.height;
            if (font == null || _screenHeight != Screen.height)
            {
                font = _assets.Font(InterfaceFonts.HudTextFileName(Screen.height));
                tooltipFont = _assets.Font(InterfaceFonts.HudTooltipFileName(Screen.height));
            }
            bool fontChanged = !ReferenceEquals(font, _font) || !ReferenceEquals(tooltipFont, _tooltipFont);
            _font = font; _tooltipFont = tooltipFont; _screenHeight = Screen.height; _screenWidth = Screen.width;
            string status = snapshot.TopStatus ?? "", tooltip = snapshot.TooltipName ?? "";
            if (_statusText != status || fontChanged)
            {
                _statusText = status; _scroll = 0; _scrollForward = true;
                ReplaceText(_statusImage, ref _statusTextTexture, font,
                    new[] { new BitmapTextLine(status, BitmapTextLayout.Measure(font, status)) }, false);
            }
            if (_tooltipText != tooltip || fontChanged || sizeChanged)
            {
                _tooltipText = tooltip;
                ReplaceText(_tooltipImage, ref _tooltipTextTexture, tooltipFont,
                    BitmapTextLayout.Wrap(tooltipFont, tooltip, Mathf.Max(1, Screen.width / 2)), true);
            }
            Rect strip = StatusBounds;
            if (sizeChanged || fontChanged) LayoutStrip(strip);
            float width = _statusTextTexture?.width ?? 0;
            float overflow = Mathf.Max(0, width - (strip.width - 24));
            if (overflow > 0)
            {
                _scroll += (_scrollForward ? 1 : -1) * Time.unscaledDeltaTime * 60;
                if (_scroll >= overflow) { _scroll = overflow; _scrollForward = false; }
                if (_scroll <= 0) { _scroll = 0; _scrollForward = true; }
            }
            else _scroll = 0;
            Place(_statusImage, -_scroll, (strip.height - (_statusTextTexture?.height ?? 0)) / 2,
                width, _statusTextTexture?.height ?? 0);
            bool popupVisible = _delay.Visible(snapshot, (long)(Time.unscaledTimeAsDouble * 1000), ShowCreatureTooltip);
            _popup.style.display = popupVisible ? DisplayStyle.Flex : DisplayStyle.None;
            if (popupVisible) LayoutTooltip(cursorGui);
        }

        private void LayoutStrip(Rect strip)
        {
            Place(_strip, strip.x, strip.y, strip.width, strip.height);
            Place(_clip, 12, 0, Mathf.Max(1, strip.width - 24), strip.height);
            float scale = strip.height / Mathf.Max(1, _status.height);
            float tileWidth = _status.width * scale, endWidth = _end.width * scale;
            int count = Mathf.CeilToInt(strip.width / tileWidth);
            while (_stripTiles.Count < count)
            {
                Image tile = Picture(_strip, _status);
                tile.SendToBack(); _stripTiles.Add(tile);
            }
            for (int i = 0; i < _stripTiles.Count; i++)
            {
                _stripTiles[i].style.display = i < count ? DisplayStyle.Flex : DisplayStyle.None;
                if (i < count) Place(_stripTiles[i], i * tileWidth, 0, tileWidth, strip.height);
            }
            Place(_endImage, strip.width - endWidth, 0, endWidth, strip.height);
            _endImage.BringToFront(); _clip.BringToFront();
        }

        private void LayoutTooltip(Vector2 cursor)
        {
            if (_tooltipTextTexture == null) return;
            float padding = Mathf.Max(4, Screen.height / 120f);
            float width = Mathf.Min(Screen.width, _tooltipTextTexture.width + padding * 2);
            float height = _tooltipTextTexture.height + padding * 2;
            float x = cursor.x + 98 * Screen.width / 640f;
            if (x + width > Screen.width - 40 * Screen.width / 640f)
                x = cursor.x - width - 30 * Screen.width / 640f;
            x = Mathf.Clamp(x, 0, Mathf.Max(0, Screen.width - width));
            float y = Mathf.Clamp(cursor.y - _tooltipFont.LineHeight, StatusHeight,
                Mathf.Max(StatusHeight, Screen.height - height - 10));
            Place(_popup, x, y, width, height);
            float border = Mathf.Min(padding, Mathf.Min(width, height) / 2);
            for (int row = 0; row < 3; row++)
            for (int column = 0; column < 3; column++)
            {
                float left = column == 0 ? 0 : column == 1 ? border : width - border;
                float top = row == 0 ? 0 : row == 1 ? border : height - border;
                Place(_popupPieces[row * 3 + column], left, top,
                    column == 1 ? width - border * 2 : border, row == 1 ? height - border * 2 : border);
            }
            Place(_tooltipImage, (width - _tooltipTextTexture.width) / 2,
                padding, _tooltipTextTexture.width, _tooltipTextTexture.height);
        }

        private void ReplaceText(Image image, ref Texture2D previous, BitmapFont font, IReadOnlyList<BitmapTextLine> lines, bool centred)
        {
            Texture2D next = Raster(font, lines, centred);
            image.image = next;
            if (previous != null) UnityEngine.Object.Destroy(previous);
            previous = next;
        }

        private static Texture2D Raster(BitmapFont font, IReadOnlyList<BitmapTextLine> lines, bool centred)
        {
            int width = 1, minX = 0, minY = 0, maxY = Math.Max(1, lines.Count * font.LineHeight);
            foreach (BitmapTextLine line in lines) width = Math.Max(width, line.Width);
            for (int row = 0; row < lines.Count; row++)
            {
                int pen = centred ? (width - lines[row].Width) / 2 : 0;
                foreach (char character in lines[row].Text)
                {
                    BitmapGlyph glyph = font.Find(character);
                    if (glyph == null) continue;
                    minX = Math.Min(minX, pen + glyph.OffsetX);
                    minY = Math.Min(minY, row * font.LineHeight + glyph.OffsetY);
                    maxY = Math.Max(maxY, row * font.LineHeight + glyph.OffsetY + glyph.Height);
                    pen += glyph.Advance;
                }
            }
            int height = Math.Max(1, maxY - minY);
            // Preserve advances and glyph bearings, including overhang beyond the final advance.
            int maxX = width;
            for (int row = 0; row < lines.Count; row++)
            {
                int pen = centred ? (width - lines[row].Width) / 2 : 0;
                foreach (char character in lines[row].Text)
                {
                    BitmapGlyph glyph = font.Find(character);
                    if (glyph == null) continue;
                    maxX = Math.Max(maxX, pen + glyph.OffsetX + glyph.Width);
                    pen += glyph.Advance;
                }
            }
            int textureWidth = Math.Max(1, maxX - minX);
            var pixels = new Color32[textureWidth * height];
            for (int row = 0; row < lines.Count; row++)
            {
                int pen = (centred ? (width - lines[row].Width) / 2 : 0) - minX;
                foreach (char character in lines[row].Text)
                {
                    BitmapGlyph glyph = font.Find(character);
                    if (glyph == null) continue;
                    for (int gy = 0; gy < glyph.Height; gy++)
                    for (int gx = 0; gx < glyph.Width; gx++)
                    {
                        int px = pen + glyph.OffsetX + gx;
                        int py = row * font.LineHeight + glyph.OffsetY - minY + gy;
                        int index = (height - 1 - py) * textureWidth + px;
                        byte alpha = (byte)(glyph.Pixels[gy * glyph.Width + gx] * 17);
                        if (alpha > pixels[index].a) pixels[index] = new Color32(255, 255, 255, alpha);
                    }
                    pen += glyph.Advance;
                }
            }
            var texture = new Texture2D(textureWidth, height, TextureFormat.RGBA32, false, true)
            { name = "DK2 retained BF4 text", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(pixels); texture.Apply(false, true);
            return texture;
        }

        private static VisualElement Element(VisualElement parent)
        {
            var element = new VisualElement { pickingMode = PickingMode.Ignore };
            element.style.position = Position.Absolute; parent.Add(element); return element;
        }
        private static Image Picture(VisualElement parent, Texture2D texture)
        {
            var image = new Image { image = texture, pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.StretchToFill };
            image.style.position = Position.Absolute; parent.Add(image); return image;
        }
        private static void Place(VisualElement element, float x, float y, float width, float height)
        {
            element.style.left = x; element.style.top = y; element.style.width = width; element.style.height = height;
        }

        public void Dispose()
        {
            _root.RemoveFromHierarchy();
            UnityEngine.Object.Destroy(_owner); UnityEngine.Object.Destroy(_settings);
            UnityEngine.Object.Destroy(_statusTextTexture); UnityEngine.Object.Destroy(_tooltipTextTexture);
            UnityEngine.Object.Destroy(_status); UnityEngine.Object.Destroy(_end);
            foreach (Texture2D piece in _pieces) UnityEngine.Object.Destroy(piece);
        }
    }
}
