// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DK2.Core;
using DK2.Presentation;
using DK2.Simulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace DK2.Unity.Editor
{
    public static class ClassicStartupCaptureProbe
    {
        [Serializable] private sealed class Sample
        {
            public int EffectMilliseconds, SimulationTick, Particles;
            public string Image;
        }
        [Serializable] private sealed class Result
        {
            public bool Passed;
            public string Error, UnityVersion;
            public int Width = 1024, Height = 540;
            public bool NativeInitialAmbient = true, FogOfWar;
            public Vector3 CameraPosition, CameraEulerAngles;
            public float CameraFieldOfView;
            public Sample[] Samples;
        }
        private static DungeonRunner _runner;
        private static Camera _camera;
        private static RenderTexture _target;
        private static Texture2D _pixels;
        private static long _milliseconds;
        private static string _output;
        private static bool _fogOfWar;
        private static readonly List<Sample> Samples = new List<Sample>();
        private static readonly HashSet<int> CaptureTimes = new HashSet<int> { 0, 500, 1000, 2000, 4000, 7000, 9000 };
        private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        public static void Run()
        {
            if (!Application.isBatchMode || !Application.dataPath.Replace('\\', '/').Contains("/build/step5-lighting-project/Assets"))
                throw new InvalidOperationException("Use only the isolated Step 5 batch capture project.");
            EditorApplication.update += StartWhenReady;
        }

        private static void StartWhenReady()
        {
            EditorApplication.update -= StartWhenReady;
            try
            {
                _output = Environment.GetEnvironmentVariable("DK2_STARTUP_CAPTURE_OUTPUT")
                    ?? Path.GetFullPath(Path.Combine(Application.dataPath, "../../step5-startup-parity"));
                Directory.CreateDirectory(_output);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var cameraObject = new GameObject("Pinned gameplay camera");
                cameraObject.tag = "MainCamera";
                _camera = cameraObject.AddComponent<Camera>();
                _camera.enabled = true;
                _camera.clearFlags = CameraClearFlags.SolidColor;
                _camera.backgroundColor = Color.black;
                _camera.pixelRect = new Rect(0, 0, 1024, 540);
                _camera.aspect = 1024f / 540;
                _target = new RenderTexture(1024, 540, 24, RenderTextureFormat.ARGB32);
                _target.Create();
                _camera.targetTexture = _target;
                _pixels = new Texture2D(1024, 540, TextureFormat.RGBA32, false);
                _runner = new GameObject("Pinned Level1 presentation").AddComponent<DungeonRunner>();
                _runner.enabled = false;
                _fogOfWar = Environment.GetEnvironmentVariable("DK2_STARTUP_CAPTURE_FOG") == "1";
                Set("levelName", "Level1"); Set("classicMeshes", true); Set("classicVertexLighting", true);
                Set("nativeInitialAmbient", true); Set("cheapLighting", false); Set("highWalls", true);
                Set("fogOfWar", _fogOfWar); Set("frameWholeMap", false); Set("speed", 0f);
                Set("showDiagnostics", false); Set("tickOnWorkerThread", false);
                typeof(DungeonRunner).GetProperty("EffectClock", Private).SetValue(_runner,
                    (Func<EffectTime>)(() => new EffectTime(_milliseconds)));
                typeof(DungeonRunner).GetMethod("StartGame", Private).Invoke(_runner, null);
                if (Camera.main != _camera || _camera.transform.position.y <= DungeonScene.FloorHeight)
                    throw new InvalidOperationException("The production camera was not positioned.");
                EditorApplication.update += CaptureFrame;
                EditorApplication.QueuePlayerLoopUpdate();
            }
            catch (Exception error) { Finish(error); }
        }

        private static void Set(string name, object value) => typeof(DungeonRunner).GetField(name, Private).SetValue(_runner, value);

        private static void CaptureFrame()
        {
            try
            {
                int targetTick = 1 + (int)(_milliseconds * SimulationTiming.TicksPerSecond / 1000);
                int delta = checked((int)(targetTick - _runner.Simulation.World.CurrentTick.Value));
                if (delta > 0) typeof(DungeonRunner).GetMethod("RunTicks", Private)
                    .Invoke(_runner, new object[] { delta, InputFrame.Empty });
                typeof(DungeonRunner).GetMethod("Update", Private).Invoke(_runner, null);
                if (CaptureTimes.Contains((int)_milliseconds))
                {
                    var request = new RenderPipeline.StandardRequest { destination = _target };
                    if (!RenderPipeline.SupportsRenderRequest(_camera, request))
                        throw new NotSupportedException("The active renderer cannot capture this camera.");
                    RenderPipeline.SubmitRenderRequest(_camera, request);
                    RenderTexture.active = _target;
                    _pixels.ReadPixels(new Rect(0, 0, 1024, 540), 0, 0); _pixels.Apply();
                    string image = "startup-" + _milliseconds.ToString("D4") + "ms.png";
                    File.WriteAllBytes(Path.Combine(_output, image), _pixels.EncodeToPNG());
                    var effects = (ClassicConstructionEffects)typeof(DungeonRunner).GetField("_constructionEffects", Private).GetValue(_runner);
                    Samples.Add(new Sample { EffectMilliseconds = (int)_milliseconds, SimulationTick = targetTick,
                        Particles = effects.Model.Particles.Count, Image = image });
                }
                _milliseconds += 50;
                if (_milliseconds > 9000) Finish(null);
                else EditorApplication.QueuePlayerLoopUpdate();
            }
            catch (Exception error) { Finish(error); }
        }

        private static void Finish(Exception error)
        {
            EditorApplication.update -= CaptureFrame;
            if (!string.IsNullOrEmpty(_output)) File.WriteAllText(Path.Combine(_output, "result.json"),
                JsonUtility.ToJson(new Result { Passed = error == null, Error = error?.ToString(),
                    UnityVersion = Application.unityVersion, Samples = Samples.ToArray(),
                    FogOfWar = _fogOfWar,
                    CameraPosition = _camera != null ? _camera.transform.position : default,
                    CameraEulerAngles = _camera != null ? _camera.transform.eulerAngles : default,
                    CameraFieldOfView = _camera != null ? _camera.fieldOfView : 0 }, true));
            if (error != null) Debug.LogException(error);
            EditorApplication.Exit(error == null ? 0 : 1);
        }
    }
}
