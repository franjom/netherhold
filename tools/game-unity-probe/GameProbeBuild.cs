// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DK2.Unity.Editor
{
    /// <summary>Builds the game probe as a standalone Development player. Runs only in the
    /// marked scratch project <c>run.ps1</c> makes, never in the owner's.</summary>
    public static class GameProbeBuild
    {
        public static void Build()
        {
            if (!File.Exists(".game-probe")) throw new InvalidOperationException("Isolated scratch project required");
            string[] args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-probeBuild");
            if (at < 0 || at + 1 == args.Length) throw new ArgumentException("-probeBuild required");
            string output = args[at + 1];
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            IncludeShadersFoundByName();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, "Assets/GameProbe.unity");
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/GameProbe.unity" },
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development,
            });
            if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException(report.summary.result.ToString());
        }

        /// <summary>The adapter finds its shaders with <c>Shader.Find</c>, which in a player only
        /// sees shaders something in the build references; nothing in the project does yet, so a
        /// material under Resources (always built) holds each one.</summary>
        private static void IncludeShadersFoundByName()
        {
            string[] names =
            {
                "Universal Render Pipeline/Lit", "Universal Render Pipeline/Unlit", "Standard", "Unlit/Color",
                "DK2/ClassicEffect",
            };
            Directory.CreateDirectory("Assets/Resources/GameProbeShaders");
            foreach (string name in names)
            {
                Shader shader = Shader.Find(name);
                if (shader == null) { Debug.LogWarning($"Game probe: no shader '{name}' to include"); continue; }
                string path = $"Assets/Resources/GameProbeShaders/{name.Replace('/', '_')}.mat";
                if (AssetDatabase.LoadAssetAtPath<Material>(path) == null) AssetDatabase.CreateAsset(new Material(shader), path);
            }
            AssetDatabase.SaveAssets();
        }
    }
}
