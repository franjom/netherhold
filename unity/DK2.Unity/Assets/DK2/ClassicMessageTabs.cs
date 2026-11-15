// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using DK2.Presentation;
using DK2.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace DK2.Unity
{
    /// <summary>Unity glue for <see cref="MessageTabsHud"/> (ADR-0805): one UI Toolkit panel holding a
    /// <see cref="HudCanvas"/>, pointer events forwarded in virtual units, and the left button polled for the
    /// message panel's scroll bar. The tabs' DK2 behaviour is the model's.</summary>
    public sealed class ClassicMessageTabs : IDisposable
    {
        private readonly MessageTabsHud _hud;
        private readonly GameObject _owner;
        private readonly PanelSettings _settings;
        private readonly VisualElement _root;
        private readonly HudCanvas _canvas;
        private readonly HudFrame _frame = new HudFrame();
        private bool _optionsOpen;
        public int UiScale { get; set; } = 100;
        private HudScaleLayout ScaleLayout => ClassicHudScaling.Layout(_optionsOpen ? 100 : UiScale);
        private HudScreen Screen => new HudScreen(Mathf.RoundToInt(ScaleLayout.LogicalWidth), UnityEngine.Screen.height);

        /// <param name="resolve">A message string reference to its text (Speech.str, level table, Text.str).</param>
        /// <param name="briefing">A line of the level's briefing table, or null.</param>
        /// <param name="locate">A message's goto location in tiles, or null to hide the button.</param>
        /// <param name="goTo">Moves the view to a location in tiles.</param>
        public ClassicMessageTabs(Transform parent, IClassicBottomPanelAssets assets, IClassicOptionsAssets textAssets,
            MessageTabsView view, Func<int, string> resolve, Func<int, string> briefing,
            Func<PlayerMessage, (float X, float Y)?> locate = null, Action<float, float> goTo = null,
            Func<byte, string> playerName = null)
        {
            if (assets == null) throw new ArgumentNullException(nameof(assets));
            if (textAssets == null) throw new ArgumentNullException(nameof(textAssets));
            _hud = new MessageTabsHud(assets, view ?? throw new ArgumentNullException(nameof(view)), textAssets.Font,
                resolve, briefing, locate, goTo, playerName);
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("DK2Hud");
            _settings.scaleMode = PanelScaleMode.ConstantPixelSize; _settings.scale = 1;
            _settings.sortingOrder = 14;
            _owner = new GameObject("Message tabs");
            if (parent != null) _owner.transform.SetParent(parent, false);
            var document = _owner.AddComponent<UIDocument>(); document.panelSettings = _settings;
            _root = document.rootVisualElement; _root.pickingMode = PickingMode.Ignore;
            _root.style.width = Length.Percent(100); _root.style.height = Length.Percent(100);
            _canvas = new HudCanvas(assets.Image, _hud.Hits);
            _root.Add(_canvas);
            foreach (HudImage image in MessageTabsHud.Images()) _canvas.Preload(image);
            _canvas.RegisterCallback<PointerDownEvent>(evt =>
            {
                Vector2 at = _canvas.ToVirtual(evt.localPosition);
                if (_hud.PointerDown(at.x, at.y, evt.button)) evt.StopPropagation();
            });
            _canvas.RegisterCallback<PointerMoveEvent>(evt => Move(evt.localPosition));
            _canvas.RegisterCallback<PointerEnterEvent>(evt => Move(evt.localPosition));
            _canvas.RegisterCallback<PointerLeaveEvent>(_ => _hud.PointerLeave());
        }

        /// <summary>The hovered tab's message text, for the status line (0x0042D820 case 5); the
        /// Review Objective button's own texts 2163/2164.</summary>
        public HoverTextSnapshot HoverText => _hud.HoverText;

        public bool ContainsPointer(Vector3 screen)
        {
            // Panel coordinates to the canvas's own: the root is offset when docked to the bottom.
            Vector2 local = _canvas.WorldToLocal(ClassicHudScaling.PanelPointer(_root, screen, UnityEngine.Screen.height));
            Vector2 at = _canvas.ToVirtual(local);
            return _hud.ContainsPointer(at.x, at.y);
        }

        /// <param name="indicators">0x00427BA0's indicators this frame; any shown lifts menu40 by 128.</param>
        public void Draw(HudVisibility visibility, bool collapsed, in HudIndicatorSnapshot indicators,
            bool optionsOpen = false, bool optionsObjective = false)
        {
            _optionsOpen = optionsOpen;
            HudScaleLayout layout = ScaleLayout;
            ClassicHudScaling.Apply(_settings, _root, layout, true);
            _root.style.display = visibility.ShowNormalHud ? DisplayStyle.Flex : DisplayStyle.None;
            HudScreen screen = Screen;
            Vector2 pointer = _canvas.WorldToLocal(ClassicHudScaling.PanelPointer(_root, Input.mousePosition, screen.Height)) / screen.Scale;
            _frame.Clear();
            _hud.Build(screen, UnityEngine.Screen.width, visibility, collapsed, indicators,
                (pointer.x, pointer.y, Input.GetMouseButton(0)), _frame, optionsOpen, optionsObjective);
            _canvas.Present(_frame, screen.Scale, layout.Factor);
            _hud.Repick();
        }

        private void Move(Vector3 local)
        {
            Vector2 at = _canvas.ToVirtual(local);
            _hud.PointerMove(at.x, at.y);
        }

        public void Dispose()
        {
            _canvas.Dispose();
            UnityEngine.Object.Destroy(_settings); UnityEngine.Object.Destroy(_owner);
        }
    }
}
