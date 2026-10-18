// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DK2.Core;
using DK2.Data;
using DK2.Presentation;
using DK2.Simulation;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace DK2.Unity.HudProbe
{
    public sealed class HudToolkitPilot : MonoBehaviour
    {
        [Serializable] public sealed class Report
        {
            public bool Passed;
            public bool GpuClassUpdates;
            public int LiveLevel1KnownTiles;
            public string Unity, Gpu, Pipeline, Error;
            public int PaletteWidth, PaletteHeight, ReferenceGlyphs, LargestGlyphs;
            public string[] Captures;
        }
        private readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        private readonly List<string> _captures = new List<string>();
        private PanelSettings _settings;
        private UIDocument _document;
        private RenderTexture _panel;
        private int _frames, _view;
        private string _output;
        private Report _report;
        private MinimapGpuMap _liveGpu;
        private MinimapFrameVisual _frame;
        private MinimapFootprintVisual _footprint;
        private MinimapFootprintVisual _wideFootprint;
        private MinimapNorthVisual _north;
        public bool Done { get; private set; }
        public bool Passed => _report != null && _report.Passed;

        private T Own<T>(T value) where T : UnityEngine.Object { _owned.Add(value); return value; }

        private void Start()
        {
            try
            {
                _output = Environment.GetEnvironmentVariable("DK2_HUD_PROBE_OUTPUT")
                    ?? throw new InvalidOperationException("Probe output required.");
                Directory.CreateDirectory(_output);
                var assets = new InstallHudPilotAssets(Environment.GetEnvironmentVariable("DK2_DIR"));
                var palette = Load(assets.PalettePng); var backdrop = Load(assets.BackdropPng);
                _report = new Report { Unity = Application.unityVersion, Gpu = SystemInfo.graphicsDeviceName,
                    Pipeline = GraphicsSettings.currentRenderPipeline?.GetType().Name ?? "built-in",
                    PaletteWidth = palette.width, PaletteHeight = palette.height,
                    ReferenceGlyphs = assets.ReferenceFont.Glyphs.Count, LargestGlyphs = assets.LargestFont.Glyphs.Count };
                var frameAssets = new InstallMinimapAssets(Environment.GetEnvironmentVariable("DK2_DIR"));
                VerifyMinimapNavigation(frameAssets);
                var frame = Own(MinimapFrameVisual.Decode(frameAssets.Frame));
                File.WriteAllBytes(Path.Combine(_output, "minimap-native-frame.png"), frame.EncodeToPNG());
                VerifyGpuMap(backdrop);
                _report.GpuClassUpdates = true;
                _settings = Own(ScriptableObject.CreateInstance<PanelSettings>());
                _settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("HudPilot")
                    ?? throw new InvalidOperationException("Pilot runtime theme required.");
                _settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                _settings.referenceResolution = new Vector2Int(1024, 768);
                _settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight; _settings.match = 1;
                _document = gameObject.AddComponent<UIDocument>(); _document.panelSettings = _settings;
                VisualElement root = _document.rootVisualElement;
                root.style.width = Length.Percent(100); root.style.height = Length.Percent(100);
                root.style.backgroundColor = new Color(.035f, .045f, .055f, 1);
                var classes = Own(new Texture2D(32, 32, TextureFormat.RGBA32, false, true));
                var pixels = new Color32[1024];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32((byte)(i / 32 < 10 ? 1 : (i / 32 + i % 32) % 44), 0, 0, 255);
                classes.SetPixels32(pixels); classes.Apply(); classes.filterMode = FilterMode.Point;
                classes.wrapMode = TextureWrapMode.Clamp;
                var minimap = NewMaterial(); minimap.SetTexture("_Classes", classes);
                minimap.SetTexture("_Palette", ClassPalette(palette)); minimap.SetTexture("_Backdrop", backdrop);
                minimap.SetFloat("_Angle", .48f);
                RenderTexture disc = Render(minimap, 512, 512);
                AssertDisc(disc);
                AddImage(root, disc, left: 260, top: 420, width: 224, height: 224);
                AddLiveLevel(root, palette, backdrop);
                _frame = new MinimapFrameVisual(root, frameAssets);
                _footprint = new MinimapFootprintVisual(frameAssets.CameraFootprint, 128, 128);
                root.Add(_footprint);
                var footprintCamera = Own(new GameObject("Footprint probe camera"));
                var camera = footprintCamera.AddComponent<Camera>();
                camera.enabled = false; camera.orthographic = true; camera.orthographicSize = 8; camera.aspect = 1;
                camera.transform.SetPositionAndRotation(new Vector3(64, 33, -64), Quaternion.Euler(90, 0, 0));
                _footprint.UpdateCamera(camera);
                var wideRoot = new VisualElement { pickingMode = PickingMode.Ignore };
                wideRoot.style.position = Position.Absolute;
                wideRoot.style.left = 500; wideRoot.style.top = -100;
                wideRoot.style.right = -500; wideRoot.style.bottom = 100;
                root.Add(wideRoot);
                _wideFootprint = new MinimapFootprintVisual(frameAssets.CameraFootprint, 128, 128);
                wideRoot.Add(_wideFootprint);
                camera.orthographicSize = 40; camera.aspect = 2;
                _wideFootprint.UpdateCamera(camera);
                _north = new MinimapNorthVisual(frameAssets.North);
                root.Add(_north);
                const string text = "Dungeon Heart 100%";
                var reference = Own(HudFontPilot.Raster(assets.ReferenceFont, text));
                var largest = Own(HudFontPilot.Raster(assets.LargestFont, text));
                var field = Own(HudFontPilot.DistanceField(largest));
                AddFont(root, reference, false, scale: 1, left: 48, top: 96);
                AddFont(root, largest, false, scale: 14f / 72, left: 48, top: 160);
                AddFont(root, field, true, scale: 14f / 72, left: 48, top: 224);
                AddFont(root, reference, false, scale: 2, left: 48, top: 300);
                AddFont(root, field, true, scale: 28f / 72, left: 48, top: 360);
                ConfigureView();
            }
            catch (Exception error) { Fail(error); }
        }

        private Texture2D Load(byte[] bytes)
        {
            var texture = Own(new Texture2D(2, 2, TextureFormat.RGBA32, false));
            if (!texture.LoadImage(bytes)) throw new InvalidOperationException("Install image did not decode.");
            texture.filterMode = FilterMode.Point; texture.wrapMode = TextureWrapMode.Clamp; return texture;
        }
        private void VerifyMinimapNavigation(IMinimapAssets assets)
        {
            var cameraObject = Own(new GameObject("Minimap input probe camera"));
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            float scale = Screen.height / 768f;
            if (scale <= 0) throw new InvalidOperationException("Navigation probe needs a screen extent.");
            Vector3 point = new Vector3(113.7f * scale, 96 * scale, 0);
            Vector3 magnifier = new Vector3(170 * scale, 23 * scale, 0);
            Vector3 focus = new Vector3(10.25f, 1, -20.75f);
            using (var minimap = new DungeonMinimap(transform, assets, 100, 80))
            {
                if (!minimap.TryNavigate(point, camera, focus, out TilePos target) || target != new TilePos(61, 40))
                    throw new InvalidOperationException("Whole-map navigation adapter changed its projection.");
                minimap.HandleZoomPress(magnifier, primaryPressed: true, secondaryPressed: true);
                if (!minimap.TryNavigate(point, camera, focus, out target) || target != new TilePos(20, 20))
                    throw new InvalidOperationException("Magnifier primary priority or zoom navigation changed.");
                minimap.HandleZoomPress(magnifier, primaryPressed: true, secondaryPressed: false);
                if (!minimap.TryNavigate(point, camera, focus, out target) || target != new TilePos(15, 20))
                    throw new InvalidOperationException("Zoomed navigation adapter lost fractional camera focus.");
                minimap.SetVisibility(HudVisibilityRules.ForCamera(new CameraModeId(3), 1));
                if (minimap.ContainsPointer(magnifier) || minimap.TryNavigate(point, camera, focus, out target))
                    throw new InvalidOperationException("A hidden HUD still accepted pointer input.");
                minimap.HandleZoomPress(magnifier, primaryPressed: true, secondaryPressed: false);
                minimap.SetVisibility(HudVisibilityRules.ForCamera(new CameraModeId(11), 1));
                if (minimap.TryNavigate(point, camera, focus, out target))
                    throw new InvalidOperationException("A visible but input-blocked HUD accepted navigation.");
                minimap.HandleZoomPress(magnifier, primaryPressed: true, secondaryPressed: false);
                minimap.SetVisibility(HudVisibilityRules.ForCamera(new CameraModeId(3), 0));
                if (!minimap.ContainsPointer(magnifier)
                    || !minimap.TryNavigate(point, camera, focus, out target) || target != new TilePos(15, 20))
                    throw new InvalidOperationException("HUD restoration lost its zoom or input state.");
                if (minimap.TryNavigate(magnifier, camera, focus, out target))
                    throw new InvalidOperationException("A magnifier click also navigated the map.");
            }
        }
        private void VerifyGpuMap(Texture backdrop)
        {
            var grid = new MinimapClassGrid(width: 3, height: 2);
            var palette = new MinimapPalette(new uint[37]);
            using (var map = new MinimapGpuMap(width: 3, height: 2, palette, backdrop))
            {
                grid.Set(new DK2.Core.TilePos(1, 0), new MinimapColourClass(300));
                grid.Set(new DK2.Core.TilePos(2, 1), new MinimapColourClass(548));
                map.UploadChanges(grid);
                if (map.LastUploadedTiles != 6) throw new InvalidOperationException("Initial minimap upload incomplete.");
                AssertClass(map.Classes, x: 1, y: 0, expected: 300);
                AssertClass(map.Classes, x: 2, y: 1, expected: 548);
                AssertClass(map.Classes, x: 0, y: 0, expected: 1);
                grid.Set(new DK2.Core.TilePos(1, 0), MinimapColourClass.Tagged);
                map.UploadChanges(grid);
                if (map.LastUploadedTiles != 1) throw new InvalidOperationException("Minimap update uploaded unchanged tiles.");
                AssertClass(map.Classes, x: 1, y: 0, expected: 13);
                AssertClass(map.Classes, x: 2, y: 1, expected: 548);
                map.UploadChanges(grid);
                if (map.LastUploadedTiles != 0) throw new InvalidOperationException("Idle minimap uploaded cells.");
                map.Render(headingDegrees: 37, diameter: 128); AssertDisc(map.Disc);
                var source = new uint[37]; source[1] = 0xFF0000; source[2] = 0x00FF00;
                map.UpdatePalette(new MinimapPalette(source));
                for (int x = 0; x < 3; x++)
                {
                    grid.Set(new TilePos(x, 0), new MinimapColourClass(2));
                    grid.Set(new TilePos(x, 1), new MinimapColourClass(3));
                }
                map.UploadChanges(grid); map.Render(headingDegrees: 0, diameter: 128);
                Texture2D rows = Read(map.Disc);
                try
                {
                    Color32 north = rows.GetPixel(64, 80), south = rows.GetPixel(64, 48);
                    if (north.r < 250 || south.g < 250)
                        throw new InvalidOperationException("Minimap display north/south reversed: " + north + ", " + south);
                }
                finally { Destroy(rows); }
                var marker = new MinimapMarker(MinimapMarkerKind.Creature,
                    new(new(0x1800), new(0x800), new(0)), 0xFF0000FF);
                map.UploadMarkers(new[] { marker });
                map.Render(headingDegrees: 0, diameter: 128);
                AssertBlueMarker(map.Disc, x: 50, y: 107);
                map.Render(headingDegrees: 90, diameter: 128);
                AssertBlueMarker(map.Disc, x: 20, y: 50);
                map.UploadMarkers(new[]
                {
                    new MinimapMarker(MinimapMarkerKind.HorizontalDoor, new(new(0x1800), new(0x1800), new(0)), 0xFF0000FF),
                    new MinimapMarker(MinimapMarkerKind.VerticalDoor, new(new(0x800), new(0x1800), new(0)), 0xFF0000FF),
                });
                map.Render(headingDegrees: 0, diameter: 128);
                AssertBlueMarker(map.Disc, x: 60, y: 77);
                AssertBlueMarker(map.Disc, x: 20, y: 70);
                map.UploadMarkers(new[] { new MinimapMarker(MinimapMarkerKind.Ring, marker.Position, 0xFF0000FF, 8) });
                map.Render(headingDegrees: 0, diameter: 128);
                AssertBlueMarker(map.Disc, x: 58, y: 107);
                Texture2D hollow = Read(map.Disc);
                try
                {
                    if (hollow.GetPixel(50, 107).b > .01f)
                        throw new InvalidOperationException("Minimap reporting outline filled its interior.");
                }
                finally { Destroy(hollow); }
                foreach (int radius in new[] { 0, 1 })
                {
                    map.UploadMarkers(new[] { new MinimapMarker(MinimapMarkerKind.Ring, marker.Position, 0xFF0000FF, radius) });
                    map.Render(headingDegrees: 0, diameter: 128);
                    AssertBlueMarker(map.Disc, x: 50, y: 107);
                }
                map.UploadMarkers(new[] { new MinimapMarker(MinimapMarkerKind.Ring, marker.Position, 0xFF9696FF, 0) });
                map.Render(headingDegrees: 0, diameter: 128);
                Texture2D reportingColour = Read(map.Disc);
                try
                {
                    Color32 colour = reportingColour.GetPixel(50, 107);
                    if (Math.Abs(colour.r - 150) > 1 || Math.Abs(colour.g - 150) > 1 || colour.b < 254)
                        throw new InvalidOperationException("Reporting ring colour changed across the GPU colour space: " + colour);
                }
                finally { Destroy(reportingColour); }
                map.UploadMarkers(new[] { new MinimapMarker(MinimapMarkerKind.Rectangle, marker.Position, 0xFF0000FF,
                    EndPosition: new(new(0x2800), new(0x1800), new(0))) });
                map.Render(headingDegrees: 0, diameter: 128);
                AssertBlueMarker(map.Disc, x: 65, y: 107);
                AssertBlueMarker(map.Disc, x: 65, y: 78);
                AssertBlueMarker(map.Disc, x: 50, y: 92);
                AssertBlueMarker(map.Disc, x: 79, y: 92);
                Texture2D rectangle = Read(map.Disc);
                try
                {
                    if (rectangle.GetPixel(65, 92).b > .01f)
                        throw new InvalidOperationException("Reporting rectangle filled its interior.");
                }
                finally { Destroy(rectangle); }
                map.Render(headingDegrees: 90, diameter: 128);
                AssertBlueMarker(map.Disc, x: 20, y: 65);
                AssertBlueMarker(map.Disc, x: 49, y: 65);
                AssertBlueMarker(map.Disc, x: 35, y: 50);
                AssertBlueMarker(map.Disc, x: 35, y: 79);
                map.UploadMarkers(Array.Empty<MinimapMarker>());
                map.Render(headingDegrees: 0, diameter: 128);
                Texture2D cleared = Read(map.Disc);
                try
                {
                    if (cleared.GetPixel(50, 107).b > .01f)
                        throw new InvalidOperationException("Removed minimap marker left stale pixels.");
                }
                finally { Destroy(cleared); }
                map.UploadMarkers(new[] { marker });
                map.SetView(new MinimapZoom(1), new(new(0x1400), new(0x800), new(0)));
                map.Render(headingDegrees: 0, diameter: 128);
                AssertBlueMarker(map.Disc, x: 65, y: 63);
                Texture2D zoomed = Read(map.Disc);
                try
                {
                    if (zoomed.GetPixel(62, 63).r < .98f || zoomed.GetPixel(62, 62).g < .98f)
                        throw new InvalidOperationException("Zoomed terrain lost its fractional raster anchor.");
                }
                finally { Destroy(zoomed); }
                map.SetView(new MinimapZoom(4), new(new(0x1400), new(0x800), new(0)));
                map.Render(headingDegrees: 0, diameter: 128);
                AssertBlueMarker(map.Disc, x: 68, y: 63);
                map.UploadMarkers(new[] { new MinimapMarker(MinimapMarkerKind.HorizontalDoor, marker.Position, 0xFF0000FF) });
                map.Render(headingDegrees: 0, diameter: 128);
                AssertBlueMarker(map.Disc, x: 61, y: 63);
                map.UploadMarkers(new[] { new MinimapMarker(MinimapMarkerKind.VerticalDoor,
                    new(new(0x1800), new(0x1800), new(0)), 0xFF0000FF) });
                map.Render(headingDegrees: 0, diameter: 128);
                AssertBlueMarker(map.Disc, x: 68, y: 55);
                map.UploadMarkers(new[] { new MinimapMarker(MinimapMarkerKind.Rectangle, marker.Position, 0xFF0000FF,
                    EndPosition: new(new(0x2800), new(0x1800), new(0))) });
                map.Render(headingDegrees: 0, diameter: 128);
                AssertBlueMarker(map.Disc, x: 76, y: 63);
                map.UploadMarkers(Array.Empty<MinimapMarker>());
                map.SetView(new MinimapZoom(0), new(new(20 << 12), new(20 << 12), new(0)));
                map.SetHeartTrail(new TilePos(40, 20), new GameTick(0));
                map.Render(headingDegrees: 0, diameter: 128);
                AssertTrailPixel(map.Disc, 84, 63, true);
                AssertTrailPixel(map.Disc, 85, 63, false);
                map.SetHeartTrail(new TilePos(40, 20), new GameTick(1));
                map.Render(headingDegrees: 0, diameter: 128);
                AssertTrailPixel(map.Disc, 85, 63, true);
                AssertTrailPixel(map.Disc, 84, 63, false);
                map.Render(headingDegrees: 90, diameter: 128);
                AssertTrailPixel(map.Disc, 64, 85, true);
                map.SetView(new MinimapZoom(0), new(new(21 << 12), new(20 << 12), new(0)));
                map.Render(headingDegrees: 0, diameter: 128);
                AssertTrailPixel(map.Disc, 84, 63, true);
                AssertTrailPixel(map.Disc, 85, 63, false);
                map.SetView(MinimapZoom.WholeMap, default);
                map.Render(headingDegrees: 0, diameter: 128);
                AssertTrailPixel(map.Disc, 84, 63, false);
                map.SetHeartTrail(null, new GameTick(1));
                map.SetView(new MinimapZoom(1), new(new(0x1400), new(0x800), new(0)));
                MinimapMarker reporting = MinimapMarkerRules.ReportingRing(marker.Position, 2, MinimapMapBounds.Fit(3, 2), 3);
                map.UploadMarkers(new[] { new MinimapMarker(reporting.Kind, reporting.Position, 0xFF0000FF,
                    reporting.RadiusRasterPixels, ReportingDistanceTiles: reporting.ReportingDistanceTiles) });
                map.Render(headingDegrees: 0, diameter: 128);
                AssertBlueMarker(map.Disc, x: 69, y: 63);
                map.UploadMarkers(new[] { marker });
                map.SetView(new MinimapZoom(1), new(new(0x6800), new(0x800), new(0)));
                map.Render(headingDegrees: 0, diameter: 128);
                AssertBlueMarker(map.Disc, x: 54, y: 63);
                map.SetView(MinimapZoom.WholeMap, default);
                map.Render(headingDegrees: 0, diameter: 128);
                AssertBlueMarker(map.Disc, x: 50, y: 107);
            }
        }

        private static void AssertBlueMarker(RenderTexture target, int x, int y)
        {
            Texture2D pixels = Read(target);
            try
            {
                Color32 colour = pixels.GetPixel(x, y);
                for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                        if (pixels.GetPixel(x + dx, y + dy).a < .999f)
                            throw new InvalidOperationException("Marker antialiasing changed the opaque disc alpha.");
                if (colour.b < 250 || colour.r > 5 || colour.g > 5)
                {
                    int left = target.width, bottom = target.height, right = -1, top = -1;
                    Color32[] all = pixels.GetPixels32();
                    for (int i = 0; i < all.Length; i++)
                        if (all[i].b > 240 && all[i].r < 15 && all[i].g < 15)
                        {
                            left = Math.Min(left, i % target.width); right = Math.Max(right, i % target.width);
                            bottom = Math.Min(bottom, i / target.width); top = Math.Max(top, i / target.width);
                        }
                    throw new InvalidOperationException("Minimap marker position/colour mismatch at " + x + "," + y + ": " + colour
                        + "; blue bounds " + left + "," + bottom + ".." + right + "," + top);
                }
            }
            finally { Destroy(pixels); }
        }

        private static void AssertClass(RenderTexture target, int x, int y, int expected)
        {
            Texture2D read = Read(target);
            try
            {
                Color32 pixel = read.GetPixel(x, y);
                int actual = pixel.r | pixel.g << 8;
                if (actual != expected) throw new InvalidOperationException("GPU minimap class mismatch: " + actual + " != " + expected
                    + "; rows: " + string.Join(",", read.GetPixels32().Select(p => p.r | p.g << 8)));
            }
            finally { Destroy(read); }
        }

        private Material NewMaterial()
        {
            Shader shader = Shader.Find("Hidden/DK2/HudPilot");
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("Pilot shader unavailable.");
            return Own(new Material(shader));
        }
        private void AddLiveLevel(VisualElement root, Texture2D source, Texture backdrop)
        {
            string install = Environment.GetEnvironmentVariable("DK2_DIR");
            var database = GameDatabaseLoader.LoadForCampaignLevel(install, "Level1");
            var simulation = CampaignSimulationFactory.Create(database,
                GameDatabaseLoader.LoadCampaignMap(install, "Level1"),
                GameDatabaseLoader.LoadCampaignLevel(install, "Level1"),
                GameDatabaseLoader.LoadCampaignPlayers(install, "Level1"),
                GameDatabaseLoader.LoadCampaignThings(install, "Level1"),
                GameDatabaseLoader.LoadCampaignTriggers(install, "Level1"), seed: 1, fogOfWarEnabled: true);
            simulation.Tick();
            var world = simulation.World;
            var view = new WorldView(database, new DK2.Data.Model.PlayerId(3));
            var grid = new MinimapClassGrid(world.Width, world.Height);
            for (int y = 0; y < world.Height; y++)
                for (int x = 0; x < world.Width; x++)
                {
                    var at = new TilePos(x, y);
                    MinimapColourClass colour = view.MinimapColourAt(world, simulation.Navigation, at);
                    grid.Set(at, colour);
                    if (colour != MinimapColourClass.Unknown) _report.LiveLevel1KnownTiles++;
                }
            if (_report.LiveLevel1KnownTiles == 0) throw new InvalidOperationException("Level1 minimap contains no known terrain.");
            MinimapPalette palette = PaletteModel(source);
            _liveGpu = new MinimapGpuMap(world.Width, world.Height, palette, backdrop);
            var markers = new System.Collections.Generic.List<MinimapMarker>();
            view.CaptureMinimapMarkers(world, palette, markers);
            _liveGpu.UploadMarkers(markers);
            _liveGpu.UploadChanges(grid); _liveGpu.Render(headingDegrees: 45, diameter: 512);
            AssertDisc(_liveGpu.Disc);
            AddImage(root, _liveGpu.Disc, left: 12.8f, top: 587.2f, width: 169.6f, height: 169.6f);
        }

        private static MinimapPalette PaletteModel(Texture2D source)
        {
            if (source.width < 37) throw new InvalidOperationException("Native palette needs 37 source pixels.");
            var pixels = new uint[37];
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 colour = source.GetPixel(i, source.height - 1);
                pixels[i] = (uint)(colour.r << 16 | colour.g << 8 | colour.b);
            }
            return new MinimapPalette(pixels);
        }

        private Texture2D ClassPalette(Texture2D source)
        {
            var model = PaletteModel(source);
            var colours = new Color32[model.Colours.Length];
            for (int i = 0; i < colours.Length; i++)
            {
                uint argb = model.Colours[i];
                colours[i] = new Color32((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, (byte)(argb >> 24));
            }
            var palette = Own(new Texture2D(colours.Length, 1, TextureFormat.RGBA32, false));
            palette.SetPixels32(colours); palette.Apply(); palette.filterMode = FilterMode.Point;
            palette.wrapMode = TextureWrapMode.Clamp; return palette;
        }
        private RenderTexture Render(Material material, int width, int height)
        {
            var target = Own(new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB));
            target.Create();
            using (var commands = new CommandBuffer())
            {
                commands.SetRenderTarget(target); commands.ClearRenderTarget(false, true, Color.clear);
                commands.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3);
                Graphics.ExecuteCommandBuffer(commands);
            }
            return target;
        }
        private static void AddImage(VisualElement root, Texture texture, float left, float top, float width, float height)
        {
            var image = new Image { image = texture, scaleMode = ScaleMode.StretchToFill };
            image.style.position = Position.Absolute; image.style.left = left; image.style.top = top;
            image.style.width = width; image.style.height = height; root.Add(image);
        }
        private void AddFont(VisualElement root, Texture2D texture, bool distance, float scale, float left, float top)
        {
            var material = NewMaterial(); material.SetFloat("_Font", 1);
            material.SetFloat("_DistanceField", distance ? 1 : 0); material.SetTexture("_Glyph", texture);
            int width = Mathf.CeilToInt(texture.width * scale * 4), height = Mathf.CeilToInt(texture.height * scale * 4);
            AddImage(root, Render(material, width, height), left: left, top: top, width: width / 4f, height: height / 4f);
        }
        private void ConfigureView()
        {
            int width = _view == 0 ? 1024 : 2560, height = _view == 0 ? 768 : 1440;
            _panel = Own(new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)); _panel.Create();
            _north.UpdateHeading(new Dk2Angle((ushort)(_view == 0 ? 0 : 256)), height);
            _settings.targetTexture = _panel; _frames = 0;
        }
        private void Update()
        {
            if (Done || _report == null || ++_frames < 24) return;
            try
            {
                Texture2D image = Read(_panel);
                try
                {
                    Color32[] pixels = image.GetPixels32();
                    if (pixels.Count(p => p.r > 45 || p.g > 45 || p.b > 45) < pixels.Length / 200)
                        throw new InvalidOperationException("UI Toolkit target has no rendered image content.");
                    AssertComposition(image);
                    AssertFootprintClip(image);
                    AssertNorth(image, new Dk2Angle((ushort)(_view == 0 ? 0 : 256)));
                    int white = 0;
                    float scale = _panel.height / 768f;
                    for (int y = Mathf.FloorToInt(661 * scale); y < 684 * scale; y++)
                        for (int x = Mathf.FloorToInt(84 * scale); x < 108 * scale; x++)
                        {
                            Color32 pixel = image.GetPixel(x, image.height - 1 - y);
                            if (pixel.r > 180 && pixel.g > 180 && pixel.b > 180) white++;
                        }
                    if (white < 8 * scale) throw new InvalidOperationException("Native camera-footprint ribbon did not render.");
                    string path = Path.Combine(_output, "hud-pilot-" + _panel.width + "x" + _panel.height + ".png");
                    File.WriteAllBytes(path, image.EncodeToPNG()); _captures.Add(Path.GetFileName(path));
                }
                finally { Destroy(image); }
                if (_view++ == 0) ConfigureView();
                else { _report.Passed = true; Finish(); }
            }
            catch (Exception error) { Fail(error); }
        }
        private static Texture2D Read(RenderTexture target)
        {
            RenderTexture old = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply(); return image;
            }
            finally { RenderTexture.active = old; }
        }
        private static void AssertDisc(RenderTexture target)
        {
            Texture2D image = Read(target);
            try
            {
                if (image.GetPixel(0, 0).a > .01 || image.GetPixel(target.width / 2, target.height / 2).a < .99)
                    throw new InvalidOperationException("Minimap shader alpha mask failed.");
            }
            finally { Destroy(image); }
        }
        private static void AssertFootprintClip(Texture2D image)
        {
            float scale = image.height / 768f;
            int inside = 0, outside = 0;
            for (int y = Mathf.CeilToInt(480 * scale); y < 665 * scale; y++)
                for (int x = Mathf.CeilToInt(505 * scale); x < 690 * scale; x++)
                {
                    Color32 pixel = image.GetPixel(x, image.height - 1 - y);
                    if (pixel.r < 180 || pixel.g < 180 || pixel.b < 180) continue;
                    float dx = (x + .5f) / scale - 597.6f;
                    float dy = (y + .5f) / scale - 572;
                    if (dx * dx + dy * dy > 86 * 86) outside++;
                    else inside++;
                }
            if (inside < 20 || outside != 0)
                throw new InvalidOperationException("Zoomed-out footprint clip failed: inside=" + inside + ", outside=" + outside);
        }
        private static void AssertNorth(Texture2D image, Dk2Angle heading)
        {
            System.Numerics.Vector2 origin = MinimapNorth.SpriteOriginPixels(heading, image.height);
            float scale = image.height / 768f;
            int white = 0;
            for (int y = (int)origin.Y; y < origin.Y + 8 * scale; y++)
                for (int x = (int)origin.X; x < origin.X + 8 * scale; x++)
                {
                    Color32 pixel = image.GetPixel(x, image.height - 1 - y);
                    if (pixel.r > 180 && pixel.g > 180 && pixel.b > 180) white++;
                }
            if (white < 3) throw new InvalidOperationException("North marker did not render at the heading-dependent position.");
        }
        private static void AssertTrailPixel(RenderTexture target, int x, int y, bool white)
        {
            Texture2D image = Read(target);
            try
            {
                Color32 pixel = image.GetPixel(x, y);
                bool actual = pixel.r > 240 && pixel.g > 240 && pixel.b > 240;
                if (actual != white) throw new InvalidOperationException("Heart trail phase/position mismatch at " + x + "," + y);
            }
            finally { Destroy(image); }
        }
        private static void AssertComposition(Texture2D image)
        {
            float scale = image.height / 768f;
            Color background = image.GetPixel(0, 0);
            Color corner = image.GetPixel(Mathf.RoundToInt(28 * scale), image.height - Mathf.RoundToInt(424 * scale));
            if (Mathf.Abs(corner.r - background.r) + Mathf.Abs(corner.g - background.g) + Mathf.Abs(corner.b - background.b) > .03f)
                throw new InvalidOperationException("UI Image did not preserve the disc's transparent corners.");
            foreach (int top in new[] { 96, 160, 224, 300, 360 })
            {
                int visible = 0;
                for (int y = Mathf.RoundToInt(top * scale); y < Mathf.RoundToInt((top + 48) * scale); y++)
                    for (int x = Mathf.RoundToInt(48 * scale); x < Mathf.RoundToInt(400 * scale); x++)
                    {
                        Color sample = image.GetPixel(x, image.height - 1 - y);
                        if (sample.r > .25f && sample.g > .15f) visible++;
                    }
                if (visible < 30) throw new InvalidOperationException("Font comparison row is missing at reference Y=" + top);
            }
        }
        private void Fail(Exception error) { Debug.LogException(error); _report ??= new Report(); _report.Error = error.ToString(); Finish(); }
        private void Finish()
        {
            _report.Pipeline = GraphicsSettings.currentRenderPipeline?.GetType().Name ?? "uninitialized";
            if (_report.Passed && _report.Pipeline == "uninitialized")
            {
                _report.Passed = false; _report.Error = "Expected the copied production URP configuration to initialize.";
            }
            _report.Captures = _captures.ToArray();
            if (_output != null) File.WriteAllText(Path.Combine(_output, "result.json"), JsonUtility.ToJson(_report, true));
            Done = true;
        }
        private void OnDestroy()
        {
            _liveGpu?.Dispose();
            _frame?.Dispose();
            _footprint?.Dispose();
            _wideFootprint?.Dispose();
            _north?.Dispose();
            foreach (UnityEngine.Object resource in _owned) if (resource != null) Destroy(resource);
        }
    }
}
