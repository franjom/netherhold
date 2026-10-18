// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.IO;
using System.Collections.Generic;
using DK2.Core;
using DK2.Art.Textures;
using DK2.Data.Model;
using DK2.Data.Wad;
using DK2.Pathfinding;
using DK2.Presentation;
using DK2.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace DK2.Unity
{
    public interface IMinimapAssets
    {
        byte[] PalettePng { get; }
        byte[] BackdropPng { get; }
        RgbaImage Frame { get; }
        RgbaImage CameraFootprint { get; }
        RgbaImage Corner(MinimapCorner corner, bool active);
    }

    public sealed class InstallMinimapAssets : IMinimapAssets
    {
        public byte[] PalettePng { get; }
        public byte[] BackdropPng { get; }
        public RgbaImage Frame { get; }
        public RgbaImage CameraFootprint { get; }
        private readonly RgbaImage[] _corners = new RgbaImage[8];
        public InstallMinimapAssets(string install)
        {
            using (var archive = WadArchive.Open(Path.Combine(install, "Data", "EngineTextures.WAD")))
            {
                PalettePng = archive.Extract("GUI\\Map\\MapColours.png");
                BackdropPng = archive.Extract("Map-BG.PNG");
            }
            using (var textures = EngineTextureArchive.OpenInstall(install))
            {
                Frame = textures.Decode("GUI\\Map\\map");
                CameraFootprint = textures.Decode("GUI\\Map\\mapcamerabox");
                for (int i = 0; i < 4; i++)
                {
                    string suffix = i.ToString("00");
                    _corners[i * 2] = textures.Decode("GUI\\Map\\" + (i == 3 ? "selected-" : "") + "mapbf-" + suffix);
                    _corners[i * 2 + 1] = textures.Decode("GUI\\Map\\" + (i == 3 ? "" : "selected-") + "mapbf-" + suffix);
                }
            }
        }
        public RgbaImage Corner(MinimapCorner corner, bool active)
        {
            if ((uint)corner > 3) throw new ArgumentOutOfRangeException(nameof(corner));
            return _corners[(int)corner * 2 + (active ? 1 : 0)];
        }
    }

    public sealed class DungeonMinimap : IDisposable
    {
        private const float Left = 12.8f, Bottom = 11.2f, Diameter = 169.6f;
        private readonly GameObject _root;
        private readonly PanelSettings _settings;
        private readonly VisualElement _hudRoot;
        private HudVisibility _visibility = new HudVisibility(true, true);
        private readonly Image _image;
        private readonly Texture2D _backdrop;
        private readonly MinimapPalette _palette;
        private readonly MinimapClassGrid _grid;
        private readonly MinimapGpuMap _gpu;
        private readonly MinimapFrameVisual _frame;
        private readonly MinimapFootprintVisual _footprint;
        private readonly List<MinimapMarker> _markers = new List<MinimapMarker>();
        private WorldState _capturedWorld;
        private long _capturedTick = -1;
        private int _capturedPreview = -1;
        private MinimapZoom _zoom = MinimapZoom.WholeMap;
        public void ZoomIn() => _zoom = _zoom.ZoomIn();
        public void ZoomOut() => _zoom = _zoom.ZoomOut();
        public void HandleZoomPress(Vector3 screenPosition, bool primaryPressed, bool secondaryPressed)
        {
            if (!_visibility.ShowNormalHud || !_visibility.AllowOrdinaryInput) return;
            if (!primaryPressed && !secondaryPressed || Screen.height <= 0) return;
            float scale = Screen.height / 768f;
            if (!MinimapControlLayout.IsMagnifier(new System.Numerics.Vector2(
                screenPosition.x / scale, 768 - screenPosition.y / scale))) return;
            if (primaryPressed) ZoomIn();
            else if (secondaryPressed) ZoomOut();
        }

        public bool TryNavigate(Vector3 screenPosition, Camera camera, Vector3 focus, out TilePos target)
        {
            target = default;
            if (!_visibility.ShowNormalHud || !_visibility.AllowOrdinaryInput || camera == null || Screen.height <= 0) return false;
            float scale = Screen.height / 768f;
            var point = new System.Numerics.Vector2(screenPosition.x / scale, 768 - screenPosition.y / scale);
            var heading = new Dk2Angle(unchecked((ushort)Mathf.RoundToInt(camera.transform.eulerAngles.y * (2048f / 360))));
            return MinimapNavigation.TryResolve(point, new MinimapNavigationView(
                _grid.Width, _grid.Height, _zoom, FixedFocus(focus), heading), out target);
        }

        private static FpPosition3 FixedFocus(Vector3 focus) => new FpPosition3(
            new Fp32((int)(focus.x * Fp32.One)), new Fp32((int)(-focus.z * Fp32.One)), new Fp32(0));

        public DungeonMinimap(Transform parent, IMinimapAssets assets, int width, int height)
        {
            _backdrop = Decode(assets.BackdropPng); _backdrop.wrapMode = TextureWrapMode.Repeat;
            Texture2D source = Decode(assets.PalettePng);
            try
            {
                if (source.width < 37) throw new InvalidOperationException("Native minimap palette is too short.");
                var pixels = new uint[37];
                for (int i = 0; i < pixels.Length; i++)
                {
                    Color32 colour = source.GetPixel(i, source.height - 1);
                    pixels[i] = (uint)(colour.r << 16 | colour.g << 8 | colour.b);
                }
                _palette = new MinimapPalette(pixels);
            }
            finally { UnityEngine.Object.Destroy(source); }
            _grid = new MinimapClassGrid(width, height);
            _gpu = new MinimapGpuMap(width, height, _palette, _backdrop);
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("DK2Hud");
            _settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            _settings.referenceResolution = new Vector2Int(1024, 768);
            _settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight; _settings.match = 1;
            _root = new GameObject("Dungeon HUD"); _root.transform.SetParent(parent, false);
            var document = _root.AddComponent<UIDocument>(); document.panelSettings = _settings;
            _hudRoot = document.rootVisualElement;
            document.rootVisualElement.style.width = Length.Percent(100);
            document.rootVisualElement.style.height = Length.Percent(100);
            document.rootVisualElement.pickingMode = PickingMode.Ignore;
            _image = new Image { pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.StretchToFill };
            _image.style.position = Position.Absolute;
            _image.style.left = Left; _image.style.bottom = Bottom;
            _image.style.width = Diameter; _image.style.height = Diameter;
            document.rootVisualElement.Add(_image);
            _frame = new MinimapFrameVisual(document.rootVisualElement, assets);
            _footprint = new MinimapFootprintVisual(assets.CameraFootprint, width, height);
            document.rootVisualElement.Add(_footprint);
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
            if (_capturedWorld == world && _capturedTick == world.CurrentTick.Value && _capturedPreview == view.PreviewVersion) return;
            _capturedWorld = world; _capturedTick = world.CurrentTick.Value; _capturedPreview = view.PreviewVersion;
            for (int y = 0; y < world.Height; y++)
                for (int x = 0; x < world.Width; x++)
                {
                    var at = new DK2.Core.TilePos(x, y);
                    _grid.Set(at, view.MinimapColourAt(world, navigation, at));
                }
            _gpu.UploadChanges(_grid);
            if (_palette.AdvanceNeutralCycle(world.CurrentTick, PlayerIds.Neutral)) _gpu.UpdatePalette(_palette);
            view.CaptureMinimapMarkers(world, _palette, _markers);
            _gpu.UploadMarkers(_markers);
        }

        public void Draw(Camera camera, Vector3? focus = null)
        {
            if (!_visibility.ShowNormalHud || camera == null) return;
            Vector3 centre = focus ?? camera.transform.position;
            if (!focus.HasValue)
            {
                var floor = new Plane(Vector3.up, new Vector3(0, 1, 0));
                Ray ray = camera.ViewportPointToRay(new Vector3(.5f, .5f, 0));
                if (floor.Raycast(ray, out float distance)) centre = ray.GetPoint(distance);
            }
            _gpu.SetView(_zoom, FixedFocus(centre));
            _gpu.Render(camera.transform.eulerAngles.y, Mathf.CeilToInt(Diameter * Screen.height / 768f));
            if (_image.image != _gpu.Disc) _image.image = _gpu.Disc;
            _footprint.UpdateCamera(_zoom.IsWholeMap ? camera : null);
        }

        public bool ContainsPointer(Vector3 position)
        {
            return _visibility.ShowNormalHud && _visibility.AllowOrdinaryInput && MinimapFrameVisual.ContainsPointer(position);
        }

        public void SetVisibility(HudVisibility visibility)
        {
            if (_visibility.ShowNormalHud != visibility.ShowNormalHud)
                _hudRoot.style.display = visibility.ShowNormalHud ? DisplayStyle.Flex : DisplayStyle.None;
            _visibility = visibility;
        }

        public void Dispose()
        {
            _gpu.Dispose();
            _frame.Dispose();
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

        public MinimapFootprintVisual(RgbaImage texture, int mapWidth, int mapHeight)
        {
            _texture = MinimapFrameVisual.Decode(texture);
            _mapWidth = mapWidth; _mapHeight = mapHeight;
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.left = 0; style.top = 0; style.right = 0; style.bottom = 0;
            generateVisualContent += DrawFootprint;
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
            if (changed) MarkDirtyRepaint();
        }

        private void DrawFootprint(MeshGenerationContext context)
        {
            if (!_valid) return;
            MeshWriteData mesh = context.Allocate(8, Indices.Length, _texture);
            for (int i = 0; i < 8; i++)
                mesh.SetNextVertex(new Vertex { position = new Vector3(_vertices[i].X, _vertices[i].Y, Vertex.nearZ),
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
