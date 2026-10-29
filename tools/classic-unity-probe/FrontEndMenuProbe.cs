// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using DK2.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DK2.Unity.Editor
{
    [InitializeOnLoad]
    public static class FrontEndMenuProbe
    {
        private const string Active = "DK2.FrontEndMenuProbe";
        private static DungeonRunner _runner;
        private static RenderTexture _overlay;
        private static int _stage, _frames;
        private static double _deadline;
        private static FrontEndRect _referenceInk;
        private static RectInt _fontPixels;
        private static string _output;
        private static bool CampaignMode => SessionState.GetBool("DK2.CampaignMenuProbe", false);
        static FrontEndMenuProbe()
        {
            if (SessionState.GetBool(Active, false))
            { _deadline = EditorApplication.timeSinceStartup + 300; EditorApplication.update += Poll; }
        }
        public static void RunCampaign()
        {
            SessionState.SetBool("DK2.CampaignMenuProbe", true);
            Run();
        }
        public static void Run()
        {
            if (!Application.isBatchMode || !File.Exists(".front-end-session-probe")) throw new Exception("Isolated probe project required.");
            EditorSceneManager.OpenScene("Assets/DK2/Scenes/Dungeon.unity");
            var runner = UnityEngine.Object.FindAnyObjectByType<DungeonRunner>();
            JsonUtility.FromJsonOverwrite("{\"sessionMode\":5,\"skipStartup\":true,\"showDiagnostics\":false,\"speed\":0,\"installDirectory\":\"E:/GogGames/Dungeon Keeper 2\"}", runner);
            if (CampaignMode)
            {
                string profile = Path.GetFullPath(Path.Combine(Application.dataPath, "../../campaign-profile-" + DateTime.UtcNow.Ticks + ".dk2p")).Replace('\\', '/');
                DK2.Data.Campaign.CampaignProfileFile.Save(profile,
                    DK2.Simulation.CampaignProgression.CompleteMap(new DK2.Data.Model.CampaignProfile(), new DK2.Data.Model.CampaignSlot(1)));
                JsonUtility.FromJsonOverwrite("{\"campaignProfileFile\":\"" + profile + "\"}", runner);
            }
            SessionState.SetBool(Active, true); EditorApplication.isPlaying = true;
        }
        private static void Poll()
        {
            try
            {
                if (EditorApplication.timeSinceStartup > _deadline) throw new Exception("Menu probe timed out at stage " + _stage);
                if (!Application.isPlaying) return;
                if (_runner == null) _runner = UnityEngine.Object.FindAnyObjectByType<DungeonRunner>();
                if (_runner == null) return;
                if (_runner.StartupFailure != null) throw new Exception(_runner.StartupFailure);
                if (_runner.FrontEndMenu == null) return;
                if (++_frames < 24) return;
                _frames = 0;
                var menu = _runner.FrontEndMenu;
                var model = menu.Model;
                switch (_stage)
                {
                    case 0:
                        using (var icons = new InstallClassicFrontEndMenuAssets("E:/GogGames/Dungeon Keeper 2"))
                        {
                            if (icons.Image("Cancel").width != 48 || icons.Image("Confirm").width != 48)
                                throw new Exception("Menu icons resolved to a different archive directory: expected the root 48px pair.");
                        }
                        _output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../menu-captures-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")));
                        Directory.CreateDirectory(_output);
                        _overlay = new RenderTexture(1024, 768, 24, RenderTextureFormat.ARGB32); _overlay.Create();
                        menu.Panel.targetTexture = _overlay; menu.Panel.clearColor = true; menu.Panel.colorClearValue = Color.clear;
                        FinishPath(); break;
                    case 1:
                        Expect(FrontEndPage.Main);
                        if (!menu.UsingReplacementFont) throw new Exception("The available DK2 TrueType font was not used.");
                        _referenceInk = menu.LabelInkBounds(new FrontEndWidgetId(1));
                        _fontPixels = Capture("main-font", _referenceInk);
                        menu.UseReplacementFont = false; break;
                    case 2:
                        if (menu.UsingReplacementFont || menu.LabelInkBounds(new FrontEndWidgetId(1)) != _referenceInk)
                            throw new Exception("Bitmap fallback changed original label geometry.");
                        RectInt bitmap = Capture("main-bitmap", _referenceInk);
                        if (Math.Abs(bitmap.xMin - _fontPixels.xMin) > 2 || Math.Abs(bitmap.xMax - _fontPixels.xMax) > 2
                            || Math.Abs(bitmap.yMin - _fontPixels.yMin) > 2 || Math.Abs(bitmap.yMax - _fontPixels.yMax) > 2)
                            throw new Exception("Live font ink differs from the original bitmap bounds: " + _fontPixels + " vs " + bitmap);
                        menu.UseReplacementFont = true;
                        model.UpdatePointer(new FrontEndPointer(_referenceInk.X + _referenceInk.Width / 2, _referenceInk.Y + _referenceInk.Height / 2, true));
                        break;
                    case 3:
                        Expect(FrontEndPage.SinglePlayer); Capture("single-player");
                        model.Activate(new FrontEndWidgetId(9)); model.Activate(new FrontEndWidgetId(3)); FinishPath(); break;
                    case 4:
                        Expect(FrontEndPage.Options); Capture("options"); model.Activate(new FrontEndWidgetId(110)); break;
                    case 5:
                        Expect(FrontEndPage.Graphics); Capture("graphics-shell");
                        model.Activate(new FrontEndWidgetId(130)); model.Activate(new FrontEndWidgetId(113)); FinishPath(); break;
                    case 6:
                        Expect(FrontEndPage.Main); model.Activate(new FrontEndWidgetId(4)); FinishPath(); break;
                    case 7:
                        Expect(FrontEndPage.Extras); Capture("extras"); model.Activate(new FrontEndWidgetId(24)); FinishPath(); break;
                    case 8:
                        Expect(FrontEndPage.Main);
                        FrontEndRect options = menu.LabelInkBounds(new FrontEndWidgetId(3));
                        _runner.enabled = false;
                        model.UpdatePointer(new FrontEndPointer(options.X + options.Width / 2, options.Y + options.Height / 2, false));
                        menu.Refresh(false); break;
                    case 9:
                        Capture("main-hover"); _runner.enabled = true;
                        model.Activate(new FrontEndWidgetId(5)); break;
                    case 10:
                        Expect(FrontEndPage.Dialog); Capture("quit-dialog"); model.Activate(new FrontEndWidgetId(668)); break;
                    case 11:
                        Expect(FrontEndPage.Main);
                        var wide = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32); wide.Create();
                        menu.Panel.targetTexture = wide; _overlay.Release(); UnityEngine.Object.DestroyImmediate(_overlay); _overlay = wide;
                        break;
                    case 12:
                        _fontPixels = Capture("main-font-wide", _referenceInk);
                        menu.UseReplacementFont = false; break;
                    case 13:
                        RectInt wideBitmap = Capture("main-bitmap-wide", _referenceInk);
                        if (Math.Abs(wideBitmap.xMin - _fontPixels.xMin) > 2 || Math.Abs(wideBitmap.xMax - _fontPixels.xMax) > 2
                            || Math.Abs(wideBitmap.yMin - _fontPixels.yMin) > 2 || Math.Abs(wideBitmap.yMax - _fontPixels.yMax) > 2)
                            throw new Exception("Widescreen font and bitmap bounds differ: " + _fontPixels + " vs " + wideBitmap);
                        break;
                    default:
                        if (CampaignMode)
                        {
                            if (!AdvanceCampaign(menu)) { _stage++; return; }
                            Debug.Log("Front-end campaign verification passed: reset confirmation, 3D triangle pick, map hover/lift, briefing font/bitmap and return paths.");
                            SessionState.SetBool("DK2.CampaignMenuProbe", false);
                        }
                        else Expect(FrontEndPage.Main);
                        Debug.Log("Front-end menu verification passed: font and bitmap ink within2pixels, navigation, station gates, option shell and Quit cancel.");
                        SessionState.SetBool(Active, false); EditorApplication.update -= Poll; EditorApplication.Exit(0); return;
                }
                _stage++;
            }
            catch (Exception error)
            { SessionState.SetBool(Active, false); Debug.LogException(error); EditorApplication.Exit(1); }
        }
        private static bool AdvanceCampaign(ClassicFrontEndMenu menu)
        {
            var map = _runner.CampaignMap;
            var model = menu.Model;
            switch (_stage)
            {
                case 14: menu.UseReplacementFont = true; model.Activate(new FrontEndWidgetId(1)); break;
                case 15: Expect(FrontEndPage.SinglePlayer); Capture("campaign-single"); model.Activate(new FrontEndWidgetId(6)); break;
                case 16:
                    Expect(FrontEndPage.Dialog); Capture("campaign-reset"); model.Activate(new FrontEndWidgetId(668));
                    if (map.Profile.PlayerLevelStatus[0] != DK2.Data.Model.CampaignLevelStatus.Won) throw new Exception("Reset cancel lost progress.");
                    model.Activate(new FrontEndWidgetId(6)); model.Activate(new FrontEndWidgetId(667)); FinishPath(); break;
                case 17:
                    Expect(FrontEndPage.CampaignMap); Capture("campaign-map"); VerifyMapPick();
                    _runner.enabled = false; map.Hover(new DK2.Data.Model.CampaignSlot(1));
                    _runner.FrontEnd.Update(_runner.FrontEnd.Now + TimeSpan.FromSeconds(.05));
                    typeof(DungeonRunner).GetMethod("RefreshCampaignMap", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_runner, null);
                    menu.Refresh(false); break;
                case 18:
                    Capture("campaign-hover");
                    if (map.LandVisual(new DK2.Data.Model.CampaignSlot(1)).Lift <= 0) throw new Exception("Hovered land did not rise.");
                    if (!map.Select(new DK2.Data.Model.CampaignSlot(1))) throw new Exception("Open campaign slot refused.");
                    _runner.enabled = true; FinishPath(); break;
                case 19: Expect(FrontEndPage.Briefing); Capture("campaign-briefing-font"); menu.UseReplacementFont = false; break;
                case 20: Capture("campaign-briefing-bitmap"); model.Activate(new FrontEndWidgetId(169)); FinishPath(); break;
                case 21: Expect(FrontEndPage.CampaignMap); Capture("campaign-return"); model.Activate(new FrontEndWidgetId(534)); FinishPath(); break;
                case 22:
                    Expect(FrontEndPage.SinglePlayer);
                    string profile = (string)typeof(DungeonRunner).GetField("campaignProfileFile", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_runner);
                    DK2.Data.Campaign.CampaignProfileFile.Save(profile,
                        DK2.Simulation.CampaignProgression.CompleteMap(map.Profile, new DK2.Data.Model.CampaignSlot(1)));
                    _runner = _runner.LoadLevel(GameSessionRequest.FrontEnd(FrontEndPage.CampaignMap)); break;
                case 23:
                    menu.Panel.targetTexture = _overlay; menu.Panel.clearColor = true; menu.Panel.colorClearValue = Color.clear;
                    FinishPath(); break;
                case 24:
                    Expect(FrontEndPage.CampaignMap);
                    if (map.LandVisual(new DK2.Data.Model.CampaignSlot(1)).TexturePhase.Raw != 0x55a)
                        throw new Exception("Return-to-map session lost won texture phase initialization.");
                    Capture("campaign-return-session"); break;
                default: Expect(FrontEndPage.CampaignMap); return true;
            }
            return false;
        }
        private static void VerifyMapPick()
        {
            var scene = (DungeonScene)typeof(DungeonRunner).GetField("_scene", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_runner);
            var slots = (System.Collections.Generic.Dictionary<int, DK2.Data.Model.CampaignSlot>)typeof(DungeonRunner)
                .GetField("_campaignPickSlots", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_runner);
            int id = slots.First(p => p.Value.Value == 1 && p.Key > -2_000_000).Key;
            var mesh = UnityEngine.Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None).First(f => f.name == "object " + id);
            var vertices = mesh.sharedMesh.vertices; var triangles = mesh.sharedMesh.triangles;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 point = mesh.transform.TransformPoint((vertices[triangles[i]] + vertices[triangles[i + 1]] + vertices[triangles[i + 2]]) / 3);
                var ray = new Ray(Camera.main.transform.position, (point - Camera.main.transform.position).normalized);
                if (scene.PickFrontEndMap(ray, slots).Value == 1) return;
            }
            throw new Exception("No rendered triangle could pick campaign slot1.");
        }

        private static void FinishPath()
        {
            FieldInfo field = typeof(DungeonRunner).GetField("_frontEndStarted", BindingFlags.Instance | BindingFlags.NonPublic);
            field.SetValue(_runner, (double)field.GetValue(_runner) - 40);
        }
        private static void Expect(FrontEndPage page)
        { if (_runner.FrontEnd.Page != page) throw new Exception("Expected " + page + " at stage " + _stage + ", got " + _runner.FrontEnd.Page); }
        private static RectInt Capture(string name, FrontEndRect? ink = null)
        {
            RenderTexture.active = _overlay;
            var pixels = new Texture2D(_overlay.width, _overlay.height, TextureFormat.RGBA32, false);
            pixels.ReadPixels(new Rect(0, 0, _overlay.width, _overlay.height), 0, 0); pixels.Apply();
            if (pixels.GetPixels32().Count(c => c.a > 20) < 500) throw new Exception("Blank menu " + name);
            File.WriteAllBytes(Path.Combine(_output, name + "-overlay.png"), pixels.EncodeToPNG());
            RectInt measured = default;
            if (ink.HasValue)
            {
                FrontEndRect box = ink.Value.Scale(_overlay.width / 640f, _overlay.height / 480f).Inflate(3);
                int left = pixels.width, top = pixels.height, right = -1, bottom = -1, bright = 0;
                for (int y = Math.Max(0, (int)box.Y); y < Math.Min(pixels.height, box.Y + box.Height); y++)
                    for (int x = Math.Max(0, (int)box.X); x < Math.Min(pixels.width, box.X + box.Width); x++)
                    {
                        Color pixel = pixels.GetPixel(x, pixels.height - y - 1);
                        if (pixel.a > .2f)
                        {
                            left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y);
                            if (Math.Max(pixel.r, Math.Max(pixel.g, pixel.b)) > .2f) bright++;
                        }
                    }
                if (right < left) throw new Exception("Menu item has no visible glyphs: " + name);
                if (bright < 100) throw new Exception("Menu glyph RGB is black: " + name);
                measured = new RectInt(left, top, right - left + 1, bottom - top + 1);
            }
            UnityEngine.Object.DestroyImmediate(pixels); RenderTexture.active = null;
            var camera = Camera.main;
            var target = new RenderTexture(_overlay.width, _overlay.height, 24); target.Create(); camera.targetTexture = target;
            typeof(DungeonRunner).GetMethod("ApplyFrontEndCamera", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { _runner.FrontEnd.Pose.Value });
            for (int i = 0; i < 3; i++) RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            var material = new Material(Shader.Find("Hidden/DK2/FrontEndProbeComposite"));
            Graphics.Blit(_overlay, target, material);
            pixels = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false); RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply();
            File.WriteAllBytes(Path.Combine(_output, name + ".png"), pixels.EncodeToPNG());
            RenderTexture.active = null; camera.targetTexture = null; target.Release();
            UnityEngine.Object.DestroyImmediate(pixels); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(material);
            Debug.Log("Front-end menu capture " + name + ": " + measured + "; " + _output);
            return measured;
        }
    }
}
