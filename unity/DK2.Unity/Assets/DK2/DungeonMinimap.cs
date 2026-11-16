// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using DK2.Core;
using DK2.Art.Textures;
using DK2.Data.Model;
using DK2.Pathfinding;
using DK2.Presentation;
using DK2.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace DK2.Unity
{
    /// <summary>The minimap's Unity side: <see cref="MinimapHud"/> (ADR-0805) drawn by a <see cref="HudCanvas"/> over
    /// the engine's own GPU disc (<see cref="MinimapGpuMap"/>); the camera footprint is the frame's first draw
    /// (<see cref="MinimapHud.SetCamera"/>). The panel is DK2's 1024x768,
    /// height-matched and docked to the bottom.</summary>
    public sealed class DungeonMinimap : IDisposable
    {
        private readonly MinimapHud _hud;
        private readonly GameObject _root;
        private readonly PanelSettings _settings;
        private readonly VisualElement _hudRoot;
        private readonly Image _image;
        private readonly Texture2D _backdrop;
        private readonly MinimapGpuMap _gpu;
        private readonly HudCanvas _canvas;
        private readonly HudFrame _frame = new HudFrame();
        private Vector3 _pointer;
        private Dk2Angle _heading;
        public int UiScale { get; set; } = 100;
        private HudScaleLayout ScaleLayout => ClassicHudScaling.Layout(UiScale);

        /// <summary>What a press does (<see cref="MinimapHud.Press"/>); without a camera only the corners act.</summary>
        public ClassicHudAction HandlePress(Vector3 screenPosition, bool primaryPressed, bool secondaryPressed, Camera camera, Vector3 focus)
        {
            if (Screen.height <= 0) return ClassicHudAction.None;
            Vector2 at = Virtual(screenPosition);
            return camera == null ? _hud.CornerPress(at.x, at.y, primaryPressed, secondaryPressed)
                : _hud.Press(at.x, at.y, primaryPressed, secondaryPressed, FixedFocus(focus), View(camera).Heading);
        }

        /// <summary>The corner control under the pointer, for its tooltip; None off the corners.</summary>
        public ClassicHudAction HoveredControl(Vector3 screenPosition)
        {
            if (Screen.height <= 0) return ClassicHudAction.None;
            Vector2 at = Virtual(screenPosition);
            return _hud.HoveredControl(at.x, at.y);
        }

        public void UpdateControls(Vector3 screenPosition, ClassicHudAction selected, bool optionsVisible)
        {
            _pointer = screenPosition;
            _hud.SetControls(selected, optionsVisible);
            Present();
        }

        /// <summary>A screen position in the panel's virtual units: 2.5 a 1024x768 unit, docked to the bottom.</summary>
        private Vector2 Virtual(Vector3 point)
        {
            float scale = Screen.height / 768f * ScaleLayout.Factor;
            return new Vector2(point.x / scale, 768 - point.y / scale) * 2.5f;
        }

        public RenderTexture ClassTexture => _gpu.Classes;
        public Texture PaletteTexture => _gpu.PaletteTexture;
        public MinimapPalette Palette => _hud.Palette;
        public int MapWidth => _hud.MapWidth;
        public int MapHeight => _hud.MapHeight;

        public void ZoomIn() => _hud.ZoomIn();
        public void ZoomOut() => _hud.ZoomOut();
        public void HandleZoomPress(Vector3 screenPosition, bool primaryPressed, bool secondaryPressed)
        {
            if (Screen.height <= 0) return;
            Vector2 at = Virtual(screenPosition);
            _hud.ZoomPress(at.x, at.y, primaryPressed, secondaryPressed);
        }

        public bool TryNavigate(Vector3 screenPosition, Camera camera, Vector3 focus, out TilePos target)
        {
            target = default;
            if (camera == null || Screen.height <= 0) return false;
            Vector2 at = Virtual(screenPosition);
            return _hud.TryNavigate(at.x, at.y, FixedFocus(focus), View(camera).Heading, out target);
        }

        /// <summary>The camera as the minimap reads it (<see cref="MinimapCameraView"/>): its pose, field of view, aspect
        /// and the off-centre shift the runner wrote as <c>projection[1, 2] = -shift</c>.</summary>
        private static MinimapCameraView View(Camera camera)
        {
            Transform t = camera.transform;
            return new MinimapCameraView(ToNumerics(t.position), ToNumerics(t.forward), ToNumerics(t.up), camera.fieldOfView,
                camera.aspect, -camera.projectionMatrix[1, 2]);
        }

        private static System.Numerics.Vector3 ToNumerics(Vector3 v) => new System.Numerics.Vector3(v.x, v.y, v.z);

        private static FpPosition3 FixedFocus(Vector3 focus) =>
            MinimapHud.FixedFocus(new System.Numerics.Vector3(focus.x, focus.y, focus.z));

        public DungeonMinimap(Transform parent, IMinimapAssets assets, int width, int height)
        {
            _backdrop = Decode(assets.BackdropPng); _backdrop.wrapMode = TextureWrapMode.Repeat;
            Texture2D source = Decode(assets.PalettePng);
            MinimapPalette palette;
            try
            {
                // GetPixels32 is bottom-up: the image's top row is its last.
                Color32[] pixels = source.GetPixels32();
                palette = MinimapPalette.FromTopRow(System.Runtime.InteropServices.MemoryMarshal.AsBytes(
                    new ReadOnlySpan<Color32>(pixels, (source.height - 1) * source.width, source.width)));
            }
            finally { UnityEngine.Object.Destroy(source); }
            _hud = new MinimapHud(palette, width, height);
            _gpu = new MinimapGpuMap(width, height, palette, _backdrop);
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("DK2Hud");
            // Over the bottom panel (12) and creature panels (13), as DK2 draws the minimap over the
            // tab strip's rail; under Options (15), tooltips (17) and the full map (20).
            _settings.sortingOrder = 14;
            _settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            _settings.referenceResolution = new Vector2Int(1024, 768);
            _settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight; _settings.match = 1;
            _root = new GameObject("Dungeon HUD"); _root.transform.SetParent(parent, false);
            var document = _root.AddComponent<UIDocument>(); document.panelSettings = _settings;
            _hudRoot = document.rootVisualElement;
            _hudRoot.style.width = Length.Percent(100);
            _hudRoot.style.height = Length.Percent(100);
            _hudRoot.pickingMode = PickingMode.Ignore;
            HudRect disc = MinimapHud.Disc;
            _image = new Image { pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.StretchToFill };
            _image.style.position = Position.Absolute;
            _image.style.left = disc.X / 2.5f; _image.style.bottom = 768 - (disc.Y + disc.Height) / 2.5f;
            _image.style.width = disc.Width / 2.5f; _image.style.height = disc.Height / 2.5f;
            _hudRoot.Add(_image);
            var art = new Dictionary<string, RgbaImage>(StringComparer.Ordinal)
            {
                [MinimapHud.FrameArt] = assets.Frame,
                [MinimapHud.NorthArt] = assets.North,
                [MinimapHud.FootprintArt] = assets.CameraFootprint,
            };
            for (int i = 0; i < 4; i++)
                for (int active = 0; active < 2; active++)
                    art[MinimapHud.CornerArt((MinimapCorner)i, active != 0)] = assets.Corner((MinimapCorner)i, active != 0);
            _canvas = new HudCanvas(path => art[path], null);
            _hudRoot.Add(_canvas);
            foreach (HudImage image in MinimapHud.Images()) _canvas.Preload(image);
        }

        private static Texture2D Decode(byte[] png)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (texture.LoadImage(png)) return texture;
            UnityEngine.Object.Destroy(texture);
            throw new InvalidOperationException("Minimap asset failed to decode.");
        }

        // Capture is called while the simulation worker is joined. Draw reads only GPU state.
        public void Capture(WorldState world, WorldView view, INavByteGrid navigation)
        {
            if (!_hud.Capture(world, view, navigation, out bool paletteChanged)) return;
            _gpu.UploadChanges(_hud.Grid);
            if (paletteChanged) _gpu.UpdatePalette(_hud.Palette);
            _gpu.UploadMarkers(_hud.Markers);
            _gpu.SetHeartTrail(view.MinimapHeartTile, world.CurrentTick);
        }

        public void Draw(Camera camera, Vector3? focus = null)
        {
            _settings.scale = ScaleLayout.Factor;
            if (!_hud.Visibility.ShowNormalHud || camera == null) return;
            MinimapCameraView view = View(camera);
            Vector3 centre = focus ?? camera.transform.position;
            if (!focus.HasValue && view.TryFloorCentre(out System.Numerics.Vector3 floor)) centre = new Vector3(floor.X, floor.Y, floor.Z);
            MinimapZoom zoom = _hud.Zoom;
            _gpu.SetView(zoom, FixedFocus(centre));
            _gpu.Render(camera.transform.eulerAngles.y, MinimapHud.DiscPixels(Screen.height, ScaleLayout.Factor));
            if (_image.image != _gpu.Disc) _image.image = _gpu.Disc;
            _hud.SetCamera(view);
            _heading = view.Heading;
            Present();
        }

        /// <summary>Builds the frame, corners and north sprite into the canvas, which covers the panel's bottom
        /// 768 units (the panel is 768 / factor tall).</summary>
        private void Present()
        {
            if (Screen.height <= 0) return;
            float factor = ScaleLayout.Factor;
            _canvas.style.top = 768 / factor - 768; _canvas.style.height = 768;
            var screen = new HudScreen(Mathf.RoundToInt(Screen.width / factor), Screen.height);
            Vector2 at = Virtual(_pointer);
            _frame.Clear();
            _hud.Build(screen, (at.x, at.y), _heading, _frame);
            _canvas.Present(_frame, 1 / 2.5f, Screen.height / 768f * factor);
        }

        public bool ContainsPointer(Vector3 position)
        {
            if (Screen.height <= 0) return false;
            Vector2 at = Virtual(position);
            return _hud.ContainsPointer(at.x, at.y);
        }

        public void SetVisibility(HudVisibility visibility)
        {
            if (_hud.Visibility.ShowNormalHud != visibility.ShowNormalHud)
            {
                _hudRoot.style.display = visibility.ShowNormalHud ? DisplayStyle.Flex : DisplayStyle.None;
                if (visibility.ShowNormalHud) _canvas.Invalidate();
            }
            _hud.SetVisibility(visibility);
        }

        public void Dispose()
        {
            _gpu.Dispose();
            _canvas.Dispose();
            UnityEngine.Object.Destroy(_root); UnityEngine.Object.Destroy(_settings);
            UnityEngine.Object.Destroy(_backdrop);
        }
    }
}
