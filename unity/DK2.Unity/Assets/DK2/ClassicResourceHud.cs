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
    public interface IClassicResourceAssets { RgbaImage Image(string name); }

    public sealed class InstallClassicResourceAssets : IClassicResourceAssets, IDisposable
    {
        private readonly InstallHudSources _sources;
        private readonly bool _owned;

        /// <summary>From the install alone, owning the texture archive it opens.</summary>
        public InstallClassicResourceAssets(string install)
        {
            _sources = new InstallHudSources(install);
            _owned = true;
        }

        /// <summary>Over shared sources, which the caller disposes.</summary>
        public InstallClassicResourceAssets(InstallHudSources sources) =>
            _sources = sources ?? throw new ArgumentNullException(nameof(sources));

        public RgbaImage Image(string name) => _sources.Texture(name);

        public void Dispose()
        {
            if (_owned) _sources.Dispose();
        }
    }

    /// <summary>Unity glue for <see cref="KeeperResourceHud"/> (ADR-0805): one UI Toolkit panel holding a
    /// <see cref="HudCanvas"/> that never takes the pointer. The strip's DK2 layout is the model's.</summary>
    public sealed class ClassicResourceHud : IDisposable
    {
        private readonly GameObject _owner;
        private readonly PanelSettings _settings;
        private readonly VisualElement _root;
        private readonly HudCanvas _canvas;
        private readonly IClassicResourceAssets _assets;
        private readonly KeeperResourceHud _hud = new KeeperResourceHud();
        private readonly HudFrame _frame = new HudFrame();
        private Vector2Int _viewport;
        public int UiScale { get; set; } = 100;
        private HudScaleLayout ScaleLayout => ClassicHudScaling.Layout(UiScale, _viewport.x, _viewport.y);
        // ScaleLayout's height is the viewport's, or Screen.height without one.
        private HudScreen Screen => ScaleLayout.Screen;
        public PanelSettings Panel => _settings;
        public float Bottom => KeeperResourceHud.Bottom(Screen);
        public Rect Bounds
        {
            get
            {
                HudScreen screen = Screen; HudRect r = KeeperResourceHud.Bounds(screen);
                return new Rect(0, 0, r.Width * screen.Scale, r.Height * screen.Scale);
            }
        }

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
            _canvas = new HudCanvas(assets.Image, null);
            _root.Add(_canvas);
            // Decoded here rather than by the first draw with the HUD visible, which comes after the
            // opening camera path (ADR-0688).
            foreach (HudImage image in KeeperResourceHud.Images()) _canvas.Preload(image);
        }

        public void SetVisibility(HudVisibility visibility)
        {
            _hud.SetVisibility(visibility);
            _root.style.display = visibility.ShowNormalHud ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public bool ContainsPointer(Vector3 screenPosition)
        {
            HudScreen screen = Screen;
            Vector2 local = ClassicHudScaling.LocalPointer(screenPosition, ScaleLayout, false);
            return _hud.ContainsPointer(screen, screen.Virtual(local.x), screen.Virtual(local.y));
        }

        public KeeperResourceKind ResourceAt(Vector3 screenPosition)
        {
            HudScreen screen = Screen;
            Vector2 local = ClassicHudScaling.LocalPointer(screenPosition, ScaleLayout, false);
            return _hud.ResourceAt(screen, screen.Virtual(local.x), screen.Virtual(local.y));
        }

        public void Draw(KeeperResourceSnapshot snapshot) => Draw(snapshot, default);

        public void Draw(KeeperResourceSnapshot snapshot, Vector2Int viewport)
            => Draw(snapshot, viewport, unchecked((uint)(Time.unscaledTimeAsDouble * 1000)));

        public void Draw(KeeperResourceSnapshot snapshot, Vector2Int viewport, uint milliseconds,
            KeeperResourceFlash flash = KeeperResourceFlash.None)
        {
            _viewport = viewport;
            HudScaleLayout layout = ScaleLayout;
            ClassicHudScaling.Apply(_settings, _root, layout, false);
            HudScreen screen = Screen;
            _frame.Clear();
            _hud.Build(screen, snapshot, milliseconds, flash, _frame);
            _canvas.Present(_frame, screen.Scale, layout.Factor);
        }

        public void Dispose()
        {
            _root.RemoveFromHierarchy();
            _canvas.Dispose();
            UnityEngine.Object.Destroy(_owner); UnityEngine.Object.Destroy(_settings);
            if (_assets is IDisposable disposable) disposable.Dispose();
        }
    }
}
