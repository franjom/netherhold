// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using DK2.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace DK2.Unity
{
    /// <summary>The creature overview's Unity side: <see cref="CreatureOverviewHud"/> (ADR-0805) drawn by a
    /// <see cref="HudCanvas"/>, docked to the bottom of the screen.</summary>
    public sealed class ClassicCreatureOverview : IDisposable
    {
        private readonly CreatureOverviewHud _hud;
        private readonly GameObject _owner;
        private readonly PanelSettings _settings;
        private readonly VisualElement _root;
        private readonly HudCanvas _canvas;
        private readonly HudFrame _frame = new HudFrame();
        private int _width, _height;
        public PanelSettings Panel => _settings;
        public CreatureOverviewMode Mode => _hud.Mode;
        public int UiScale { get; set; } = 100;
        private HudScaleLayout ScaleLayout => ClassicHudScaling.Layout(UiScale, _width, _height);

        public ClassicCreatureOverview(Transform parent, ICreatureOverviewAssets assets, CreatureOverview view)
        {
            if (assets == null) throw new ArgumentNullException(nameof(assets));
            _hud = new CreatureOverviewHud(assets.Portrait, view);
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("DK2Hud");
            _settings.scaleMode = PanelScaleMode.ConstantPixelSize; _settings.sortingOrder = 13;
            _owner = new GameObject("Creature overview");
            if (parent != null) _owner.transform.SetParent(parent, false);
            var document = _owner.AddComponent<UIDocument>(); document.panelSettings = _settings;
            _root = document.rootVisualElement; _root.pickingMode = PickingMode.Ignore;
            _canvas = new HudCanvas(assets.Image, null);
            _root.Add(_canvas);
            foreach (HudImage image in CreatureOverviewHud.Images()) _canvas.Preload(image);
            _root.style.display = DisplayStyle.None;
        }

        private HudScreen Screen(HudScaleLayout layout) => new HudScreen(Mathf.RoundToInt(layout.LogicalWidth), layout.Height);

        /// <summary>A screen position in virtual units of the bottom-docked panel.</summary>
        private Vector2 Virtual(Vector3 pointer)
        {
            HudScaleLayout layout = ScaleLayout;
            return ClassicHudScaling.LocalPointer(pointer, layout, true) / Screen(layout).Scale;
        }

        /// <summary><paramref name="area"/> is the Creatures panel's place (ClassicBottomPanel.PanelArea), null while it is closed.</summary>
        public void Draw(HudVisibility visibility, BottomPanelRect? area, Vector3 pointer, Vector2Int viewport = default)
        {
            _width = viewport.x > 0 ? viewport.x : UnityEngine.Screen.width;
            _height = viewport.y > 0 ? viewport.y : UnityEngine.Screen.height;
            HudScaleLayout layout = ScaleLayout;
            ClassicHudScaling.Apply(_settings, _root, layout, true);
            bool visible = visibility.ShowNormalHud && area.HasValue;
            _root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            HudScreen screen = Screen(layout);
            Vector2 at = Virtual(pointer);
            _frame.Clear();
            _hud.Build(screen, _width, visibility, area, (at.x, at.y), _frame);
            _canvas.Present(_frame, screen.Scale, layout.Factor);
        }

        /// <summary>The hovered widget's text id (record +0x30): the worker row icons 2308..2314 and the
        /// mode selectors 2316..2322; 0 for none.</summary>
        public int HoveredTextId(Vector3 pointer) { Vector2 at = Virtual(pointer); return _hud.HoveredTextId(at.x, at.y); }

        /// <summary>The creature type whose column is hovered, for the status strip (0x00418DD0).</summary>
        public CreatureOverviewType? HoveredColumnType(Vector3 pointer) { Vector2 at = Virtual(pointer); return _hud.HoveredColumnType(at.x, at.y); }

        public bool ContainsPointer(Vector3 pointer) { Vector2 at = Virtual(pointer); return _hud.ContainsPointer(at.x, at.y); }

        public bool TryPress(Vector3 pointer, bool primary, bool secondary, bool lowestLevel, bool highestLevel, out CreatureOverviewRequest request)
        {
            Vector2 at = Virtual(pointer);
            return _hud.TryPress(at.x, at.y, primary, secondary, lowestLevel, highestLevel, out request);
        }

        public void Dispose()
        {
            _canvas.Dispose();
            UnityEngine.Object.Destroy(_owner); UnityEngine.Object.Destroy(_settings);
        }
    }
}
