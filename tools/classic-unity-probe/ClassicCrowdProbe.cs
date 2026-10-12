// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using DK2.Art;
using DK2.Assets;
using DK2.Data;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DK2.Unity
{
    public sealed class ClassicCrowdProbe : MonoBehaviour
    {
        private const int Count = 200, Samples = 600;
        private readonly ClassicCreatureAnimation[] _players = new ClassicCreatureAnimation[Count];
        private readonly ClassicCrowdAlternative[] _alternatives = new ClassicCrowdAlternative[Count];
        private ClassicCrowdBank _bank;
        private string _representation;
        private bool _configured;
        private readonly double[,] _rows = new double[Samples, 13];
        private readonly FrameTiming[] _timing = new FrameTiming[1];
        private ProfilerRecorder _sample, _upload, _alloc, _memory, _main, _batches, _triangles;
        private InstallArtProvider _art;
        private double _ready, _previous;
        private int _row;
        private int _finishFrames;
        private Camera _camera;
        private RenderTexture _target;
        private UniversalRenderPipeline.SingleCameraRequest _request;
        private int _renderedFrames;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot() => new GameObject("Classic crowd performance probe").AddComponent<ClassicCrowdProbe>();

        private static string Argument(string key, string fallback)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == key) return args[i + 1];
            return fallback;
        }

        private void Start()
        {
            try
            {
                if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                    throw new InvalidOperationException("Graphics device is Null; rendering capture requires graphics");
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = -1;
                Screen.SetResolution(1280, 720, false);
                var database = GameDatabaseLoader.LoadForCampaignLevel(Argument("-dk2Install", "E:/GogGames/Dungeon Keeper 2"), "Level8");
                _art = InstallArtProvider.Open(Argument("-dk2Install", "E:/GogGames/Dungeon Keeper 2"), database);
                var meshes = new ClassicMeshes(_art);
                var key = AssetRef.ForCreature(Argument("-probeCreature", "Imp"), AssetKind.Animation, "moving_to_job");
                if (meshes.ShaderName != "Universal Render Pipeline/Lit") throw new InvalidOperationException("Production Lit shader missing");
                if (!_art.TryResolve(key, out ResolvedAsset asset)) throw new InvalidOperationException("Creature movement asset missing: " + key.Key);
                _representation = Argument("-probeRepresentation", "cpu");
                if (_representation != "cpu") _bank = new ClassicCrowdBank(_art, meshes, asset, _representation, transform);
                for (int i = 0; i < Count; i++)
                {
                    GameObject go = meshes.TryCreate(asset, "Crowd " + i, transform);
                    if (go == null) throw new InvalidOperationException("Incomplete crowd mesh");
                    if (_representation == "cpu")
                    {
                        if (!meshes.ConfigureAnimation(go, asset, true, 0)) throw new InvalidOperationException("Incomplete crowd animation");
                        _players[i] = go.GetComponent<ClassicCreatureAnimation>();
                    }
                    else _alternatives[i] = new ClassicCrowdAlternative(go, _bank, _representation == "vat");
                    go.transform.position = new Vector3((i % 20) * 1.5f, 0, (i / 20) * 1.5f);
                }
                var cameraObject = new GameObject("Probe camera");
                var camera = cameraObject.AddComponent<Camera>();
                _camera = camera;
                camera.enabled = false;
                _target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
                _target.Create();
                _request = new UniversalRenderPipeline.SingleCameraRequest { destination = _target };
                camera.transform.position = new Vector3(14, 24, -10);
                camera.transform.LookAt(new Vector3(14, 0, 7));
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 100;
                var sun = new GameObject("Probe sun").AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.transform.rotation = Quaternion.Euler(50, -30, 0);
                _sample = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "DK2.Animation.Sample");
                _upload = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "DK2.Animation.MeshUpload");
                _alloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
                _memory = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Used Memory");
                _main = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "CPU Main Thread Frame Time");
                _batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
                _triangles = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
                _ready = Time.realtimeSinceStartupAsDouble + 10;
                _previous = Time.realtimeSinceStartupAsDouble;
                _configured = true;
            }
            catch (Exception ex) { UnityEngine.Debug.LogException(ex); Application.Quit(1); }
        }

        private static double Read(ProfilerRecorder recorder, double scale = 1) => recorder.Valid && recorder.Count > 0 ? recorder.LastValue * scale : double.NaN;

        private void Update()
        {
            if (_finishFrames > 0) { if (++_finishFrames == 4) Application.Quit(0); return; }
            if (!_configured) return;
            double now = Time.realtimeSinceStartupAsDouble;
            FrameTimingManager.CaptureFrameTimings();
            uint timingCount = FrameTimingManager.GetLatestTimings(1, _timing);
            long beforeAlloc = GC.GetAllocatedBytesForCurrentThread();
            long before = Stopwatch.GetTimestamp();
            if (_representation == "cpu") foreach (ClassicCreatureAnimation player in _players) player.Draw(now);
            else foreach (ClassicCrowdAlternative player in _alternatives) player.Draw(now);
            double draw = (Stopwatch.GetTimestamp() - before) * 1000.0 / Stopwatch.Frequency;
            long drawAlloc = GC.GetAllocatedBytesForCurrentThread() - beforeAlloc;
            double render = double.NaN;
            if (RenderPipeline.SupportsRenderRequest(_camera, _request))
            {
                before = Stopwatch.GetTimestamp();
                RenderPipeline.SubmitRenderRequest(_camera, _request);
                render = (Stopwatch.GetTimestamp() - before) * 1000.0 / Stopwatch.Frequency;
                _renderedFrames++;
            }
            if (now >= _ready)
            {
                if (_renderedFrames == 0) { UnityEngine.Debug.LogError("No offscreen rendering completed"); Application.Quit(1); return; }
                _rows[_row, 0] = (now - _previous) * 1000;
                _rows[_row, 1] = draw;
                _rows[_row, 2] = Read(_sample, 1e-6);
                _rows[_row, 3] = Read(_upload, 1e-6);
                _rows[_row, 4] = drawAlloc;
                _rows[_row, 5] = Read(_alloc);
                _rows[_row, 6] = Read(_memory);
                _rows[_row, 7] = Read(_main, 1e-6);
                _rows[_row, 8] = timingCount > 0 && _timing[0].cpuFrameTime > 0 ? _timing[0].cpuFrameTime : double.NaN;
                _rows[_row, 9] = timingCount > 0 && _timing[0].gpuFrameTime > 0 ? _timing[0].gpuFrameTime : double.NaN;
                double batches = Read(_batches), triangles = Read(_triangles);
                _rows[_row, 10] = batches > 0 ? batches : double.NaN;
                _rows[_row, 11] = triangles > 0 ? triangles : double.NaN;
                _rows[_row, 12] = render;
                if (++_row == Samples) { Save(); _finishFrames = 1; }
            }
            _previous = now;
        }

        private void Save()
        {
            string output = Argument("-probeOutput", Path.Combine(Application.persistentDataPath, "classic-crowd.csv"));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
            using (var writer = new StreamWriter(output))
            {
                writer.WriteLine("frame_ms,draw200_ms,sample_marker_ms,upload_bounds_marker_ms,draw_gc_bytes,frame_gc_bytes,total_used_bytes,main_thread_ms,cpu_frame_ms,gpu_frame_ms,batches,triangles,render_submission_ms");
                for (int row = 0; row < Samples; row++)
                {
                    for (int column = 0; column < 13; column++)
                    { if (column > 0) writer.Write(','); writer.Write(_rows[row, column].ToString("R", CultureInfo.InvariantCulture)); }
                    writer.WriteLine();
                }
            }
            File.WriteAllText(output + ".metadata.txt", $"Unity={Application.unityVersion}\nDevelopment={UnityEngine.Debug.isDebugBuild}\nCPU={SystemInfo.processorType}\nGPU={SystemInfo.graphicsDeviceName}\nAPI={SystemInfo.graphicsDeviceType}\nCreature={Argument("-probeCreature", "Imp")}\nResolution={Screen.width}x{Screen.height}\nCount={Count}\nWarmupSeconds=10\nSamples={Samples}\nVSync={QualitySettings.vSyncCount}\nFrameCap={Application.targetFrameRate}\nQuality={QualitySettings.names[QualitySettings.GetQualityLevel()]}\nShaderPipeline={UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline}\nGraphicsMemoryMB={SystemInfo.graphicsMemorySize}\nSystemMemoryMB={SystemInfo.systemMemorySize}\n");
            RenderTexture previous = RenderTexture.active;
            File.AppendAllText(output + ".metadata.txt", $"Representation={_representation}\nSharedPositionPayloadBytes={_bank?.PositionStorageBytes ?? 0}\nDecodedPositionBytes={_bank?.DecodedPositionBytes ?? 0}\nSegmentVertices={_bank?.Vertices ?? 0}\n");
            RenderTexture.active = _target;
            var picture = new Texture2D(_target.width, _target.height, TextureFormat.RGB24, false);
            picture.ReadPixels(new Rect(0, 0, _target.width, _target.height), 0, 0);
            picture.Apply();
            File.WriteAllBytes(output + ".png", picture.EncodeToPNG());
            RenderTexture.active = previous;
            Destroy(picture);
        }

        private void OnDestroy()
        { _sample.Dispose(); _upload.Dispose(); _alloc.Dispose(); _memory.Dispose(); _main.Dispose(); _batches.Dispose(); _triangles.Dispose(); _art?.Dispose(); if (_target != null) { _target.Release(); Destroy(_target); } }
    }
}
