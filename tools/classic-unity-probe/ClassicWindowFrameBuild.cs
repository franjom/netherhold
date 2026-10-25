// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace DK2.Unity.Editor
{
    public static class ClassicWindowFrameBuild
    {
        public static void Build()
        {
            if (!File.Exists(".classic-window-frame-probe"))
                throw new InvalidOperationException("Requires the marked isolated scratch project from run-window-frames.ps1.");
            string[] args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "-windowProbeBuild");
            if (at < 0 || at + 1 >= args.Length) throw new ArgumentException("-windowProbeBuild output required.");
            Directory.CreateDirectory(Path.GetDirectoryName(args[at + 1]));
            var scene = EditorSceneManager.OpenScene("Assets/DK2/Scenes/Dungeon.unity", OpenSceneMode.Single);
            DungeonRunner runner = UnityEngine.Object.FindAnyObjectByType<DungeonRunner>();
            if (runner == null) throw new InvalidOperationException("Production scene has no DungeonRunner.");
            var serialized = new SerializedObject(runner);
            bool arrival = Environment.GetEnvironmentVariable("DK2_PORTAL_INTRO_PROFILE") == "1";
            serialized.FindProperty("levelName").stringValue = arrival ? "Level2" : "Level8";
            serialized.FindProperty("installDirectory").stringValue = Environment.GetEnvironmentVariable("DK2_DIR");
            foreach (string name in new[] { "speed", "cameraPanSpeed" })
                serialized.FindProperty(name).floatValue = 0;
            foreach (string name in new[] { "classicVertexLighting", "classicMeshes" }) serialized.FindProperty(name).boolValue = true;
            foreach (string name in new[] { "nativeInitialAmbient", "cheapLighting", "tickOnWorkerThread", "showDiagnostics", "fogOfWar", "frameWholeMap" })
                serialized.FindProperty(name).boolValue = false;
            bool motion = Environment.GetEnvironmentVariable("DK2_LEVEL8_MOTION_PROBE") == "1";
            if (arrival)
                foreach (string name in new[] { "nativeInitialAmbient", "tickOnWorkerThread", "showDiagnostics", "fogOfWar" })
                    serialized.FindProperty(name).boolValue = true;
            if (motion)
            {
                serialized.FindProperty("speed").floatValue = 1;
                serialized.FindProperty("tickOnWorkerThread").boolValue = true;
            }
            serialized.FindProperty("seed").intValue = 1;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var probe = new GameObject("Windowed frame performance probe");
            if (arrival) probe.AddComponent<PortalIntroductionProfile>();
            else if (motion) probe.AddComponent<Level8MotionProbe>();
            else if (Environment.GetEnvironmentVariable("DK2_WINDOW_ROTATION_PROBE") == "1") probe.AddComponent<ClassicRotationProbe>();
            else probe.AddComponent<ClassicWindowFrameProbe>();
            EditorSceneManager.SaveScene(scene, "Assets/ClassicWindowFrameProbe.unity");
            PlayerSettings.enableFrameTimingStats = Environment.GetEnvironmentVariable("DK2_PROBE_CAPTURE_MODE") != "fps";
            PlayerSettings.runInBackground = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = arrival ? 1920 : 1280; PlayerSettings.defaultScreenHeight = arrival ? 1080 : 720;
            PlayerSettings.resizableWindow = false;
            if (Environment.GetEnvironmentVariable("DK2_PROBE_COMPARE_APIS") == "1")
            {
                PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
                PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,
                    new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.Direct3D12, GraphicsDeviceType.Direct3D11 });
            }
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/ClassicWindowFrameProbe.unity" }, locationPathName = args[at + 1],
                target = BuildTarget.StandaloneWindows64,
                options = Environment.GetEnvironmentVariable("DK2_PROBE_RELEASE") == "1" ? BuildOptions.None : BuildOptions.Development,
            });
            if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException(report.summary.result.ToString());
        }
    }
}
