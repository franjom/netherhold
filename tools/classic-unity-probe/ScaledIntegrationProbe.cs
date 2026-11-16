// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using DK2.Core;
using DK2.Data.Campaign;
using DK2.Data.Model;
using DK2.Data.Settings;
using DK2.Presentation;
using DK2.Simulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace DK2.Unity.Editor
{
    [InitializeOnLoad]
    public static class ScaledIntegrationProbe
    {
        private const string Active = "DK2.ScaledIntegrationProbe";
        private static readonly int[] Requests = { 100, 125, 200 };
        private static DungeonRunner _runner;
        private static int _phase, _stage, _frames, _lastFrame;
        private static double _deadline;
        private static Vector3 _pointer = new Vector3(-9000, -9000, 0);
        private static string _initialHash;
        private static string Output => File.ReadAllText(".scaled-integration-probe");
        private static object Get(object o, string name) => o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance).GetValue(o);
        private static void Set(object o, string name, object value) => o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance).SetValue(o, value);
        private static object Call(object o, string name, params object[] args) => o.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance).Invoke(o, args);
        private static T Field<T>(string name) => (T)Get(_runner, name);
        static ScaledIntegrationProbe()
        {
            if (SessionState.GetBool(Active, false)) { _deadline = EditorApplication.timeSinceStartup + 300; EditorApplication.update += Poll; }
        }
        public static void Run()
        {
            if (!Application.isBatchMode || !File.Exists(".scaled-integration-probe")) throw new Exception("Isolated scaled probe required.");
            PlayModeWindow.SetCustomRenderingResolution(2560, 1440, "Scaled integration fixture");
            EditorSceneManager.OpenScene("Assets/DK2/Scenes/Dungeon.unity");
            var runner = UnityEngine.Object.FindAnyObjectByType<DungeonRunner>();
            var settings = new GameSettings { MasterVolume = 40, SpeechVolume = 50, MusicVolume = 25,
                SoundEffectVolume = 75, SoundQuality = 2, MusicSwitch = false,
                Bindings = new[] { new GameKeyBinding(GameAction.Heart, 0x30, KeyModifiers.Control) } };
            string profile = Path.Combine(Output, "campaign.dk2p");
            CampaignProfileFile.Save(profile, new CampaignProfile());
            GameSettingsFile.Save(Path.Combine(Output, "game-settings.ini"), settings);
            JsonUtility.FromJsonOverwrite("{\"sessionMode\":1,\"levelName\":\"Level1\",\"classicMeshes\":true,\"showDiagnostics\":false,\"speed\":0,\"installDirectory\":\"E:/GogGames/Dungeon Keeper 2\"}", runner);
            Set(runner, "campaignProfileFile", profile);
            SessionState.SetBool(Active, true); EditorApplication.EnterPlaymode();
        }
        private static void Poll()
        {
            try
            {
                if (EditorApplication.timeSinceStartup > _deadline) throw new Exception("Scaled integration timeout: " + _phase + "/" + _stage);
                if (!Application.isPlaying || EditorApplication.isCompiling) return;
                if (_runner == null)
                {
                    _runner = UnityEngine.Object.FindAnyObjectByType<DungeonRunner>();
                    if (_runner == null || Get(_runner, "_resourceHud") == null) { _runner = null; return; }
                    if (_runner.StartupFailure != null) throw new Exception(_runner.StartupFailure);
                    _runner.enabled = false;
                    Screen.SetResolution(2560, 1440, FullScreenMode.Windowed);
                    var director = Field<ScriptedViewDirector>("_scriptedView");
                    var simulation = Field<GameSimulation>("_simulation");
                    director.Update(Time.unscaledTimeAsDouble * 1000, simulation.World.CurrentTick.Value); director.Skip();
                    director.Emit(new SimEvent(simulation.World.CurrentTick, "scaled_probe", "scripted_view", "ordinary HUD fixture")
                    { ScriptedView = new ScriptedViewRequest(ScriptedViewKind.Letterbox, new PlayerId(3), 2, default, false) });
                    director.Update(Time.unscaledTimeAsDouble * 1000, simulation.World.CurrentTick.Value);
                    Field<ClassicHudControls>("_hudControls").SetGuiVisible(true);
                    Field<ClassicHudControls>("_hudControls").UpdateResult(default);
                    _initialHash = simulation.StateHash().ToString();
                    if (Type.GetType("NLayer.MpegFile, NLayer") == null) throw new Exception("Ninth runtime assembly NLayer is missing.");
                    CheckAudio(); CheckBindingsAndFight();
                    BeginScale(); return;
                }
                Draw();
                if (_lastFrame == Time.frameCount) return; _lastFrame = Time.frameCount;
                if (++_frames < 12) return; _frames = 0;
                if (Screen.width != 2560 || Screen.height != 1440) throw new Exception("Screen does not match2560x1440: " + Screen.width + "x" + Screen.height);
                var overview = Field<ClassicCreatureOverview>("_overviewPanel");
                if (_stage == 0)
                {
                    float expected = Requests[_phase] == 200 ? 4f / 3 : Requests[_phase] / 100f;
                    if (Math.Abs(overview.Panel.scale - expected) > .0001f) throw new Exception("Wrong effective HUD factor " + overview.Panel.scale);
                    _pointer = OverviewPoint("ModeRect", 1); Next(1); return;
                }
                if (_stage == 1)
                {
                    // The panels are CreatureOverviewHud and friends drawn by HudCanvas (ADR-0805): what they drew is the
                    // facade's last frame, and where is the model's rect, both in virtual units.
                    var modeHover = ((HudImage[])typeof(CreatureOverviewHud).GetField("ModeHovers", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null))[1];
                    if (!Drawn(overview, draw => draw.Image == modeHover))
                        throw new Exception("Scale " + Requests[_phase] + " mode hover overlay missed the rendered selector.");
                    Capture(overview.Panel, "mode", _pointer);
                    _pointer = OverviewPoint("WorkerCountRect", 1); Next(2); return;
                }
                if (_stage == 2)
                {
                    var worker = (HudRect)Call(Get(overview, "_hud"), "WorkerCountRect", 1);
                    if (!Drawn(overview, draw => draw.Rect == worker && draw.Image.Path == "GUI\\Tabs\\t-cp-hilight")) throw new Exception("Worker hover overlay missed.");
                    Capture(overview.Panel, "worker", _pointer);
                    CheckMinimapAndGold();
                    Field<ClassicBottomPanel>("_bottomPanel").SelectCategory(BottomPanelCategory.Rooms);
                    Next(3); return;
                }
                if (_stage == 3)
                {
                    var bottom = Field<ClassicBottomPanel>("_bottomPanel");
                    var hud = (BottomPanelHud)Get(bottom, "_hud");
                    var slots = (BottomPanelChoice[])Get(hud, "_slots");
                    int slot = Array.FindIndex(slots, s => s != null && s.Kind == BottomPanelChoiceKind.Room);
                    if (slot < 0) throw new Exception("No built room slot in real Level1 fixture.");
                    var element = (int?)Call(hud, "ElementOf", slot);
                    if (element == null) throw new Exception("Room slot " + slot + " has no element.");
                    var icon = (HudRect)Get(((Array)Get(hud, "_elements")).GetValue(element.Value), "Rect");
                    Vector3 point = ScreenOf(icon, true);
                    if (!bottom.ContainsPointer(point) || !(bool)Call(_runner, "PointerOverHud", point)) throw new Exception("Room icon leaks through to world input.");
                    // The canvas's own pointer callbacks hand the model these calls, at the icon's centre.
                    float x = icon.X + icon.Width / 2, y = icon.Y + icon.Height / 2;
                    if (hud.HitAction(x, y) != slot || !hud.PointerDown(x, y, 0) || !hud.PointerUp(x, y, 0, false, false, false))
                        throw new Exception("Room icon did not take the press.");
                    if (!(Field<HudToolState>("_toolState").PanelChoice is BottomPanelChoiceKey choice) || choice.Kind != BottomPanelChoiceKind.Room)
                        throw new Exception("Actual room icon callback did not select a room.");
                    Capture(bottom.Panel, "rooms", point);
                    Debug.Log("Scaled HUD fixture passed request=" + Requests[_phase] + " effective=" + (bottom.Panel.scale * 100) + "%: mode/worker overlays, minimap magnifier, Gold and actual room callback.");
                    if (++_phase < Requests.Length) { BeginScale(); return; }
                    if (Field<GameSimulation>("_simulation").StateHash().ToString() != _initialHash) throw new Exception("Presentation fixture changed simulation state.");
                    Debug.Log("Scaled integration verification passed:100baseline,125hits,200viewportcap; persisted/live audio state; rebound resolver and F StartGlide; unchanged world hash. Input and battle pointers are explicit adapter fixtures, not OS input or native parity proof.");
                    SessionState.SetBool(Active, false); EditorApplication.Exit(0);
                }
            }
            catch (Exception error) { SessionState.SetBool(Active, false); Debug.LogException(error); EditorApplication.Exit(1); }
        }
        /// <summary>Every panel renders to its own 2560x1440 texture: also those the HUD built after the probe found
        /// the runner, so each phase targets them again before its first capture.</summary>
        private static void TargetPanels()
        {
            var documents = UnityEngine.Object.FindObjectsByType<UIDocument>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var panel in documents.Select(d => d.panelSettings).Where(p => p != null && p.targetTexture == null).Distinct())
            {
                var target = new RenderTexture(2560, 1440, 24, RenderTextureFormat.ARGB32); target.Create(); panel.targetTexture = target;
                panel.clearColor = true; panel.colorClearValue = Color.clear;
            }
            // A re-targeted panel loses the meshes its canvases built, and a canvas redraws only a changed frame.
            foreach (var document in documents)
                document.rootVisualElement?.Query<HudCanvas>().ForEach(canvas => canvas.Invalidate());
        }
        private static void BeginScale()
        {
            TargetPanels();
            Call(_runner, "ApplyGameSettings", _runner.Settings with { UiScale = Requests[_phase] });
            var bottom = Field<ClassicBottomPanel>("_bottomPanel");
            if (bottom.PanelArea(BottomPanelCategory.Creatures) == null) bottom.SelectCategory(BottomPanelCategory.Creatures);
            _pointer = new Vector3(-9000, -9000, 0); Next(0); Draw();
        }
        private static void Draw()
        {
            Call(_runner, "UpdateHudControls", false);
            Field<ClassicResourceHud>("_resourceHud").SetVisibility(new HudVisibility(true, true));
            Call(_runner, "DrawHudControls");
            Field<ClassicResourceHud>("_resourceHud").Draw(Field<KeeperResourceSnapshot>("_resources"));
            var bottom = Field<ClassicBottomPanel>("_bottomPanel");
            Field<ClassicCreatureOverview>("_overviewPanel").Draw(new HudVisibility(true, true), bottom.PanelArea(BottomPanelCategory.Creatures), _pointer);
            var minimap = Field<DungeonMinimap>("_minimap");
            minimap.SetVisibility(new HudVisibility(true, true)); minimap.Draw(Camera.main, Field<Vector3>("_cameraFocus"));
            float unit = Screen.height / 768f * ClassicHudScaling.Layout(Requests[_phase]).Factor;
            minimap.UpdateControls(new Vector3(170 * unit, 24 * unit, 0), ClassicHudAction.None, false);
        }
        private static void Next(int stage) { _stage = stage; _frames = 0; _lastFrame = Time.frameCount; }
        private static Vector3 OverviewPoint(string rectangle, int row)
        {
            var overview = Field<ClassicCreatureOverview>("_overviewPanel");
            return ScreenOf((HudRect)Call(Get(overview, "_hud"), rectangle, row), true);
        }
        /// <summary>A virtual-unit rect's centre as a bottom-up screen point, for a panel at this phase's UI scale.</summary>
        private static Vector3 ScreenOf(HudRect rect, bool bottomDocked)
        {
            HudScaleLayout layout = ClassicHudScaling.Layout(Requests[_phase]);
            float local = Screen.height / 1920f;
            System.Numerics.Vector2 top = layout.ToScreenTop(new System.Numerics.Vector2(
                (rect.X + rect.Width / 2) * local, (rect.Y + rect.Height / 2) * local), bottomDocked);
            return new Vector3(top.X, Screen.height - top.Y, 0);
        }
        private static bool Drawn(object facade, Func<HudDraw, bool> match)
        {
            foreach (HudDraw draw in ((HudFrame)Get(facade, "_frame")).Draws) if (match(draw)) return true;
            return false;
        }
        private static void CheckMinimapAndGold()
        {
            var minimap = Field<DungeonMinimap>("_minimap");
            // The magnifier's centre, (169.6, 745.6) of 1024x768 docked to the bottom, at the scaled panel's pixels (ADR-0805).
            float scale = Screen.height / 768f * ClassicHudScaling.Layout(minimap.UiScale).Factor;
            var point = new Vector3(169.6f * scale, (768 - 745.6f) * scale, 0);
            if (minimap.HandlePress(point, true, false, null, default) != ClassicHudAction.ZoomIn || !minimap.ContainsPointer(point)) throw new Exception("Scaled magnifier hit mismatch.");
            var hud = (MinimapHud)Get(minimap, "_hud");
            while (!hud.Zoom.IsWholeMap) hud.ZoomOut();
            minimap.HandleZoomPress(point, true, false);
            if (hud.Zoom.Level != 0) throw new Exception("Magnifier handler did not zoom.");
            // The first gold digit KeeperResourceHud drew, at its place in the resource strip (top-docked).
            var resources = Field<ClassicResourceHud>("_resourceHud");
            object model = Get(resources, "_hud");
            var elements = (Array)Get(model, "_elements");
            var glyphs = (int[])Get(Get(model, "_gold"), "_glyphs");
            object gold = glyphs.Select(index => elements.GetValue(index)).First(glyph => (bool)Get(glyph, "Show"));
            point = ScreenOf((HudRect)Get(gold, "Rect"), false);
            Debug.Log("Gold rendered hit fixture: pointer=" + point + " kind=" + resources.ResourceAt(point) + " contains=" + resources.ContainsPointer(point) + " hud=" + Call(_runner, "PointerOverHud", point));
            if (resources.ResourceAt(point) != KeeperResourceKind.Gold || !resources.ContainsPointer(point) || !(bool)Call(_runner, "PointerOverHud", point))
                throw new Exception("Scaled Gold glyph and resource hit disagree.");
        }
        private static void CheckAudio()
        {
            var sound = _runner.SoundPlayer;
            Near((float)Get(sound, "_master"), .4f, "persisted master");
            Near(((AudioSource)Get(sound, "_speech")).volume, .2f, "persisted speech source");
            Near(((AudioSource)Get(Get(sound, "_music"), "Source")).volume, 0, "persisted music switch");
            if (sound.SoundQuality != 2) throw new Exception("Persisted sound quality not applied after player init.");
            _runner.OptionsPages.Enter(FrontEndPage.Sound, OptionsApplicationMode.InGame);
            _runner.OptionsPages.SetWidget(139, 60); _runner.OptionsPages.SetWidget(676, 1); _runner.OptionsPages.SetWidget(133, 3);
            Near(((AudioSource)Get(sound, "_speech")).volume, .3f, "live speech source");
            Near(((AudioSource)Get(Get(sound, "_music"), "Source")).volume, .15f, "live music source");
            Near(((AudioSource)Get(Get(sound, "_ambience"), "Source")).volume, .45f, "live effects source");
            if (sound.SoundQuality != 3) throw new Exception("Live quality not applied.");
            _runner.OptionsPages.Accept();
            if (GameSettingsFile.Load(Path.Combine(Output, "game-settings.ini")).MasterVolume != 60) throw new Exception("Live audio settings not persisted.");
            Debug.Log("Scaled integration audio state passed: persisted40/50/25/75quality2/musicOff; live60quality3/musicOn -> speech0.3/music0.15/ambience0.45. Hearing unverified.");
        }
        private static void Near(float actual, float expected, string label) { if (Math.Abs(actual - expected) > .0001f) throw new Exception(label + ": " + actual + " expected " + expected); }
        private static void CheckBindingsAndFight()
        {
            var input = new GameBindingInput(_runner.Settings, KeyModifiers.Control, s => s.Value == 0x30, s => s.Value == 0x30);
            if (input.HudShortcut(new GameShortcutContext()) != ClassicHudAction.NavigateHeart) throw new Exception("Loaded rebound Heart did not resolve.");
            var old = new GameBindingInput(_runner.Settings, KeyModifiers.None, s => s.Value == 0x23, s => s.Value == 0x23);
            if (old.HudShortcut(new GameShortcutContext()) != ClassicHudAction.None) throw new Exception("Old Heart key remains active.");
            Call(_runner, "ApplyHudAction", input.HudShortcut(new GameShortcutContext()));
            if (Field<WorldView>("_worldView").MinimapHeartTile is TilePos heart)
                CheckGlide(heart);
            var observer = Field<MusicWorldObserver>("_musicObserver");
            var snapshots = (BattleViewSnapshot[])Get(observer, "_snapshots");
            var original = (BattleViewSnapshot[])snapshots.Clone();
            var tile = new TilePos(5, 6);
            snapshots[0] = new BattleViewSnapshot(new BattleViewSlot(0), tile, PlayerMask.None.With(new PlayerId(3)), 2);
            var fight = new GameBindingInput(_runner.Settings, KeyModifiers.None, s => s.Value == 0x21, s => s.Value == 0x21);
            var action = fight.HudShortcut(new GameShortcutContext());
            if (action != ClassicHudAction.NavigateFight) throw new Exception("F did not resolve.");
            Call(_runner, "ApplyHudAction", action); CheckGlide(tile); original.CopyTo(snapshots, 0);
            Debug.Log("Rebound key and F navigation fixture passed through actual ApplyHudAction/StartGlide; presentation-only battle slot fixture.");
        }
        private static void CheckGlide(TilePos tile)
        {
            System.Numerics.Vector3 target = Field<CameraGlide>("_glide").Target;
            Near(target.X, tile.X + .5f, "glideX"); Near(target.Z, -tile.Y - .5f, "glideZ");
        }
        private static int Ink(RenderTexture layer)
        {
            var previous = RenderTexture.active; RenderTexture.active = layer;
            var read = new Texture2D(layer.width, layer.height, TextureFormat.RGBA32, false);
            read.ReadPixels(new Rect(0, 0, layer.width, layer.height), 0, 0); read.Apply();
            int ink = read.GetPixels32().Count(p => p.a > 0);
            UnityEngine.Object.DestroyImmediate(read); RenderTexture.active = previous;
            return ink;
        }
        private static void Capture(PanelSettings panel, string name, Vector3 point)
        {
            var target = panel.targetTexture;
            if (target == null) throw new Exception("Scaled UI " + name + " has no target texture.");
            RenderTexture.active = target;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0, 0, image.width, image.height), 0, 0); image.Apply();
            var pixels = image.GetPixels32();
            if (pixels.Count(p => p.a > 0) < 500)
            {
                File.WriteAllBytes(Path.Combine(Output, "diagnostic-blank-" + Requests[_phase] + "-" + name + ".png"), image.EncodeToPNG());
                foreach (var document in UnityEngine.Object.FindObjectsByType<UIDocument>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    var root = document.rootVisualElement; var settings = document.panelSettings;
                    string canvases = root == null ? "no root" : string.Join(";", root.Query<HudCanvas>().ToList()
                        .Select(c => c.layout + " shown=" + ((HudFrame)Get(c, "_shown")).Count));
                    Debug.Log("Blank diagnostic " + document.name + " sort=" + settings?.sortingOrder + " same=" + (settings == panel)
                        + " target=" + (settings?.targetTexture != null) + " enabled=" + document.enabled + "/" + document.gameObject.activeInHierarchy
                        + " display=" + root?.resolvedStyle.display + " root=" + root?.layout + " canvases=" + canvases
                        + " ink=" + (settings?.targetTexture is RenderTexture layer ? Ink(layer) : -1));
                }
                throw new Exception("Blank scaled UI " + name + " (" + target.width + "x" + target.height + ")");
            }
            int x = Mathf.Clamp((int)point.x, 0, image.width - 1), y = Mathf.Clamp((int)point.y, 0, image.height - 1);
            int count = 0;
            for (int dy = -20; dy <= 20; dy++) for (int dx = -20; dx <= 20; dx++)
                if (x + dx >= 0 && x + dx < image.width && y + dy >= 0 && y + dy < image.height && pixels[(y + dy) * image.width + x + dx].a > 0) count++;
            if (count < 200) throw new Exception("Scaled element region blank: " + name);
            File.WriteAllBytes(Path.Combine(Output, "scale-" + Requests[_phase] + "-" + name + ".png"), image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image); RenderTexture.active = null;
            Debug.Log("Scaled UI element coverage " + Requests[_phase] + " " + name + "=" + count);
        }
    }
}
