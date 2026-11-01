// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DK2.Core;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace DK2.Unity.Editor
{
    // Exercises the 0x0049BD20 death request and 0x0049B5E0 cleanup through the production
    // Level7 viewer. GPU captures check remake integration, not original-game pixel parity.
    [InitializeOnLoad]
    public static class HeartDestructionProbe
    {
        [Serializable] private sealed class Metrics
        {
            public string unity, gpu;
            public long deathTick = -1;
            public int deathRequestHeightRaw = int.MinValue;
            public int deathRequests, rivalImps, peakDebris, peakExplosionSprites, captures, effectChangedPixels;
            public bool playMode, pathPlayingAtDeath, debrisExpired, explosionSpritesExpired, passed;
            public string[] debrisMeshes;
        }

        private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string Pending = "DK2.HeartDestructionProbe.Pending";
        private static readonly Metrics Result = new Metrics();
        private static readonly HashSet<string> DebrisMeshes = new HashSet<string>();
        private static DungeonRunner _runner;
        private static DungeonScene _scene;
        private static ScenePresenter _presenter;
        private static WorldView _view;
        private static ScriptedViewDirector _director;
        private static ClassicConstructionEffects _effects;
        private static Camera _camera;
        private static RenderTexture _target;
        private static ListEventSink _events;
        private static StreamWriter _rows;
        private static string _output;
        private static long _milliseconds, _deathMilliseconds = -1;
        private static int _warmFrames;
        private static bool _closeCapture;
        private static bool _controlCapture, _controlDone;
        private static Color32[] _effectPixels;
        private static double _startupDeadline;

        static HeartDestructionProbe()
        {
            if (SessionState.GetBool(Pending, false)) WaitForPlayMode();
        }

        public static void Run()
        {
            if (!Application.isBatchMode || !File.Exists(".heart-destruction-probe"))
                throw new InvalidOperationException("Requires the isolated Heart destruction project.");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SessionState.SetBool(Pending, true);
            WaitForPlayMode();
            EditorApplication.EnterPlaymode();
        }

        private static void WaitForPlayMode()
        {
            _startupDeadline = EditorApplication.timeSinceStartup + 300;
            EditorApplication.update += Start;
        }

        private static void Start()
        {
            if (EditorApplication.timeSinceStartup > _startupDeadline)
            {
                EditorApplication.update -= Start;
                Fail(new Exception("Timed out entering isolated Play Mode."));
                return;
            }
            if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
            EditorApplication.update -= Start;
            SessionState.EraseBool(Pending);
            try
            {
                _output = Environment.GetEnvironmentVariable("DK2_HEART_DEATH_OUTPUT")
                    ?? Path.GetFullPath(Path.Combine(Application.dataPath, "../../heart-death-captures"));
                Directory.CreateDirectory(_output);
                Result.unity = Application.unityVersion;
                Result.gpu = SystemInfo.graphicsDeviceName;
                Result.playMode = Application.isPlaying;
                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new Exception("Graphics required.");
                _camera = new GameObject("Heart destruction camera").AddComponent<Camera>();
                _camera.tag = "MainCamera";
                _camera.clearFlags = CameraClearFlags.SolidColor;
                _camera.backgroundColor = new Color(.05f, .05f, .07f, 1);
                _camera.allowHDR = false;
                _camera.allowMSAA = false;
                _target = new RenderTexture(1068, 836, 24);
                _target.Create();
                _camera.targetTexture = _target;
                _runner = new GameObject("Level7 Heart destruction runner").AddComponent<DungeonRunner>();
                _runner.enabled = false;
                Set("campaignProfileFile", Path.Combine(_output, "campaign.dk2p"));
                Set("installDirectory", Environment.GetEnvironmentVariable("DK2_HEART_DEATH_INSTALL")
                    ?? "E:/GogGames/Dungeon Keeper 2");
                Set("levelName", "Level7");
                Set("seed", 7UL);
                Set("fogOfWar", true);
                Set("tickOnWorkerThread", false);
                Set("classicMeshes", true);
                Set("classicVertexLighting", true);
                Set("nativeInitialAmbient", true);
                Set("prefetchFirstFrameArt", false);
                Set("_startup", Activator.CreateInstance(typeof(DungeonRunner)
                    .GetNestedType("StartupTimings", BindingFlags.NonPublic), true));
                typeof(DungeonRunner).GetProperty("EffectClock", Private).SetValue(_runner,
                    (Func<EffectTime>)(() => new EffectTime(_milliseconds)));
                Call("StartGame");
                _scene = Get<DungeonScene>("_scene");
                _presenter = Get<ScenePresenter>("_presenter");
                _view = Get<WorldView>("_worldView");
                _director = Get<ScriptedViewDirector>("_scriptedView");
                _effects = Get<ClassicConstructionEffects>("_constructionEffects");
                _events = new ListEventSink();
                _runner.Simulation.ObserveWith(new Tee(Get<ISimulationEventSink>("_handCursorEvents"), _events));
                _rows = new StreamWriter(Path.Combine(_output, "timeline.csv"));
                _rows.WriteLine("milliseconds,tick,heartHealth,ruined,rivalImps,requests,debris,explosionSprites,pathPlaying");
                ShaderUtil.allowAsyncCompilation = false;
                EditorApplication.update += Sample;
                EditorApplication.QueuePlayerLoopUpdate();
            }
            catch (Exception error) { Fail(error); }
        }

        private static void Sample()
        {
            try
            {
                if (_warmFrames++ < 8)
                {
                    PrepareCamera(false);
                    Render(false, "warm");
                    EditorApplication.QueuePlayerLoopUpdate();
                    return;
                }
                if (_closeCapture)
                {
                    _closeCapture = false;
                    PrepareCamera(true);
                    var pixels = Render(true, "heart");
                    if (!_controlDone && _deathMilliseconds >= 0
                        && _effects.Model.Particles.Any(p => p.Definition.EffectElementId == 512)
                        && _effects.Model.Particles.Any(p => p.Definition.EffectElementId >= 505
                            && p.Definition.EffectElementId <= 511))
                    {
                        _effectPixels = pixels;
                        _controlCapture = true;
                    }
                    EditorApplication.QueuePlayerLoopUpdate();
                    return;
                }
                if (_controlCapture)
                {
                    _controlCapture = false;
                    _controlDone = true;
                    PrepareCamera(true);
                    var control = Render(true, "heart-no-effects", drawEffects: false);
                    for (int i = 0; i < control.Length; i++)
                        if (Math.Abs(control[i].r - _effectPixels[i].r)
                            + Math.Abs(control[i].g - _effectPixels[i].g)
                            + Math.Abs(control[i].b - _effectPixels[i].b) > 12) Result.effectChangedPixels++;
                    foreach (var renderer in _runner.GetComponentsInChildren<ParticleSystemRenderer>(true)) renderer.enabled = true;
                    EditorApplication.QueuePlayerLoopUpdate();
                    return;
                }
                if (_milliseconds > 0 && _milliseconds % 250 == 0 && _runner.Simulation.World.CurrentTick.Value < 50)
                    Call("RunTicks", 1, InputFrame.Empty);
                var world = _runner.Simulation.World;
                ref var rival = ref world.Player(new PlayerId(4));
                _director.Update(_milliseconds, world.CurrentTick.Value, CameraModeId.Dungeon);
                _director.SeekCamera(world.CurrentTick.Value);
                PrepareCamera(false);
                _effects.Advance();
                int debris = _effects.Model.Particles.Count(p => p.Definition.EffectElementId == 512);
                int sprites = _effects.Model.Particles.Count(p => p.Definition.EffectElementId >= 505
                    && p.Definition.EffectElementId <= 511);
                foreach (var part in _effects.Model.Particles.Where(p => p.Definition.EffectElementId == 512))
                    if (part.MeshEntry != null) DebrisMeshes.Add(part.MeshEntry);
                Result.peakDebris = Math.Max(Result.peakDebris, debris);
                Result.peakExplosionSprites = Math.Max(Result.peakExplosionSprites, sprites);
                Result.deathRequests = 0;
                foreach (var emitted in _events.Events)
                {
                    if (emitted.EffectRequest is not ViewEffectRequest request
                        || request.EffectId != new EffectId(76) || request.Owner != new PlayerId(4)) continue;
                    Result.deathRequests++;
                    Result.deathRequestHeightRaw = request.Position.Z.Raw;
                }
                Result.rivalImps = Math.Max(Result.rivalImps, world.Creatures.Count(c => c.OwnerPlayerId == 4
                    && c.CreatureTypeId == 1));
                if (rival.HeartHealth <= 0 && _deathMilliseconds < 0)
                {
                    _deathMilliseconds = _milliseconds;
                    Result.deathTick = rival.HeartDiedTick.Value;
                    Result.pathPlayingAtDeath = _director.PathPlaying;
                }
                _rows.WriteLine($"{_milliseconds},{world.CurrentTick.Value},{rival.HeartHealth},{rival.HeartRuined},"
                    + $"{Result.rivalImps},{Result.deathRequests},{debris},{sprites},{_director.PathPlaying}");
                bool capture = _milliseconds % 250 == 0 && (_deathMilliseconds < 0
                    || _milliseconds - _deathMilliseconds <= 3000)
                    || _deathMilliseconds >= 0 && (_milliseconds - _deathMilliseconds) % 5000 == 0;
                if (capture)
                {
                    Render(true, "flyby");
                    _closeCapture = true;
                }
                if (_deathMilliseconds >= 0 && _milliseconds >= _deathMilliseconds + 20000)
                {
                    Result.debrisExpired = debris == 0;
                    Result.explosionSpritesExpired = sprites == 0;
                    Result.debrisMeshes = DebrisMeshes.OrderBy(s => s).ToArray();
                    Result.passed = Result.playMode && Result.deathRequests == 1 && Result.deathRequestHeightRaw == 0
                        && Result.rivalImps == 0
                        && Result.pathPlayingAtDeath && Result.peakDebris > 0 && Result.peakExplosionSprites > 0
                        && Result.debrisMeshes.Length > 0 && Result.debrisExpired && Result.explosionSpritesExpired
                        && Result.effectChangedPixels > 50;
                    File.WriteAllText(Path.Combine(_output, "metrics.json"), JsonUtility.ToJson(Result, true));
                    _rows.Dispose();
                    EditorApplication.update -= Sample;
                    if (!Result.passed) throw new Exception("Heart destruction acceptance failed; inspect metrics.json.");
                    Debug.Log("Heart destruction verification passed: " + JsonUtility.ToJson(Result));
                    EditorApplication.Exit(0);
                    return;
                }
                if (_milliseconds > 40000) throw new Exception("Level7 Heart did not die during the opening.");
                _milliseconds += 50;
                EditorApplication.QueuePlayerLoopUpdate();
            }
            catch (Exception error) { Fail(error); }
        }

        private static void PrepareCamera(bool close)
        {
            var world = _runner.Simulation.World;
            _scene.SetCreatureFrameTime(world.CurrentTick, 1);
            _presenter.Present(world);
            if (_director.PathPlaying) Call("ApplyPathCamera");
            if (close)
            {
                Vector3 focus = new Vector3(22.5f, 1, -13.5f);
                _camera.transform.position = focus + new Vector3(5, 8, 5);
                _camera.transform.LookAt(focus);
                _camera.fieldOfView = 50;
                _camera.farClipPlane = 100;
            }
            _camera.pixelRect = new Rect(0, 0, _target.width, _target.height);
            _camera.aspect = _target.width / (float)_target.height;
            Call("RefreshEffectCamera");
            _scene.SetPortalLights(_view.PortalLightTiles(world));
            _scene.SetObjectLights(_view.LightObjects(world), 1);
            _scene.SetEffectLights(_effects.Advance());
            Call("ApplyClassicLook");
            _scene.PrepareClassicLighting(_camera, close ? 100 : _director.Pose?.ViewDistance ?? 100);
            _scene.DrawCreatureFrame(world.CurrentTick, 1);
            _scene.Flush();
        }

        private static Color32[] Render(bool save, string prefix, bool drawEffects = true)
        {
            if (drawEffects) _effects.Draw();
            else
            {
                foreach (var renderer in _runner.GetComponentsInChildren<ParticleSystemRenderer>(true)) renderer.enabled = false;
                foreach (var transparent in _runner.GetComponentsInChildren<TransparentTerrain>(true))
                {
                    transparent.BeginEffectFrame();
                    transparent.EndEffectFrame();
                }
            }
            foreach (var transparent in UnityEngine.Object.FindObjectsByType<TransparentTerrain>())
                typeof(TransparentTerrain).GetMethod("BeforeCamera", Private).Invoke(transparent,
                    new object[] { default(ScriptableRenderContext), _camera });
            var request = new RenderPipeline.StandardRequest { destination = _target };
            RenderPipeline.SupportsRenderRequest(_camera, request);
            RenderPipeline.SubmitRenderRequest(_camera, request);
            if (!save) return null;
            var previous = RenderTexture.active;
            RenderTexture.active = _target;
            var pixels = new Texture2D(_target.width, _target.height, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, _target.width, _target.height), 0, 0);
            pixels.Apply();
            var colours = pixels.GetPixels32();
            File.WriteAllBytes(Path.Combine(_output, $"{prefix}-{_milliseconds:D5}.png"), pixels.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(pixels);
            RenderTexture.active = previous;
            Result.captures++;
            return colours;
        }

        private sealed class Tee : ISimulationEventSink
        {
            private readonly ISimulationEventSink _first, _second;
            public Tee(ISimulationEventSink first, ISimulationEventSink second) { _first = first; _second = second; }
            public void Emit(in SimEvent value) { _first.Emit(value); _second.Emit(value); }
        }
        private static T Get<T>(string field) => (T)typeof(DungeonRunner).GetField(field, Private).GetValue(_runner);
        private static void Set(string field, object value) => typeof(DungeonRunner).GetField(field, Private).SetValue(_runner, value);
        private static void Call(string method, params object[] args) => typeof(DungeonRunner).GetMethod(method, Private).Invoke(_runner, args);
        private static void Fail(Exception error)
        {
            EditorApplication.update -= Sample;
            SessionState.EraseBool(Pending);
            _rows?.Dispose();
            Debug.LogException(error);
            if (_output != null) File.WriteAllText(Path.Combine(_output, "error.txt"), error.ToString());
            EditorApplication.Exit(1);
        }
    }
}
