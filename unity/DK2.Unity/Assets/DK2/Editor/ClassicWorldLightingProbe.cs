// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DK2.Presentation;
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
            public bool NativeInitialAmbient;
            public string Pipeline;
            public bool SrpBatcher;
            public string Shader;
            public int TorchCount;
            public int BufferCountBeforeFlush;
            public int BufferCountAfterFlush;
            public int MeshCount;
            public double ShadowPrepareMedianMs, ShadowPrepareP95Ms;
            public long ShadowPrepareAllocatedBytes;
            public int AnchoredTerrainVertices, AnchoredTransparentVertices, AnchoredLiquidVertices;
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
            public string Error;
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
                scene.Flush();
                result.BufferCountAfterFlush = Shader.GetGlobalInt("_DK2TorchGridCount");
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
                scene.DrawCreatureFrame(simulation.World.CurrentTick, 1);
                scene.DrawCreatureShadows(probeCamera, view, simulation.World);
                if (!RenderPipeline.SupportsRenderRequest(probeCamera, new RenderPipeline.StandardRequest()))
                    throw new NotSupportedException("The configured pipeline cannot render the campaign lighting probe.");
                result.Pipeline = RenderPipelineManager.currentPipeline.GetType().FullName;
                PropertyInfo batcher = GraphicsSettings.currentRenderPipeline.GetType().GetProperty("useSRPBatcher");
                result.SrpBatcher = batcher != null && (bool)batcher.GetValue(GraphicsSettings.currentRenderPipeline);

                var comparisons = new List<Comparison>();
                comparisons.Add(Compare("start", probeCamera, target, readback, torches, output));
                probeCamera.transform.position += new Vector3(1, 0, 1);
                comparisons.Add(Compare("shifted", probeCamera, target, readback, torches, output));
                probeCamera.transform.position = result.CameraPosition;
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = Color.black;
                RenderSettings.ambientProbe = default;
                RenderSettings.reflectionIntensity = 0;
                foreach (Light light in root.GetComponentsInChildren<Light>(true))
                    if (light.type == LightType.Directional) light.enabled = false;
                comparisons.Add(Compare("torch-only", probeCamera, target, readback, torches, output));
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
                scene.DrawCreatureFrame(simulation.World.CurrentTick, 1);
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 31;
                scene.DrawCreatureShadows(probeCamera, view, simulation.World);
                torches = runner.GetComponentsInChildren<Light>(true).Where(light => light.type == LightType.Point).ToArray();
                comparisons.Add(Compare("tick70", probeCamera, target, readback, torches, output));
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
                result.ShadowPrepareAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                Array.Sort(samples);
                result.ShadowPrepareMedianMs = samples[samples.Length / 2];
                result.ShadowPrepareP95Ms = samples[(int)(samples.Length * .95)];
                result.Comparisons = comparisons.ToArray();
                if (Environment.GetEnvironmentVariable("DK2_LIGHTING_VERIFY_ANCHORS") == "1")
                    ValidateTerrainAnchors(result, root, simulation.World.Width, simulation.World.Height);
                if (result.TorchCount == 0 || result.Shader == null)
                    throw new InvalidOperationException("Production " + level + " did not create classic torch lighting.");
                if (comparisons.Any(comparison => comparison.ChangedPixels == 0))
                    throw new InvalidOperationException("At least one production capture received no torch contribution.");
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

        private static void ValidateTerrainAnchors(Result result, GameObject root, int width, int height)
        {
            var anchors = new List<Vector2>();
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                bool opaque = filter.name.StartsWith("terrain chunk ", StringComparison.Ordinal);
                bool transparent = filter.name.StartsWith("transparent terrain run ", StringComparison.Ordinal);
                bool liquid = filter.name == "Water surface" || filter.name == "Lava surface";
                if (!opaque && !transparent && !liquid) continue;
                Mesh mesh = filter.sharedMesh;
                if (mesh == null || mesh.vertexCount == 0) continue;
                anchors.Clear();
                mesh.GetUVs(1, anchors);
                if (anchors.Count != mesh.vertexCount)
                    throw new InvalidOperationException(filter.name + " lost its tile light anchors.");
                foreach (Vector2 anchor in anchors)
                    if (anchor.y != 1 || anchor.x < 0 || anchor.x >= width * height || anchor.x != Mathf.Floor(anchor.x))
                        throw new InvalidOperationException(filter.name + " contains an invalid tile light anchor.");
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
            Light[] lights, string output)
        {
            foreach (Light light in lights) light.enabled = true;
            Capture(camera, target, readback);
            Color32[] lit = Capture(camera, target, readback);
            File.WriteAllBytes(Path.Combine(output, name + "-torches.png"), readback.EncodeToPNG());
            foreach (Light light in lights) light.enabled = false;
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
            return comparison;
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
