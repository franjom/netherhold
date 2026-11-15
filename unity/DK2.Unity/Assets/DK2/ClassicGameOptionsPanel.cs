// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using DK2.Data.Settings;
using DK2.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace DK2.Unity
{
    /// <summary>The in-game settings pages' Unity side: <see cref="GameOptionsHud"/> (ADR-0805) drawn by a
    /// <see cref="HudCanvas"/> over the paused level. It forwards the pointer and the captured key.</summary>
    public sealed class ClassicGameOptionsPanel : IDisposable
    {
        private readonly GameOptionsHud _hud;
        private readonly GameObject _owner;
        private readonly VisualElement _root;
        private readonly HudCanvas _canvas;
        private readonly HudFrame _frame = new HudFrame();
        public InGameOptionsController Model => _hud.Model;
        public PanelSettings Panel { get; }
        private int Width => Panel.targetTexture != null ? Panel.targetTexture.width : Screen.width;
        private int Height => Panel.targetTexture != null ? Panel.targetTexture.height : Screen.height;
        public ClassicGameOptionsPanel(Transform parent, InstallHudSources sources, InGameOptionsController model)
        {
            if (sources == null) throw new ArgumentNullException(nameof(sources));
            var assets = new InstallClassicOptionsAssets(sources);
            _hud = new GameOptionsHud(model, assets.Text, assets.Font, ClassicKeyNames.Name);
            Panel = ScriptableObject.CreateInstance<PanelSettings>();
            Panel.themeStyleSheet = Resources.Load<ThemeStyleSheet>("DK2Hud");
            Panel.scaleMode = PanelScaleMode.ConstantPixelSize; Panel.scale = 1; Panel.sortingOrder = 15;
            _owner = new GameObject("Dungeon game options"); _owner.transform.SetParent(parent, false);
            var doc = _owner.AddComponent<UIDocument>(); doc.panelSettings = Panel; _root = doc.rootVisualElement;
            _root.pickingMode = PickingMode.Ignore; _root.style.width = Length.Percent(100); _root.style.height = Length.Percent(100);
            _root.focusable = true;
            _root.RegisterCallback<KeyDownEvent>(e =>
            {
                if (!_hud.Visible || Model.Settings.CapturingAction == null) return;
                KeyModifiers modifiers = (e.shiftKey ? KeyModifiers.Shift : 0) | (e.ctrlKey ? KeyModifiers.Control : 0) | (e.altKey ? KeyModifiers.Alt : 0);
                Model.Settings.CaptureKey(ClassicKeyBindings.ScanCode(e.keyCode), modifiers); e.StopPropagation();
            });
            _hud.KeyCaptureStarted += () => _root.Focus();
            _canvas = new HudCanvas(sources.Texture, _hud.Hits);
            _root.Add(_canvas);
            foreach (HudImage image in GameOptionsHud.Images()) _canvas.Preload(image);
            _canvas.RegisterCallback<PointerDownEvent>(e =>
            {
                Vector2 at = _canvas.ToVirtual(e.localPosition);
                if (!_hud.PointerDown(at.x, at.y, e.button)) return;
                e.StopPropagation();
                if (_hud.Holding) _canvas.CapturePointer(e.pointerId);
            });
            _canvas.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!_canvas.HasPointerCapture(e.pointerId)) return;
                Vector2 at = _canvas.ToVirtual(e.localPosition);
                _hud.PointerMove(at.x, at.y); e.StopPropagation();
            });
            _canvas.RegisterCallback<PointerUpEvent>(e =>
            {
                _hud.PointerUp();
                if (_canvas.HasPointerCapture(e.pointerId)) _canvas.ReleasePointer(e.pointerId);
            });
            // A release the canvas never sees (focus lost mid-drag) must not leave the press held.
            _canvas.RegisterCallback<PointerCaptureOutEvent>(_ => _hud.PointerUp());
            Show(false);
        }
        public bool ContainsPointer(Vector3 position) => _hud.Visible;
        public void Show(bool visible)
        {
            _hud.Show(visible);
            _root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
        /// <summary>Polls the keyboard while a key is being captured.</summary>
        public void PollKeyboard()
        {
            _hud.CaptureKey(ClassicKeyBindings.Read(Model.Settings.Current));
        }
        public void Draw()
        {
            if (!_hud.Visible) return;
            var screen = new HudScreen(Width, Height);
            float toVirtual = 1920f / Height;
            Vector3 mouse = Input.mousePosition;
            _frame.Clear();
            _hud.Build(screen, (mouse.x * toVirtual, 1920 - mouse.y * toVirtual), _frame);
            _canvas.Present(_frame, screen.Scale, 1);
        }
        public void Dispose()
        {
            _canvas.Dispose();
            UnityEngine.Object.Destroy(_owner); UnityEngine.Object.Destroy(Panel);
        }
    }
}
