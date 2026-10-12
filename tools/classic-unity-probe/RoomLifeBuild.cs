// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DK2.Unity.Editor
{
    public static class RoomLifeBuild
    {
        public static void Build()
        {
            if (!File.Exists(".room-life-probe")) throw new InvalidOperationException("Isolated scratch project required");
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-probeBuild");
            if (at < 0 || at + 1 == args.Length) throw new ArgumentException("-probeBuild required");
            string output = args[at + 1];
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            Directory.CreateDirectory("Assets/Resources");
            if (AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/RoomLifeLit.mat") == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) throw new InvalidOperationException("URP Lit unavailable");
                AssetDatabase.CreateAsset(new Material(shader), "Assets/Resources/RoomLifeLit.mat");
                AssetDatabase.SaveAssets();
            }
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, "Assets/RoomLifeProbe.unity");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/RoomLifeProbe.unity" }, locationPathName = output,
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException(report.summary.result.ToString());
        }
    }
}
