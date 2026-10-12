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
    public static class ClassicCrowdBuild
    {
        public static void Build()
        {
            if (!File.Exists(".classic-crowd-probe")) throw new InvalidOperationException("Run tools/classic-unity-probe/run.ps1: requires isolated scratch project");
            string[] args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-probeBuild");
            if (at < 0 || at + 1 == args.Length) throw new ArgumentException("-probeBuild output required");
            string output = args[at + 1];
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            Directory.CreateDirectory("Assets/Resources");
            if (AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/CrowdLit.mat") == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) throw new InvalidOperationException("URP Lit shader unavailable");
                AssetDatabase.CreateAsset(new Material(shader), "Assets/Resources/CrowdLit.mat");
                AssetDatabase.SaveAssets();
            }
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/CrowdVAT.mat") == null)
            {
                var shader = Shader.Find("DK2/Probe/VAT Lit");
                if (shader == null) throw new InvalidOperationException("VAT shader unavailable");
                AssetDatabase.CreateAsset(new Material(shader), "Assets/Resources/CrowdVAT.mat");
                AssetDatabase.SaveAssets();
            }
            EditorSceneManager.SaveScene(scene, "Assets/ClassicCrowdProbe.unity");
            PlayerSettings.enableFrameTimingStats = true;
            var options = Array.IndexOf(args, "-probeDevelopment") >= 0 ? BuildOptions.Development : BuildOptions.None;
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            { scenes = new[] { "Assets/ClassicCrowdProbe.unity" }, locationPathName = output, target = BuildTarget.StandaloneWindows64, options = options });
            if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException(report.summary.result.ToString());
        }
    }
}
