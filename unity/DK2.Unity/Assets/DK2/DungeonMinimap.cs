// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.IO;
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
        RgbaImage Corner(MinimapCorner corner, bool active);
    }

    public sealed class InstallMinimapAssets : IMinimapAssets
    {
        public byte[] PalettePng { get; }
        public byte[] BackdropPng { get; }
        public RgbaImage Frame { get; }
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
        private readonly Image _image;
        private readonly Texture2D _backdrop;
        private readonly MinimapPalette _palette;
        private readonly MinimapClassGrid _grid;
        private readonly MinimapGpuMap _gpu;
        private readonly MinimapFrameVisual _frame;
        private WorldState _capturedWorld;
        private long _capturedTick = -1;
        private int _capturedPreview = -1;

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
            document.rootVisualElement.style.width = Length.Percent(100);
            document.rootVisualElement.style.height = Length.Percent(100);
            document.rootVisualElement.pickingMode = PickingMode.Ignore;
            _image = new Image { pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.StretchToFill };
            _image.style.position = Position.Absolute;
            _image.style.left = Left; _image.style.bottom = Bottom;
            _image.style.width = Diameter; _image.style.height = Diameter;
            document.rootVisualElement.Add(_image);
            _frame = new MinimapFrameVisual(document.rootVisualElement, assets);
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
        }

        public void Draw(Camera camera)
        {
            if (camera == null) return;
            _gpu.Render(camera.transform.eulerAngles.y, Mathf.CeilToInt(Diameter * Screen.height / 768f));
            if (_image.image != _gpu.Disc) _image.image = _gpu.Disc;
        }

        public bool ContainsPointer(Vector3 position)
        {
            return MinimapFrameVisual.ContainsPointer(position);
        }

        public void Dispose()
        {
            _gpu.Dispose();
            _frame.Dispose();
            UnityEngine.Object.Destroy(_root); UnityEngine.Object.Destroy(_settings);
            UnityEngine.Object.Destroy(_backdrop);
        }
    }
}
