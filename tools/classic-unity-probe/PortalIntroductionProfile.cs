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
using System.Runtime.InteropServices;
using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Profiling;

namespace DK2.Unity
{
    [DefaultExecutionOrder(10000)]
    public sealed class PortalIntroductionProfile : MonoBehaviour
    {
        private readonly List<ProfilerRecorder> _recorders = new List<ProfilerRecorder>();
        private const int MaximumSamples = 65536;
        private const string CsvHeader = "phase,seconds,intro_seconds,unity_frame,render_tick,path,effect_meshes,loop_ms,timing_stamp,timing_cpu_ms,timing_gpu_ms,main_ms,render_ms,gpu_counter_ms,sort_ms,depth_ms,upload_ms,lighting_ms,animation_ms,present_ms,tick_ms,gc_bytes,draw_calls,batches,triangles,present_wait_ms,camera_x,camera_y,camera_z";
        private struct FrameSample
        {
            public double Elapsed, Intro, RenderTick, LoopMs, CpuMs, GpuMs;
            public int Frame, Path, Effects;
            public ulong Stamp;
            public Vector3 Camera;
        }
        private struct GpuSample
        {
            public ulong Stamp;
            public double ObservedIntro, Cpu, Gpu, Main, Render, Wait;
        }
        private readonly FrameSample[] _samples = new FrameSample[MaximumSamples];
        private readonly long[] _counterSamples = new long[MaximumSamples * 15];
        private readonly GpuSample[] _gpuSamples = new GpuSample[MaximumSamples];
        private readonly HashSet<ulong> _completedTimings = new HashSet<ulong>(MaximumSamples);
        private readonly string[] _names = { "CPU Main Thread Frame Time", "CPU Render Thread Frame Time", "GPU Frame Time",
            "DK2.TransparentTerrain.Sort", "DK2.TransparentTerrain.DepthOrder", "DK2.TransparentTerrain.UploadRuns",
            "DK2.Scene.PrepareLighting", "DK2.Scene.AnimatedFrame", "DK2.Scene.Present", "DK2.Simulation.Tick",
            "GC Allocated In Frame", "Draw Calls Count", "Batches Count", "Triangles Count", "Gfx.WaitForPresentOnGfxThread" };
        private readonly FrameTiming[] _timings = new FrameTiming[8];
        private DungeonRunner _runner;
        private GameSimulation _simulation;
        private Camera _camera;
        private ClassicConstructionEffects _effects;
        private DungeonScene _scene;
        private TransparentTerrain _transparent;
        private ScriptedViewDirector _director;
        private string _output;
        private bool _configured, _started, _finished, _omitEffect, _screenshotRequested, _arrivalScreenshot, _captureGpu, _captureCpu, _cpuProfiling, _nativeGpu;
        private double _begin, _introduced, _previous;
        private ulong _lastTiming;
        private long _introCpuStamp, _endCpuStamp;
        private int _gpuCount;
        private long _counterFrequency;
        private int _renders, _sampleFrame = -1, _lastRenderedFrame = -1, _sampleCount, _effectRequests, _pathRequests;

        private static FieldInfo RunnerField(string name) => typeof(DungeonRunner).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);

        [DllImport("kernel32.dll")]
        private static extern bool QueryPerformanceCounter(out long value);
        [DllImport("kernel32.dll")]
        private static extern bool QueryPerformanceFrequency(out long value);

        private static long CpuTimestamp()
        {
            if (!QueryPerformanceCounter(out long stamp)) throw new InvalidOperationException("Native CPU timer unavailable.");
            return stamp;
        }

