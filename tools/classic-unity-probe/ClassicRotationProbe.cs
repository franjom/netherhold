// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using DK2.Core;
using DK2.Data.Model;
using DK2.Presentation;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

namespace DK2.Unity
{
    [DefaultExecutionOrder(-10000)]
    public sealed class ClassicRotationProbe : MonoBehaviour
    {
        private static readonly string[] Phases = { "IdleFirst", "RotateFirst", "IdleMiddle", "RotateWarm", "IdleLast" };
        private readonly ProfilerMarker[] _phaseMarkers = {
            new ProfilerMarker("DK2.Probe.IdleFirst"), new ProfilerMarker("DK2.Probe.RotateFirst"),
            new ProfilerMarker("DK2.Probe.IdleMiddle"), new ProfilerMarker("DK2.Probe.RotateWarm"),
            new ProfilerMarker("DK2.Probe.IdleLast") };
        private readonly List<ProfilerRecorder> _recorders = new List<ProfilerRecorder>();
        private readonly double[,] _rows = new double[30000, 14];
        private readonly int[] _renderCounts = new int[5];
        private readonly int[] _samples = new int[5];
        private readonly FrameTiming[] _timings = new FrameTiming[1];
        private DungeonRunner _runner;
        private Camera _camera;
        private ClassicCamera _classic;
        private Action _applyCamera;
        private string _output;
        private double _started, _ready, _phaseStart, _previous, _angleRemainder;
        private int _phase, _phaseFrames, _row, _sampleFrame = -1, _lastRenderFrame = -1, _saving;
        private long _tick;
        private ulong _timingStamp;
        private bool _configured, _allocationTrace, _profiling, _finished;
        private byte[] _calibration;

        private static FieldInfo Field(string name) => typeof(DungeonRunner).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingFieldException(typeof(DungeonRunner).FullName, name);

        private void Initialize()
        {
            string[] args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-windowProbeOutput");
            if (at < 0 || at + 1 >= args.Length) throw new ArgumentException("-windowProbeOutput required.");
            _output = args[at + 1]; Directory.CreateDirectory(_output);
            _allocationTrace = Array.IndexOf(args, "-rotationAllocationTrace") >= 0;
            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("A visible standalone render loop is required.");
            _runner = FindAnyObjectByType<DungeonRunner>(); _camera = Camera.main;
            if (_runner == null || _runner.Simulation == null || _camera == null)
                throw new InvalidOperationException("Production scene did not start.");
            if ((float)Field("speed").GetValue(_runner) != 0) throw new InvalidOperationException("Expected paused simulation.");
            _tick = _runner.Simulation.World.CurrentTick.Value;
            Field("_scriptedView").SetValue(_runner, new ScriptedViewDirector(
                new PlayerId((byte)(int)Field("viewingPlayerId").GetValue(_runner)), _ => null, _ => null));
            Field("_glideMilliseconds").SetValue(_runner, 0d);
            Field("_cameraFocus").SetValue(_runner, new Vector3(42.5f, 1f, -18.5f));
            _classic = (ClassicCamera)Field("_classicCamera").GetValue(_runner);
            _applyCamera = (Action)Delegate.CreateDelegate(typeof(Action), _runner,
                typeof(DungeonRunner).GetMethod("ApplyClassicCamera", BindingFlags.NonPublic | BindingFlags.Instance));
            _camera.rect = new Rect(0, 0, 1, 1); _applyCamera();
            Application.runInBackground = true; Application.targetFrameRate = -1; QualitySettings.vSyncCount = 0;
            Profiler.enabled = false;
            AddRecorder(ProfilerCategory.Memory, "GC Allocated In Frame");
            AddRecorder(ProfilerCategory.Internal, "CPU Main Thread Frame Time");
            AddRecorder(ProfilerCategory.Internal, "CPU Render Thread Frame Time");
            AddRecorder(ProfilerCategory.Scripts, "DK2.TransparentTerrain.Sort");
            AddRecorder(ProfilerCategory.Scripts, "DK2.Scene.PrepareLighting");
            AddRecorder(ProfilerCategory.Scripts, "DK2.Shadows.Selection");
            AddRecorder(ProfilerCategory.Scripts, "DK2.Shadows.Silhouette");
            AddRecorder(ProfilerCategory.Scripts, "DK2.Shadows.ReceiverUpload");
            RenderPipelineManager.endCameraRendering += Rendered;
            _started = _previous = Time.realtimeSinceStartupAsDouble; _ready = _started + 10;
            File.WriteAllText(Path.Combine(_output, "metadata.txt"),
                $"Unity={Application.unityVersion}\nCPU={SystemInfo.processorType}\nGPU={SystemInfo.graphicsDeviceName}\nAPI={SystemInfo.graphicsDeviceType}\n" +
                $"Resolution={Screen.width}x{Screen.height}\nDevelopment={Debug.isDebugBuild}\nLevel=Level8\nWorldTick={_tick}\nFocus=(42.5,1,-18.5)\n" +
                $"AllocationTrace={_allocationTrace}\nWarmupSeconds=10\nRotationDegreesPerSecond=90\nFog=False\nNativeInitialAmbient=False\n" +
                "ScriptedCameraSuppressed=True\nTimingLatency=Recorder/timing rows describe recent completed frames; first8frames per phase excluded\n");
            _configured = true;
        }

        private void AddRecorder(ProfilerCategory category, string name) =>
            _recorders.Add(ProfilerRecorder.StartNew(category, name));

