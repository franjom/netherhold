// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using DK2.Data.Model;
using DK2.Data.Settings;
using DK2.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DK2.Unity.Editor
{
    [InitializeOnLoad]
    public static class OptionsSettingsProbe
    {
        private static ClassicFrontEndMenu _menu;
        private static FrontEndSession _session;
        private static OptionsPagesController _options;
        private static RenderTexture _target;
        private static GameObject _owner;
        private static int _stage, _frames;
        private static double _deadline;
        public static void Run()
        {
            if (!Application.isBatchMode || !File.Exists(".options-settings-probe")) throw new Exception("Isolated options probe required.");
            _deadline = EditorApplication.timeSinceStartup + 120;
            EditorApplication.update += Poll;
        }
        private static void Poll()
        {
            try
            {
                if (EditorApplication.timeSinceStartup > _deadline) throw new Exception("Options probe timeout.");
                if (_stage == 0)
                {
                    _owner = new GameObject("Options settings probe");
                    _options = new OptionsPagesController(new GameSettings());
                    _session = new FrontEndSession(_ => new[] { new KcsFrame(0, 0, -3, new KcsRotation(1,0,0,0,1,0,0,0,1),1,20000) });
                    _session.Update(TimeSpan.Zero); _session.Update(TimeSpan.Zero);
                    _menu = new ClassicFrontEndMenu(_owner.transform, _session,
                        new InstallClassicFrontEndMenuAssets("E:/GogGames/Dungeon Keeper 2"), null, _options);
                    _target = new RenderTexture(1024, 768, 24); _target.Create();
                    _menu.Panel.targetTexture = _target; _menu.Panel.clearColor = true;
                    _menu.Panel.colorClearValue = Color.clear;
                    Show(FrontEndPage.Graphics); _stage++; return;
                }
                _menu.Refresh(false);
                if (++_frames < 8) return; _frames = 0;
                if (_stage == 1)
                {
                    Capture("graphics");
                    _options.Edit(s => s with { Gamma = 45, UiScale = 125 });
                    if (_options.Current.Gamma != 30) throw new Exception("Graphics applied before Confirm.");
                    _options.Accept();
                    // Exercise actual URP volume API resolution and setting writes, without loading a game.
                    var runner = _owner.AddComponent<DungeonRunner>(); runner.enabled = false;
                    typeof(DungeonRunner).GetField("_frontEndMenu", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(runner, _menu);
                    typeof(DungeonRunner).GetMethod("ApplyGameSettings", BindingFlags.NonPublic | BindingFlags.Instance)
                        .Invoke(runner, new object[] { _options.Current });
                    var profile = UnityEngine.Object.FindAnyObjectByType<Volume>();
                    if (profile == null || !profile.sharedProfile.TryGet<ColorAdjustments>(out var colour)
                        || Math.Abs(colour.postExposure.value - Mathf.Log(1.5f, 2)) > .001f)
                        throw new Exception("Gamma did not reach URP colour adjustment.");
                    Show(FrontEndPage.Sound); _stage++; return;
                }
                if (_stage == 2)
                {
                    Capture("sound");
                    _options.Edit(s => s with { MasterVolume = 14 });
                    if (_options.Current.MasterVolume != 14) throw new Exception("Volume did not apply live.");
                    _options.Cancel(); if (_options.Current.MasterVolume != 100) throw new Exception("Cancel did not restore volume.");
                    Show(FrontEndPage.Controls); _stage++; return;
                }
                if (_stage == 3)
                {
                    Capture("controls");
                    if (!_options.BeginKeyCapture(GameAction.Heart) || !_options.CaptureKey(0x20, KeyModifiers.Control))
                        throw new Exception("Key capture failed.");
                    _options.Accept();
                    if (_options.Current.Binding(GameAction.Heart).ScanCode != 0x20) throw new Exception("Binding not retained.");
                    Debug.Log("Options settings verification passed: graphics/sound/controls art; live/revert; key capture; URP gamma.");
                    EditorApplication.Exit(0);
                }
            }
            catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
        }
        private static void Show(FrontEndPage page)
        {
            if (!_session.TryShowPage(page)) throw new Exception("Cannot show " + page);
            _menu.Refresh(false);
        }
        private static void Capture(string page)
        {
            RenderTexture.active = _target;
            var image = new Texture2D(_target.width, _target.height, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0,0,_target.width,_target.height),0,0); image.Apply();
            int nonempty = image.GetPixels32().Count(c => c.a > 0);
            if (nonempty < 500) throw new Exception("Blank options page " + page);
            foreach (FrontEndWidget widget in _menu.Model.Widgets.Where(w => w.Draw == FrontEndDrawKind.Accept || w.Draw == FrontEndDrawKind.Cancel))
            {
                FrontEndRect bounds = widget.ReferenceRect(FrontEndPages.Get(_menu.Model.Page).DrawRect);
                int count = 0;
                for (int y = (int)(bounds.Y * _target.height / 480); y < (bounds.Y + bounds.Height) * _target.height / 480; y++)
                    for (int x = (int)(bounds.X * _target.width / 640); x < (bounds.X + bounds.Width) * _target.width / 640; x++)
                        if (x >= 0 && x < image.width && y >= 0 && y < image.height && image.GetPixel(x, image.height - y - 1).a > .1f) count++;
                if (count < 200) throw new Exception(page + " missing " + widget.Draw + " icon: " + count);
                Debug.Log("Options icon " + page + " " + widget.Draw + " pixels=" + count);
            }
            File.WriteAllBytes(Path.GetFullPath("../" + page + ".png"), image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image); RenderTexture.active = null;
            Debug.Log("Options settings capture " + page + " nonempty=" + nonempty);
        }
    }
}
