// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.Globalization;
using DK2.Art.Textures;
using DK2.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace DK2.Unity
{
    public interface IClassicResourceAssets { RgbaImage Image(string name); }

    public sealed class InstallClassicResourceAssets : IClassicResourceAssets, IDisposable
    {
        private readonly EngineTextureArchive _archive;
        public InstallClassicResourceAssets(string install) => _archive = EngineTextureArchive.OpenInstall(install);
        public RgbaImage Image(string name) => _archive.Decode(name);
        public void Dispose() => _archive.Dispose();
    }

    /// <summary>Original resource artwork and numeric sprites in retained UI Toolkit elements.</summary>
    public sealed class ClassicResourceHud : IDisposable
    {
        private readonly GameObject _owner;
        private readonly PanelSettings _settings;
        private readonly VisualElement _root;
        private readonly Dictionary<string, Texture2D> _textures = new Dictionary<string, Texture2D>();
        private readonly IClassicResourceAssets _assets;
        private readonly Image[] _backgrounds = new Image[4];
        private readonly Image _heart, _heartFill, _payday;
        private readonly Texture2D _heartFillTexture;
        private readonly Color32[] _heartOriginal, _heartPixels;
        private HudGaugeFraction _health;
        private bool _healthDrawn;
        private readonly NumericRow _gold, _mana, _income, _upkeep;
        private bool _visible = true, _allowInput = true;
        private Vector2Int _viewport;
        private int Width => _viewport.x > 0 ? _viewport.x : Screen.width;
        private int Height => _viewport.y > 0 ? _viewport.y : Screen.height;
        public PanelSettings Panel => _settings;
        public float Bottom => 68 * Height / 768f;
        public Rect Bounds => new Rect(0, 0, Mathf.Min(Width, V(1020)), Bottom);

        public ClassicResourceHud(Transform parent, IClassicResourceAssets assets)
        {
            _assets = assets ?? throw new ArgumentNullException(nameof(assets));
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("DK2Hud");
            _settings.scaleMode = PanelScaleMode.ConstantPixelSize; _settings.scale = 1;
            _settings.sortingOrder = 11;
            _owner = new GameObject("Keeper resources HUD");
            if (parent != null) _owner.transform.SetParent(parent, false);
            var document = _owner.AddComponent<UIDocument>(); document.panelSettings = _settings;
            _root = document.rootVisualElement; _root.pickingMode = PickingMode.Ignore;
            _root.style.width = Length.Percent(100); _root.style.height = Length.Percent(100);
            for (int i = 0; i < _backgrounds.Length; i++)
                _backgrounds[i] = Picture(Texture("GUI\\Windows\\Mana-Bg-0" + (i % 2)));
            _heart = Picture(Texture("GUI\\Windows\\heart-bg"));
            Texture2D fill = Texture("GUI\\Windows\\heart-hp");
            _heartOriginal = fill.GetPixels32(); _heartPixels = new Color32[_heartOriginal.Length];
            _heartFillTexture = new Texture2D(fill.width, fill.height, TextureFormat.RGBA32, false, true)
                { name = "Heart health gauge", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            _heartFill = Picture(_heartFillTexture);
            _payday = Picture(Texture("GUI\\Windows\\payday-BG"));
            _gold = new NumericRow(this); _mana = new NumericRow(this);
            _income = new NumericRow(this); _upkeep = new NumericRow(this);
        }

        public void SetVisibility(HudVisibility visibility)
        {
            _visible = visibility.ShowNormalHud; _allowInput = visibility.AllowOrdinaryInput;
            _root.style.display = _visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public bool ContainsPointer(Vector3 screenPosition) => _visible && _allowInput &&
            Bounds.Contains(new Vector2(screenPosition.x, Height - screenPosition.y));

        public void Draw(KeeperResourceSnapshot snapshot) => Draw(snapshot, default);

        public void Draw(KeeperResourceSnapshot snapshot, Vector2Int viewport)
        {
            _viewport = viewport;
            if (!_visible) return;
            int dx = V(12), y = V(12), height = V(124) - y;
            int manaWidth = V(224), goldLeft = V(516), goldWidth = V(740) - goldLeft;
            Place(_backgrounds[0], 4 * dx, y, manaWidth, height);
            Place(_backgrounds[1], 4 * dx + manaWidth, y, manaWidth, height);
            Place(_backgrounds[2], goldLeft + 4 * dx, y, goldWidth, height);
            Place(_backgrounds[3], goldLeft + 4 * dx + goldWidth, y, goldWidth, height);
            Place(_heart, 0, 3, V(160), V(160));
            Place(_heartFill, 0, 3, V(160), V(160));
            if (!_healthDrawn || _health != snapshot.HeartHealth) UpdateHeart(snapshot.HeartHealth);
            Place(_payday, goldLeft, y, V(160), V(160));
            string family = Width >= 640 ? "GUI\\Icons\\i-" : "GUI\\Icons\\lo-";
            _mana.Draw(snapshot.Mana.Value, family, V(172), V(32), V(48), V(40), 7,
                new Color32(0, 252, 156, 255));
            _gold.Draw(snapshot.Gold.Value, family, V(680), V(40), V(48), V(40), 7,
                new Color32(238, 182, 0, 255));
            bool rates = Width > 600;
            _income.SetVisible(rates); _upkeep.SetVisible(rates);
            if (rates)
            {
                _income.Draw(snapshot.Income.Value, "GUI\\Icons\\Mana\\s-", V(184), V(88), V(64), V(16), 0,
                    new Color32(50, 255, 50, 255), "in", "+");
                _upkeep.Draw(snapshot.Upkeep.Value, "GUI\\Icons\\Mana\\s-", V(360), V(88), V(64), V(16), 0,
                    new Color32(255, 50, 50, 255), "out", "-");
            }
        }

        private int V(int coordinate) => Mathf.FloorToInt(coordinate * Height / 1920f);
        private void UpdateHeart(HudGaugeFraction health)
        {
            _healthDrawn = true; _health = health;
            int width = _heartFillTexture.width, height = _heartFillTexture.height;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                var point = new System.Numerics.Vector2(x + .5f - width / 2f, height / 2f - y - .5f);
                Color32 source = _heartOriginal[index];
                _heartPixels[index] = health.Contains(point) ? source : new Color32(0, 0, 0, source.a);
            }
            _heartFillTexture.SetPixels32(_heartPixels); _heartFillTexture.Apply();
        }
        private Texture2D Texture(string name)
        {
            if (!_textures.TryGetValue(name, out Texture2D texture))
            {
                texture = MinimapFrameVisual.Decode(_assets.Image(name));
                _textures.Add(name, texture);
            }
            return texture;
        }
        private Image Picture(Texture2D texture)
        {
            var image = new Image { image = texture, pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.StretchToFill };
            image.style.position = Position.Absolute; _root.Add(image); return image;
        }
        private static void Place(VisualElement element, float x, float y, float width, float height)
        { element.style.left = x; element.style.top = y; element.style.width = width; element.style.height = height; }

        private sealed class NumericRow
        {
            private readonly ClassicResourceHud _hud;
            private readonly Image[] _glyphs = new Image[13];
            private int _value;
            private string _digits, _family;
            public NumericRow(ClassicResourceHud hud)
            { _hud = hud; for (int i = 0; i < _glyphs.Length; i++) _glyphs[i] = hud.Picture(null); }
            public void SetVisible(bool visible)
            { foreach (Image glyph in _glyphs) glyph.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None; }
            public void Draw(int value, string family, float x, float y, float size, float advance, int slots,
                Color color, string icon = null, string sign = null)
            {
                bool changed = _digits == null || value != _value || family != _family;
                if (changed)
                { _value = value; _family = family; _digits = value.ToString(CultureInfo.InvariantCulture); }
                int prefix = icon == null ? 0 : 2;
                x += Mathf.Max(0, slots - _digits.Length) * advance;
                for (int i = 0; i < _glyphs.Length; i++)
                {
                    Image glyph = _glyphs[i];
                    bool used = i < prefix + _digits.Length;
                    glyph.style.display = used ? DisplayStyle.Flex : DisplayStyle.None;
                    if (!used) continue;
                    if (changed)
                    {
                        string suffix = i == 0 && prefix != 0 ? icon : i == 1 && prefix != 0 ? sign : _digits[i - prefix].ToString();
                        glyph.image = _hud.Texture(family + suffix);
                    }
                    glyph.tintColor = color;
                    Place(glyph, x + i * advance, y, size, size);
                }
            }
        }

        public void Dispose()
        {
            _root.RemoveFromHierarchy();
            UnityEngine.Object.Destroy(_owner); UnityEngine.Object.Destroy(_settings);
            UnityEngine.Object.Destroy(_heartFillTexture);
            foreach (Texture2D texture in _textures.Values) UnityEngine.Object.Destroy(texture);
            if (_assets is IDisposable disposable) disposable.Dispose();
        }
    }
}
