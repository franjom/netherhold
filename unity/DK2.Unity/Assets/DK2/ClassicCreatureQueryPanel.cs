// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using DK2.Art.Textures;
using DK2.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace DK2.Unity
{
    /// <summary>The creature query details' Unity side: <see cref="CreatureQueryHud"/> (ADR-0805) drawn by a
    /// <see cref="HudCanvas"/>, docked to the bottom of the screen.</summary>
    public sealed class ClassicCreatureQueryPanel : IDisposable
    {
        private static readonly RgbaImage Blank = new RgbaImage(1, 1, new byte[4]);
        private readonly CreatureQueryHud _hud;
        private readonly GameObject _owner;
        private readonly PanelSettings _settings;
        private readonly VisualElement _root;
        private readonly HudCanvas _canvas;
        private readonly HudFrame _frame = new HudFrame();
        private HudScaleLayout _layout = ClassicHudScaling.Layout(100);
        public PanelSettings Panel => _settings;
        public int UiScale { get; set; } = 100;

        /// <param name="image">Reads a catalogue image; without it the scroll bar is not drawn.</param>
        public ClassicCreatureQueryPanel(Transform parent, IClassicOptionsAssets assets, Func<string, RgbaImage> image = null)
        {
            if (assets == null) throw new ArgumentNullException(nameof(assets));
            _hud = new CreatureQueryHud(assets.Font);
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("DK2Hud");
            _settings.scaleMode = PanelScaleMode.ConstantPixelSize; _settings.scale = 1;
            _settings.sortingOrder = 13;
            _owner = new GameObject("Creature query details"); _owner.transform.SetParent(parent, false);
            var document = _owner.AddComponent<UIDocument>(); document.panelSettings = _settings;
            _root = document.rootVisualElement; _root.pickingMode = PickingMode.Ignore;
            _canvas = new HudCanvas(image ?? (_ => Blank), null);
            _root.Add(_canvas);
            foreach (HudImage part in CreatureQueryHud.Images()) _canvas.Preload(part);
            _root.style.display = DisplayStyle.None;
        }

        private static HudScreen Screen(HudScaleLayout layout) => new HudScreen(Mathf.RoundToInt(layout.LogicalWidth), layout.Height);

        public void Draw(CreatureQuerySnapshot snapshot, HudVisibility visibility, bool otherPanelOpen = false, Vector2Int viewport = default)
        {
            _layout = ClassicHudScaling.Layout(UiScale, viewport.x, viewport.y);
            ClassicHudScaling.Apply(_settings, _root, _layout, true);
            HudScreen screen = Screen(_layout);
            _frame.Clear();
            _hud.Build(screen, snapshot, visibility, otherPanelOpen, _frame);
            _root.style.display = _frame.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _canvas.Present(_frame, screen.Scale, _layout.Factor);
        }

        public bool ContainsPointer(Vector3 screenPosition, HudVisibility visibility)
        {
            Vector2 at = ClassicHudScaling.LocalPointer(screenPosition, _layout, true) / Screen(_layout).Scale;
            return _hud.ContainsPointer(at.x, at.y, visibility);
        }

        public void Dispose()
        {
            _canvas.Dispose();
            UnityEngine.Object.Destroy(_owner); UnityEngine.Object.Destroy(_settings);
        }
    }
}
