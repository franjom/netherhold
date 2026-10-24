// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using DK2.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace DK2.Unity
{
    /// <summary>The tool icon, the No Go icon over it and the price beside the hand, or in Hand mode the
    /// held-item icons with I-Dismiss or No Go over them (0x0040CAE0, 0x0040CFE0,
    /// docs/research/placement-cursor.md section 3).</summary>
    public sealed class ClassicPlacementOverlay : IDisposable
    {
        private const int MaxDigits = 7;
        private readonly IClassicResourceAssets _assets;
        private readonly Dictionary<string, Texture2D> _textures = new Dictionary<string, Texture2D>();
        private readonly GameObject _owner;
        private readonly PanelSettings _settings;
        private readonly Image _icon, _noGo, _more, _dispose;
        private readonly Image[] _digits = new Image[MaxDigits];
        // The price the digit images show; a count of 0 means none set yet.
        private long _priceMagnitude;
        private string _priceFamily;
        private int _priceDigitCount;
        private readonly Image[] _held = new Image[HeldItemStack.MaxIcons];

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
            // Children draw in the order added, as DK2 does: held icon 0, I-Dismiss inside its draw
            // (0x0040D58B..0x0040D5AF), held icons 1..7, the overflow icon (0x0040D1F1..0x0040D27C),
            // then No Go over everything (0x0040CF1E).
            _icon = Picture(root);
            _held[0] = Picture(root); _dispose = Picture(root);
            for (int i = 1; i < _held.Length; i++) _held[i] = Picture(root);
            _more = Picture(root); _noGo = Picture(root);
            _noGo.image = Texture("GUI\\Cursors\\I-No_Go");
            for (int i = 0; i < MaxDigits; i++) _digits[i] = Picture(root);
        }

        /// <summary>A keeper spell's GUI icon resource by spell id: the record's first art resource,
        /// which 0x0040D310 installs from the runtime spell (KeeperSpells file +0x20, the bottom
        /// panel's icon). Null or an unknown id draws no spell icon.</summary>
        public Func<byte, string> SpellIconName { get; set; }

        /// <summary>A creature type's held-item icon: Creatures.kwd Icon1Resource (file +0xC44), the
        /// compact definition's art +0x510 that 0x0040D450 installs. Null or empty draws none.</summary>
        public Func<byte, string> CreatureIconName { get; set; }

        /// <summary>An object type's held-item icon: Objects.kwd GuiIconResource (file +0x74), the
        /// compact definition's first art that 0x0040D450 installs. Null or empty draws none.</summary>
        public Func<byte, string> ObjectIconName { get; set; }

        /// <param name="pointer">The pointer in screen pixels from the top left.</param>
        /// <param name="overHud">DK2 moves the icon left by 40 * W / 640 over the GUI.</param>
        /// <param name="held">The hand's icons, read when <paramref name="feedback"/> shows
        /// <see cref="PlacementIcon.Held"/>.</param>
        public void Draw(in PlacementFeedback feedback, Vector2 pointer, bool overHud, HeldItemStack held = null)
        {
            float width = Screen.width, height = Screen.height;
            float x = pointer.x + 82 * width / 640 - (overHud ? 40 * width / 640 : 0);
            float y = pointer.y + 10 * height / 480;
            // The draw passes 0x20 x 0x20 and the +30/+10 price offset in pixels whose screen scale
            // is unread; both are taken at the owner's 1200-line photos (2026-10-23), HYPOTHESIS.
            float scale = height / 1200f, size = 32 * scale;
            if (feedback.Icon == PlacementIcon.Held && held != null && held.Icons.Count > 0)
            {
                _icon.style.display = DisplayStyle.None;
                HideDigits(0);
                DrawHeld(held, feedback.Dispose, x, y, size, scale);
                // 0x0040CFE0 leaves the No Go anchor at icon 0 moved by (-2, +2); bit 0x04 hides it.
                _noGo.style.display = feedback.NoGo && !feedback.Dispose ? DisplayStyle.Flex : DisplayStyle.None;
                Place(_noGo, x - 2 * scale, y + 2 * scale, size, size);
                return;
            }
            HideHeld(0);
            string iconName = IconName(feedback);
            bool shown = iconName != null;
            _icon.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
            _noGo.style.display = shown && feedback.NoGo ? DisplayStyle.Flex : DisplayStyle.None;
            if (!shown) { HideDigits(0); return; }
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
            int count = PriceDigits(Math.Abs((long)feedback.Price), family);
            Color32 colour = feedback.Price > 0 ? new Color32(128, 255, 255, 255)
                : feedback.Affordable ? new Color32(255, 128, 128, 255) : new Color32(255, 0, 0, 255);
            float glyph = 48 * height / 1920f, advance = 40 * height / 1920f;
            for (int i = 0; i < count; i++)
            {
                Image digit = _digits[i];
                digit.tintColor = colour;
                digit.style.display = DisplayStyle.Flex;
                Place(digit, x + 30 * scale + i * advance, y + 10 * scale, glyph, glyph);
            }
            HideDigits(count);
        }

        /// <summary>Sets the digit images to the price's last <see cref="MaxDigits"/> decimal digits, as
        /// the formatted number's tail was; returns how many. The images keep their textures until the
        /// price or the family changes, so a steady price builds no string (as ClassicResourceHud's rows).</summary>
        private int PriceDigits(long magnitude, string family)
        {
            if (_priceDigitCount > 0 && magnitude == _priceMagnitude && family == _priceFamily)
                return _priceDigitCount;
            int count = 0;
            for (long rest = magnitude; count == 0 || rest > 0; rest /= 10) count++;
            count = Math.Min(count, MaxDigits);
            long value = magnitude;
            for (int i = count - 1; i >= 0; i--, value /= 10)
                _digits[i].image = Texture(family + (char)('0' + (int)(value % 10)));
            _priceMagnitude = magnitude; _priceFamily = family; _priceDigitCount = count;
            return count;
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

        /// <summary>0x0040CFE0: icon i at the anchor plus HeldItemStack.Offset(i) (unscaled pixels, the
        /// 0x006CE910 table), each 0x20 x 0x20; the overflow icon past eight; and 0x0040D450's I-Dismiss
        /// over icon 0, 2 pixels left, when it is a creature and bit 0x04 is set.</summary>
        private void DrawHeld(HeldItemStack held, bool dispose, float x, float y, float size, float scale)
        {
            int count = held.Icons.Count;
            for (int i = 0; i < count; i++)
            {
                HeldIcon icon = held.Icons[i];
                string name = icon.Kind == HeldIconKind.Creature ? CreatureIconName?.Invoke(icon.TypeId) : ObjectIconName?.Invoke(icon.TypeId);
                Image image = _held[i];
                if (string.IsNullOrEmpty(name)) { image.style.display = DisplayStyle.None; continue; }
                (int ox, int oy) = HeldItemStack.Offset(i);
                image.image = Texture(name);
                image.style.display = DisplayStyle.Flex;
                Place(image, x + ox * scale, y + oy * scale, size, size);
            }
            HideHeld(count);
            if (held.More)
            {
                (int ox, int oy) = HeldItemStack.Offset(HeldItemStack.MaxIcons);
                _more.image = Texture(HeldItemStack.MoreIconName);
                _more.style.display = DisplayStyle.Flex;
                Place(_more, x + ox * scale, y + oy * scale, size, size);
            }
            else _more.style.display = DisplayStyle.None;
            bool bin = dispose && held.Icons[0].Kind == HeldIconKind.Creature;
            _dispose.style.display = bin ? DisplayStyle.Flex : DisplayStyle.None;
            if (!bin) return;
            _dispose.image = Texture(HeldItemStack.DisposeIconName);
            Place(_dispose, x - 2 * scale, y, size, size);
        }

        private void HideHeld(int from)
        {
            for (int i = from; i < _held.Length; i++) _held[i].style.display = DisplayStyle.None;
            if (from == 0) { _more.style.display = DisplayStyle.None; _dispose.style.display = DisplayStyle.None; }
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
