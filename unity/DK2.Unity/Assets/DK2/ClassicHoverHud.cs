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
        private readonly InstallHudSources _sources;
        private readonly RgbaImage[] _pieces = new RgbaImage[9];
        public RgbaImage StatusBar { get; }
        public RgbaImage StatusEnd { get; }

        /// <summary>From the install alone; its texture archive is closed once the art is read.</summary>
        public InstallClassicHoverAssets(string install) : this(new InstallHudSources(install))
        {
            _sources.Dispose();
        }

        public InstallClassicHoverAssets(InstallHudSources sources)
        {
            _sources = sources ?? throw new ArgumentNullException(nameof(sources));
            StatusBar = sources.Texture("GUI\\Windows\\Bar-00");
            StatusEnd = sources.Texture("GUI\\Windows\\Bar-01");
            string[] suffixes = { "tl", "t", "tr", "l", "m", "r", "bl", "b", "br" };
            for (int i = 0; i < 9; i++) _pieces[i] = sources.Texture("GUI\\Windows\\text_box-" + suffixes[i]);
        }
        public RgbaImage TooltipPiece(int index) => _pieces[index];
        public BitmapFont Font(string file) => _sources.Font(file);
    }

    /// <summary>Retained UI Toolkit hover/status visuals from immutable settled snapshots.</summary>
    public sealed class ClassicHoverHud : IDisposable
    {
        private readonly IClassicHoverAssets _assets;
        private readonly Texture2D _status, _end;
        private readonly Texture2D[] _pieces = new Texture2D[9];
        private readonly HoverTooltipDelay _delay = new HoverTooltipDelay();
        private readonly GameObject _owner, _barOwner;
        private readonly PanelSettings _settings, _barSettings;
        private readonly VisualElement _root, _bar, _strip, _clip, _popup;
        private readonly Image _endImage, _statusImage, _tooltipImage;
        private readonly Image[] _popupPieces = new Image[9];
        private readonly List<Image> _stripTiles = new List<Image>();
        private BitmapFont _font, _tooltipFont;
        private Texture2D _statusTextTexture, _tooltipTextTexture;
        private string _statusText, _tooltipText;
        private float _scroll, _clipWidth = 1, _cursorReach = 80;
        private bool _scrollForward = true, _visible = true, _allowInput = true;
        private int _screenWidth, _screenHeight;
        public bool ShowCreatureTooltip { get; set; } = true;
        public PanelSettings Panel => _settings;
        public Rect StatusBounds => new Rect(ResourceWidth, StatusTop,
            Mathf.Max(0, Screen.width - ResourceWidth), StatusHeight);
        /// <summary>The wooden bar: the whole width, behind the Heart, mana and gold panels, as in DK2.</summary>
        public Rect BarBounds => new Rect(0, StatusTop, Screen.width, StatusHeight);
        private static float StatusTop => 8 * Screen.height / 768f;
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
            _settings.sortingOrder = 17;   // tooltips over every panel but the full map (20)
            _owner = new GameObject("Dungeon hover HUD");
            if (parent != null) _owner.transform.SetParent(parent, false);
            var document = _owner.AddComponent<UIDocument>();
            document.panelSettings = _settings;
            _root = document.rootVisualElement;
            _root.pickingMode = PickingMode.Ignore;
            _root.style.width = Length.Percent(100); _root.style.height = Length.Percent(100);
            // The bar draws under the resource panels (11); its text and the popup stay on top.
            _barSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _barSettings.themeStyleSheet = _settings.themeStyleSheet;
            _barSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
            _barSettings.scale = 1;
            _barSettings.sortingOrder = 10;
            _barOwner = new GameObject("Dungeon status bar");
            if (parent != null) _barOwner.transform.SetParent(parent, false);
            var barDocument = _barOwner.AddComponent<UIDocument>();
            barDocument.panelSettings = _barSettings;
            VisualElement barRoot = barDocument.rootVisualElement;
            barRoot.pickingMode = PickingMode.Ignore;
            barRoot.style.width = Length.Percent(100); barRoot.style.height = Length.Percent(100);
            _bar = Element(barRoot);
            _bar.style.overflow = Overflow.Hidden;
            // The cap is not clipped to the bar: it is taller and runs off the right edge.
            _endImage = Picture(barRoot, _end);
            _strip = Element(_root);
            _strip.style.overflow = Overflow.Hidden;
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
            _bar.style.display = _root.style.display;
            _endImage.style.display = _root.style.display;
            if (!_visible) _delay.Visible(HoverTextSnapshot.Empty, (long)(Time.unscaledTimeAsDouble * 1000));
        }

        public bool ContainsPointer(Vector3 screenPosition) => _visible && _allowInput
            && BarBounds.Contains(new Vector2(screenPosition.x, Screen.height - screenPosition.y));

        /// <summary>Refreshes retained visuals in Update, before worker simulation dispatch.</summary>
        /// <param name="cursorReach">How far the drawn cursor reaches right of the pointer, in DK2's
        /// 640×480 units. DK2's 98-unit offset is the 82-wide Point hand (hotspot 2) plus 18; the
        /// small pointer shown over the HUD is 42 wide, so the same offset left a wide gap (owner, 2026-10-20).</param>
        public void Draw(HoverTextSnapshot snapshot, Vector2 cursorGui, float cursorReach = 80)
        {
            _cursorReach = cursorReach;
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
            float overflow = Mathf.Max(0, width - _clipWidth);
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
            Rect bar = BarBounds;
            float scale = bar.height / Mathf.Max(1, _status.height);
            float tileWidth = _status.width * scale;
            // 0x0042E140: the cap is a 128-unit square at (W - 80, 0), virtual 2560x1920, over a
            // bar of 88 at y20, so it stands above and below the bar and 48 units off the right edge.
            float unit = Screen.height / 1920f, cap = 128 * unit, capLeft = bar.width - 80 * unit;
            Place(_strip, strip.x, strip.y, strip.width, strip.height);
            // The text stops short of the iron end cap.
            _clipWidth = Mathf.Max(1, capLeft - strip.x - 24);
            Place(_clip, 12, 0, _clipWidth, strip.height);
            Place(_bar, bar.x, bar.y, bar.width, bar.height);
            int count = Mathf.CeilToInt(bar.width / tileWidth);
            while (_stripTiles.Count < count)
            {
                Image tile = Picture(_bar, _status);
                tile.SendToBack(); _stripTiles.Add(tile);
            }
            for (int i = 0; i < _stripTiles.Count; i++)
            {
                _stripTiles[i].style.display = i < count ? DisplayStyle.Flex : DisplayStyle.None;
                if (i < count) Place(_stripTiles[i], i * tileWidth, 0, tileWidth, bar.height);
            }
            Place(_endImage, capLeft, 0, cap, cap);
            _endImage.BringToFront();
        }

        private void LayoutTooltip(Vector2 cursor)
        {
            if (_tooltipTextTexture == null) return;
            float textWidth = _tooltipTextTexture.width, textHeight = _tooltipTextTexture.height;
            float padding = Mathf.Max(4, InterfaceFonts.HudTooltipPixels(Screen.height));
            float width = Mathf.Min(Screen.width, textWidth + padding * 2);
            float height = textHeight + padding * 2;
            // DK2 offsets the popup 98 units at width 640, and scales the Hand by that width too.
            // Our Hand scales by height/480 (HandCursor.Place), so the offsets follow it.
            float unit = Screen.height / 480f;
            float x = cursor.x + (_cursorReach + 18) * unit;
            if (x + width > Screen.width - 40 * unit)
                x = cursor.x - width - 30 * unit;
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
            Place(_tooltipImage, (width - textWidth) / 2, padding, textWidth, textHeight);
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
            _root.RemoveFromHierarchy(); _bar.RemoveFromHierarchy(); _endImage.RemoveFromHierarchy();
            UnityEngine.Object.Destroy(_owner); UnityEngine.Object.Destroy(_settings);
            UnityEngine.Object.Destroy(_barOwner); UnityEngine.Object.Destroy(_barSettings);
            UnityEngine.Object.Destroy(_statusTextTexture); UnityEngine.Object.Destroy(_tooltipTextTexture);
            UnityEngine.Object.Destroy(_status); UnityEngine.Object.Destroy(_end);
            foreach (Texture2D piece in _pieces) UnityEngine.Object.Destroy(piece);
        }
    }
}
