// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using DK2.Art.Textures;
using DK2.Data.Save;
using DK2.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace DK2.Unity
{
    /// <summary>The named-save browser's Unity side: <see cref="SaveLoadHud"/> (ADR-0805) drawn by a
    /// <see cref="HudCanvas"/> that takes the pointer only on its pressable lines.</summary>
    public sealed class ClassicSaveLoadPanel : IDisposable
    {
        private static readonly RgbaImage Blank = new RgbaImage(1, 1, new byte[4]);
        private readonly SaveLoadHud _hud;
        private readonly GameObject _owner;
        private readonly PanelSettings _settings;
        private readonly VisualElement _root;
        private readonly HudCanvas _canvas;
        private readonly HudFrame _frame = new HudFrame();
        public event Action Submit { add => _hud.Submit += value; remove => _hud.Submit -= value; }
        /// <summary>A press on the Back or Continue line, with the options menu's action it applies.</summary>
        public event Action<ClassicHudAction> HudAction { add => _hud.HudAction += value; remove => _hud.HudAction -= value; }

        public ClassicSaveLoadPanel(Transform parent, IClassicOptionsAssets assets)
        {
            if (assets == null) throw new ArgumentNullException(nameof(assets));
            _hud = new SaveLoadHud(assets.Text, assets.Font);
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("DK2Hud");
            _settings.scaleMode = PanelScaleMode.ConstantPixelSize; _settings.scale = 1;
            _settings.sortingOrder = 16;
            _owner = new GameObject("Saved games"); _owner.transform.SetParent(parent, false);
            var document = _owner.AddComponent<UIDocument>(); document.panelSettings = _settings;
            _root = document.rootVisualElement; _root.pickingMode = PickingMode.Ignore;
            _root.style.width = Length.Percent(100); _root.style.height = Length.Percent(100);
            _root.style.display = DisplayStyle.None;
            _canvas = new HudCanvas(_ => Blank, _hud.Hits);
            _root.Add(_canvas);
            _canvas.RegisterCallback<PointerDownEvent>(evt =>
            {
                Vector2 at = _canvas.ToVirtual(evt.localPosition);
                if (_hud.PointerDown(at.x, at.y, evt.button)) evt.StopPropagation();
            });
        }

        /// <summary>Shows or hides the browser, and feeds it this frame's typing and wheel.</summary>
        public void Show(SaveSlotsView view, bool visible, bool save, bool frontEnd = false)
        {
            _hud.Show(view, visible, save, frontEnd);
            _root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!_hud.Visible) return;
            _hud.Type(Input.inputString);
            _hud.Wheel((int)Input.mouseScrollDelta.y);
            var screen = new HudScreen(Screen.width, Screen.height);
            _frame.Clear();
            _hud.Build(screen, _frame);
            _canvas.Present(_frame, screen.Scale, 1);
        }

        public void Error(string message) => _hud.Error(message);

        public void Dispose()
        {
            _canvas.Dispose();
            UnityEngine.Object.Destroy(_owner); UnityEngine.Object.Destroy(_settings);
        }
    }
}
