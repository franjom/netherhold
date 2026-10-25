// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace DK2.Unity
{
    [DefaultExecutionOrder(10000)]
    public sealed class PortalIntroductionProfile : MonoBehaviour
    {
        private readonly List<ProfilerRecorder> _recorders = new List<ProfilerRecorder>();
        private readonly List<string> _rows = new List<string>();
        private readonly string[] _names = { "CPU Main Thread Frame Time", "CPU Render Thread Frame Time", "GPU Frame Time",
            "DK2.TransparentTerrain.Sort", "DK2.TransparentTerrain.DepthOrder", "DK2.TransparentTerrain.UploadRuns",
            "DK2.Scene.PrepareLighting", "DK2.Scene.AnimatedFrame", "DK2.Scene.Present", "DK2.Simulation.Tick",
            "GC Allocated In Frame", "Draw Calls Count", "Batches Count", "Triangles Count", "Gfx.WaitForPresentOnGfxThread" };
        private readonly FrameTiming[] _timings = new FrameTiming[1];
        private DungeonRunner _runner;
        private GameSimulation _simulation;
        private Camera _camera;
        private ClassicConstructionEffects _effects;
        private DungeonScene _scene;
        private TransparentTerrain _transparent;
        private ScriptedViewDirector _director;
        private string _output;
        private bool _configured, _started, _finished, _omitEffect, _screenshotRequested;
        private double _begin, _introduced, _previous;
        private ulong _lastTiming;
        private int _renders, _sampleFrame = -1, _lastRenderedFrame = -1, _sampleCount, _effectRequests, _pathRequests;

        private static FieldInfo RunnerField(string name) => typeof(DungeonRunner).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);

        private void Configure()
        {
            string[] args = Environment.GetCommandLineArgs();
            _output = args[Array.IndexOf(args, "-windowProbeOutput") + 1];
            _omitEffect = args.Contains("-omitArrivalEffect");
            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Normal standalone screen rendering required.");
            _runner = FindAnyObjectByType<DungeonRunner>(); _simulation = _runner.Simulation; _camera = Camera.main;
            _effects = (ClassicConstructionEffects)RunnerField("_constructionEffects").GetValue(_runner);
            _scene = (DungeonScene)RunnerField("_scene").GetValue(_runner);
            _transparent = _runner.GetComponentInChildren<TransparentTerrain>();
            _director = (ScriptedViewDirector)RunnerField("_scriptedView").GetValue(_runner);
            _director.Skip();
            WorldState world = _simulation.World;
            world.Player(new PlayerId(3)).CameraPathEndTick = 0;
            RoomInstance portal = RoomIndex.Build(world, _simulation.Database).First(r => r.Definition.TypeId == RoomTypeId.Portal);
            foreach (int tile in portal.TileIndices) world.TileAtIndex(tile).OwnerPlayerId = 3;
            for (int y = portal.MinY - 3; y <= portal.MaxY + 3; y++)
                for (int x = portal.MinX - 3; x <= portal.MaxX + 3; x++)
                    DiscoveryRules.SetExplored(world, _simulation.Database, new TilePos(x, y), new PlayerId(3));
            Application.runInBackground = true; QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
            for (int i = 0; i < _names.Length; i++)
            {
                ProfilerCategory category = i < 2 || i == 14 ? ProfilerCategory.Internal : i == 10 ? ProfilerCategory.Memory
                    : i == 2 || i > 10 ? ProfilerCategory.Render : ProfilerCategory.Scripts;
                _recorders.Add(ProfilerRecorder.StartNew(category, _names[i]));
            }
            RenderPipelineManager.endCameraRendering += Rendered;
            _begin = _previous = Time.realtimeSinceStartupAsDouble;
            _rows.Add("phase,seconds,intro_seconds,unity_frame,render_tick,path,effect_meshes,loop_ms,timing_stamp,timing_cpu_ms,timing_gpu_ms,main_ms,render_ms,gpu_counter_ms,sort_ms,depth_ms,upload_ms,lighting_ms,animation_ms,present_ms,tick_ms,gc_bytes,draw_calls,batches,triangles,present_wait_ms,camera_x,camera_y,camera_z");
            File.WriteAllText(Path.Combine(_output, "metadata.txt"),
                "Unity=" + Application.unityVersion + "\nCPU=" + SystemInfo.processorType + "\nGPU=" + SystemInfo.graphicsDeviceName +
                "\nAPI=" + SystemInfo.graphicsDeviceType + "\nResolution=" + Screen.width + "x" + Screen.height +
                "\nDevelopment=" + Debug.isDebugBuild + "\nVSync=0\nFrameCap=-1\nLevel=level2\nSeed=1\n" +
                "Fixture=Initial production scene with portal claimed and nearby fog revealed; original actions65531->65530 forced after10s warmup\n" +
                "Camera=Authored path60/AP4\nWorker=True\nSimulationSpeedAfterIntroduction=1\nEffectOmitted=" + _omitEffect +
                "\nTimingUnits=ms except allocations/counts; completed GPU/CPU timings delayed and deduplicated by timestamp\n" +
                "FrameTimingEnabled=" + FrameTimingManager.IsFeatureEnabled() + "\nGpuTimerFrequency=" + FrameTimingManager.GetGpuTimerFrequency() + "\n");
            File.AppendAllText(Path.Combine(_output, "metadata.txt"), "AllocationCounters=Include probe CSV formatting overhead\nRenderTick=Main-thread scene render clock, not a worker world read\n");
            _configured = true;
        }

        private void Introduce()
        {
            WorldState world = _simulation.World;
            _director.Skip(); world.Player(new PlayerId(3)).CameraPathEndTick = 0;
            var executor = (TriggerExecutor)typeof(GameSimulation).GetField("_triggers", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(_simulation);
            var effectProperty = typeof(TriggerExecutor).GetProperty("EffectRequested", BindingFlags.NonPublic | BindingFlags.Instance);
            var viewProperty = typeof(TriggerExecutor).GetProperty("ViewRequested", BindingFlags.NonPublic | BindingFlags.Instance);
            object savedEffect = effectProperty.GetValue(executor), savedView = viewProperty.GetValue(executor);
            typeof(TriggerExecutor).GetProperty("EffectRequested", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(executor,
                (Action<string, ViewEffectRequest>)((system, request) =>
                {
                    if (request.EffectId.Value == 165) _effectRequests++;
                    if (_omitEffect && request.EffectId.Value == 165) return;
                    _simulation.Events.Emit(new SimEvent(world.CurrentTick, system, "view_effect", "arrival profile") { EffectRequest = request });
                }));
            typeof(TriggerExecutor).GetProperty("ViewRequested", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(executor,
                (Action<ScriptedViewRequest>)(request =>
                {
                    if (request.Kind == ScriptedViewKind.CameraPath && request.Value == 60) _pathRequests++;
                    _simulation.Events.Emit(new SimEvent(world.CurrentTick, "TriggerExecutor", "scripted_view", "arrival profile") { ScriptedView = request });
                }));
            try
            {
                Type runType = typeof(TriggerExecutor).Assembly.GetType("DK2.Simulation.TriggerRun", true);
                object run = Activator.CreateInstance(runType, new object[] { (int)world.CurrentTick.Value });
                runType.GetProperty("Player").SetValue(run, new PlayerId(3));
                typeof(TriggerExecutor).GetMethod("Walk", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(executor, new object[] { unchecked((short)65531), (byte)1, TriggerContext.Player, run });
            }
            finally { effectProperty.SetValue(executor, savedEffect); viewProperty.SetValue(executor, savedView); }
            if (_effectRequests != 1 || _pathRequests != 1 || !world.Creatures.Any(c => c.CreatureTypeId == 5))
                throw new InvalidOperationException("The shipped Warlock introduction did not execute.");
            RunnerField("speed").SetValue(_runner, 1f);
            _introduced = Time.realtimeSinceStartupAsDouble; _started = true;
        }

        private void LateUpdate()
        {
            if (_finished) return;
            try
            {
                if (!_configured) Configure();
                double now = Time.realtimeSinceStartupAsDouble, elapsed = now - _begin;
                if (!_started && elapsed >= 10) { Introduce(); now = Time.realtimeSinceStartupAsDouble; }
                FrameTimingManager.CaptureFrameTimings();
                uint count = FrameTimingManager.GetLatestTimings(1, _timings);
                bool fresh = count > 0 && _timings[0].frameStartTimestamp != _lastTiming;
                if (count > 0) _lastTiming = _timings[0].frameStartTimestamp;
                double intro = _started ? now - _introduced : -1;
                var cells = new List<string> { !_started ? "warmup" : intro <= 6 ? "arrival" : "after", F(elapsed), F(intro),
                    Time.frameCount.ToString(), F((double)typeof(DungeonScene).GetField("_renderSeconds", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(_scene) * 4),
                    (_director.PathId ?? 0).ToString(), ((ICollection)typeof(TransparentTerrain).GetField("_effects", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(_transparent)).Count.ToString(),
                    F((now - _previous) * 1000), fresh ? _lastTiming.ToString() : "0", F(fresh && _timings[0].cpuFrameTime > 0 ? _timings[0].cpuFrameTime : double.NaN),
                    F(fresh && _timings[0].gpuFrameTime > 0 ? _timings[0].gpuFrameTime : double.NaN) };
                for (int i = 0; i < _recorders.Count; i++)
                    cells.Add(F(_recorders[i].Valid && _recorders[i].Count > 0 ? _recorders[i].LastValue * (i >= 10 && i <= 13 ? 1 : 1e-6) : double.NaN));
                Vector3 p = _camera.transform.position; cells.Add(F(p.x)); cells.Add(F(p.y)); cells.Add(F(p.z));
                _rows.Add(string.Join(",", cells)); _sampleCount++; _sampleFrame = Time.frameCount; _previous = now;
                if (_started && intro >= 2 && !_screenshotRequested)
                { _screenshotRequested = true; ScreenCapture.CaptureScreenshot(Path.Combine(_output, "introduction.png")); }
                if (_started && intro >= 14)
                {
                    File.WriteAllLines(Path.Combine(_output, "frames.csv"), _rows);
                    File.AppendAllText(Path.Combine(_output, "metadata.txt"), "RenderingCallbacks=" + _renders + "\nSamples=" + _sampleCount +
                        "\nEffectRequests=" + _effectRequests + "\nPathRequests=" + _pathRequests + "\nRenderingVerified=" + (_renders >= _sampleCount - 1) + "\n");
                    foreach (var r in _recorders) r.Dispose();
                    RenderPipelineManager.endCameraRendering -= Rendered;
                    _finished = true; Application.Quit(_renders >= _sampleCount - 1 ? 0 : 2);
                }
            }
            catch (Exception error) { Debug.LogException(error); _finished = true; Application.Quit(1); }
        }

        private void Rendered(ScriptableRenderContext context, Camera camera)
        {
            if (camera == _camera && _sampleFrame == Time.frameCount && _lastRenderedFrame != _sampleFrame)
            { _lastRenderedFrame = _sampleFrame; _renders++; }
        }

        private static string F(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    }
}