        private void Update()
        {
            if (_finished || _saving > 0) return;
            try
            {
                if (!_configured) Initialize();
                double now = Time.realtimeSinceStartupAsDouble;
                if (now - _started > 150) throw new TimeoutException("Rotation probe exceeded150seconds.");
                if (_runner.Simulation.World.CurrentTick.Value != _tick) throw new InvalidOperationException("World tick changed.");
                if (now < _ready) return;
                if (_phaseStart == 0)
                {
                    _phaseStart = now;
                    if (_allocationTrace)
                    {
                        Profiler.logFile = Path.Combine(_output, "allocations.raw");
                        Profiler.enableBinaryLog = true; Profiler.enableAllocationCallstacks = true; Profiler.enabled = true;
                        _profiling = true;
                        Profiler.BeginSample("DK2.Probe.AllocationCalibration");
                        _calibration = new byte[65536]; _calibration[0] = 1;
                        Profiler.EndSample();
                    }
                }
                bool rotating = _phase == 1 || _phase == 3;
                double duration = rotating ? 10 : 5;
                if (_allocationTrace ? _phaseFrames >= 40 : now - _phaseStart >= duration)
                {
                    if (++_phase == Phases.Length)
                    {
                        _sampleFrame = -1; _saving = 1;
                        if (_profiling) { Profiler.enabled = false; Profiler.enableBinaryLog = false; _profiling = false; }
                        ScreenCapture.CaptureScreenshot(Path.Combine(_output, "rendered.png"));
                        return;
                    }
                    _phaseStart = now; _phaseFrames = 0;
                    rotating = _phase == 1 || _phase == 3;
                }
                using (_phaseMarkers[_phase].Auto()) { }
                if (rotating)
                {
                    _angleRemainder += Time.unscaledDeltaTime * 90 * 2048 / 360;
                    int delta = (int)_angleRemainder; _angleRemainder -= delta;
                    _classic.Rotate(new Dk2Angle((ushort)delta)); _applyCamera();
                }
                _phaseFrames++;
            }
            catch (Exception error) { Fail(error); }
        }

        private void LateUpdate()
        {
            if (!_configured || _finished) return;
            try
            {
                if (_saving > 0)
                {
                    if (++_saving < 8) return;
                    Save(); _finished = true; Application.Quit(0); return;
                }
                double now = Time.realtimeSinceStartupAsDouble;
                FrameTimingManager.CaptureFrameTimings();
                if (now < _ready || _phaseFrames <= 8) { _previous = now; return; }
                if (_row == _rows.GetLength(0)) throw new InvalidOperationException("Sample capacity exceeded.");
                _rows[_row, 0] = _phase; _rows[_row, 1] = Time.frameCount;
                _rows[_row, 2] = (now - _previous) * 1000; _previous = now;
                uint available = FrameTimingManager.GetLatestTimings(1, _timings);
                bool fresh = available > 0 && _timings[0].frameStartTimestamp != _timingStamp;
                _rows[_row, 3] = fresh && _timings[0].cpuFrameTime > 0 ? _timings[0].cpuFrameTime : double.NaN;
                _rows[_row, 4] = fresh && _timings[0].gpuFrameTime > 0 ? _timings[0].gpuFrameTime : double.NaN;
                if (available > 0) _timingStamp = _timings[0].frameStartTimestamp;
                for (int i = 0; i < _recorders.Count; i++)
                    _rows[_row, i + 5] = _recorders[i].Valid && _recorders[i].Count > 0
                        ? _recorders[i].LastValue * (i == 0 ? 1 : 1e-6) : double.NaN;
                _rows[_row, 13] = _camera.transform.eulerAngles.y;
                _samples[_phase]++; _row++; _sampleFrame = Time.frameCount;
            }
            catch (Exception error) { Fail(error); }
        }

        private void Rendered(ScriptableRenderContext context, Camera camera)
        {
            if (camera == _camera && _sampleFrame == Time.frameCount && _lastRenderFrame != _sampleFrame)
            { _renderCounts[_phase]++; _lastRenderFrame = _sampleFrame; }
        }

        private void Save()
        {
            for (int i = 0; i < Phases.Length; i++)
                if (_samples[i] == 0 || _samples[i] != _renderCounts[i]) throw new InvalidOperationException("Render verification failed: " + Phases[i]);
            if (!File.Exists(Path.Combine(_output, "rendered.png"))) throw new InvalidOperationException("No screenshot.");
            using (var writer = new StreamWriter(Path.Combine(_output, "frames.csv")))
            {
                writer.WriteLine("phase,unity_frame,loop_ms,cpu_ms,gpu_ms,gc_bytes,main_ms,render_ms,sort_ms,lighting_ms,shadow_selection_ms,shadow_silhouette_ms,shadow_upload_ms,yaw_degrees");
                for (int row = 0; row < _row; row++)
                {
                    writer.Write(Phases[(int)_rows[row, 0]]);
                    for (int col = 1; col < _rows.GetLength(1); col++)
                    { writer.Write(','); writer.Write(_rows[row, col].ToString("R", CultureInfo.InvariantCulture)); }
                    writer.WriteLine();
                }
            }
            File.AppendAllText(Path.Combine(_output, "metadata.txt"), $"RenderingVerified=True\nSamples={_row}\nCompleted=True\n");
            GC.KeepAlive(_calibration);
        }

        private void Fail(Exception error)
        {
            _finished = true; if (_profiling) { Profiler.enabled = false; Profiler.enableBinaryLog = false; }
            if (_output != null) File.WriteAllText(Path.Combine(_output, "error.txt"), error.ToString());
            Debug.LogException(error); Application.Quit(1);
        }

        private void OnDestroy()
        {
            RenderPipelineManager.endCameraRendering -= Rendered;
            foreach (ProfilerRecorder recorder in _recorders) recorder.Dispose();
        }
    }
}
