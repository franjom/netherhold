// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using DK2.Core;
using DK2.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
using NumericsVector3 = System.Numerics.Vector3;

namespace DK2.Unity.Editor
{
    public static class ClassicLightingRenderProbe
    {
        [Serializable]
        private sealed class Result
        {
            public bool Passed;
            public int Cases;
            public float MaximumError;
            public string Pipeline;
            public bool SrpBatcher;
            public string Error;
        }

        private static bool _running;

        [MenuItem("DK2/Classic look/Verify native lighting through URP")]
        public static void Run()
        {
            if (_running) throw new InvalidOperationException("The lighting render probe is already running.");
            _running = true;
            EditorApplication.update += RenderWhenReady;
            EditorApplication.QueuePlayerLoopUpdate();
        }

        private static void RenderWhenReady()
        {
            EditorApplication.update -= RenderWhenReady;
            var result = new Result();
            try
            {
                EnsurePipeline();
                result.Pipeline = RenderPipelineManager.currentPipeline.GetType().FullName;
                PropertyInfo batcher = GraphicsSettings.currentRenderPipeline.GetType().GetProperty("useSRPBatcher");
                result.SrpBatcher = batcher != null && (bool)batcher.GetValue(GraphicsSettings.currentRenderPipeline);
                Verify(result);
                result.Passed = true;
            }
            catch (Exception error) { result.Error = error.ToString(); }
            finally { _running = false; }
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../build/step5-lighting-render-probe.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, JsonUtility.ToJson(result, true));
            Debug.Log($"DK2 lighting render probe: {(result.Passed ? "PASS" : "FAIL")}; {result.Cases} cases; {output}");
            if (Application.isBatchMode) EditorApplication.Exit(result.Passed ? 0 : 1);
        }

