// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace DK2.Unity.Editor
{
    /// <summary>Builds the game as a release player (IL2CPP, ADR-0690; no development options) for
    /// the tester package. Runs only in the marked scratch project <c>prepare-project.ps1</c> makes.</summary>
    public static class ReleasePlayerBuild
    {
        public static void Build()
        {
            if (!File.Exists(".game-probe")) throw new InvalidOperationException("Isolated scratch project required");
            string[] args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-playerBuild");
            if (at < 0 || at + 1 == args.Length) throw new ArgumentException("-playerBuild required");
            string output = args[at + 1];
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.IL2CPP);
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/DK2/Scenes/Dungeon.unity" },
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });
            if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException(report.summary.result.ToString());
        }
    }
}
