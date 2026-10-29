// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using DK2.Presentation;
using DK2.Simulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DK2.Unity.Editor
{
    [InitializeOnLoad]
    public static class FrontEndSessionProbe
    {
        private const string Active = "DK2.FrontEndSessionProbe";
        private static int _stage, _frames;
        private static double _deadline;
        private static DungeonRunner _runner, _old;
        private static bool _waiting;
        private static readonly FrontEndTransition[] Stations =
        {
            new(FrontEndPath.EntryToOptions, FrontEndState.OptionsReady),
            new(FrontEndPath.EntryToExtras, FrontEndState.ExtrasReady),
            new(FrontEndPath.EntryToTable, FrontEndState.CampaignMap),
            new(FrontEndPath.EntryToPetDungeon, FrontEndState.PetDungeonReady),
        };

        static FrontEndSessionProbe()
        {
            if (SessionState.GetBool(Active, false))
            {
                _deadline = EditorApplication.timeSinceStartup + 300;
                EditorApplication.update += Poll;
            }
        }

        public static void Run()
        {
            if (!Application.isBatchMode || !File.Exists(".front-end-session-probe"))
                throw new InvalidOperationException("Requires the isolated front-end probe project.");
            EditorSceneManager.OpenScene("Assets/DK2/Scenes/Dungeon.unity");
            var runner = UnityEngine.Object.FindAnyObjectByType<DungeonRunner>();
            JsonUtility.FromJsonOverwrite("{\"sessionMode\":5,\"skipStartup\":true,\"fogOfWar\":true,\"showDiagnostics\":false,\"speed\":0,\"installDirectory\":\"E:/GogGames/Dungeon Keeper 2\"}", runner);
            SessionState.SetBool(Active, true);
            EditorApplication.isPlaying = true;
        }

        private static void Poll()
        {
            try
            {
                if (EditorApplication.timeSinceStartup > _deadline) throw new Exception("Front-end probe timed out at stage " + _stage);
                if (!Application.isPlaying) return;
                if (_runner == null) _runner = UnityEngine.Object.FindAnyObjectByType<DungeonRunner>();
                if (_runner == null) return;
                if (_runner.StartupFailure != null) throw new Exception(_runner.StartupFailure);
                if (!_waiting)
                {
                    if (_runner.FrontEnd == null) return;
                    _frames++;
                    if (_frames < 20) return;
                    double started = (double)Field("_frontEndStarted").GetValue(_runner);
                    Field("_frontEndStarted").SetValue(_runner, started - 40);
                    _waiting = true;
                    _frames = 0;
                    return;
                }
                if (++_frames < 20 || _runner.FrontEnd?.PathPlaying == true) return;
                if (_stage <= Stations.Length)
                {
                    CheckFrontEnd();
                    Capture(_stage == 0 ? "main" : Stations[_stage - 1].Path.ToString());
                    if (_stage < Stations.Length)
                    {
                        if (!_runner.FrontEnd.TryTransition(Stations[_stage])) throw new Exception("Station request rejected.");
                        _stage++; _waiting = false; _frames = 0;
                        return;
                    }
                    _old = _runner;
                    _runner = _runner.LoadLevel(GameSessionRequest.Campaign("Level1"));
                    _stage++; _frames = 0;
                    return;
                }
                if (_stage == Stations.Length + 1)
                {
                    if (Field("_simulation").GetValue(_runner) == null) return;
                    if (_old != null || _runner.FrontEnd != null || _runner.LoadedLevelName != "Level1"
                        || Field("_hudSources").GetValue(_runner) == null || !(bool)Field("fogOfWar").GetValue(_runner))
                        throw new Exception("Campaign replacement did not initialize/dispose/preserve fog settings correctly.");
                    _old = _runner;
                    _runner = _runner.LoadLevel(GameSessionRequest.FrontEnd(FrontEndPage.Sessions));
                    _stage++; _waiting = false; _frames = 0;
                    return;
                }
                CheckFrontEnd();
                if (_old != null || _runner.FrontEnd.Path != FrontEndPath.EntryToOptions
                    || _runner.FrontEnd.Page != FrontEndPage.Multiplayer) throw new Exception("Return-page route or disposal failed.");
                Capture("multiplayer-return");
                Debug.Log("Front-end session verification passed: five stations, frozen simulation/no HUD, campaign round trip and native multiplayer return.");
                SessionState.SetBool(Active, false);
                EditorApplication.update -= Poll;
                EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                SessionState.SetBool(Active, false);
                Debug.LogException(error);
                EditorApplication.Exit(1);
            }
        }

        private static FieldInfo Field(string name) => typeof(DungeonRunner).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new Exception("Missing runner field " + name);

        private static void CheckFrontEnd()
        {
            var simulation = (GameSimulation)Field("_simulation").GetValue(_runner);
            if (simulation.World.CurrentTick.Value != 0 || Field("_hudSources").GetValue(_runner) != null
                || _runner.SessionMode != GameSessionMode.FrontEnd) throw new Exception("Front end ticked or created gameplay HUD.");
            if (UnityEngine.Object.FindObjectsByType<DungeonRunner>(FindObjectsSortMode.None).Length != 1)
                throw new Exception("Multiple active runners survived switching.");
        }

        private static void Capture(string name)
        {
            var camera = Camera.main;
            if (camera == null || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new Exception("No rendering camera/device.");
            var target = new RenderTexture(1024, 768, 24);
            target.Create();
            camera.targetTexture = target;
            typeof(DungeonRunner).GetMethod("UpdateFrontEnd", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(_runner, null);
            for (int i = 0; i < 3; i++)
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            var pixels = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply();
            if (pixels.GetPixels32().Count(p => p.r > 20 || p.g > 20 || p.b > 20) < 10000) throw new Exception("Blank station " + name);
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../captures"));
            Directory.CreateDirectory(output);
            File.WriteAllBytes(Path.Combine(output, name + ".png"), pixels.EncodeToPNG());
            Debug.Log("Front-end capture " + name + ": " + camera.transform.position + "; objects=" + _runner.GetComponentsInChildren<Renderer>().Length);
            RenderTexture.active = null; camera.targetTexture = null; target.Release();
            UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(pixels);
        }
    }
}
