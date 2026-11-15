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
    public static class InGameOptionsProbe
    {
        private const string Active = "DK2.InGameOptionsProbe";
        private static double _deadline;
        private static int _frames, _lastFrame;
        private static readonly System.Collections.Generic.Dictionary<string, int> CaptureAttempts = new System.Collections.Generic.Dictionary<string, int>();
        private static HudRect _drag;
        private static RenderTexture _target;
        static InGameOptionsProbe()
        {
            if (SessionState.GetBool(Active, false)) { _deadline = EditorApplication.timeSinceStartup + 300; EditorApplication.update += Poll; }
        }
        private static object Get(object o, string name) => o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(o);
        private static void Set(object o, string name, object value) => o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(o, value);
        private static void Call(object o, string name, params object[] args) => o.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(o, args);
        public static void Run()
        {
            if (!Application.isBatchMode || !File.Exists(".in-game-options-probe")) throw new Exception("Isolated probe required.");
            EditorSceneManager.OpenScene("Assets/DK2/Scenes/Dungeon.unity");
            var runner = UnityEngine.Object.FindAnyObjectByType<DungeonRunner>();
            string profile = Path.Combine(File.ReadAllText(".in-game-options-probe"), "campaign.dk2p"); CampaignProfileFile.Save(profile, new CampaignProfile());
            JsonUtility.FromJsonOverwrite("{\"sessionMode\":1,\"levelName\":\"Level1\",\"classicMeshes\":true,\"showDiagnostics\":false,\"speed\":0,\"installDirectory\":\"E:/GogGames/Dungeon Keeper 2\"}", runner);
            Set(runner, "campaignProfileFile", profile);
            SessionState.SetInt(Active + ".stage", 0); SessionState.SetBool(Active, true); EditorApplication.EnterPlaymode();
        }
        private static void Poll()
        {
            try
            {
                if (EditorApplication.timeSinceStartup > _deadline) throw new Exception("In-game settings timeout.");
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
                var runner = UnityEngine.Object.FindAnyObjectByType<DungeonRunner>();
                if (runner == null || !runner.enabled) return;
                if (runner.StartupFailure != null) throw new Exception(runner.StartupFailure);
                var panel = Get(runner, "_gameOptionsPanel") as ClassicGameOptionsPanel; if (panel == null) return;
                var hud = (ClassicHudControls)Get(runner, "_hudControls");
                var sim = (GameSimulation)Get(runner, "_simulation");
                int stage = SessionState.GetInt(Active + ".stage", 0);
                if (stage == 0)
                {
                    if (++_frames < 20) return; _frames = 0;
                    var view = (ScriptedViewDirector)Get(runner, "_scriptedView");
                    view.Update(Time.unscaledTimeAsDouble * 1000, sim.World.CurrentTick.Value); view.Skip();
                    view.Emit(new SimEvent(sim.World.CurrentTick, "options_probe", "scripted_view", "ordinary HUD fixture")
                    { ScriptedView = new ScriptedViewRequest(ScriptedViewKind.Letterbox, new PlayerId(3), 2, default, false) });
                    view.Update(Time.unscaledTimeAsDouble * 1000, sim.World.CurrentTick.Value);
                    hud.SetGuiVisible(true); hud.UpdateResult(default);
                    Call(runner, "ApplyHudAction", ClassicHudAction.ToggleOptions); Call(runner, "ApplyHudAction", ClassicHudAction.OpenGameOptions);
                    Call(runner, "UpdateHudControls", true);
                    _target = new RenderTexture(2560, 1440, 24); _target.Create(); panel.Panel.targetTexture = _target;
                    panel.Panel.clearColor = true; panel.Panel.colorClearValue = Color.clear; panel.Draw();
                    SessionState.SetString(Active + ".tick", sim.World.CurrentTick.Value.ToString()); Set(runner, "speed", 1f);
                    Next(1); return;
                }
                if (_lastFrame == Time.frameCount) return; _lastFrame = Time.frameCount;
                if (++_frames < 12) return; _frames = 0;
                if (stage != 12 && (!hud.GameOptionsVisible || sim.World.CurrentTick.Value.ToString() != SessionState.GetString(Active + ".tick", "")))
                    throw new Exception("Editing did not preserve Options pause.");
                var root = (VisualElement)Get(panel, "_root");
                var model = (GameOptionsHud)Get(panel, "_hud");
                if (stage == 1) { Debug.Log("Options probe state: visible=" + panel.ContainsPointer(default) + ", page=" + panel.Model.Page + ", children=" + root.childCount + ", bounds=" + root.worldBound + ", path=" + ((ScriptedViewDirector)Get(runner, "_scriptedView")).PathPlaying + ", gui=" + hud.GuiVisible); if (!Capture("game-options", panel)) return; Click(model, 1); Next(2); return; }
                if (stage == 2)
                {
                    if (!Capture("graphics", panel)) return; _drag = Slider(model, 126); Press(model, _drag, .25f); Next(3); return;
                }
                if (stage == 3)
                {
                    if (!model.Holding) throw new Exception("Slider drag was dropped during live apply.");
                    model.PointerMove(_drag.X + _drag.Width * 2f / 3, _drag.Y + _drag.Height / 2); Next(4); return;
                }
                if (stage == 4)
                {
                    model.PointerUp();
                    if (runner.Settings.Gamma != 45) throw new Exception("Gamma drag failed: " + runner.Settings.Gamma);
                    Click(model, 12); Next(5); return;
                }
                if (stage == 5) { Click(model, 2); Next(6); return; }
                if (stage == 6) { if (!Capture("sound", panel)) return; Click(model, 21); Next(7); return; }
                if (stage == 7) { Click(model, 3); Next(8); return; }
                if (stage == 8)
                {
                    if (!Capture("controls", panel)) return; Press(model, Slider(model, 154), .4f); model.PointerUp();
                    if (runner.Settings.GameSpeed != 7) throw new Exception("In-game game speed did not apply live.");
                    Click(model, 15); Next(9); return;
                }
                if (stage == 9)
                {
                    if (!Capture("keyboard", panel)) return; DragKeyboardToBottom(panel, model); ScrollToHeart(panel, model); Next(91); return;
                }
                if (stage == 91)
                {
                    ClickKey(model, GameAction.Heart);
                    if (runner.OptionsPages.CapturingAction != GameAction.Heart) throw new Exception("Keyboard row did not capture.");
                    Click(model, 3);
                    if (runner.OptionsPages.CapturingAction != null || runner.Settings.Binding(GameAction.Heart).ScanCode != 0x23)
                        throw new Exception("Back did not cancel pending capture.");
                    Next(10); return;
                }
                if (stage == 10) { Click(model, 15); Next(11); return; }
                if (stage == 11)
                {
                    ScrollToHeart(panel, model); Next(111); return;
                }
                if (stage == 111)
                {
                    ClickKey(model, GameAction.Heart);
                    if (runner.OptionsPages.CapturingAction != GameAction.Heart) throw new Exception("Keyboard row did not enter second capture.");
                    using (var key = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.D })) root.SendEvent(key);
                    if (runner.Settings.Binding(GameAction.Heart).ScanCode != 0x20) throw new Exception("Key event did not rebind Heart.");
                    Click(model, 3); Next(112); return;
                }
                if (stage == 112)
                {
                    Click(model, 16);
                    if (hud.OptionsVisible || runner.Settings.Gamma != 45) throw new Exception("Continue did not retain settings and close Options.");
                    var saved = GameSettingsFile.Load((string)Get(runner, "_settingsPath"));
                    if (saved.Gamma != 45 || saved.GameSpeed != 7 || saved.Binding(GameAction.Heart).ScanCode != 0x20) throw new Exception("Settings were not persisted.");
                    Next(12); return;
                }
                if (stage == 12 && sim.World.CurrentTick.Value.ToString() != SessionState.GetString(Active + ".tick", ""))
                {
                    Debug.Log("In-game Options verification passed: native pages, pause, live drag, Back/Continue, cancel/capture, persistence and resumed ticks.");
                    SessionState.SetBool(Active, false); EditorApplication.Exit(0);
                }
            }
            catch (Exception ex) { SessionState.SetBool(Active, false); Debug.LogException(ex); EditorApplication.Exit(1); }
        }
        private static void Next(int stage) { SessionState.SetInt(Active + ".stage", stage); _frames = 0; }
        /// <summary>A primary press on a page widget, by its native index, through the panel's model: the
        /// draw list has no elements to send events to.</summary>
        private static void Click(GameOptionsHud model, int nativeIndex)
        {
            HudRect r = model.WidgetRect(nativeIndex) ?? throw new Exception("Missing control " + nativeIndex);
            if (!model.PointerDown(r.X + r.Width / 2, r.Y + r.Height / 2, 0)) throw new Exception("Control " + nativeIndex + " took no press.");
            model.PointerUp();
        }
        private static HudRect Slider(GameOptionsHud model, ushort setting) => model.SliderRect(setting) ?? throw new Exception("Missing slider " + setting);
        private static void Press(GameOptionsHud model, HudRect track, float fraction)
        {
            if (!model.PointerDown(track.X + track.Width * fraction, track.Y + track.Height / 2, 0)) throw new Exception("Slider took no press.");
        }
        private static void ClickKey(GameOptionsHud model, GameAction action)
        {
            HudRect r = model.KeyRowRect(action) ?? throw new Exception("Missing key row " + action);
            if (!model.PointerDown(r.X + r.Width / 2, r.Y + r.Height / 2, 0)) throw new Exception("Key row " + action + " took no press.");
            model.PointerUp();
        }
        private static void DragKeyboardToBottom(ClassicGameOptionsPanel panel, GameOptionsHud model)
        {
            (HudRect thumb, HudRect track) = model.KeyBar() ?? throw new Exception("No key list.");
            if (!model.PointerDown(thumb.X + thumb.Width / 2, thumb.Y + thumb.Height / 2, 0)) throw new Exception("Key thumb took no press.");
            model.PointerMove(track.X + track.Width / 2, track.Y + track.Height);
            model.PointerUp();
            TextScrollBar scroll = model.KeyScroll;
            if (scroll.Range <= 0 || scroll.Position != scroll.Range)
                throw new Exception("1440p keyboard thumb did not reach maximum: " + scroll.Position + "/" + scroll.Range);
            Debug.Log("1440p keyboard thumb reached maximum: " + scroll.Position + "/" + scroll.Range);
            panel.Draw();
        }
        private static void ScrollToHeart(ClassicGameOptionsPanel panel, GameOptionsHud model)
        {
            TextScrollBar scroll = model.KeyScroll;
            int row = KeyboardSetup.Actions.ToList().FindIndex(a => a.Action == GameAction.Heart);
            int target = (int)(row * model.KeyLine);
            while (scroll.Position < target && scroll.Position < scroll.Range) scroll.Step(1);
            while (scroll.Position > target) scroll.Step(-1);
            panel.Draw();
        }
        private static bool Capture(string name, ClassicGameOptionsPanel panel)
        {
            RenderTexture.active = _target; var image = new Texture2D(_target.width, _target.height, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0, 0, image.width, image.height), 0, 0); image.Apply();
            var pixels = image.GetPixels32();
            bool ready = pixels.Count(c => c.a > 0) >= 500;
            foreach (var widget in ClassicGameOptionsLayout.ForPage(panel.Model.Page).Where(w => w.Kind == InGameOptionsWidgetKind.Continue || w.Kind == InGameOptionsWidgetKind.Back))
            {
                int count = 0;
                var box = widget.Bounds; float scale = _target.height / 768f;
                for (int y = (int)(box.Y * scale); y < (box.Y + box.Height + 12) * scale; y++)
                    for (int x = (int)(box.X * scale); x < (box.X + box.Width) * scale; x++)
                        if (x >= 0 && x < image.width && y >= 0 && y < image.height && pixels[(image.height - y - 1) * image.width + x].a > 0) count++;
                Debug.Log("Options page button " + name + " " + widget.Kind + " pixels=" + count);
                if (count < 200) ready = false;
            }
            if (!ready)
            {
                UnityEngine.Object.DestroyImmediate(image); RenderTexture.active = null;
                CaptureAttempts.TryGetValue(name, out int attempts); CaptureAttempts[name] = attempts + 1;
                if (attempts >= 3) throw new Exception("Blank or incomplete settings page " + name);
                return false;
            }
            File.WriteAllBytes(Path.Combine(File.ReadAllText(".in-game-options-probe"), name + ".png"), image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image); RenderTexture.active = null; return true;
        }
    }
}