        private void Configure()
        {
            string[] args = Environment.GetCommandLineArgs();
            _output = args[Array.IndexOf(args, "-windowProbeOutput") + 1];
            _omitEffect = args.Contains("-omitArrivalEffect");
            _arrivalScreenshot = !args.Contains("-noArrivalScreenshot");
            _captureCpu = args.Contains("-arrivalCpuProfile");
            _nativeGpu = args.Contains("-nativeGpuDiagnostic");
            _captureGpu = !args.Contains("-fpsQualification") && !_captureCpu && !_nativeGpu;
            if (_captureCpu && !Debug.isDebugBuild) throw new InvalidOperationException("CPU profiling requires a Development player.");
            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Normal standalone screen rendering required.");
            if (!QueryPerformanceFrequency(out _counterFrequency)) throw new InvalidOperationException("Native CPU timer frequency unavailable.");
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
                if (!_captureGpu) break;
                ProfilerCategory category = i < 2 || i == 14 ? ProfilerCategory.Internal : i == 10 ? ProfilerCategory.Memory
                    : i == 2 || i > 10 ? ProfilerCategory.Render : ProfilerCategory.Scripts;
                _recorders.Add(ProfilerRecorder.StartNew(category, _names[i]));
            }
            RenderPipelineManager.endCameraRendering += Rendered;
            _begin = _previous = Time.realtimeSinceStartupAsDouble;
            File.WriteAllText(Path.Combine(_output, "metadata.txt"),
                "Unity=" + Application.unityVersion + "\nCPU=" + SystemInfo.processorType + "\nGPU=" + SystemInfo.graphicsDeviceName +
                "\nAPI=" + SystemInfo.graphicsDeviceType + "\nResolution=" + Screen.width + "x" + Screen.height +
                "\nDevelopment=" + Debug.isDebugBuild + "\nVSync=0\nFrameCap=-1\nLevel=level2\nSeed=1\n" +
                "Fixture=Initial production scene with portal claimed and nearby fog revealed; original actions65531->65530 forced after10s warmup\n" +
                "Camera=Authored path60/AP4\nWorker=True\nSimulationSpeedAfterIntroduction=1\nEffectOmitted=" + _omitEffect +
                "\nTimingUnits=ms except allocations/counts; completed GPU/CPU timings delayed and deduplicated by timestamp\n" +
                "FrameTimingEnabled=" + FrameTimingManager.IsFeatureEnabled() + "\nGpuTimerFrequency=" + FrameTimingManager.GetGpuTimerFrequency() + "\n");
            File.AppendAllText(Path.Combine(_output, "metadata.txt"), "ProbeStorage=Preallocated frame/counter buffers; CSV formatting after measurement\nRenderTick=Main-thread scene render clock, not a worker world read\n");
            File.AppendAllText(Path.Combine(_output, "metadata.txt"), "ScreenshotDuringMeasurement=" + _arrivalScreenshot + "\n");
            File.AppendAllText(Path.Combine(_output, "metadata.txt"), "CaptureMode=" + (_captureCpu ? "cpu" : _nativeGpu ? "native-gpu" : _captureGpu ? "diagnostic-gpu" : "fps") + "\n");
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
            _introCpuStamp = CpuTimestamp();
            _introduced = Time.realtimeSinceStartupAsDouble; _started = true;
        }

        private bool CaptureTimings(out FrameTiming latest)
        {
            latest = default;
            if (!_captureGpu) return false;
            FrameTimingManager.CaptureFrameTimings();
            uint count = FrameTimingManager.GetLatestTimings((uint)_timings.Length, _timings);
            for (int i = 0; i < count; i++)
            {
                FrameTiming timing = _timings[i];
                if (timing.frameStartTimestamp == 0 || timing.gpuFrameTime <= 0) continue;
                if (_completedTimings.Add(timing.frameStartTimestamp))
                {
                    if (_gpuCount == MaximumSamples) throw new InvalidOperationException("GPU capture buffer exhausted.");
                    _gpuSamples[_gpuCount++] = new GpuSample
                    {
                        Stamp = timing.frameStartTimestamp, ObservedIntro = _started ? Time.realtimeSinceStartupAsDouble - _introduced : -1,
                        Cpu = timing.cpuFrameTime, Gpu = timing.gpuFrameTime, Main = timing.cpuMainThreadFrameTime,
                        Render = timing.cpuRenderThreadFrameTime, Wait = timing.cpuMainThreadPresentWaitTime,
                    };
                }
                if (timing.frameStartTimestamp > _lastTiming && timing.frameStartTimestamp > latest.frameStartTimestamp)
                    latest = timing;
            }
            if (latest.frameStartTimestamp == 0) return false;
            _lastTiming = latest.frameStartTimestamp;
            return true;
        }

        private void LateUpdate()
        {
            if (_finished) return;
            try
            {
                if (!_configured) Configure();
                double now = Time.realtimeSinceStartupAsDouble, elapsed = now - _begin;
                if (_captureCpu && !_cpuProfiling && !_started && elapsed >= 9)
                {
                    Profiler.logFile = Path.Combine(_output, "arrival-cpu.raw");
                    Profiler.enableBinaryLog = true;
                    Profiler.SetAreaEnabled(ProfilerArea.GPU, false);
                    Profiler.enableAllocationCallstacks = Environment.GetCommandLineArgs().Contains("-arrivalAllocationStacks");
                    Profiler.enabled = true;
                    File.AppendAllText(Path.Combine(_output, "metadata.txt"), "ProfilerGpuArea=" + Profiler.GetAreaEnabled(ProfilerArea.GPU) + "\n");
                    _cpuProfiling = true;
                }
                if (_cpuProfiling)
                {
                    Profiler.BeginSample("DK2.Probe.ArrivalFrame:" + Time.frameCount);
                    Profiler.EndSample();
                }
                if (!_started && elapsed >= 10)
                {
                    Profiler.BeginSample("DK2.Probe.Introduce");
                    try { Introduce(); }
                    finally { Profiler.EndSample(); }
                    now = Time.realtimeSinceStartupAsDouble;
                }
                bool fresh = CaptureTimings(out FrameTiming timing);
                double intro = _started ? (CpuTimestamp() - _introCpuStamp) / (double)_counterFrequency : -1;
                if (_sampleCount == MaximumSamples) throw new InvalidOperationException("Frame capture buffer exhausted.");
                _samples[_sampleCount] = new FrameSample
                {
                    Elapsed = elapsed, Intro = intro, Frame = Time.frameCount,
                    RenderTick = (double)typeof(DungeonScene).GetField("_renderSeconds", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(_scene) * 4,
                    Path = _director.PathId ?? 0,
                    Effects = ((ICollection)typeof(TransparentTerrain).GetField("_effects", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(_transparent)).Count,
                    LoopMs = (now - _previous) * 1000, Stamp = fresh ? _lastTiming : 0,
                    CpuMs = fresh && timing.cpuFrameTime > 0 ? timing.cpuFrameTime : double.NaN,
                    GpuMs = fresh ? timing.gpuFrameTime : double.NaN, Camera = _camera.transform.position,
                };
                for (int i = 0; i < _recorders.Count; i++)
                    _counterSamples[_sampleCount * 15 + i] = _recorders[i].Valid && _recorders[i].Count > 0 ? _recorders[i].LastValue : long.MinValue;
                _sampleCount++; _sampleFrame = Time.frameCount; _previous = now;
                if (_started && intro >= 2 && !_screenshotRequested && _arrivalScreenshot)
                {
                    _screenshotRequested = true;
                    ScreenCapture.CaptureScreenshot(Path.Combine(_output, "introduction.png"));
                    var runs = _transparent.GetComponentsInChildren<MeshRenderer>()
                        .GroupBy(r => r.sharedMaterial).Select(g =>
                        {
                            Material m = g.Key; Texture texture = m.mainTexture;
                            return string.Join(",", new[] { m.name.Replace(',', '_'), m.shader.name, g.Count().ToString(),
                                g.Sum(r => r.GetComponent<MeshFilter>().sharedMesh.triangles.Length).ToString(),
                                texture != null ? texture.name : "none", texture != null ? texture.width.ToString() : "0",
                                texture != null ? texture.height.ToString() : "0" });
                        });
                    File.WriteAllLines(Path.Combine(_output, "transparent-materials.csv"),
                        new[] { "material,shader,runs,vertices,texture,width,height" }.Concat(runs));
                }
                if (_started && intro >= 14)
                {
                    _finished = true;
                    _endCpuStamp = CpuTimestamp();
                    StartCoroutine(Complete());
                }
            }
            catch (Exception error) { Debug.LogException(error); _finished = true; Application.Quit(1); }
        }

        private IEnumerator Complete()
        {
            if (_cpuProfiling)
            {
                Profiler.enabled = false;
                Profiler.enableBinaryLog = false;
                _cpuProfiling = false;
            }
            for (int i = 0; i < 8; i++)
            {
                yield return null;
                CaptureTimings(out _);
            }
            yield return new WaitForEndOfFrame();
            Texture2D proof = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(_output, "render-proof.png"), proof.EncodeToPNG());
            Destroy(proof);
            using (var csv = new StreamWriter(Path.Combine(_output, "frames.csv")))
            {
                csv.WriteLine(CsvHeader);
                for (int row = 0; row < _sampleCount; row++)
                {
                    FrameSample s = _samples[row];
                    var cells = new List<string> { s.Intro < 0 ? "warmup" : s.Intro <= 6 ? "arrival" : "after",
                        F(s.Elapsed), F(s.Intro), s.Frame.ToString(), F(s.RenderTick), s.Path.ToString(), s.Effects.ToString(),
                        F(s.LoopMs), s.Stamp.ToString(), F(s.CpuMs), F(s.GpuMs) };
                    for (int i = 0; i < _names.Length; i++)
                    {
                        long counter = i < _recorders.Count ? _counterSamples[row * 15 + i] : long.MinValue;
                        cells.Add(F(counter == long.MinValue ? double.NaN : counter * (i >= 10 && i <= 13 ? 1 : 1e-6)));
                    }
                    cells.Add(F(s.Camera.x)); cells.Add(F(s.Camera.y)); cells.Add(F(s.Camera.z));
                    csv.WriteLine(string.Join(",", cells));
                }
            }
            Array.Sort(_gpuSamples, 0, _gpuCount, Comparer<GpuSample>.Create((a, b) => a.Stamp.CompareTo(b.Stamp)));
            using (var csv = new StreamWriter(Path.Combine(_output, "timings.csv")))
            {
                csv.WriteLine("timing_stamp,observed_intro_seconds,timing_cpu_ms,timing_gpu_ms,timing_main_ms,timing_render_ms,timing_present_wait_ms");
                for (int i = 0; i < _gpuCount; i++)
                {
                    GpuSample s = _gpuSamples[i];
                    csv.WriteLine(string.Join(",", new[] { s.Stamp.ToString(), F(s.ObservedIntro), F(s.Cpu), F(s.Gpu), F(s.Main), F(s.Render), F(s.Wait) }));
                }
            }
            File.AppendAllText(Path.Combine(_output, "metadata.txt"), "RenderingCallbacks=" + _renders + "\nSamples=" + _sampleCount +
                "\nEffectRequests=" + _effectRequests + "\nPathRequests=" + _pathRequests + "\nRenderingVerified=" + (_renders >= _sampleCount - 1) + "\n");
            var pool = (ICollection)typeof(TransparentTerrain).GetField("_runs", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(_transparent);
            MeshRenderer sample = _transparent.GetComponentInChildren<MeshRenderer>();
            int capacity = sample != null ? sample.GetComponent<MeshFilter>().sharedMesh.vertexCount : 0;
            long primaryBytes = 0;
            foreach (object run in pool)
            {
                Mesh mesh = (Mesh)run.GetType().GetField("Mesh").GetValue(run);
                for (int stream = 0; stream < mesh.vertexBufferCount; stream++)
                    primaryBytes += (long)mesh.vertexCount * mesh.GetVertexBufferStride(stream);
            }
            File.AppendAllText(Path.Combine(_output, "metadata.txt"), "TransparentRunPool=" + pool.Count +
                "\nTransparentVertexCapacity=" + capacity + "\nPrimaryVertexBufferBytes=" + primaryBytes + "\n");
            File.AppendAllText(Path.Combine(_output, "metadata.txt"), "IntroductionCpuTimestamp=" + _introCpuStamp +
                "\nEndCpuTimestamp=" + _endCpuStamp + "\nCpuTimerFrequency=" + FrameTimingManager.GetCpuTimerFrequency() +
                "\nCpuCounterFrequency=" + _counterFrequency + "\nCpuTimestampSource=WindowsQueryPerformanceCounter\nGpuTimingDrainFrames=8\n");
            foreach (var r in _recorders) r.Dispose();
            RenderPipelineManager.endCameraRendering -= Rendered;
            Application.Quit(_renders >= _sampleCount - 1 ? 0 : 2);
        }

        private void Rendered(ScriptableRenderContext context, Camera camera)
        {
            if (camera == _camera && _sampleFrame == Time.frameCount && _lastRenderedFrame != _sampleFrame)
            { _lastRenderedFrame = _sampleFrame; _renders++; }
        }

        private static string F(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    }
}
