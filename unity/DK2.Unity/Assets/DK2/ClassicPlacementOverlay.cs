// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using DK2.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace DK2.Unity
{
    /// <summary>Unity glue for <see cref="PlacementCursorHud"/> (plan step 3e): the tool icon, No Go and price, or the
    /// held-item icons, drawn by a <see cref="HudCanvas"/> on its own panel over the HUD, in screen pixels.</summary>
    public sealed class ClassicPlacementOverlay : IDisposable
    {
        private readonly IClassicResourceAssets _assets;
        private readonly GameObject _owner;
        private readonly PanelSettings _settings;
        private readonly VisualElement _root;
        private readonly HudCanvas _canvas;
        private readonly PlacementCursorHud _hud = new PlacementCursorHud();
        private readonly HudFrame _frame = new HudFrame();

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
            _root = document.rootVisualElement; _root.pickingMode = PickingMode.Ignore;
            _root.style.width = Length.Percent(100); _root.style.height = Length.Percent(100);
            _canvas = new HudCanvas(assets.Image, null);
            _root.Add(_canvas);
            _canvas.Preload(PlacementCursorHud.NoGoIconName);
        }

        /// <inheritdoc cref="PlacementCursorHud.SpellIconName"/>
        public Func<byte, string> SpellIconName { get => _hud.SpellIconName; set => _hud.SpellIconName = value; }

        /// <inheritdoc cref="PlacementCursorHud.CreatureIconName"/>
        public Func<byte, string> CreatureIconName { get => _hud.CreatureIconName; set => _hud.CreatureIconName = value; }

        /// <inheritdoc cref="PlacementCursorHud.ObjectIconName"/>
        public Func<byte, string> ObjectIconName { get => _hud.ObjectIconName; set => _hud.ObjectIconName = value; }

        /// <param name="pointer">The pointer in screen pixels from the top left.</param>
        /// <param name="overHud">Moves the icon left by 40 reference units over the GUI.</param>
        /// <param name="held">The hand's icons, read when <paramref name="feedback"/> shows
        /// <see cref="PlacementIcon.Held"/>.</param>
        public void Draw(in PlacementFeedback feedback, Vector2 pointer, bool overHud, HeldItemStack held = null,
            HandCursorKind handKind = HandCursorKind.Point)
        {
            var screen = new HudScreen(Screen.width, Screen.height);
            _frame.Clear();
            _hud.Build(_frame, feedback, new System.Numerics.Vector2(pointer.x, pointer.y), Screen.width, Screen.height,
                overHud, held, handKind);
            _canvas.Present(_frame, screen.Scale, 1);
        }

        public void Dispose()
        {
            _root.RemoveFromHierarchy();
            _canvas.Dispose();
            UnityEngine.Object.Destroy(_owner); UnityEngine.Object.Destroy(_settings);
        }
    }
}
