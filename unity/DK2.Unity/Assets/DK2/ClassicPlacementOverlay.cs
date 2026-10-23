// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.Globalization;
using DK2.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace DK2.Unity
{
    /// <summary>The tool icon, the No Go icon over it and the price beside the hand
    /// (0x0040CAE0, docs/research/placement-cursor.md section 3).</summary>
    public sealed class ClassicPlacementOverlay : IDisposable
    {
        private const int MaxDigits = 7;
        private readonly IClassicResourceAssets _assets;
        private readonly Dictionary<string, Texture2D> _textures = new Dictionary<string, Texture2D>();
        private readonly GameObject _owner;
        private readonly PanelSettings _settings;
        private readonly Image _icon, _noGo;
        private readonly Image[] _digits = new Image[MaxDigits];

        public ClassicPlacementOverlay(Transform parent, IClassicResourceAssets assets)
        {
            _assets = assets ?? throw new ArgumentNullException(nameof(assets));
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("DK2Hud");
            _settings.scaleMode = PanelScaleMode.ConstantPixelSize; _settings.scale = 1;
            _settings.sortingOrder = 999;
            _owner = new GameObject("Placement cursor");
            if (parent != null) _owner.transform.SetParent(parent, false);
            var document = _owner.AddComponent<UIDocument>(); document.panelSettings = _settings;
            VisualElement root = document.rootVisualElement; root.pickingMode = PickingMode.Ignore;
            _icon = Picture(root); _noGo = Picture(root);
            _noGo.image = Texture("GUI\\Cursors\\I-No_Go");
            for (int i = 0; i < MaxDigits; i++) _digits[i] = Picture(root);
        }

        /// <summary>A keeper spell's GUI icon resource by spell id: the record's first art resource,
        /// which 0x0040D310 installs from the runtime spell (KeeperSpells file +0x20, the bottom
        /// panel's icon). Null or an unknown id draws no spell icon.</summary>
        public Func<byte, string> SpellIconName { get; set; }

        /// <param name="pointer">The pointer in screen pixels from the top left.</param>
        /// <param name="overHud">DK2 moves the icon left by 40 * W / 640 over the GUI.</param>
        public void Draw(in PlacementFeedback feedback, Vector2 pointer, bool overHud)
        {
            string iconName = IconName(feedback);
            bool shown = iconName != null;
            _icon.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
            _noGo.style.display = shown && feedback.NoGo ? DisplayStyle.Flex : DisplayStyle.None;
            if (!shown) { HideDigits(0); return; }
            float width = Screen.width, height = Screen.height;
            float x = pointer.x + 82 * width / 640 - (overHud ? 40 * width / 640 : 0);
            float y = pointer.y + 10 * height / 480;
            // The draw passes 0x20 x 0x20 and the +30/+10 price offset in pixels whose screen scale
            // is unread; both are taken at the owner's 1200-line photos (2026-10-23), HYPOTHESIS.
            float scale = height / 1200f, size = 32 * scale;
            _icon.image = Texture(iconName);
            Place(_icon, x, y, size, size);
            // 0x0040D310 draws the spell's art at the same anchor and size, then moves the No Go
            // anchor by (-2, +2) unscaled pixels; it draws no price.
            float shift = feedback.Icon == PlacementIcon.Spell ? 2 * scale : 0;
            Place(_noGo, x - shift, y + shift, size, size);
            if (feedback.Price == 0 || feedback.Icon == PlacementIcon.Spell) { HideDigits(0); return; }
            // 0x00415650 mode 0 (48 square, 40 advance, i- digits from 640 wide), on screen as our gold
            // counter scales it (H/1920): fitted, not read.
            string family = width >= 640 ? "GUI\\Icons\\i-" : "GUI\\Icons\\lo-";
            string text = Math.Abs(feedback.Price).ToString(CultureInfo.InvariantCulture);
            if (text.Length > MaxDigits) text = text.Substring(text.Length - MaxDigits);
            Color32 colour = feedback.Price > 0 ? new Color32(128, 255, 255, 255)
                : feedback.Affordable ? new Color32(255, 128, 128, 255) : new Color32(255, 0, 0, 255);
            float glyph = 48 * height / 1920f, advance = 40 * height / 1920f;
            for (int i = 0; i < text.Length; i++)
            {
                Image digit = _digits[i];
                digit.image = Texture(family + text[i]);
                digit.tintColor = colour;
                digit.style.display = DisplayStyle.Flex;
                Place(digit, x + 30 * scale + i * advance, y + 10 * scale, glyph, glyph);
            }
            HideDigits(text.Length);
        }

        private string IconName(in PlacementFeedback feedback)
        {
            switch (feedback.Icon)
            {
                case PlacementIcon.PlaceRoom: return "GUI\\Cursors\\I-Place_Room";
                case PlacementIcon.Sell: return "GUI\\Cursors\\I-Sell";
                case PlacementIcon.PlaceDoor: return "GUI\\Cursors\\I-Place_Door";
                case PlacementIcon.PlaceTrap: return "GUI\\Cursors\\I-Place_Trap";
                case PlacementIcon.Query: return "GUI\\Cursors\\I-Query";
                case PlacementIcon.Spell:
                    string name = SpellIconName?.Invoke(feedback.SpellId);
                    return string.IsNullOrEmpty(name) ? null : name;
                default: return null;
            }
        }

        private void HideDigits(int from)
        {
            for (int i = from; i < MaxDigits; i++) _digits[i].style.display = DisplayStyle.None;
        }

        private static Image Picture(VisualElement root)
        {
            var image = new Image { pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.StretchToFill };
            image.style.position = Position.Absolute; image.style.display = DisplayStyle.None;
            root.Add(image); return image;
        }

        private static void Place(VisualElement element, float x, float y, float width, float height)
        { element.style.left = x; element.style.top = y; element.style.width = width; element.style.height = height; }

        private Texture2D Texture(string name)
        {
            if (!_textures.TryGetValue(name, out Texture2D texture))
            {
                texture = MinimapFrameVisual.Decode(_assets.Image(name));
                _textures.Add(name, texture);
            }
            return texture;
        }

        public void Dispose()
        {
            foreach (Texture2D texture in _textures.Values) UnityEngine.Object.Destroy(texture);
            UnityEngine.Object.Destroy(_owner); UnityEngine.Object.Destroy(_settings);
        }
    }
}
