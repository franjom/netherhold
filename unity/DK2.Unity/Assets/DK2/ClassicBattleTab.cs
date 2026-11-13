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
    /// <summary>Menu 43, the Battle tab right of the message tabs: the crossed blades, the Call to
    /// Arms cancel and the alarm (docs/research/battle-tab.md, <see cref="BattleTabView"/>). Clicks
    /// are raised as <see cref="Clicked"/>; the blades take only the right click, the cancel both,
    /// the alarm only the right. Not built: the blades' sound 0x30F (Q775). The fly-in is
    /// DungeonRunner.DrawBattleFlyIn (ADR-0774).</summary>
    public sealed class ClassicBattleTab : IDisposable
    {
        private static readonly BattleTabButton[] Buttons = { BattleTabButton.Battle, BattleTabButton.CallToArms, BattleTabButton.Alarm };

        private readonly IClassicBottomPanelAssets _assets;
        private readonly GameObject _owner;
        private readonly PanelSettings _settings;
        private readonly VisualElement _root;
        private readonly Image[] _images = new Image[Buttons.Length];
        private readonly Dictionary<string, Texture2D> _textures = new Dictionary<string, Texture2D>();
        private BattleTabButton _hovered;
        private bool _visible = true, _allowInput = true;
        private HoverTextSnapshot _battleHover;

        /// <summary>A button and the mouse button (0 left, 1 right) that pressed it.</summary>
        public event Action<BattleTabButton, int> Clicked;

        public int UiScale { get; set; } = 100;
        private int Height => Screen.height;
        private float V(int value) => value * Height / 1920f;

        public ClassicBattleTab(Transform parent, IClassicBottomPanelAssets assets)
        {
            _assets = assets ?? throw new ArgumentNullException(nameof(assets));
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("DK2Hud");
            _settings.scaleMode = PanelScaleMode.ConstantPixelSize; _settings.scale = 1;
            _settings.sortingOrder = 14;
            _owner = new GameObject("Battle tab");
            if (parent != null) _owner.transform.SetParent(parent, false);
            var document = _owner.AddComponent<UIDocument>(); document.panelSettings = _settings;
            _root = document.rootVisualElement; _root.pickingMode = PickingMode.Ignore;
            _root.style.width = Length.Percent(100); _root.style.height = Length.Percent(100);
            for (int i = 0; i < Buttons.Length; i++)
            {
                BattleTabButton button = Buttons[i];
                var image = new Image { pickingMode = PickingMode.Position, scaleMode = ScaleMode.StretchToFill };
                image.style.position = Position.Absolute; image.style.display = DisplayStyle.None;
                image.RegisterCallback<PointerEnterEvent>(_ => _hovered = button);
                image.RegisterCallback<PointerLeaveEvent>(_ => { if (_hovered == button) _hovered = BattleTabButton.None; });
                image.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (!_allowInput) return;
                    evt.StopPropagation();
                    if (evt.button == 0 || evt.button == 1) Clicked?.Invoke(button, evt.button);
                });
                _root.Add(image); _images[i] = image;
            }
        }

        /// <summary>The blades' texts: Text.str 2165 "Zoom to Battle" as the name, 2166 "Battle Tab:
        /// right-click to zoom, click again to cycle." as the status line (seen in DK2, 2026-11-13,
        /// battle-tab.md section 8). The cancel and the alarm have no text.</summary>
        public HoverTextSnapshot HoverText
        {
            get
            {
                if (_hovered != BattleTabButton.Battle) return HoverTextSnapshot.Empty;
                if (_battleHover.TooltipName == null)
                    _battleHover = new HoverTextSnapshot(_assets.Text(BattleTabView.BattleStatusText), _assets.Text(BattleTabView.BattleText), default);
                return _battleHover;
            }
        }

        /// <summary>The blades' centre in screen pixels from the top left (<c>0x00426D80</c>), placed
        /// or not: the fly-in's target, read once when it starts.</summary>
        public Vector2 BattleCentre(bool collapsed)
        {
            float factor = ClassicHudScaling.Layout(UiScale).Factor, height = Height;
            int half = BattleTabView.ButtonSize / 2;
            int top = BattleTabView.MenuY + (collapsed ? ClassicCamera.CollapsedPanelTop - ClassicCamera.ExpandedPanelTop : 0);
            // The panel is bottom-docked: its root sits height / factor - height down, then scales.
            return new Vector2(V(BattleTabView.MenuX + BattleTabView.LocalX(BattleTabButton.Battle) + half) * factor,
                height - height * factor + V(top + half) * factor);
        }

        public bool ContainsPointer(Vector3 screen)
        {
            if (!_visible || !_allowInput) return false;
            Vector2 point = ClassicHudScaling.PanelPointer(_root, screen, Height);
            foreach (Image image in _images)
                if (image.style.display == DisplayStyle.Flex && image.worldBound.Contains(point)) return true;
            return false;
        }

        /// <param name="milliseconds">The real clock, which the frames step on whatever the game speed.</param>
        public void Draw(HudVisibility visibility, bool collapsed, BattleTabSnapshot shown, long milliseconds)
        {
            ClassicHudScaling.Apply(_settings, _root, ClassicHudScaling.Layout(UiScale), true);
            _visible = visibility.ShowNormalHud; _allowInput = visibility.AllowOrdinaryInput;
            _root.style.display = _visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!_visible) { _hovered = BattleTabButton.None; return; }
            int top = BattleTabView.MenuY + (collapsed ? ClassicCamera.CollapsedPanelTop - ClassicCamera.ExpandedPanelTop : 0);
            int phase = BattleTabView.Phase(milliseconds);
            for (int i = 0; i < Buttons.Length; i++)
            {
                BattleTabButton button = Buttons[i];
                Image image = _images[i];
                if (!shown.Shows(button))
                {
                    image.style.display = DisplayStyle.None;
                    if (_hovered == button) _hovered = BattleTabButton.None;
                    continue;
                }
                image.style.display = DisplayStyle.Flex;
                image.image = Texture(BattleTabView.Art(button, phase));
                image.style.left = V(BattleTabView.MenuX + BattleTabView.LocalX(button)); image.style.top = V(top);
                image.style.width = V(BattleTabView.ButtonSize); image.style.height = V(BattleTabView.ButtonSize);
            }
        }

        private Texture2D Texture(string name)
        {
            if (_textures.TryGetValue(name, out Texture2D texture)) return texture;
            texture = MinimapFrameVisual.Decode(_assets.Image(name)); _textures.Add(name, texture); return texture;
        }

        public void Dispose()
        {
            foreach (Texture2D texture in _textures.Values) UnityEngine.Object.Destroy(texture);
            UnityEngine.Object.Destroy(_settings); UnityEngine.Object.Destroy(_owner);
        }
    }
}
