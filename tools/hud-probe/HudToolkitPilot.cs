// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
                AddImage(root, disc, left: 24, top: 420, width: 224, height: 224);
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
        private Material NewMaterial()
        {
            Shader shader = Shader.Find("Hidden/DK2/HudPilot");
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("Pilot shader unavailable.");
            return Own(new Material(shader));
        }
        private Texture2D ClassPalette(Texture2D source)
        {
            if (source.width < 37) throw new InvalidOperationException("Native palette needs 37 source pixels.");
            var colours = Enumerable.Repeat(new Color32(255, 255, 255, 255), 0x225).ToArray();
            colours[0] = source.GetPixel(0, source.height - 1);
            for (int pixel = 1; pixel < 16; pixel++) colours[pixel + 1] = source.GetPixel(pixel, source.height - 1);
            for (int bank = 0; bank < 3; bank++)
                for (int keeper = 0; keeper < 7; keeper++)
                    colours[18 + bank * 9 + keeper] = source.GetPixel(16 + bank * 7 + keeper, source.height - 1);
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
            _panel = Own(new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)); _panel.Create();
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
        private void OnDestroy() { foreach (UnityEngine.Object resource in _owned) if (resource != null) Destroy(resource); }
    }
}
