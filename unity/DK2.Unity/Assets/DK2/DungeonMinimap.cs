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
    /// the engine's own GPU disc (<see cref="MinimapGpuMap"/>) and camera footprint. The panel is DK2's 1024x768,
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
        private readonly MinimapFootprintVisual _footprint;
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
                : _hud.Press(at.x, at.y, primaryPressed, secondaryPressed, FixedFocus(focus), Heading(camera));
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
            return _hud.TryNavigate(at.x, at.y, FixedFocus(focus), Heading(camera), out target);
        }

        private static Dk2Angle Heading(Camera camera) =>
            new Dk2Angle(unchecked((ushort)Mathf.RoundToInt(camera.transform.eulerAngles.y * (2048f / 360))));

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
            // Our choice, the native z-order being unread: under the frame, as the map is. The native
            // corner clamp is to the disc's square, so a clamped corner can leave the circle, and the
            // ring covers it (minimap-rendering.md).
            _footprint = new MinimapFootprintVisual(assets.CameraFootprint, width, height);
            _hudRoot.Add(_footprint);
            var art = new Dictionary<string, RgbaImage>(StringComparer.Ordinal)
            {
                [MinimapHud.FrameArt] = assets.Frame,
                [MinimapHud.NorthArt] = assets.North,
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
            Vector3 centre = focus ?? camera.transform.position;
            if (!focus.HasValue)
            {
                var floor = new Plane(Vector3.up, new Vector3(0, 1, 0));
                Ray ray = camera.ViewportPointToRay(new Vector3(.5f, .5f, 0));
                if (floor.Raycast(ray, out float distance)) centre = ray.GetPoint(distance);
            }
            MinimapZoom zoom = _hud.Zoom;
            _gpu.SetView(zoom, FixedFocus(centre));
            _gpu.Render(camera.transform.eulerAngles.y, MinimapHud.DiscPixels(Screen.height, ScaleLayout.Factor));
            if (_image.image != _gpu.Disc) _image.image = _gpu.Disc;
            _footprint.UpdateCamera(zoom.IsWholeMap ? camera : null);
            _heading = Heading(camera);
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
            _footprint.Dispose();
            UnityEngine.Object.Destroy(_root); UnityEngine.Object.Destroy(_settings);
            UnityEngine.Object.Destroy(_backdrop);
        }
    }

    public sealed class MinimapFootprintVisual : VisualElement, IDisposable
    {
        private static readonly ushort[] Indices = { 0,2,1, 1,2,3, 2,4,3, 3,4,5, 5,4,6, 7,5,6, 0,7,6, 0,1,7 };
        private readonly Texture2D _texture;
        private readonly int _mapWidth, _mapHeight;
        private readonly System.Numerics.Vector2[] _corners = new System.Numerics.Vector2[4];
        private readonly System.Numerics.Vector2[] _vertices = new System.Numerics.Vector2[8];
        private readonly System.Numerics.Vector2[] _next = new System.Numerics.Vector2[8];
        private bool _valid;
        private readonly VisualElement _ribbon;

        public MinimapFootprintVisual(RgbaImage texture, int mapWidth, int mapHeight)
        {
            _texture = MinimapFrameVisual.Decode(texture);
            _mapWidth = mapWidth; _mapHeight = mapHeight;
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.left = 12.8f; style.bottom = 11.2f;
            style.width = 169.6f; style.height = 169.6f;
            style.overflow = Overflow.Hidden;
            style.borderTopLeftRadius = style.borderTopRightRadius = 84.8f;
            style.borderBottomLeftRadius = style.borderBottomRightRadius = 84.8f;
            _ribbon = new VisualElement { pickingMode = PickingMode.Ignore };
            _ribbon.style.position = Position.Absolute;
            _ribbon.style.left = _ribbon.style.top = _ribbon.style.right = _ribbon.style.bottom = 0;
            Add(_ribbon);
            _ribbon.generateVisualContent += DrawFootprint;
        }

        public void UpdateCamera(Camera camera)
        {
            bool valid = camera != null;
            var floor = new Plane(Vector3.up, new Vector3(0, 1, 0));
            for (int i = 0; valid && i < 4; i++)
            {
                Ray ray = camera.ViewportPointToRay(new Vector3(i == 1 || i == 2 ? 1 : 0, i < 2 ? 1 : 0, 0));
                if (!floor.Raycast(ray, out float distance)) { valid = false; break; }
                Vector3 ground = ray.GetPoint(distance);
                _corners[i] = new System.Numerics.Vector2(ground.x, -ground.z);
            }
            valid = valid && MinimapCameraFootprint.TryBuild(_corners, _mapWidth, _mapHeight,
                camera.transform.eulerAngles.y * Mathf.Deg2Rad, _next);
            bool changed = valid != _valid;
            if (valid)
                for (int i = 0; i < 8; i++)
                {
                    changed |= _vertices[i] != _next[i];
                    _vertices[i] = _next[i];
                }
            _valid = valid;
            if (changed) _ribbon.MarkDirtyRepaint();
        }

        private void DrawFootprint(MeshGenerationContext context)
        {
            if (!_valid) return;
            MeshWriteData mesh = context.Allocate(8, Indices.Length, _texture);
            for (int i = 0; i < 8; i++)
                mesh.SetNextVertex(new Vertex { position = new Vector3(_vertices[i].X - 12.8f, _vertices[i].Y - 587.2f, Vertex.nearZ),
                    tint = Color.white, uv = new Vector2(.5f, i % 2 == 0 ? 1 : 0) });
            foreach (ushort index in Indices) mesh.SetNextIndex(index);
        }

        public void Dispose()
        {
            RemoveFromHierarchy();
            UnityEngine.Object.Destroy(_texture);
        }
    }
}
