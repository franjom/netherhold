// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using DK2.Core;
using DK2.Data.Model;
using DK2.Presentation;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace DK2.Unity
{
    [DefaultExecutionOrder(10000)]
    public sealed class ClassicWindowFrameProbe : MonoBehaviour
    {
        private const int Samples = 120, Columns = 11;
        private readonly double[,] _rows = new double[Samples * 2, Columns];
        private readonly FrameTiming[] _timings = new FrameTiming[1];
        private readonly List<ProfilerRecorder> _recorders = new List<ProfilerRecorder>();
        private readonly string[] _names = { "CPU Main Thread Frame Time", "CPU Render Thread Frame Time",
            "GPU Frame Time", "GC Allocated In Frame", "Batches Count", "Draw Calls Count", "Triangles Count", "Total Used Memory" };
        private readonly ProfilerCategory[] _categories = { ProfilerCategory.Internal, ProfilerCategory.Internal,
            ProfilerCategory.Render, ProfilerCategory.Memory, ProfilerCategory.Render, ProfilerCategory.Render,
            ProfilerCategory.Render, ProfilerCategory.Memory };
        private DungeonRunner _runner;
        private Camera _camera;
        private string _output;
        private Vector3 _quietFocus, _denseFocus;
        private int _phase, _row, _rendered, _savingFrames, _sampleFrame = -1, _lastRenderedSampleFrame = -1;
        private readonly int[] _renders = new int[2];
        private readonly Vector3[] _positions = new Vector3[2];
        private readonly Quaternion[] _rotations = new Quaternion[2];
        private readonly float[] _fieldsOfView = new float[2];
        private double _ready, _previous, _started;
        private ulong _previousTiming;
        private long _fixedTick;
        private bool _configured, _finished, _renderVerified = true;

        private static FieldInfo Field(Type type, string name) => type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingFieldException(type.FullName, name);

        private void Start()
        {
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                int at = Array.IndexOf(args, "-windowProbeOutput");
                if (at < 0 || at + 1 >= args.Length) throw new ArgumentException("-windowProbeOutput is required.");
                _output = Path.GetFullPath(args[at + 1]); Directory.CreateDirectory(_output);
                if (Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                    throw new InvalidOperationException("This probe requires the standalone window render loop, without -batchmode/-nographics.");
                Application.runInBackground = true; QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
                _runner = FindAnyObjectByType<DungeonRunner>(); _camera = Camera.main;
                if (_runner == null || _runner.Simulation == null || _camera == null || !_camera.enabled)
                    throw new InvalidOperationException("Production runner or active screen camera did not start.");
                if (_camera.targetTexture != null) throw new InvalidOperationException("The camera must render to the player screen.");
                if ((float)Field(typeof(DungeonRunner), "speed").GetValue(_runner) != 0)
                    throw new InvalidOperationException("The profiling world must remain at a fixed tick.");
                _fixedTick = _runner.Simulation.World.CurrentTick.Value;
                _quietFocus = (Vector3)Field(typeof(DungeonRunner), "_cameraFocus").GetValue(_runner);
                // Keep tick-one script requests from replacing the fixed gameplay views.
                Field(typeof(DungeonRunner), "_scriptedView").SetValue(_runner,
                    new ScriptedViewDirector(new PlayerId((byte)(int)Field(typeof(DungeonRunner), "viewingPlayerId").GetValue(_runner)),
                        _ => null, _ => null));
                Field(typeof(DungeonRunner), "_glideMilliseconds").SetValue(_runner, 0d);
                _camera.rect = new Rect(0, 0, 1, 1);
                typeof(DungeonRunner).GetMethod("ApplyClassicCamera", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(_runner, null);
                var scene = (DungeonScene)Field(typeof(DungeonRunner), "_scene").GetValue(_runner);
                var cache = (ClassicWorldLightCache)Field(typeof(DungeonScene), "_worldLights").GetValue(scene);
                var sources = (ClassicPointLight?[])Field(typeof(ClassicWorldLightCache), "_torches").GetValue(cache);
                int width = _runner.Simulation.World.Width, height = _runner.Simulation.World.Height;
                TilePos dense = Densest(sources, width, height);
                _denseFocus = new Vector3(dense.X + .5f, _quietFocus.y, -dense.Y - .5f);
                for (int i = 0; i < _names.Length; i++)
                    _recorders.Add(ProfilerRecorder.StartNew(_categories[i], _names[i]));
                RenderPipelineManager.endCameraRendering += Rendered;
                _started = _previous = Time.realtimeSinceStartupAsDouble;
                _ready = _started + 10;
                _positions[0] = _camera.transform.position;
                _rotations[0] = _camera.transform.rotation;
                _fieldsOfView[0] = _camera.fieldOfView;
                _configured = true;
                File.WriteAllText(Path.Combine(_output, "metadata.txt"), Metadata(sources.Count(s => s.HasValue)));
            }
            catch (Exception error) { Fail(error); }
        }

        private void Rendered(ScriptableRenderContext context, Camera camera)
        {
            if (camera != _camera) return;
            _rendered++;
            if (_sampleFrame == Time.frameCount && _lastRenderedSampleFrame != _sampleFrame)
            {
                _renders[_phase]++;
                _lastRenderedSampleFrame = _sampleFrame;
            }
        }

        private void LateUpdate()
        {
            if (!_configured || _finished) return;
            try
            {
                double now = Time.realtimeSinceStartupAsDouble;
                if (now - _started > 180) throw new TimeoutException("Windowed capture exceeded three minutes.");
                if (_runner.Simulation.World.CurrentTick.Value != _fixedTick)
                    throw new InvalidOperationException("World tick changed during the fixed-state capture.");
                FrameTimingManager.CaptureFrameTimings();
                if (_savingFrames > 0)
                {
                    if (++_savingFrames == 8)
                    {
                        if (_renders[_phase] != Samples) _renderVerified = false;
                        string screenshot = Path.Combine(_output, _phase == 0 ? "quiet.png" : "dense.png");
                        if (!File.Exists(screenshot)) throw new InvalidOperationException("Automatic screen capture did not complete: " + screenshot);
                        if (_phase == 1) { Save(); _finished = true; Application.Quit(_renderVerified ? 0 : 2); }
                        else
                        {
                            _phase = 1; _savingFrames = 0; _row = 0;
                            Field(typeof(DungeonRunner), "_cameraFocus").SetValue(_runner, _denseFocus);
                            typeof(DungeonRunner).GetMethod("ApplyClassicCamera", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(_runner, null);
                            _positions[1] = _camera.transform.position;
                            _rotations[1] = _camera.transform.rotation;
                            _fieldsOfView[1] = _camera.fieldOfView;
                            _ready = now + 10;
                        }
                    }
                    _previous = now; return;
                }
                if (now < _ready) { _previous = now; return; }
                if ((_camera.transform.position - _positions[_phase]).sqrMagnitude > 0.000001f
                    || Quaternion.Angle(_camera.transform.rotation, _rotations[_phase]) > 0.001f
                    || Mathf.Abs(_camera.fieldOfView - _fieldsOfView[_phase]) > 0.001f
                    || _camera.rect != new Rect(0, 0, 1, 1))
                    throw new InvalidOperationException("The fixed gameplay camera changed during capture.");
                int row = _phase * Samples + _row;
                _sampleFrame = Time.frameCount;
                _rows[row, 0] = (now - _previous) * 1000;
                uint available = FrameTimingManager.GetLatestTimings(1, _timings);
                bool fresh = available > 0 && _timings[0].frameStartTimestamp != _previousTiming;
                _rows[row, 1] = fresh && _timings[0].cpuFrameTime > 0 ? _timings[0].cpuFrameTime : double.NaN;
                _rows[row, 2] = fresh && _timings[0].gpuFrameTime > 0 ? _timings[0].gpuFrameTime : double.NaN;
                if (available > 0) _previousTiming = _timings[0].frameStartTimestamp;
                for (int i = 0; i < _recorders.Count; i++)
                {
                    _rows[row, i + 3] = _recorders[i].Valid && _recorders[i].Count > 0
                        ? _recorders[i].LastValue * (i < 3 ? 1e-6 : 1) : double.NaN;
                    if (i == 2 && _rows[row, i + 3] <= 0) _rows[row, i + 3] = double.NaN;
                }
                _previous = now;
                if (++_row == Samples)
                {
                    ScreenCapture.CaptureScreenshot(Path.Combine(_output, _phase == 0 ? "quiet.png" : "dense.png"));
                    _savingFrames = 1;
                }
            }
            catch (Exception error) { Fail(error); }
        }

        private string Metadata(int torches) =>
            $"Unity={Application.unityVersion}\nDevelopment={Debug.isDebugBuild}\nCPU={SystemInfo.processorType}\nGPU={SystemInfo.graphicsDeviceName}\nAPI={SystemInfo.graphicsDeviceType}\n" +
            $"BatchMode={Application.isBatchMode}\nRunInBackground={Application.runInBackground}\nResolution={Screen.width}x{Screen.height}\nVSync={QualitySettings.vSyncCount}\nFrameCap={Application.targetFrameRate}\n" +
            $"Level=Level8\nSeed=1\nWorldTick={_fixedTick}\nSpeed=0\nClassicVertexLighting=True\nNativeInitialAmbient=False\nCheapLighting=False\nFog=False\n" +
            $"WarmupSecondsPerView=10\nSamplesPerView={Samples}\nAuthoredTorches={torches}\nQuietFocus={_quietFocus}\nDenseFocus={_denseFocus}\n" +
            "ScriptedCameraSuppressed=True\nCameraPolicy=Fixed ordinary gameplay camera, full viewport\n" +
            $"FrameTimingEnabled={FrameTimingManager.IsFeatureEnabled()}\nGpuTimerFrequency={FrameTimingManager.GetGpuTimerFrequency()}\nCpuTimerFrequency={FrameTimingManager.GetCpuTimerFrequency()}\n" +
            $"Pipeline={GraphicsSettings.currentRenderPipeline}\nQuality={QualitySettings.names[QualitySettings.GetQualityLevel()]}\nColorSpace={QualitySettings.activeColorSpace}\n" +
            "TimingUnits=FrameTiming milliseconds; Profiler CPU/GPU nanoseconds converted to milliseconds\nTimingLatency=FrameTimingManager reports recent completed frames with a four-frame delay; rows are not synchronized frame traces\n";

        private void Save()
        {
            using (var writer = new StreamWriter(Path.Combine(_output, "frames.csv")))
            {
                writer.WriteLine("view,frame,loop_ms,timing_cpu_ms,timing_gpu_ms,main_thread_ms,render_thread_ms,gpu_counter_ms,gc_bytes,batches,draw_calls,triangles,total_used_bytes");
                for (int row = 0; row < Samples * 2; row++)
                {
                    writer.Write(row < Samples ? "quiet" : "dense"); writer.Write(','); writer.Write(row % Samples);
                    for (int column = 0; column < Columns; column++)
                    { writer.Write(','); writer.Write(_rows[row, column].ToString("R", CultureInfo.InvariantCulture)); }
                    writer.WriteLine();
                }
            }
            File.AppendAllText(Path.Combine(_output, "metadata.txt"),
                $"QuietSampledFrameRenderCallbacks={_renders[0]}\nDenseSampledFrameRenderCallbacks={_renders[1]}\nTotalCameraRenderCallbacks={_rendered}\nQuietCamera={_positions[0]}\nDenseCamera={_positions[1]}\n" +
                string.Join("\n", _names.Select((name, i) => $"Counter:{name}:Valid={_recorders[i].Valid},Count={_recorders[i].Count}")) +
                $"\nRenderingVerified={_renderVerified}\nCompleted=True\n");
        }

        private static TilePos Densest(ClassicPointLight?[] sources, int width, int height)
        {
            int[] sums = new int[(width + 1) * (height + 1)];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    sums[(y + 1) * (width + 1) + x + 1] = (sources[y * width + x].HasValue ? 1 : 0)
                        + sums[y * (width + 1) + x + 1] + sums[(y + 1) * (width + 1) + x] - sums[y * (width + 1) + x];
            TilePos best = default; int maximum = -1;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int left = Math.Max(0, x - 6), right = Math.Min(width, x + 7);
                    int top = Math.Max(0, y - 6), bottom = Math.Min(height, y + 7);
                    int count = sums[bottom * (width + 1) + right] - sums[top * (width + 1) + right]
                        - sums[bottom * (width + 1) + left] + sums[top * (width + 1) + left];
                    if (count > maximum) { maximum = count; best = new TilePos(x, y); }
                }
            return best;
        }

        private void Fail(Exception error)
        {
            _finished = true; Debug.LogException(error);
            if (_output != null) File.WriteAllText(Path.Combine(_output, "error.txt"), error.ToString());
            Application.Quit(1);
        }

        private void OnDestroy()
        {
            RenderPipelineManager.endCameraRendering -= Rendered;
            foreach (ProfilerRecorder recorder in _recorders) recorder.Dispose();
        }
    }
}
