// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DK2.Unity.HudProbe.Editor
{
    public static class HudPilotEntry
    {
        private static DateTime _deadline;
        public static void Run()
        {
            if (!File.Exists(".dk2-hud-pilot")) throw new InvalidOperationException("Marked isolated project required.");
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DK2_HUD_PROBE_OUTPUT")))
                throw new InvalidOperationException("Output directory required.");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Pilot render camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            new GameObject("HUD shader/font feasibility pilot").AddComponent<HudToolkitPilot>();
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            _deadline = DateTime.UtcNow.AddMinutes(3);
            EditorApplication.update += Check;
            EditorApplication.EnterPlaymode();
        }
        private static void Check()
        {
            if (DateTime.UtcNow > _deadline) { Debug.LogError("HUD pilot timed out."); EditorApplication.Exit(1); return; }
            if (!EditorApplication.isPlaying) return;
            EditorApplication.QueuePlayerLoopUpdate();
            var pilot = UnityEngine.Object.FindAnyObjectByType<HudToolkitPilot>();
            if (pilot != null && pilot.Done) EditorApplication.Exit(pilot.Passed ? 0 : 1);
        }
    }
}
