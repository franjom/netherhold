// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DK2.Art;
using DK2.Core;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace DK2.Unity.Editor
{
    [InitializeOnLoad]
    public static class HudComparisonProbe
    {
        private const string Active = "DK2.HudComparisonProbe";
        private static readonly string[] Slices = { "minimap-yaw256", "minimap-yaw512", "topbar", "cursor-cage", "bottom-sectors", "message-panel", "flowers", "fullmap" };
        private static readonly int[] Widths = { 1024, 2560 }, Heights = { 768, 1440 };
        private static readonly Vector3 Focus = new Vector3(14.5f, 1, -28.5f);
        private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly List<PanelSettings> Panels = new List<PanelSettings>();
        private static readonly List<RenderTexture> Layers = new List<RenderTexture>();
        private static DungeonRunner _runner;
        private static int _resolution, _slice, _frames, _resolutionFrames;
        private static double _deadline;
        private static string _output, _conditions, _pathRegression;
        private static TilePos? _cage;
        private static TilePos _previewTile;
        private static int _flowerId;
        private static Camera _camera;
        private static bool FullMapOnly => SessionState.GetBool(Active + ".fullmapOnly", false);

        static HudComparisonProbe()
        {
            if (SessionState.GetBool(Active, false))
            { _deadline = EditorApplication.timeSinceStartup + 600; EditorApplication.update += Poll; }
        }

        public static void Run() => Start(false);
        public static void RunFullMap() => Start(true);
        private static void Start(bool fullMapOnly)
        {
            if (!Application.isBatchMode || !File.Exists(".hud-comparison-probe"))
                throw new Exception("A marked isolated HUD probe project is required.");
            _output = Environment.GetEnvironmentVariable("DK2_HUD_CAPTURE_OUTPUT");
            if (string.IsNullOrEmpty(_output) || !Directory.Exists(_output) || Directory.EnumerateFiles(_output).Any())
                throw new Exception("A fresh, empty external capture directory is required.");
            SessionState.SetString(Active + ".output", _output);
            SessionState.SetBool(Active + ".fullmapOnly", fullMapOnly);
            EditorSceneManager.OpenScene("Assets/DK2/Scenes/Dungeon.unity");
            var runner = UnityEngine.Object.FindAnyObjectByType<DungeonRunner>();
            JsonUtility.FromJsonOverwrite("{\"sessionMode\":1,\"levelName\":\"Level1\",\"seed\":1,\"fogOfWar\":false,\"speed\":0,\"tickOnWorkerThread\":false,\"showDiagnostics\":false,\"installDirectory\":\"E:/GogGames/Dungeon Keeper 2\"}", runner);
            SetResolution();
            SessionState.SetBool(Active, true); EditorApplication.isPlaying = true;
        }

        private static T Field<T>(string name) => (T)typeof(DungeonRunner).GetField(name, Private).GetValue(_runner);
        private static void Set(string name, object value) => typeof(DungeonRunner).GetField(name, Private).SetValue(_runner, value);
        private static void Call(string name) => typeof(DungeonRunner).GetMethod(name, Private).Invoke(_runner, null);
        private static void SetResolution()
        {
            PlayModeWindow.SetCustomRenderingResolution((uint)Widths[_resolution], (uint)Heights[_resolution], "HUD comparison");
            if (Application.isPlaying) Screen.SetResolution(Widths[_resolution], Heights[_resolution], FullScreenMode.Windowed);
        }

        private static void Poll()
        {
            try
            {
                if (EditorApplication.timeSinceStartup > _deadline) throw new Exception("HUD probe timeout: " + _resolution + "/" + _slice);
                if (!Application.isPlaying) return;
                if (_runner == null)
                {
                    _runner = UnityEngine.Object.FindAnyObjectByType<DungeonRunner>();
                    if (_runner == null) return;
                    if (_runner.StartupFailure != null) throw new Exception(_runner.StartupFailure);
                    if (_runner.Simulation == null) { _runner = null; return; }
                    _runner.enabled = false; _camera = Camera.main;
                    _slice = FullMapOnly ? 7 : 0;
                    _output = SessionState.GetString(Active + ".output", "");
                    var art = Field<InstallArtProvider>("_installArt");
                    if (!art.TryResolve(StatusFlowerArt.Dollar, out var dollar) || !art.TryLoadModel(dollar, out var model)
                        || model.Animation?.IsComplete != true || !Field<ClassicMeshes>("_classic").TryGetMesh(StatusFlowerArt.Dollar, out _, out _))
                        throw new Exception("Casino Dollar failed actual resource/model/Unity mesh resolution.");
                    Debug.Log("HUD Dollar resource verification passed: " + dollar.Reference.Key);
                    var director = Field<ScriptedViewDirector>("_scriptedView");
                    director.Update(Time.unscaledTimeAsDouble * 1000, _runner.Simulation.CurrentTick.Value);
                    _camera.rect = new Rect(0, 0, 1, 1);
                    CheckPathReset(); director.Skip();
                    director.Emit(new SimEvent(_runner.Simulation.CurrentTick, "hud_probe", "scripted_view", "ordinary HUD fixture")
                    { ScriptedView = new ScriptedViewRequest(ScriptedViewKind.Letterbox, new PlayerId(3), 2, default, false) });
                    director.Update(Time.unscaledTimeAsDouble * 1000, _runner.Simulation.CurrentTick.Value);
                    Debug.Log(_pathRegression);
                    Field<ClassicHudControls>("_hudControls").SetGuiVisible(true);
                    SetResolution(); _frames = 0;
                }
                if (Screen.width != Widths[_resolution] || Screen.height != Heights[_resolution]
                    || _camera.pixelWidth != Widths[_resolution] || _camera.pixelHeight != Heights[_resolution])
                {
                    _frames = 0;
                    if (++_resolutionFrames > 300) throw new Exception("Game view and Camera viewport did not settle to the requested resolution.");
                    return;
                }
                _resolutionFrames = 0;
                if (++_frames == 3) { ConfigurePanels(); ConfigureSlice(); }
                if (_frames < 3) return;
                Draw();
                if (_frames < 24) return;
                AssertScreen(); Capture();
                _frames = 0;
                if (++_slice < Slices.Length) return;
                _slice = FullMapOnly ? 7 : 0;
                if (++_resolution < Widths.Length) { SetResolution(); return; }
                int count = FullMapOnly ? 2 : 16;
                WriteNew("complete.txt", System.Text.Encoding.UTF8.GetBytes(count + " full-view captures; Screen dimensions asserted; all runtime UI panels composited in sorting order.\n" + _pathRegression));
                Debug.Log("HUD comparison verification passed: " + count + " full-view captures, " + _output);
                SessionState.SetBool(Active, false); EditorApplication.update -= Poll; EditorApplication.Exit(0);
            }
            catch (Exception error)
            { SessionState.SetBool(Active, false); Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static void CheckPathReset()
        {
            var path = Field<ScriptedViewDirector>("_scriptedView");
            if (!path.PathPlaying) { _pathRegression = "Scripted-path host reset: pending (no active bootstrap path)."; return; }
            var controls = Field<ClassicCameraControls>("_classicCameraControls");
            controls.AdvancePointer(default, true, false);
            Set("_cameraPointer", new Vector3(-9000, -9000, 0)); Call("Update");
            if (Field<Vector3>("_cameraPointer") != Input.mousePosition) throw new Exception("Scripted path retained the old pointer baseline.");
            var armed = (bool)typeof(ClassicCameraControls).GetField("_mouseMotionArmed", Private).GetValue(controls);
            if (armed) throw new Exception("Scripted path retained the pointer motion latch.");
            _pathRegression = "Scripted-path host reset: passed during the original Level1 bootstrap path (pointer baseline and latch).";
        }

        private static void ConfigurePanels()
        {
            foreach (var layer in Layers) { layer.Release(); UnityEngine.Object.DestroyImmediate(layer); }
            Panels.Clear(); Layers.Clear();
            foreach (var panel in UnityEngine.Object.FindObjectsByType<UIDocument>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Select(document => document.panelSettings).Where(panel => panel != null).Distinct().OrderBy(panel => panel.sortingOrder))
            {
                var target = new RenderTexture(Widths[_resolution], Heights[_resolution], 24, RenderTextureFormat.ARGB32);
                target.Create(); panel.targetTexture = target; panel.clearColor = true; panel.colorClearValue = Color.clear;
                Panels.Add(panel); Layers.Add(target);
            }
            if (Panels.Count < 5) throw new Exception("Missing HUD panels: " + Panels.Count);
        }

        private static void ConfigureSlice()
        {
            _conditions = "Level1 Heart courtyard; seed1; paused after bootstrap; fog disabled; ordinary HUD; unmodified source assets. Presentation fixture skips the bootstrap path and applies the existing force-off letterbox request; no simulation advance.";
            if (!Field<ScriptedViewDirector>("_scriptedView").HudFor(ClassicCameraMode.Dungeon).ShowNormalHud)
                throw new Exception("Scripted view still hides the normal HUD; refusing a hidden-panel capture.");
            var controls = Field<ClassicHudControls>("_hudControls");
            if (controls.FullMapVisible) controls.Apply(ClassicHudAction.ToggleFullMap);
            Field<ClassicFullMap>("_fullMap").SetVisible(false);
            Field<MessageTabsView>("_messageView").CloseOpen();
            Field<ClassicBottomPanel>("_bottomPanel").SelectCategory(_slice == 4 ? BottomPanelCategory.Rooms : BottomPanelCategory.Creatures);
            Field<ClassicCamera>("_classicCamera").Rotate(new Dk2Angle((ushort)(_slice == 1 ? 256 : _slice == 2 ? 1792 : 0)));
            Set("_cameraFocus", Focus); Call("ApplyClassicCamera");
            _cage = null; _flowerId = 0;
            if (_slice == 3)
            {
                var world = _runner.Simulation.World;
                _cage = Enumerable.Range(1, world.Width - 2).SelectMany(x => Enumerable.Range(1, world.Height - 2).Select(y => new TilePos(x, y)))
                    .Where(tile => HandCursor.ShowsDig(world, _runner.Simulation.Database, tile, new PlayerId(3)))
                    .Where(tile => { var p = _camera.WorldToViewportPoint(new Vector3(tile.X + .5f, 1, -tile.Y - .5f)); return p.z > 0 && p.x > .25f && p.x < .8f && p.y > .3f && p.y < .8f; })
                    .OrderBy(tile => Math.Abs(tile.X - 14) + Math.Abs(tile.Y - 28)).Cast<TilePos?>().FirstOrDefault();
                if (!_cage.HasValue) throw new Exception("No real visible dig tile for the cage.");
                _conditions += " Dig cursor/cage driven by actual taggable tile " + _cage.Value + "; pointer is its projected centre.";
            }
            if (_slice == 5)
            {
                var world = _runner.Simulation.World; var view = Field<MessageTabsView>("_messageView");
                if (!world.Messages.Entries.Any(message => message.PlayerId == new PlayerId(3) && message.NeedsTab))
                {
                    world.Messages.Post(world, 3, PlayerMessageKind.ScriptedSpeech, 1, 0);
                    _conditions += " Presentation fixture: posted retained level speech1 through PlayerMessageQueue (not an observed trigger).";
                }
                view.Capture(world, 0); view.Capture(world, 1000); view.OpenTab(0);
                if (!view.Open.HasValue) throw new Exception("Message fixture did not open.");
                _conditions += " Opened first real retained message tab " + view.Open.Value.Id + ".";
            }
            if (_slice == 6)
            {
                var scene = Field<DungeonScene>("_scene");
                var creature = _runner.Simulation.World.Creatures.FirstOrDefault(c => {
                    if (!scene.TryGetCreaturePosition(new CreatureQueryTarget(c.Id), out var p)) return false;
                    var screen = _camera.WorldToViewportPoint(p); return screen.z > 0 && screen.x > .2f && screen.x < .8f && screen.y > .3f && screen.y < .8f;
                });
                if (creature == null) throw new Exception("No real visible creature for a hover flower.");
                _flowerId = creature.Id; _conditions += " StatusFlowers receives hovered creature " + creature.Id
                    + ", type " + creature.CreatureTypeId + ", tile " + new TilePos(creature.PosX / Fp32.One, creature.PosY / Fp32.One)
                    + ", health " + creature.Health + ", level " + creature.Level + "; no health/state edits.";
            }
            if (_slice == 7)
            {
                controls.Apply(ClassicHudAction.ToggleFullMap); Field<ClassicFullMap>("_fullMap").SetVisible(true);
                var world = _runner.Simulation.World;
                var lit = Field<WorldView>("_worldView").LightObjects(world).Where(item => world.Contains(item.Tile))
                    .OrderByDescending(item => Math.Abs(item.Tile.X - 14) + Math.Abs(item.Tile.Y - 28)).ToArray();
                if (lit.Length == 0) throw new Exception("No authored light-object tile for the far preview.");
                _previewTile = lit[0].Tile;
                _conditions += " Fullmap preview camera uses layout.Preview at authored light-object tile " + _previewTile
                    + " after Draw (labelled camera override; no OS pointer automation; preview clip distance10 is the current host adaptation).";
                var room = RoomIndex.RoomAt(world, _runner.Simulation.Database, world.TileIndex(_previewTile));
                if (room == null || !room.Definition.TypeId.HasValue) throw new Exception("Preview target has no real room for the matching hover marker.");
                _conditions += " Logical pointer-equivalent room marker uses real RoomIndex type " + room.Definition.TypeId.Value
                    + " centre " + new TilePos(room.CentreFieldX, room.CentreFieldY) + ".";
                Debug.Log("HUD far preview target " + _previewTile + " map=" + world.Width + "x" + world.Height);
            }
        }

        private static void Draw()
        {
            Set("_cameraFocus", Focus); Call("ApplyClassicCamera");
            var world = _runner.Simulation.World; var scene = Field<DungeonScene>("_scene");
            Field<ScenePresenter>("_presenter").Present(world); scene.DrawCreatureFrame(world.CurrentTick, 0);
            bool ordinary = _slice != 7;
            var visible = new HudVisibility(ordinary, ordinary);
            Field<ClassicBottomPanel>("_bottomPanel").SetVisibility(visible);
            Call("ApplyClassicLook"); Call("DrawHudControls");
            Field<DungeonMinimap>("_minimap").SetVisibility(visible); Field<DungeonMinimap>("_minimap").Draw(_camera, Focus);
            var resources = Field<ClassicResourceHud>("_resourceHud"); resources.SetVisibility(visible);
            resources.Draw(Field<KeeperResourceSnapshot>("_resources"), default, 1000);
            Set("_hoveredCreature", _flowerId == 0 ? CreatureQueryTarget.None : new CreatureQueryTarget(_flowerId));
            Call("DrawStatusFlowers");
            var cursor = Field<ClassicHandCursor>("_handCursor");
            cursor.Update(_cage.HasValue ? HandCursorKind.Dig : HandCursorKind.Point, ordinary, 1);
            var pointer = new Vector2(Screen.width * .55f, Screen.height * .42f);
            if (_cage.HasValue)
            { var p = _camera.WorldToScreenPoint(new Vector3(_cage.Value.X + .5f, 1, -_cage.Value.Y - .5f)); pointer = new Vector2(p.x, Screen.height - p.y); }
            cursor.Draw(pointer);
            Field<ClassicTileHoverOutline>("_tileHighlight").Update(_cage.HasValue ? PlacementCursor.HoverCage(_cage.Value, world.Width, world.Height) : null, true, _camera, 1 + _frames * .02);
            var fullmap = Field<ClassicFullMap>("_fullMap");
            if (_slice == 7)
            {
                var layout = ClassicFullMapLayout.Fit(Screen.width, Screen.height, world.Width, world.Height);
                var preview = layout.Preview(Screen.width, Screen.height, layout.Origin + layout.Size
                    * new System.Numerics.Vector2((_previewTile.X + .5f) / world.Width, (_previewTile.Y + .5f) / world.Height));
                fullmap.PreviewCamera.transform.position = new Vector3(preview.Pose.Position.X, preview.Pose.Position.Y, preview.Pose.Position.Z);
                var room = RoomIndex.RoomAt(world, _runner.Simulation.Database, world.TileIndex(_previewTile));
                var centre = new TilePos(room.CentreFieldX, room.CentreFieldY);
                bool explored = !world.FogOfWarEnabled || world.Tile(centre).IsExploredBy(new PlayerId(3));
                if (!ClassicFullMapMarkers.TryRoom(room.Definition.TypeId.Value, centre, explored, out var marker))
                    throw new Exception("Real room was refused by the production full-map marker API.");
                typeof(ClassicFullMap).GetField("_roomMarker", Private).SetValue(fullmap, (MinimapMarker?)marker);
                ((VisualElement)typeof(ClassicFullMap).GetField("_overlay", Private).GetValue(fullmap)).MarkDirtyRepaint();
            }
            scene.PrepareClassicLighting(_camera, Field<ClassicCamera>("_classicCamera").NativeViewDistance, fullmap.PreviewCamera);
        }

        private static void AssertScreen()
        {
            if (Screen.width != Widths[_resolution] || Screen.height != Heights[_resolution])
                throw new Exception("Actual Screen is " + Screen.width + "x" + Screen.height + ", expected " + Widths[_resolution] + "x" + Heights[_resolution]);
        }

        private static void Capture()
        {
            var target = new RenderTexture(Widths[_resolution], Heights[_resolution], 24); target.Create();
            _camera.targetTexture = target;
            int renderWidth = 0, renderHeight = 0;
            Action<ScriptableRenderContext, Camera> dimensions = (context, rendered) =>
            { if (rendered == _camera) { renderWidth = Screen.width; renderHeight = Screen.height; } };
            RenderPipelineManager.endCameraRendering += dimensions;
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    var preview = Field<ClassicFullMap>("_fullMap").PreviewCamera;
                    if (preview != null) RenderPipeline.SubmitRenderRequest(preview, new UniversalRenderPipeline.SingleCameraRequest { destination = preview.targetTexture });
                    RenderPipeline.SubmitRenderRequest(_camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                }
            }
            finally { RenderPipelineManager.endCameraRendering -= dimensions; }
            if (renderWidth != Widths[_resolution] || renderHeight != Heights[_resolution])
                throw new Exception("Live camera callback Screen mismatch: " + renderWidth + "x" + renderHeight);
            var composite = new Material(Shader.Find("Hidden/DK2/FrontEndProbeComposite"));
            if (_slice == 7)
            {
                var roomTextures = (Dictionary<RoomTypeId, Texture2D>)typeof(ClassicFullMap).GetField("_roomTextures", Private)
                    .GetValue(Field<ClassicFullMap>("_fullMap"));
                if (roomTextures.Count == 0) throw new Exception("Full-map room hover icon was never drawn.");
                var preview = Pixels(Field<ClassicFullMap>("_fullMap").PreviewCamera.targetTexture);
                if (preview.GetPixels32().Count(c => c.r > 20 || c.g > 20 || c.b > 20) < preview.width * preview.height / 20)
                {
                    WriteNew("diagnostic-dark-preview-" + Widths[_resolution] + "x" + Heights[_resolution] + ".png", preview.EncodeToPNG());
                    throw new Exception("Full-map authored-light preview is blank at " + _previewTile);
                }
                WriteNew("level1-far-preview-" + Widths[_resolution] + "x" + Heights[_resolution] + ".png", preview.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(preview);
            }
            if (_slice == 6 && !Field<StatusFlowers>("_statusFlowers").Shown.Any(flower => flower.CreatureId == _flowerId))
                throw new Exception("Hovered creature has no produced status flower.");
            int ink = 0; var panelInk = new List<string>();
            for (int i = 0; i < Layers.Count; i++)
            {
                var layerPixels = Pixels(Layers[i]); int alpha = layerPixels.GetPixels32().Count(c => c.a > 20); ink += alpha;
                panelInk.Add(Panels[i].sortingOrder + ":" + alpha);
                if (_slice == 3 && Panels[i] == Field<ClassicHandCursor>("_handCursor").Panel && alpha < 100)
                    throw new Exception("Cursor panel is blank.");
                UnityEngine.Object.DestroyImmediate(layerPixels); Graphics.Blit(Layers[i], target, composite);
            }
            if (ink < 10000) throw new Exception("HUD panels are blank.");
            var pixels = Pixels(target);
            if (pixels.GetPixels32().Count(c => c.r > 20 || c.g > 20 || c.b > 20) < 20000) throw new Exception("Composited capture is blank.");
            string mapSamples = "not a full-map view";
            if (_slice == 7)
            {
                bool aligned = VerifyFullMapPixels(pixels, out mapSamples);
                WriteNew("fullmap-palette-" + Widths[_resolution] + "x" + Heights[_resolution] + ".txt", System.Text.Encoding.UTF8.GetBytes(mapSamples));
                if (!aligned)
                {
                    WriteNew("diagnostic-fullmap-y-" + Widths[_resolution] + "x" + Heights[_resolution] + ".png", pixels.EncodeToPNG());
                    throw new Exception("Full-map world-tile/palette alignment failed: " + mapSamples);
                }
                Debug.Log("HUD full-map palette verification passed: " + mapSamples);
            }
            string name = "level1-heart-courtyard-" + Widths[_resolution] + "x" + Heights[_resolution] + "-" + Slices[_slice];
            WriteNew(name + ".png", pixels.EncodeToPNG());
            var metadata = new CaptureMetadata { slice = Slices[_slice], width = Screen.width, height = Screen.height,
                tick = _runner.Simulation.CurrentTick.Value, seed = 1, level = "Level1", focus = Focus.ToString("F3"),
                cameraPosition = _camera.transform.position.ToString("F3"), cameraEuler = _camera.transform.eulerAngles.ToString("F3"),
                fieldOfView = _camera.fieldOfView, conditions = _conditions, panelSortOrders = string.Join(",", Panels.Select(p => p.sortingOrder)),
                panelAlphaPixels = string.Join(",", panelInk), stateHash = _runner.Simulation.StateHash().ToString("X16"), pathReset = _pathRegression, mapSamples = mapSamples };
            WriteNew(name + ".json", System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(metadata, true)));
            Debug.Log("HUD capture " + name + " tick=" + metadata.tick + " panels=" + Panels.Count);
            RenderTexture.active = null; _camera.targetTexture = null; target.Release();
            UnityEngine.Object.DestroyImmediate(pixels); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(composite);
        }
        private static Texture2D Pixels(RenderTexture target)
        {
            RenderTexture.active = target; var pixels = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply(); RenderTexture.active = null; return pixels;
        }
        private static bool VerifyFullMapPixels(Texture2D fullView, out string report)
        {
            var world = _runner.Simulation.World;
            var source = Field<DungeonMinimap>("_minimap");
            var grid = (MinimapClassGrid)typeof(DungeonMinimap).GetField("_grid", Private).GetValue(source);
            var rooms = RoomIndex.Build(world, _runner.Simulation.Database);
            var heart = rooms.First(room => room.Definition.TypeId == RoomTypeId.DungeonHeart);
            var portal = rooms.First(room => room.Definition.TypeId == RoomTypeId.Portal);
            var layout = ClassicFullMapLayout.Fit(fullView.width, fullView.height, world.Width, world.Height);
            int iconWidth = fullView.width * 32 / 640, iconHeight = fullView.height * 32 / 480;
            var icon = new Rect(layout.Origin.X + heart.CentreFieldX * layout.Size.X / world.Width - iconWidth / 2f,
                layout.Origin.Y + heart.CentreFieldY * layout.Size.Y / world.Height - iconHeight / 2f, iconWidth, iconHeight);
            Color32 Expected(TilePos tile)
            {
                uint argb = source.Palette.Colours[grid[tile].Index];
                return new Color32((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, 255);
            }
            Vector2 Pixel(TilePos tile) => new Vector2(layout.Origin.X + (tile.X + .5f) * layout.Size.X / world.Width,
                layout.Origin.Y + (tile.Y + .5f) * layout.Size.Y / world.Height);
            var markers = (List<MinimapMarker>)typeof(ClassicFullMap).GetField("_markers", Private).GetValue(Field<ClassicFullMap>("_fullMap"));
            bool Uncovered(TilePos tile)
            {
                var p = Pixel(tile);
                if (icon.Contains(p)) return false;
                float size = 3 * fullView.height / 768f;
                foreach (var marker in markers)
                {
                    float x = (int)(marker.Position.X.Raw / (float)Fp32.One * layout.Size.X / world.Width - 1) + layout.Origin.X;
                    float y = (int)(marker.Position.Y.Raw / (float)Fp32.One * layout.Size.Y / world.Height - 1) + layout.Origin.Y;
                    if (new Rect(x - 2, y - 2, size + 4, size + 4).Contains(p)) return false;
                }
                return true;
            }
            var heartTile = heart.TileIndices.Select(world.TilePosition).First(tile => {
                var c = Expected(tile); return c.r > c.g + 30 && c.r > c.b + 30 && Uncovered(tile); });
            var portalTile = portal.TileIndices.Select(world.TilePosition).First(tile => {
                var c = Expected(tile); return c.r > 30 && Math.Abs(c.r - c.g) < 5 && Math.Abs(c.r - c.b) < 5 && Uncovered(tile); });
            bool passed = true; var samples = new List<string>();
            foreach (var tile in new[] { heartTile, portalTile })
            {
                var p = Pixel(tile); var expected = Expected(tile);
                Color32 actual = fullView.GetPixel((int)p.x, fullView.height - 1 - (int)p.y);
                bool equal = Math.Abs(actual.r - expected.r) <= 3 && Math.Abs(actual.g - expected.g) <= 3 && Math.Abs(actual.b - expected.b) <= 3;
                passed &= equal;
                samples.Add("tile" + tile + " class=" + grid[tile].Index + " pixel=" + (int)p.x + "," + (int)p.y
                    + " expected=" + expected.r + "," + expected.g + "," + expected.b + " actual=" + actual.r + "," + actual.g + "," + actual.b + " pass=" + equal);
            }
            report = string.Join("; ", samples); return passed;
        }
        private static void WriteNew(string name, byte[] bytes)
        { using (var file = new FileStream(Path.Combine(_output, name), FileMode.CreateNew, FileAccess.Write)) file.Write(bytes, 0, bytes.Length); }
        [Serializable] private sealed class CaptureMetadata
        {
            public string slice, level, focus, cameraPosition, cameraEuler, conditions, panelSortOrders, panelAlphaPixels, stateHash, pathReset, mapSamples;
            public int width, height; public long tick; public ulong seed; public float fieldOfView;
        }
    }
}