        private static void EnsurePipeline()
        {
            if (RenderPipelineManager.currentPipeline != null) return;
            if (GraphicsSettings.currentRenderPipeline == null)
                throw new InvalidOperationException("No render pipeline asset is configured for the active quality level.");
            // SupportsRenderRequest initializes the configured pipeline through its manager even
            // when batch mode has no editor view driving a normal render loop.
            var bootstrap = new GameObject("DK2 render pipeline bootstrap") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                Camera camera = bootstrap.AddComponent<Camera>();
                camera.enabled = false;
                if (!RenderPipeline.SupportsRenderRequest(camera, new RenderPipeline.StandardRequest())
                    || RenderPipelineManager.currentPipeline == null)
                    throw new NotSupportedException("The configured pipeline could not initialize a standard render request.");
            }
            finally { Object.DestroyImmediate(bootstrap); }
        }

        private static void Verify(Result result)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play mode before running this transient render probe.");
            int previousCount = Shader.GetGlobalInt("_DK2TorchGridCount");
            int previousWidth = Shader.GetGlobalInt("_DK2TorchGridWidth");
            if (previousCount != 0)
                throw new InvalidOperationException("A live torch buffer is published; the probe will not replace it.");
            Shader shader = Resources.Load<Shader>("Shaders/ClassicVertexLit");
            if (shader == null || !shader.isSupported)
                throw new InvalidOperationException("The classic vertex lighting shader is missing or unsupported.");

            Light[] sceneLights = Resources.FindObjectsOfTypeAll<Light>()
                .Where(light => light.gameObject.scene.IsValid()).ToArray();
            bool[] lightEnabled = sceneLights.Select(light => light.enabled).ToArray();
            AmbientMode ambientMode = RenderSettings.ambientMode;
            Color ambientLight = RenderSettings.ambientLight;
            SphericalHarmonicsL2 ambientProbe = RenderSettings.ambientProbe;
            float reflection = RenderSettings.reflectionIntensity;
            RenderTexture previousTarget = RenderTexture.active;
            bool asyncCompilation = ShaderUtil.allowAsyncCompilation;
            GameObject root = null;
            Mesh mesh = null;
            Material material = null;
            RenderTexture target = null;
            Texture2D readback = null;
            IDisposable buffer = null;
            try
            {
                foreach (Light sceneLight in sceneLights) sceneLight.enabled = false;
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = Color.black;
                RenderSettings.ambientProbe = default;
                RenderSettings.reflectionIntensity = 0;
                ShaderUtil.allowAsyncCompilation = false;
                root = new GameObject("DK2 transient lighting render probe") { hideFlags = HideFlags.HideAndDontSave };
                var surface = new GameObject("Surface") { layer = 31 };
                surface.transform.SetParent(root.transform);
                mesh = new Mesh { name = "Probe quad", hideFlags = HideFlags.HideAndDontSave };
                mesh.vertices = new[] { new Vector3(.49f, 0, -.51f), new Vector3(.49f, 0, -.49f),
                    new Vector3(.51f, 0, -.49f), new Vector3(.51f, 0, -.51f) };
                mesh.normals = Enumerable.Repeat(Vector3.up, 4).ToArray();
                mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
                mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                mesh.RecalculateBounds();
                surface.AddComponent<MeshFilter>().sharedMesh = mesh;
                material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                material.SetColor("_BaseColor", new Color(.25f, .5f, 1f, 1f));
                material.SetTexture("_BaseMap", Texture2D.whiteTexture);
                surface.AddComponent<MeshRenderer>().sharedMaterial = material;
                var source = new GameObject("Point light") { layer = 31 };
                source.transform.SetParent(root.transform);
                source.transform.position = new Vector3(.5f, 1f, -.5f);
                Light light = source.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = 2;
                light.color = Color.white;
                light.intensity = .01f;
                light.shadows = LightShadows.None;
                light.cullingMask = 1 << 31;
                var cameraObject = new GameObject("Camera");
                cameraObject.transform.SetParent(root.transform);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.transform.position = new Vector3(.5f, 3f, -.5f);
                camera.transform.rotation = Quaternion.Euler(90, 0, 0);
                camera.orthographic = true;
                camera.orthographicSize = .015f;
                camera.nearClipPlane = .1f;
                camera.farClipPlane = 5;
                camera.cullingMask = 1 << 31;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.allowHDR = true;
                camera.allowMSAA = false;
                target = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                target.Create();
                readback = new Texture2D(64, 64, TextureFormat.RGBAFloat, false, true);

                Type bufferType = typeof(DungeonRunner).Assembly.GetType("DK2.Unity.ClassicTorchLightBuffer", true);
                buffer = (IDisposable)Activator.CreateInstance(bufferType, 1, 1);
                var torch = new TorchView(0, new TilePos(0, 0), 2048, 2048, 4096, 0,
                    255, 128, 0, 128, 8192, default);
                var native = ClassicPointLight.FromTorch(torch);
                NumericsVector3 value = native.ContributionAt(new NumericsVector3(.49f, 0, -.51f), NumericsVector3.UnitY);
                var expected = new Color(Pack(value.X) * .25f, Pack(value.Y) * .5f, Pack(value.Z), 1);
                Invoke(buffer, "Set", 0, torch);
                Invoke(buffer, "Flush");
                Check(result, Draw(camera, target, readback), expected);
                camera.orthographicSize = .005f;
                Check(result, Draw(camera, target, readback), expected);
                camera.orthographicSize = .015f;
                Invoke(buffer, "Remove", 0);
                Invoke(buffer, "Flush");
                CheckRemoved(result, Draw(camera, target, readback));
                Invoke(buffer, "Set", 0, torch);
                Invoke(buffer, "Flush");
                Check(result, Draw(camera, target, readback), expected);
                Invoke(buffer, "Clear");
                CheckRemoved(result, Draw(camera, target, readback));
            }
            finally
            {
                buffer?.Dispose();
                Shader.SetGlobalInt("_DK2TorchGridCount", previousCount);
                Shader.SetGlobalInt("_DK2TorchGridWidth", previousWidth);
                RenderTexture.active = previousTarget;
                if (target != null) target.Release();
                if (root != null) Object.DestroyImmediate(root);
                if (mesh != null) Object.DestroyImmediate(mesh);
                if (material != null) Object.DestroyImmediate(material);
                if (target != null) Object.DestroyImmediate(target);
                if (readback != null) Object.DestroyImmediate(readback);
                ShaderUtil.allowAsyncCompilation = asyncCompilation;
                RenderSettings.ambientMode = ambientMode;
                RenderSettings.ambientLight = ambientLight;
                RenderSettings.ambientProbe = ambientProbe;
                RenderSettings.reflectionIntensity = reflection;
                for (int i = 0; i < sceneLights.Length; i++)
                    if (sceneLights[i] != null) sceneLights[i].enabled = lightEnabled[i];
            }
        }

        private static void Invoke(IDisposable buffer, string method, params object[] arguments)
            => buffer.GetType().GetMethod(method).Invoke(buffer, arguments);

        private static float Pack(float value) => Mathf.Min(255, Mathf.Floor(Mathf.Max(0, value) * 255f + .00001f)) / 255f;

        private static Color Draw(Camera camera, RenderTexture target, Texture2D readback)
        {
            var request = new RenderPipeline.StandardRequest { destination = target };
            if (!RenderPipeline.SupportsRenderRequest(camera, request))
                throw new NotSupportedException("The active pipeline cannot render a standard request.");
            RenderPipeline.SubmitRenderRequest(camera, request);
            RenderTexture.active = target;
            readback.ReadPixels(new Rect(0, 0, 64, 64), 0, 0);
            readback.Apply();
            return readback.GetPixel(32, 32);
        }

        private static void Check(Result result, Color actual, Color expected)
        {
            float error = Mathf.Max(Mathf.Abs(actual.r - expected.r), Mathf.Abs(actual.g - expected.g),
                Mathf.Abs(actual.b - expected.b), Mathf.Abs(actual.a - expected.a));
            result.MaximumError = Mathf.Max(result.MaximumError, error);
            if (float.IsNaN(error) || error > .002f)
                throw new InvalidOperationException($"URP case {result.Cases}: expected {expected}, got {actual}, error {error}.");
            result.Cases++;
        }

        private static void CheckRemoved(Result result, Color actual)
        {
            if (float.IsNaN(actual.r) || actual.r > .05f || actual.a < .99f)
                throw new InvalidOperationException($"Torch removal retained native light, or the surface was not rendered: {actual}.");
            result.Cases++;
        }
    }
}
