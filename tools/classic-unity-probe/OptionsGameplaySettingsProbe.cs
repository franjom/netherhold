// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.IO;
using System.Reflection;
using DK2.Data.Campaign;
using DK2.Data.Model;
using DK2.Data.Settings;
using DK2.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DK2.Unity.Editor
{
    [InitializeOnLoad]
    public static class OptionsGameplaySettingsProbe
    {
        private const string Active = "DK2.OptionsGameplaySettingsProbe";
        private static double _deadline;
        static OptionsGameplaySettingsProbe()
        {
            if (SessionState.GetBool(Active, false)) BeginPolling();
        }
        public static void Run()
        {
            if (!Application.isBatchMode || !File.Exists(".options-settings-probe")) throw new Exception("Isolated options probe required.");
            EditorSceneManager.OpenScene("Assets/DK2/Scenes/Dungeon.unity");
            var runner = UnityEngine.Object.FindAnyObjectByType<DungeonRunner>();
            string profile = Path.GetFullPath("../startup-campaign.dk2p");
            CampaignProfileFile.Save(profile, new CampaignProfile());
            GameSettingsFile.Save(Path.Combine(Path.GetDirectoryName(profile), "game-settings.ini"),
                new GameSettings { Gamma = 45, ScrollSpeed = 17 });
            JsonUtility.FromJsonOverwrite("{\"sessionMode\":1,\"levelName\":\"Level1\",\"classicMeshes\":true,\"showDiagnostics\":false,\"speed\":0,\"installDirectory\":\"E:/GogGames/Dungeon Keeper 2\"}", runner);
            typeof(DungeonRunner).GetField("campaignProfileFile", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(runner, profile);
            SessionState.SetInt(Active + ".stage", 0); SessionState.SetBool(Active, true);
            EditorApplication.EnterPlaymode();
        }
        private static void BeginPolling()
        {
            _deadline = EditorApplication.timeSinceStartup + 300;
            EditorApplication.update += Poll;
        }
        private static void Poll()
        {
            try
            {
                if (EditorApplication.timeSinceStartup > _deadline) throw new Exception("Gameplay settings probe timeout.");
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
                var runner = UnityEngine.Object.FindAnyObjectByType<DungeonRunner>();
                if (runner == null || !runner.enabled) return;
                if (runner.StartupFailure != null) throw new Exception(runner.StartupFailure);
                var controls = typeof(DungeonRunner).GetField("_classicCameraControls", BindingFlags.NonPublic | BindingFlags.Instance)
                    .GetValue(runner) as ClassicCameraControls;
                if (controls == null) return;
                if (controls.ScrollSpeed != 17) throw new Exception("Loaded scroll speed was not applied: " + controls.ScrollSpeed);
                GameObject settings = GameObject.Find("DK2 display settings");
                Volume volume = settings == null ? null : settings.GetComponent<Volume>();
                if (volume == null || !volume.sharedProfile.TryGet<ColorAdjustments>(out var colour)
                    || Math.Abs(colour.postExposure.value - Mathf.Log(1.5f, 2)) > .001f)
                    throw new Exception("Loaded gamma was not applied.");
                int stage = SessionState.GetInt(Active + ".stage", 0);
                if (stage == 0)
                {
                    Debug.Log("Gameplay settings fresh startup passed: scroll17, gamma45.");
                    SessionState.SetInt(Active + ".stage", 1);
                    runner.LoadLevel(GameSessionRequest.Campaign("Level1")); return;
                }
                Debug.Log("Gameplay settings restart passed: scroll17, gamma45.");
                SessionState.SetBool(Active, false); EditorApplication.Exit(0);
            }
            catch (Exception ex) { SessionState.SetBool(Active, false); Debug.LogException(ex); EditorApplication.Exit(1); }
        }
    }
}
