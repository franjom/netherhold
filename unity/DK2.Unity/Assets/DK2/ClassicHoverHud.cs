// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.IO;
using DK2.Art.Textures;
using DK2.Data.Model;
using DK2.Data.Text;
using DK2.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace DK2.Unity
{
    public interface IClassicHoverAssets
    {
        BitmapFont Font(string fileName);
        RgbaImage StatusBar { get; }
        RgbaImage StatusEnd { get; }
        RgbaImage TooltipPiece(int index);
    }

    public sealed class InstallClassicHoverAssets : IClassicHoverAssets
    {
        private readonly InstallHudSources _sources;
        private readonly RgbaImage[] _pieces = new RgbaImage[9];
        public RgbaImage StatusBar { get; }
        public RgbaImage StatusEnd { get; }

        /// <summary>From the install alone; its texture archive is closed once the art is read.</summary>
        public InstallClassicHoverAssets(string install) : this(new InstallHudSources(install))
        {
            _sources.Dispose();
        }

        public InstallClassicHoverAssets(InstallHudSources sources)
        {
            _sources = sources ?? throw new ArgumentNullException(nameof(sources));
            StatusBar = sources.Texture("GUI\\Windows\\Bar-00");
            StatusEnd = sources.Texture("GUI\\Windows\\Bar-01");
            string[] suffixes = { "tl", "t", "tr", "l", "m", "r", "bl", "b", "br" };
            for (int i = 0; i < 9; i++) _pieces[i] = sources.Texture("GUI\\Windows\\text_box-" + suffixes[i]);
        }
        public RgbaImage TooltipPiece(int index) => _pieces[index];
        public BitmapFont Font(string file) => _sources.Font(file);
    }

    /// <summary>The hover HUD's Unity side: <see cref="HoverHud"/> (ADR-0805) drawn by two
    /// <see cref="HudCanvas"/>es, the bar under the resource panels and the text and popup over them.</summary>
    public sealed class ClassicHoverHud : IDisposable
    {
        private readonly IClassicHoverAssets _assets;
        private readonly HoverHud _hud;
        private readonly GameObject _owner, _barOwner;
        private readonly PanelSettings _settings, _barSettings;
        private readonly VisualElement _root, _barRoot;
        private readonly HudCanvas _canvas, _barCanvas;
        private readonly HudFrame _frame = new HudFrame(), _barFrame = new HudFrame();
        public bool ShowCreatureTooltip { get => _hud.ShowCreatureTooltip; set => _hud.ShowCreatureTooltip = value; }
        public PanelSettings Panel => _settings;
        public int UiScale { get; set; } = 100;
        private HudScaleLayout ScaleLayout => ClassicHudScaling.Layout(UiScale);
        private HudScreen Screen => new HudScreen(Mathf.RoundToInt(ScaleLayout.LogicalWidth), UnityEngine.Screen.height);
        public Rect StatusBounds => Local(_hud.StatusBounds(Screen));
        /// <summary>The wooden bar: the whole width, behind the Heart, mana and gold panels, as in DK2.</summary>
        public Rect BarBounds => Local(_hud.BarBounds(Screen));

        public ClassicHoverHud(IClassicHoverAssets assets) : this(null, assets) { }

        public ClassicHoverHud(Transform parent, IClassicHoverAssets assets)
        {
            _assets = assets ?? throw new ArgumentNullException(nameof(assets));
            _hud = new HoverHud(assets.Font, (assets.StatusBar.Width, assets.StatusBar.Height));
            // Tooltips over every panel but the full map (20); the bar under the resource panels (11).
            _settings = Settings(17);
            _barSettings = Settings(10);
            _canvas = Canvas(parent, _settings, "Dungeon hover HUD", out _owner, out _root);
            _barCanvas = Canvas(parent, _barSettings, "Dungeon status bar", out _barOwner, out _barRoot);
            foreach (HudImage image in HoverHud.Images())
            {
                if (image == HoverHud.Bar || image == HoverHud.Cap) _barCanvas.Preload(image);
                else _canvas.Preload(image);
            }
        }

        private static PanelSettings Settings(int order)
        {
            PanelSettings settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("DK2Hud");
            settings.scaleMode = PanelScaleMode.ConstantPixelSize; settings.scale = 1;
            settings.sortingOrder = order;
            return settings;
        }

        private HudCanvas Canvas(Transform parent, PanelSettings settings, string name, out GameObject owner, out VisualElement root)
        {
            owner = new GameObject(name);
            if (parent != null) owner.transform.SetParent(parent, false);
            var document = owner.AddComponent<UIDocument>(); document.panelSettings = settings;
            root = document.rootVisualElement; root.pickingMode = PickingMode.Ignore;
            root.style.width = Length.Percent(100); root.style.height = Length.Percent(100);
            var canvas = new HudCanvas(Load, null);
            root.Add(canvas);
            return canvas;
        }

        private RgbaImage Load(string path)
        {
            if (path == HoverHud.Bar.Path) return _assets.StatusBar;
            if (path == HoverHud.Cap.Path) return _assets.StatusEnd;
            const string Piece = "GUI\\Windows\\text_box-";
            string[] suffixes = { "tl", "t", "tr", "l", "m", "r", "bl", "b", "br" };
            if (path.StartsWith(Piece, StringComparison.Ordinal))
            {
                int index = Array.IndexOf(suffixes, path.Substring(Piece.Length));
                if (index >= 0) return _assets.TooltipPiece(index);
            }
            throw new FileNotFoundException("Not a hover HUD image: " + path);
        }

        private Rect Local(HudRect r)
        {
            float scale = Screen.Scale;
            return new Rect(r.X * scale, r.Y * scale, r.Width * scale, r.Height * scale);
        }

        public void SetVisibility(HudVisibility visibility)
        {
            _hud.SetVisibility(visibility, (long)(Time.unscaledTimeAsDouble * 1000));
            DisplayStyle display = visibility.ShowNormalHud ? DisplayStyle.Flex : DisplayStyle.None;
            _root.style.display = display; _barRoot.style.display = display;
        }

        public bool ContainsPointer(Vector3 screenPosition)
        {
            Vector2 local = ClassicHudScaling.LocalPointer(screenPosition, ScaleLayout, false);
            float scale = Screen.Scale;
            return _hud.ContainsPointer(local.x / scale, local.y / scale);
        }

        /// <summary>Refreshes the frames in Update, before worker simulation dispatch.</summary>
        /// <param name="cursorGui">The pointer in screen pixels from the top-left.</param>
        /// <param name="cursorReach">How far the drawn cursor reaches right of the pointer, in DK2's
        /// 640×480 units (<see cref="HandCursor.Reach"/>). DK2's 98-unit offset is the 82-wide Point hand
        /// (hotspot 2) plus 18; ours anchors Point at its claw tip, whose least X over the loop is 0, so
        /// 100. The small pointer shown over the HUD is 42 wide, so the same offset left a wide gap (owner, 2026-10-20).</param>
        public void Draw(HoverTextSnapshot snapshot, Vector2 cursorGui, float cursorReach = 80)
        {
            HudScaleLayout layout = ScaleLayout;
            _settings.scale = _barSettings.scale = layout.Factor;
            HudScreen screen = Screen;
            float toVirtual = layout.Factor * screen.Scale;
            _frame.Clear(); _barFrame.Clear();
            _hud.Build(screen, UnityEngine.Screen.height / toVirtual, snapshot,
                (cursorGui.x / toVirtual, cursorGui.y / toVirtual), cursorReach,
                (long)(Time.unscaledTimeAsDouble * 1000), _barFrame, _frame);
            _barCanvas.Present(_barFrame, screen.Scale, layout.Factor);
            _canvas.Present(_frame, screen.Scale, layout.Factor);
        }

        public void Dispose()
        {
            _canvas.Dispose(); _barCanvas.Dispose();
            UnityEngine.Object.Destroy(_owner); UnityEngine.Object.Destroy(_settings);
            UnityEngine.Object.Destroy(_barOwner); UnityEngine.Object.Destroy(_barSettings);
        }
    }
}
