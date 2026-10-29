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
using DK2.Data.Save;
using DK2.Presentation;
using DK2.Simulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace DK2.Unity.Editor
{
    [InitializeOnLoad]
    public static class SessionSaveLoadProbe
    {
        private const string Active = "DK2.SessionSaveLoadProbe";
        private static double _deadline;
        static SessionSaveLoadProbe()
        {
            if (SessionState.GetBool(Active, false)) { _deadline = EditorApplication.timeSinceStartup + 300; EditorApplication.update += Poll; }
        }
        private static object Get(object o, string name) => o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(o);
        private static void Set(object o, string name, object value) => o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(o, value);
        private static void Call(object o, string name, params object[] args) => o.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(o, args);
        private static string Folder => Path.GetFullPath("../sessions");
        private static string SavePath => SavedSessionFile.PathForName(Folder, "checkpoint");
        public static void Run()
        {
            if (!Application.isBatchMode || !File.Exists(".session-save-load-probe")) throw new Exception("Isolated probe project required.");
            EditorSceneManager.OpenScene("Assets/DK2/Scenes/Dungeon.unity");
            var runner = UnityEngine.Object.FindAnyObjectByType<DungeonRunner>();
            string profile = Path.GetFullPath("../campaign.dk2p");
            CampaignProfileFile.Save(profile, new CampaignProfile { PlayerValues = new uint[] { 42,0,0,0,0,0,0,0 } });
            JsonUtility.FromJsonOverwrite("{\"sessionMode\":1,\"levelName\":\"Level1\",\"frontEndReturnPage\":26,\"seed\":317,\"classicMeshes\":true,\"showDiagnostics\":false,\"speed\":0,\"installDirectory\":\"E:/GogGames/Dungeon Keeper 2\"}", runner);
            Set(runner, "campaignProfileFile", profile); Set(runner, "hiScoresFile", Path.GetFullPath("../HiScores.dat"));
            SessionState.SetInt(Active + ".stage", 0); SessionState.SetBool(Active, true);
            EditorApplication.EnterPlaymode();
        }
        private static void Poll()
        {
            try
            {
                if (EditorApplication.timeSinceStartup > _deadline) throw new Exception("Save/load probe timeout.");
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
                var runner = UnityEngine.Object.FindAnyObjectByType<DungeonRunner>();
                if (runner == null || !runner.enabled) return;
                if (runner.StartupFailure != null) throw new Exception(runner.StartupFailure);
                if (Get(runner, "_savePanel") == null) return;
                GameSimulation simulation = (GameSimulation)Get(runner, "_simulation");
                int stage = SessionState.GetInt(Active + ".stage", 0);
                if (stage == 0)
                {
                    SessionState.SetString(Active + ".initial", simulation.StateHash().ToString());
                    Call(runner, "RunTicks", 4, InputFrame.Empty); Call(runner, "WaitForTick");
                    simulation.World.Carry = new CampaignCarry { HiddenLandFound = 1 };
                    var saves = new SaveSlotsView(Folder); saves.SetName("checkpoint"); Set(runner, "_saves", saves);
                    Call(runner, "SaveSession"); if (!File.Exists(SavePath)) throw new Exception("Save did not create the requested file.");
                    SavedSession saved = SavedSessionFile.Load(SavePath);
                    SessionState.SetString(Active + ".hash", simulation.StateHash().ToString());
                    SessionState.SetString(Active + ".tick", simulation.World.CurrentTick.Value.ToString());
                    SessionState.SetString(Active + ".profile", Convert.ToBase64String(CampaignProfileFile.Write(saved.Profile)));
                    SessionState.SetInt(Active + ".stage", 1);
                    Call(runner, "LoadSession", SavePath); return;
                }
                if (stage == 1 || stage == 3)
                {
                    if (runner.LoadedLevelName != "Level1" || Get(runner, "_resumeSession") == null) return;
                    if (simulation.World.CurrentTick.Value.ToString() != SessionState.GetString(Active + ".tick", ""))
                        throw new Exception("Resumed checkpoint advanced a tick: " + simulation.World.CurrentTick);
                    if (simulation.StateHash().ToString() != SessionState.GetString(Active + ".hash", "")) throw new Exception("Resumed checkpoint hash differs.");
                    if (simulation.World.Carry.HiddenLandFound != 1) throw new Exception("Carry was not restored.");
                    if (Convert.ToBase64String(CampaignProfileFile.Write((CampaignProfile)Get(runner, "_campaignProfile")))
                        != SessionState.GetString(Active + ".profile", "")) throw new Exception("Profile was not restored.");
                    Debug.Log("Session restore passed " + (stage == 1 ? "in-game" : "front-end") + ": tick/hash/Level1/profile/carry.");
                    if (stage == 1)
                    {
                        SessionState.SetInt(Active + ".stage", 2); runner.LoadLevel(GameSessionRequest.FrontEnd()); return;
                    }
                    SessionState.SetInt(Active + ".stage", 4);
                    runner.LoadLevel(GameSessionRequest.Restart(GameSessionRequest.Campaign("Level1"))); return;
                }
                if (stage == 2)
                {
                    if (runner.FrontEndMenu == null || runner.FrontEnd.PathPlaying) return;
                    var saves = new SaveSlotsView(Folder); if (!saves.Select(0)) throw new Exception("Saved file missing from front-end list.");
                    Set(runner, "_saves", saves); runner.FrontEndMenu.Model.SetSaveBrowser(saves);
                    if (!runner.FrontEnd.TryShowPage(FrontEndPage.Load)) return;
                    SessionState.SetInt(Active + ".stage", 3);
                    if (!runner.FrontEndMenu.Model.Activate(new FrontEndWidgetId(73))) throw new Exception("Front-end Load accept failed.");
                    return;
                }
                if (stage == 4)
                {
                    if (Get(runner, "_resumeSession") != null) return;
                    if (runner.LoadedLevelName != "Level1" || (ulong)Get(runner, "seed") != 317UL
                        || simulation.StateHash().ToString() != SessionState.GetString(Active + ".initial", ""))
                        throw new Exception("Restart did not recreate original Level1 with seed317.");
                    var controls = (ClassicHudControls)Get(runner, "_hudControls");
                    controls.UpdateResult(default);
                    var tabs = Get(runner, "_messageTabs"); Set(tabs, "_objectiveOpen", true);
                    Call(runner, "ApplyHudAction", ClassicHudAction.ToggleOptions);
                    Call(runner, "ApplyHudAction", ClassicHudAction.ShowObjective);
                    if (!controls.OptionsVisible || !controls.ObjectiveVisible) throw new Exception("Objective did not retain Options pause.");
                    Set(tabs, "_optionsOpen", true);
                    using (var down = PointerDownEvent.GetPooled()) ((VisualElement)Get(tabs, "_objectivePanel")).SendEvent(down);
                    if (!(bool)Get(tabs, "_objectiveOpen")) throw new Exception("Options objective click cleared review state.");
                    Call(runner, "ApplyHudAction", ClassicHudAction.ToggleOptions);
                    if (controls.OptionsVisible || controls.ObjectiveVisible || !(bool)Get(tabs, "_objectiveOpen")) throw new Exception("Continue did not restore prior objective state.");
                    Debug.Log("Session restart and Options objective/Continue verification passed.");
                    SessionState.SetBool(Active, false); EditorApplication.Exit(0);
                }
            }
            catch (Exception ex) { SessionState.SetBool(Active, false); Debug.LogException(ex); EditorApplication.Exit(1); }
        }
    }
}
