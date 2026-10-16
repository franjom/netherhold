// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DK2.Presentation;
using DK2.Core;
using DK2.Assets;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace DK2.Unity.Editor
{
    public static class ClassicWorldLightingProbe
    {
        [Serializable]
        private sealed class Comparison
        {
            public string Name;
            public int ChangedPixels;
            public int BrighterPixels;
            public float MaximumDifference;
            public float MeanDifference;
            public float MeanWithTorches;
            public float MeanWithoutTorches;
        }

        [Serializable]
        private sealed class TorchInput
        {
            public Vector3 Position;
            public Color Colour;
            public float Intensity;
            public float Range;
        }

        [Serializable]
        private sealed class Result
        {
            public bool Passed;
            public string Level;
            public string UnityVersion, Cpu, GraphicsDevice, GraphicsApi, ColourSpace;
            public int ViewportWidth, ViewportHeight;
            public bool NativeInitialAmbient;
            public string Pipeline;
            public bool SrpBatcher;
            public string Shader;
            public int TorchCount;
            public int AuthoredObjectLightCount;
            public int BufferCountBeforeFlush;
            public int BufferCountAfterFlush;
            public int TileTableGridCount;
            public float NativeViewDistance;
            public Vector2Int[] LightVisibilityPolygon;
            public int MeshCount;
            public double ShadowPrepareMedianMs, ShadowPrepareP95Ms;
            public bool ShadowAllocationAvailable;
            public long ShadowPrepareAllocatedBytes = -1;
            public ClassicAllocationCalibration.Result AllocationCalibration;
            public int AnchoredTerrainVertices, AnchoredTransparentVertices, AnchoredLiquidVertices;
            public int VerifiedTopNormalVertices;
            public int VerifiedWallCapNormalVertices;
            public int VerifiedRoomObjectColourMaterials;
            public int VerifiedStaticObjectLightingVertices, VerifiedTorchLightingVertices;
            public int SampledVertices;
            public int FrontFacingInRangePairs;
            public int PositiveNativeContributions;
            public int SaturatedNativeContributions;
            public float MaximumNativeContribution;
            public Vector3 CameraPosition;
            public Vector3 CameraEulerAngles;
            public Color AmbientLight;
            public TorchInput[] TorchInputs;
            public Comparison[] Comparisons;
            public Profile[] Profiles;
            public Phase[] Phases;
            public TableDiagnostic[] TableDiagnostics;
            public string Error;
        }

        [Serializable]
        private sealed class TableDiagnostic
        {
            public string Name;
            public bool KeywordEnabled;
            public int VisibleTables, NonemptyTables, SelectedLights, GpuCountMismatches, GpuSourceMismatches;
            public int TerrainVertices, MissingAnchors, EmptyAnchors, PositiveVertices, OppositeNormalPositiveVertices;
            public float MaximumContribution;
            public Vector3 FirstPositivePosition, FirstPositiveNormal;
            public NearTable[] NearHeart;
        }

        [Serializable]
        private sealed class NearTable
        {
            public int X, Y, CpuCount, GpuCount, TorchSources;
        }

        [Serializable]
        private sealed class Profile
        {
            public string View, Mode;
            public int Samples, VisibleTables, SelectedSources;
            public double PrepareMedianMs, PrepareP95Ms, SubmitMedianMs, SubmitP95Ms;
            public bool AllocationAvailable;
            public long PrepareAllocatedBytes = -1;
        }

        [Serializable]
        private sealed class Phase
        {
            public string Name;
            public int Samples;
            public bool AllocationAvailable;
            public long AllocatedBytes = -1;
            public double BytesPerCall = -1, MedianMs, P95Ms;
        }

        private const int Width = 1024, Height = 576;
        private static bool _running;

        [MenuItem("DK2/Classic look/Compare campaign torch lighting")]
        public static void Run()
        {
            if (_running) throw new InvalidOperationException("The world lighting probe is already running.");
            _running = true;
            EditorApplication.update += RenderWhenReady;
            EditorApplication.QueuePlayerLoopUpdate();
        }

        private static void RenderWhenReady()
        {
            EditorApplication.update -= RenderWhenReady;
            var result = new Result();
            string output = Environment.GetEnvironmentVariable("DK2_LIGHTING_PROBE_OUTPUT");
            if (string.IsNullOrWhiteSpace(output))
                output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../build/step5-world-lighting"));
            try
            {
                Directory.CreateDirectory(output);
                Verify(result, output);
                result.Passed = true;
            }
            catch (Exception error) { result.Error = error.ToString(); }
            finally { _running = false; }
            File.WriteAllText(Path.Combine(output, "result.json"), JsonUtility.ToJson(result, true));
            Debug.Log($"DK2 world lighting probe: {(result.Passed ? "PASS" : "FAIL")}; {output}");
            if (Application.isBatchMode) EditorApplication.Exit(result.Passed ? 0 : 1);
        }

        private static void Verify(Result result, string output)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play mode before running the world lighting probe.");
            string level = Environment.GetEnvironmentVariable("DK2_LIGHTING_PROBE_LEVEL") ?? "Level1";
            if (level != "Level1" && level != "Level7" && level != "Level8")
                throw new ArgumentException("DK2_LIGHTING_PROBE_LEVEL must be Level1, Level7 or Level8.");
            result.Level = level;
            result.AllocationCalibration = ClassicAllocationCalibration.Measure();
            result.UnityVersion = Application.unityVersion;
            result.Cpu = SystemInfo.processorType;
            result.GraphicsDevice = SystemInfo.graphicsDeviceName;
            result.GraphicsApi = SystemInfo.graphicsDeviceType.ToString();
            result.ColourSpace = QualitySettings.activeColorSpace.ToString();
            result.ViewportWidth = Width; result.ViewportHeight = Height;
            result.NativeInitialAmbient = Environment.GetEnvironmentVariable("DK2_NATIVE_AMBIENT_PROBE") == "1";
            int previousCount = Shader.GetGlobalInt("_DK2TorchGridCount");
            int previousWidth = Shader.GetGlobalInt("_DK2TorchGridWidth");
            if (previousCount != 0 || Shader.GetGlobalInt("_DK2TileLightGridCount") != 0)
                throw new InvalidOperationException("A live lighting buffer is published; the probe will not replace it.");

            Light[] originalLights = Resources.FindObjectsOfTypeAll<Light>()
                .Where(light => light.gameObject.scene.IsValid()).ToArray();
            bool[] enabled = originalLights.Select(light => light.enabled).ToArray();
            Camera[] mainCameras = Resources.FindObjectsOfTypeAll<Camera>()
                .Where(camera => camera.gameObject.scene.IsValid() && camera.CompareTag("MainCamera")).ToArray();
            AmbientMode ambientMode = RenderSettings.ambientMode;
            Color ambientLight = RenderSettings.ambientLight;
            SphericalHarmonicsL2 ambientProbe = RenderSettings.ambientProbe;
            float reflection = RenderSettings.reflectionIntensity;
            bool asyncCompilation = ShaderUtil.allowAsyncCompilation;
            RenderTexture previousTarget = RenderTexture.active;
            Vector4 previousAmbientColour = Shader.GetGlobalVector("_DK2AmbientColour");
            float previousAmbientPreview = Shader.GetGlobalFloat("_DK2InitialAmbientPreview");
            GameObject root = null;
            RenderTexture target = null;
            Texture2D readback = null;
            try
            {
                foreach (Camera camera in mainCameras) camera.tag = "Untagged";
                foreach (Light light in originalLights) light.enabled = false;
                ShaderUtil.allowAsyncCompilation = false;
                root = new GameObject("DK2 transient " + level + " lighting probe") { hideFlags = HideFlags.HideAndDontSave };
                var cameraObject = new GameObject("Probe main camera");
                cameraObject.transform.SetParent(root.transform, false);
                cameraObject.tag = "MainCamera";
                Camera probeCamera = cameraObject.AddComponent<Camera>();
                probeCamera.enabled = true;
                probeCamera.aspect = Width / (float)Height;
                probeCamera.pixelRect = new Rect(0, 0, Width, Height);
                probeCamera.cullingMask = 1 << 31;
                probeCamera.clearFlags = CameraClearFlags.SolidColor;
                probeCamera.backgroundColor = Color.black;
                probeCamera.allowHDR = false;
                probeCamera.allowMSAA = false;
                target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
                target.Create();
                probeCamera.targetTexture = target;
                readback = new Texture2D(Width, Height, TextureFormat.RGBA32, false);

                foreach (Light original in originalLights.Where(light => light.type == LightType.Directional))
                {
                    int index = Array.IndexOf(originalLights, original);
                    if (!enabled[index]) continue;
                    var lightObject = new GameObject("Probe copy of " + original.name);
                    lightObject.transform.SetParent(root.transform, false);
                    lightObject.transform.rotation = original.transform.rotation;
                    Light copy = lightObject.AddComponent<Light>();
                    copy.type = LightType.Directional;
                    copy.color = original.color;
                    copy.intensity = original.intensity;
                    copy.shadows = original.shadows;
                    copy.cullingMask = 1 << 31;
                }

                var runnerObject = new GameObject("Production DungeonRunner");
                runnerObject.transform.SetParent(root.transform, false);
                DungeonRunner runner = runnerObject.AddComponent<DungeonRunner>();
                runner.enabled = false;
                Set(runner, "levelName", level);
                Set(runner, "classicVertexLighting", true);
                Set(runner, "nativeInitialAmbient", result.NativeInitialAmbient);
                Set(runner, "tickOnWorkerThread", false);
                typeof(DungeonRunner).GetMethod("StartGame", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(runner, null);
                probeCamera.enabled = false;
                result.BufferCountBeforeFlush = Shader.GetGlobalInt("_DK2TorchGridCount");
                var scene = (DungeonScene)typeof(DungeonRunner).GetField("_scene", BindingFlags.NonPublic | BindingFlags.Instance)
                    .GetValue(runner);
                var classicCamera = (ClassicCamera)typeof(DungeonRunner).GetField("_classicCamera", BindingFlags.NonPublic | BindingFlags.Instance)
                    .GetValue(runner);
                float viewDistance = classicCamera.NativeViewDistance;
                result.NativeViewDistance = viewDistance;
                Vector3 cameraPosition = probeCamera.transform.position, cameraForward = probeCamera.transform.forward,
                    cameraUp = probeCamera.transform.up;
                var lightPose = new ClassicCameraPose(new System.Numerics.Vector3(cameraPosition.x, cameraPosition.y, cameraPosition.z),
                    new System.Numerics.Vector3(cameraForward.x, cameraForward.y, cameraForward.z),
                    new System.Numerics.Vector3(cameraUp.x, cameraUp.y, cameraUp.z), probeCamera.fieldOfView,
                    probeCamera.farClipPlane, 0, 0);
                result.LightVisibilityPolygon = ClassicLightVisibility.BuildPolygon(lightPose,
                    new CameraViewport(Mathf.Max(probeCamera.pixelWidth, Screen.width), probeCamera.pixelWidth, probeCamera.pixelHeight),
                    viewDistance).Select(tile => new Vector2Int(tile.X, tile.Y)).ToArray();
                var cache = (ClassicWorldLightCache)typeof(DungeonScene).GetField("_worldLights", BindingFlags.NonPublic | BindingFlags.Instance)
                    .GetValue(scene);
                var torchSources = (ClassicPointLight?[])((ClassicPointLight?[])typeof(ClassicWorldLightCache)
                    .GetField("_torches", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(cache)).Clone();
                Action<bool> setTorches = on =>
                {
                    for (int tile = 0; tile < torchSources.Length; tile++)
                        if (torchSources[tile].HasValue)
                            cache.SetTorch(new TilePos(tile % runner.Simulation.World.Width, tile / runner.Simulation.World.Width),
                                on ? torchSources[tile] : null);
                    scene.PrepareClassicLighting(probeCamera, viewDistance);
                };
                scene.Flush();
                result.BufferCountAfterFlush = Shader.GetGlobalInt("_DK2TorchGridCount");
                result.TileTableGridCount = Shader.GetGlobalInt("_DK2TileLightGridCount");
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 31;
                Light[] torches = runner.GetComponentsInChildren<Light>(true).Where(light => light.type == LightType.Point).ToArray();
                result.TorchCount = torches.Length;
                result.TorchInputs = torches.Select(light => new TorchInput
                {
                    Position = light.transform.position, Colour = light.color,
                    Intensity = light.intensity, Range = light.range,
                }).ToArray();
                result.CameraPosition = probeCamera.transform.position;
                result.CameraEulerAngles = probeCamera.transform.eulerAngles;
                result.AmbientLight = ambientLight;
                result.Shader = runner.GetComponentsInChildren<MeshRenderer>(true)
                    .SelectMany(renderer => renderer.sharedMaterials).Where(material => material != null)
                    .Select(material => material.shader.name).FirstOrDefault(name => name == "DK2/Classic Vertex Lit");
                SampleGeometry(result, runner, torches);
                var view = (WorldView)typeof(DungeonRunner).GetField("_worldView", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(runner);
                var simulation = (DK2.Simulation.GameSimulation)typeof(DungeonRunner).GetField("_simulation", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(runner);
                result.AuthoredObjectLightCount = view.LightObjects(simulation.World).Count;
                if (result.AuthoredObjectLightCount == 0)
                    throw new InvalidOperationException("Campaign fixture has no published authored object lights.");
                int authoredDynamicCount = 0;
                foreach (ClassicLightTable table in cache.VisibleTables)
                    authoredDynamicCount += table.Count - table.StaticCount;
                if (result.Level == "Level1" && authoredDynamicCount == 0)
                    throw new InvalidOperationException("Authored object lights did not reach the prepared tile tables.");
                scene.DrawCreatureFrame(simulation.World.CurrentTick, 1);
                scene.DrawCreatureShadows(probeCamera, view, simulation.World);
                if (!RenderPipeline.SupportsRenderRequest(probeCamera, new RenderPipeline.StandardRequest()))
                    throw new NotSupportedException("The configured pipeline cannot render the campaign lighting probe.");
                result.Pipeline = RenderPipelineManager.currentPipeline.GetType().FullName;
                PropertyInfo batcher = GraphicsSettings.currentRenderPipeline.GetType().GetProperty("useSRPBatcher");
                result.SrpBatcher = batcher != null && (bool)batcher.GetValue(GraphicsSettings.currentRenderPipeline);

                var tableDiagnostics = new List<TableDiagnostic>();
                scene.PrepareClassicLighting(probeCamera, viewDistance);
                tableDiagnostics.Add(DiagnoseTables("start", runner, scene, cache));

                var comparisons = new List<Comparison>();
                comparisons.Add(Compare("start", probeCamera, target, readback, torches, output, setTorches));
                probeCamera.transform.position += new Vector3(1, 0, 1);
                comparisons.Add(Compare("shifted", probeCamera, target, readback, torches, output, setTorches));
                probeCamera.transform.position = result.CameraPosition;
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = Color.black;
                RenderSettings.ambientProbe = default;
                RenderSettings.reflectionIntensity = 0;
                foreach (Light light in root.GetComponentsInChildren<Light>(true))
                    if (light.type == LightType.Directional) light.enabled = false;
                comparisons.Add(Compare("torch-only", probeCamera, target, readback, torches, output, setTorches));
                RenderSettings.ambientMode = ambientMode;
                RenderSettings.ambientLight = ambientLight;
                RenderSettings.ambientProbe = ambientProbe;
                RenderSettings.reflectionIntensity = reflection;
                foreach (Light light in root.GetComponentsInChildren<Light>(true))
                    if (light.type == LightType.Directional) light.enabled = true;
                typeof(DungeonRunner).GetMethod("RunTicks", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(runner, new object[] { 69, DK2.Simulation.InputFrame.Empty });
                var presenter = (ScenePresenter)typeof(DungeonRunner).GetField("_presenter", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(runner);
                scene.SetCreatureFrameTime(simulation.World.CurrentTick, 1);
                presenter.Present(simulation.World);
                scene.SetObjectLights(view.LightObjects(simulation.World), 1);
                scene.DrawCreatureFrame(simulation.World.CurrentTick, 1);
                torchSources = (ClassicPointLight?[])((ClassicPointLight?[])typeof(ClassicWorldLightCache)
                    .GetField("_torches", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(cache)).Clone();
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 31;
                scene.DrawCreatureShadows(probeCamera, view, simulation.World);
                torches = runner.GetComponentsInChildren<Light>(true).Where(light => light.type == LightType.Point).ToArray();
                scene.PrepareClassicLighting(probeCamera, viewDistance);
                tableDiagnostics.Add(DiagnoseTables("tick70", runner, scene, cache));
                result.TableDiagnostics = tableDiagnostics.ToArray();
                comparisons.Add(Compare("tick70", probeCamera, target, readback, torches, output, setTorches));
                if (Environment.GetEnvironmentVariable("DK2_LIGHTING_PROFILE") == "1")
                {
                    var profiles = new List<Profile>();
                    ProfileView("quiet", probeCamera, target, scene, cache, viewDistance, profiles, result.AllocationCalibration.ThreadMeterAvailable);
                    TilePos busy = DensestTorchArea(torchSources, simulation.World.Width, simulation.World.Height);
                    Vector3 focus = (Vector3)typeof(DungeonRunner).GetField("_cameraFocus", BindingFlags.NonPublic | BindingFlags.Instance)
                        .GetValue(runner);
                    probeCamera.transform.position += DungeonScene.Ground(busy.X + .5f, busy.Y + .5f) - new Vector3(focus.x, 0, focus.z);
                    ProfileView("dense torches", probeCamera, target, scene, cache, viewDistance, profiles, result.AllocationCalibration.ThreadMeterAvailable);
                    probeCamera.transform.position = result.CameraPosition;
                    scene.PrepareClassicLighting(probeCamera, viewDistance);
                    result.Profiles = profiles.ToArray();
                    result.Phases = ProfilePhases(runner, presenter, scene, view, simulation.World, probeCamera, viewDistance,
                        result.AllocationCalibration.ThreadMeterAvailable);
                }
                for (int warmup = 0; warmup < 5; warmup++) scene.DrawCreatureShadows(probeCamera, view, simulation.World);
                var samples = new double[30];
                long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                for (int sample = 0; sample < samples.Length; sample++)
                {
                    long started = System.Diagnostics.Stopwatch.GetTimestamp();
                    scene.DrawCreatureShadows(probeCamera, view, simulation.World);
                    samples[sample] = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0
                        / System.Diagnostics.Stopwatch.Frequency;
                }
                result.ShadowAllocationAvailable = result.AllocationCalibration.ThreadMeterAvailable;
                result.ShadowPrepareAllocatedBytes = result.ShadowAllocationAvailable
                    ? GC.GetAllocatedBytesForCurrentThread() - allocatedBefore : -1;
                Array.Sort(samples);
                result.ShadowPrepareMedianMs = samples[samples.Length / 2];
                result.ShadowPrepareP95Ms = samples[(int)(samples.Length * .95)];
                result.Comparisons = comparisons.ToArray();
                if (Environment.GetEnvironmentVariable("DK2_LIGHTING_VERIFY_ANCHORS") == "1")
                {
                    ValidateTerrainAnchors(result, root, view, simulation.World,
                        (ClassicMeshes)typeof(DungeonRunner).GetField("_classic", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(runner));
                    ValidateObjectLightingModes(result, root);
                    ValidateRoomObjectColour(result, root, view, simulation.World);
                }
                if (result.TorchCount == 0 || result.Shader == null)
                    throw new InvalidOperationException("Production " + level + " did not create classic torch lighting.");
                if (comparisons.Any(comparison => comparison.ChangedPixels == 0))
                    throw new InvalidOperationException("At least one production capture received no torch contribution.");
                if (result.NativeInitialAmbient && result.Level == "Level1"
                    && comparisons.Any(comparison => comparison.MeanDifference < .01f))
                    throw new InvalidOperationException("Level1 torch lighting reaches too little geometry; small lit torch models alone cannot pass.");
            }
            finally
            {
                RenderTexture.active = previousTarget;
                if (root != null)
                    foreach (Camera camera in root.GetComponentsInChildren<Camera>(true)) camera.targetTexture = null;
                if (root != null) Object.DestroyImmediate(root);
                Shader.SetGlobalInt("_DK2TorchGridCount", previousCount);
                Shader.SetGlobalInt("_DK2TorchGridWidth", previousWidth);
                Shader.SetGlobalVector("_DK2AmbientColour", previousAmbientColour);
                Shader.SetGlobalFloat("_DK2InitialAmbientPreview", previousAmbientPreview);
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
                if (readback != null) Object.DestroyImmediate(readback);
                ShaderUtil.allowAsyncCompilation = asyncCompilation;
                RenderSettings.ambientMode = ambientMode;
                RenderSettings.ambientLight = ambientLight;
                RenderSettings.ambientProbe = ambientProbe;
                RenderSettings.reflectionIntensity = reflection;
                foreach (Camera camera in mainCameras) if (camera != null) camera.tag = "MainCamera";
                for (int index = 0; index < originalLights.Length; index++)
                    if (originalLights[index] != null) originalLights[index].enabled = enabled[index];
            }
        }

        private static TableDiagnostic DiagnoseTables(string name, DungeonRunner runner, DungeonScene scene,
            ClassicWorldLightCache cache)
        {
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var result = new TableDiagnostic { Name = name, KeywordEnabled = Shader.IsKeywordEnabled(ClassicTileLightBuffer.Keyword) };
            var buffer = (ClassicTileLightBuffer)typeof(DungeonScene).GetField("_tileLights", flags).GetValue(scene);
            var gpu = (GraphicsBuffer)typeof(ClassicTileLightBuffer).GetField("_tableCounts", flags).GetValue(buffer);
            int width = runner.Simulation.World.Width, height = runner.Simulation.World.Height;
            var counts = new uint[width * height];
            gpu.GetData(counts);
            Array published = (Array)typeof(ClassicTileLightBuffer).GetField("_entries", flags).GetValue(buffer);
            Array readback = Array.CreateInstance(published.GetType().GetElementType(), width * height * ClassicLightTable.MaximumSources);
            var sources = (GraphicsBuffer)typeof(ClassicTileLightBuffer).GetField("_sources", flags).GetValue(buffer);
            sources.GetData(readback);
            var tables = new Dictionary<int, ClassicLightTable>();
            var near = new List<NearTable>();
            Vector3 focus = (Vector3)typeof(DungeonRunner).GetField("_cameraFocus", flags).GetValue(runner);
            foreach (ClassicLightTable table in cache.VisibleTables)
            {
                result.VisibleTables++;
                if (table.Count > 0) result.NonemptyTables++;
                result.SelectedLights += table.Count;
                if ((uint)table.Tile.X >= width || (uint)table.Tile.Y >= height) continue;
                int tile = table.Tile.Y * width + table.Tile.X;
                tables[tile] = table;
                if (counts[tile] != table.Count) result.GpuCountMismatches++;
                for (int slot = 0; slot < table.Count; slot++)
                {
                    int source = tile * ClassicLightTable.MaximumSources + slot;
                    if (!published.GetValue(source).Equals(readback.GetValue(source))) result.GpuSourceMismatches++;
                }
                if (Math.Abs(table.Tile.X + .5f - focus.x) > 3 || Math.Abs(-table.Tile.Y - .5f - focus.z) > 3) continue;
                int torches = 0;
                foreach (ClassicPointLight light in table.Lights)
                    if (light.Flags == ClassicLightFlags.NormalTorch || light.Flags == ClassicLightFlags.CheapTorch) torches++;
                near.Add(new NearTable { X = table.Tile.X, Y = table.Tile.Y, CpuCount = table.Count,
                    GpuCount = (int)counts[tile], TorchSources = torches });
            }
            result.NearHeart = near.ToArray();
            var anchors = new List<Vector2>();
            foreach (MeshFilter filter in runner.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!filter.name.StartsWith("terrain chunk ", StringComparison.Ordinal)
                    && !filter.name.StartsWith("transparent terrain run ", StringComparison.Ordinal)) continue;
                Mesh mesh = filter.sharedMesh;
                if (mesh == null || !mesh.isReadable) continue;
                Vector3[] positions = mesh.vertices, normals = mesh.normals;
                mesh.GetUVs(1, anchors);
                for (int index = 0; index < positions.Length; index++)
                {
                    result.TerrainVertices++;
                    if (index >= anchors.Count || anchors[index].y != 1 || !tables.TryGetValue((int)anchors[index].x, out ClassicLightTable table))
                    { result.MissingAnchors++; continue; }
                    if (table.Count == 0) { result.EmptyAnchors++; continue; }
                    Vector3 position = filter.transform.TransformPoint(positions[index]);
                    Vector3 normal = filter.transform.TransformDirection(normals[index]).normalized;
                    var p = new System.Numerics.Vector3(position.x, position.y, position.z);
                    var n = new System.Numerics.Vector3(normal.x, normal.y, normal.z);
                    bool positive = false, opposite = false;
                    foreach (ClassicPointLight light in table.Lights)
                    {
                        uint bits = (uint)light.Flags;
                        if ((bits & 0x60) != 0x60 && (bits & 0xA0) != 0xA0 && (bits & 1) == 0) continue;
                        try
                        {
                            float amount = light.ContributionAt(p, n).Length();
                            positive |= amount > 0;
                            opposite |= light.ContributionAt(p, -n).LengthSquared() > 0;
                            result.MaximumContribution = Math.Max(result.MaximumContribution, amount);
                        }
                        catch (NotSupportedException) { }
                    }
                    if (positive)
                    {
                        if (result.PositiveVertices == 0) { result.FirstPositivePosition = position; result.FirstPositiveNormal = normal; }
                        result.PositiveVertices++;
                    }
                    if (opposite) result.OppositeNormalPositiveVertices++;
                }
            }
            return result;
        }

        private static void ValidateTerrainAnchors(Result result, GameObject root, WorldView view,
            DK2.Simulation.WorldState world, ClassicMeshes classic)
        {
            int width = world.Width, height = world.Height;
            var anchors = new List<Vector2>();
            var topOnlyTiles = new HashSet<int>();
            var capOnlyTiles = new HashSet<int>();
            AssetRef rockTop = AssetCatalogue.TerrainPiece("Rock", AssetCatalogue.TerrainTop);
            foreach (TileView tile in view.Tiles(world))
                if (tile.RoomKeys == null && tile.BedKeys == null && tile.PieceKeys?.Top == rockTop
                    && tile.Pieces.HasTop && !tile.Pieces.HasFloor && tile.Pieces.Quarters == QuarterKind.None
                    && !Enumerable.Range(0, 4).Any(tile.Pieces.HasFace))
                    topOnlyTiles.Add(tile.Position.Y * width + tile.Position.X);
            foreach (TileView tile in view.Tiles(world))
                if (tile.RoomKeys == null && tile.BedKeys == null && tile.Pieces.Quarters == QuarterKind.Top
                    && !tile.Pieces.HasTop && !tile.Pieces.HasFloor
                    && !Enumerable.Range(0, 4).Any(tile.Pieces.HasFace))
                {
                    for (int quarter = 0; quarter < 4; quarter++)
                    {
                        ClassicMeshes.PieceSource source = classic.Piece(tile.PieceKeys.Quarter(
                            tile.Pieces.QuarterSet, tile.Pieces.QuarterPiece(quarter)));
                        if (source == null || source.Normals.Any(normal => normal.y < .99f))
                            throw new InvalidOperationException("Wall-cap fixture requires authored upward plane normals.");
                    }
                    capOnlyTiles.Add(tile.Position.Y * width + tile.Position.X);
                }
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                bool opaque = filter.name.StartsWith("terrain chunk ", StringComparison.Ordinal);
                bool transparent = filter.name.StartsWith("transparent terrain run ", StringComparison.Ordinal);
                bool liquid = filter.name == "Water surface" || filter.name == "Lava surface";
                if (!opaque && !transparent && !liquid) continue;
                Mesh mesh = filter.sharedMesh;
                if (mesh == null || mesh.vertexCount == 0) continue;
                var lightBounds = new List<Vector4>();
                var lightKinds = new List<Vector2>();
                mesh.GetUVs(2, lightBounds); mesh.GetUVs(3, lightKinds);
                if (lightBounds.Count != mesh.vertexCount || lightKinds.Count != mesh.vertexCount)
                    throw new InvalidOperationException(filter.name + " has implicit static light inputs; missing channels can select animated lighting.");
                if (lightBounds.Any(value => value != Vector4.zero) || lightKinds.Any(value => value != Vector2.zero))
                    throw new InvalidOperationException(filter.name + " contains animated light inputs on static terrain.");
                anchors.Clear();
                mesh.GetUVs(1, anchors);
                if (anchors.Count != mesh.vertexCount)
                    throw new InvalidOperationException(filter.name + " lost its tile light anchors.");
                foreach (Vector2 anchor in anchors)
                    if (anchor.y != 1 || anchor.x < 0 || anchor.x >= width * height || anchor.x != Mathf.Floor(anchor.x))
                        throw new InvalidOperationException(filter.name + " contains an invalid tile light anchor.");
                Vector3[] normals = mesh.normals;
                for (int vertex = 0; vertex < anchors.Count; vertex++)
                    if (opaque && topOnlyTiles.Contains((int)anchors[vertex].x))
                    {
                        if (filter.transform.TransformDirection(normals[vertex]).y > -.99f)
                            throw new InvalidOperationException("Ordinary top lacks native drawflag0x80 normal inversion.");
                        result.VerifiedTopNormalVertices++;
                    }
                for (int vertex = 0; vertex < anchors.Count; vertex++)
                    if (opaque && capOnlyTiles.Contains((int)anchors[vertex].x))
                    {
                        if (filter.transform.TransformDirection(normals[vertex]).y > -.99f)
                            throw new InvalidOperationException("Wall cap quarter lacks native drawflag0x80 normal inversion.");
                        result.VerifiedWallCapNormalVertices++;
                    }
                foreach (int submesh in Enumerable.Range(0, mesh.subMeshCount))
                {
                    int[] indices = mesh.GetTriangles(submesh);
                    for (int index = 0; index < indices.Length; index += 3)
                        if (anchors[indices[index]] != anchors[indices[index + 1]]
                            || anchors[indices[index]] != anchors[indices[index + 2]])
                            throw new InvalidOperationException(filter.name + " mixes light anchors within a triangle.");
                }
                if (opaque) result.AnchoredTerrainVertices += anchors.Count;
                else if (transparent) result.AnchoredTransparentVertices += anchors.Count;
                else result.AnchoredLiquidVertices += anchors.Count;
            }
            if (result.AnchoredTerrainVertices == 0 || result.AnchoredTransparentVertices == 0)
                throw new InvalidOperationException("Anchor verification needs opaque and sorted transparent terrain.");
            if (result.Level == "Level8" && result.AnchoredLiquidVertices == 0)
                throw new InvalidOperationException("Level8 anchor verification needs its water/lava surfaces.");
            if (result.Level == "Level1" && result.VerifiedWallCapNormalVertices == 0)
                throw new InvalidOperationException("Wall-cap verification needs cap-only terrain.");
            if (result.Level == "Level1" && result.VerifiedTopNormalVertices == 0)
                throw new InvalidOperationException("Top-normal verification needs ordinary top-only terrain.");
        }

        private static void ValidateRoomObjectColour(Result result, GameObject root, WorldView view,
            DK2.Simulation.WorldState world)
        {
            MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            foreach (ObjectView instance in view.Decorations(world))
            {
                if (instance.ObjectTypeId != 86) continue;
                MeshRenderer renderer = renderers.FirstOrDefault(candidate => candidate.name == "object " + instance.Id);
                if (renderer == null) continue;
                foreach (Material material in renderer.sharedMaterials)
                {
                    Color expected = new Color(instance.AddedLight.Red / 255f, instance.AddedLight.Green / 255f,
                        instance.AddedLight.Blue / 255f, 0f);
                    Color actual = material.GetColor("_EmissionColor");
                    if (!material.IsKeywordEnabled("_EMISSION") || Math.Abs(actual.r - expected.r) > .00001f
                        || Math.Abs(actual.g - expected.g) > .00001f || Math.Abs(actual.b - expected.b) > .00001f)
                        throw new InvalidOperationException("Heart arches lost their captured room colour contribution.");
                    result.VerifiedRoomObjectColourMaterials++;
                }
            }
            if (result.Level == "Level1" && result.VerifiedRoomObjectColourMaterials == 0)
                throw new InvalidOperationException("Room-colour verification needs the Heart arches.");
        }

        private static void ValidateObjectLightingModes(Result result, GameObject root)
        {
            var markers = new List<Vector2>();
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                bool torch = filter.name == "torch mesh";
                bool staticObject = filter.name.StartsWith("object ", StringComparison.Ordinal)
                    && filter.GetComponent<ClassicCreatureAnimation>() == null;
                if (!torch && !staticObject) continue;
                Mesh mesh = filter.sharedMesh;
                if (mesh == null) continue;
                markers.Clear(); mesh.GetUVs(3, markers);
                if (markers.Count == 0) continue;
                Vector2 expected = torch ? new Vector2(0, 1) : Vector2.one;
                if (markers.Count != mesh.vertexCount || markers.Any(marker => marker != expected))
                    throw new InvalidOperationException(filter.name + " uses the wrong native lighting path.");
                if (torch) result.VerifiedTorchLightingVertices += markers.Count;
                else result.VerifiedStaticObjectLightingVertices += markers.Count;
            }
            if (result.VerifiedTorchLightingVertices == 0 || result.VerifiedStaticObjectLightingVertices == 0)
                throw new InvalidOperationException("Object-lighting verification needs static objects and torch mounts.");
        }

        private static void SampleGeometry(Result result, DungeonRunner runner, Light[] lights)
        {
            MeshFilter[] filters = runner.GetComponentsInChildren<MeshFilter>(true);
            result.MeshCount = filters.Length;
            foreach (MeshFilter filter in filters)
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null || !mesh.isReadable) continue;
                Vector3[] positions = mesh.vertices, normals = mesh.normals;
                if (normals.Length != positions.Length) continue;
                int stride = Math.Max(1, positions.Length / 16);
                for (int index = 0; index < positions.Length; index += stride)
                {
                    Vector3 vertex = filter.transform.TransformPoint(positions[index]);
                    Vector3 normal = filter.transform.TransformDirection(normals[index]).normalized;
                    result.SampledVertices++;
                    foreach (Light light in lights)
                    {
                        Vector3 delta = light.transform.position - vertex;
                        float squared = delta.sqrMagnitude, radiusSquared = light.range * light.range;
                        float incidence = Vector3.Dot(normal, delta) * light.intensity;
                        if (squared >= radiusSquared || incidence <= 0) continue;
                        result.FrontFacingInRangePairs++;
                        int bin = Mathf.RoundToInt(squared * 16 - .49999f);
                        float factor = bin == 0 ? 1 : Mathf.Min(1,
                            incidence * (.25f + 16f / bin) * (radiusSquared - squared) / radiusSquared);
                        if (factor > 0) result.PositiveNativeContributions++;
                        if (factor >= 1) result.SaturatedNativeContributions++;
                        result.MaximumNativeContribution = Mathf.Max(result.MaximumNativeContribution, factor);
                    }
                }
            }
        }

        private static Comparison Compare(string name, Camera camera, RenderTexture target, Texture2D readback,
            Light[] lights, string output, Action<bool> setTorches)
        {
            foreach (Light light in lights) light.enabled = true;
            setTorches(true);
            Capture(camera, target, readback);
            Color32[] lit = Capture(camera, target, readback);
            File.WriteAllBytes(Path.Combine(output, name + "-torches.png"), readback.EncodeToPNG());
            foreach (Light light in lights) light.enabled = false;
            setTorches(false);
            Color32[] unlit = Capture(camera, target, readback);
            File.WriteAllBytes(Path.Combine(output, name + "-without-torches.png"), readback.EncodeToPNG());
            var comparison = new Comparison { Name = name };
            for (int index = 0; index < lit.Length; index++)
            {
                int difference = Math.Max(Math.Abs(lit[index].r - unlit[index].r),
                    Math.Max(Math.Abs(lit[index].g - unlit[index].g), Math.Abs(lit[index].b - unlit[index].b)));
                if (difference > 1) comparison.ChangedPixels++;
                if (lit[index].r + lit[index].g + lit[index].b > unlit[index].r + unlit[index].g + unlit[index].b + 3)
                    comparison.BrighterPixels++;
                comparison.MaximumDifference = Mathf.Max(comparison.MaximumDifference, difference / 255f);
                comparison.MeanDifference += difference / 255f / lit.Length;
                comparison.MeanWithTorches += (lit[index].r + lit[index].g + lit[index].b) / (765f * lit.Length);
                comparison.MeanWithoutTorches += (unlit[index].r + unlit[index].g + unlit[index].b) / (765f * lit.Length);
            }
            foreach (Light light in lights) light.enabled = true;
            setTorches(true);
            return comparison;
        }

        private static void ProfileView(string view, Camera camera, RenderTexture target, DungeonScene scene,
            ClassicWorldLightCache cache, float distance, List<Profile> profiles, bool allocationAvailable)
        {
            scene.PrepareClassicLighting(camera, distance);
            int selectedSources = 0;
            foreach (ClassicLightTable table in cache.VisibleTables) selectedSources += table.Count;
            foreach (bool native in new[] { false, true })
            {
                const int count = 120;
                var prepare = new double[count]; var submit = new double[count];
                long allocated = 0;
                for (int sample = -20; sample < count; sample++)
                {
                    long bytes = GC.GetAllocatedBytesForCurrentThread();
                    long start = System.Diagnostics.Stopwatch.GetTimestamp();
                    if (native) scene.PrepareClassicLighting(camera, distance);
                    else Shader.DisableKeyword(ClassicTileLightBuffer.Keyword);
                    long prepared = System.Diagnostics.Stopwatch.GetTimestamp();
                    long delta = GC.GetAllocatedBytesForCurrentThread() - bytes;
                    RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
                    long end = System.Diagnostics.Stopwatch.GetTimestamp();
                    if (sample < 0) continue;
                    prepare[sample] = (prepared - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                    submit[sample] = (end - prepared) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                    allocated += delta;
                }
                Array.Sort(prepare); Array.Sort(submit);
                profiles.Add(new Profile { View = view, Mode = native ? "native tables" : "legacy torch preview",
                    Samples = count, VisibleTables = cache.VisibleTables.Length, SelectedSources = selectedSources,
                    PrepareMedianMs = prepare[count / 2], PrepareP95Ms = prepare[(int)(count * .95)],
                    SubmitMedianMs = submit[count / 2], SubmitP95Ms = submit[(int)(count * .95)],
                    AllocationAvailable = allocationAvailable, PrepareAllocatedBytes = allocationAvailable ? allocated : -1 });
            }
        }

        private static Phase[] ProfilePhases(DungeonRunner runner, ScenePresenter presenter, DungeonScene scene,
            WorldView view, DK2.Simulation.WorldState world, Camera camera, float distance, bool allocationAvailable)
        {
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            FieldInfo speed = typeof(DungeonRunner).GetField("speed", flags);
            FieldInfo diagnostics = typeof(DungeonRunner).GetField("showDiagnostics", flags);
            float previousSpeed = (float)speed.GetValue(runner);
            bool previousDiagnostics = (bool)diagnostics.GetValue(runner);
            var construction = (ClassicConstructionEffects)typeof(DungeonRunner).GetField("_constructionEffects", flags).GetValue(runner);
            Action update = (Action)Delegate.CreateDelegate(typeof(Action), runner, typeof(DungeonRunner).GetMethod("Update", flags));
            Action effects = construction != null ? construction.Draw : () => { };
            Action[] actions = { () => presenter.Present(world), () => scene.DrawCreatureFrame(world.CurrentTick, 1),
                () => scene.DrawCreatureShadows(camera, view, world), () => scene.PrepareClassicLighting(camera, distance), effects, update };
            string[] names = { "Presenter.Present", "Scene.DrawCreatureFrame", "Scene.DrawCreatureShadows",
                "Scene.PrepareClassicLighting", "ConstructionEffects.Draw", "Runner.Update" };
            var phases = new Phase[actions.Length];
            GameTick tick = world.CurrentTick;
            try
            {
                speed.SetValue(runner, 0f); diagnostics.SetValue(runner, false);
                for (int index = 0; index < actions.Length; index++)
                {
                    Action action = actions[index];
                    for (int warm = 0; warm < 5; warm++) action();
                    const int count = 100;
                    var samples = new double[count];
                    long allocated = 0;
                    for (int sample = 0; sample < count; sample++)
                    {
                        long bytes = GC.GetAllocatedBytesForCurrentThread();
                        long start = System.Diagnostics.Stopwatch.GetTimestamp();
                        action();
                        long end = System.Diagnostics.Stopwatch.GetTimestamp();
                        allocated += GC.GetAllocatedBytesForCurrentThread() - bytes;
                        samples[sample] = (end - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                    }
                    Array.Sort(samples);
                    phases[index] = new Phase { Name = names[index], Samples = count, AllocationAvailable = allocationAvailable,
                        AllocatedBytes = allocationAvailable ? allocated : -1, BytesPerCall = allocationAvailable ? allocated / (double)count : -1,
                        MedianMs = samples[count / 2], P95Ms = samples[(int)(count * .95)] };
                }
                if (world.CurrentTick != tick) throw new InvalidOperationException("Allocation profiling advanced the fixed simulation tick.");
                return phases;
            }
            finally
            {
                speed.SetValue(runner, previousSpeed); diagnostics.SetValue(runner, previousDiagnostics);
                scene.SetCreatureFrameTime(world.CurrentTick, 1);
            }
        }

        private static TilePos DensestTorchArea(ClassicPointLight?[] sources, int width, int height)
        {
            var sums = new int[(width + 1) * (height + 1)];
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

        private static Color32[] Capture(Camera camera, RenderTexture target, Texture2D readback)
        {
            // Edit-mode camera preparation: see docs/TRAPS.md.
            foreach (TransparentTerrain terrain in camera.transform.root.GetComponentsInChildren<TransparentTerrain>(true))
                typeof(TransparentTerrain).GetMethod("BeforeCamera", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(terrain, new object[] { default(ScriptableRenderContext), camera });
            RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
            RenderTexture.active = target;
            readback.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            readback.Apply();
            return readback.GetPixels32();
        }

        private static void Set(DungeonRunner runner, string name, object value)
            => typeof(DungeonRunner).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(runner, value);
    }
}
