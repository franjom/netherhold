// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using DK2.Core;
using DK2.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
using NVector3 = System.Numerics.Vector3;

namespace DK2.Unity.Editor
{
    public static class ClassicTileLightProbe
    {
        [Serializable] private sealed class Result
        {
            public bool Passed;
            public int Cases;
            public int SceneLifecycleChecks;
            public float MaximumError;
            public string Pipeline, ShaderMessages;
            public bool ShaderSupported, TableKeyword;
            public int PublishedWidth, PublishedTiles, PublishedSelectedSources;
            public Vector4 FirstSourcePositionRadius, FirstSourceColourIntensity;
            public Bounds RendererBoundsBefore, RendererBoundsAfter;
            public Vector3 CentreViewport;
            public bool FrustumVisible, RendererEnabled, RendererActive;
            public int VertexCount, SubmeshCount, RendererLayer, CameraMask;
            public float OriginalCull;
            public Color UnlitControl, EmissionBackCull, EmissionCullOff;
            public string Error;
        }

        [StructLayout(LayoutKind.Sequential)] private struct SourceEntry
        {
            public Vector4 PositionRadiusSquared;
            public Vector4 ColourIntensity;
        }

        [MenuItem("DK2/Classic look/Verify preselected tile light tables on GPU")]
        public static void Run()
        {
            EditorApplication.update += RunWhenReady;
            EditorApplication.QueuePlayerLoopUpdate();
        }

        private static void RunWhenReady()
        {
            EditorApplication.update -= RunWhenReady;
            var result = new Result();
            try { EnsurePipeline(); Verify(result); result.Passed = true; }
            catch (Exception error) { result.Error = error.ToString(); }
            string output = Environment.GetEnvironmentVariable("DK2_TILE_LIGHT_PROBE_OUTPUT")
                ?? Path.GetFullPath(Path.Combine(Application.dataPath, "../../../build/step5-tile-lights/result.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, JsonUtility.ToJson(result, true));
            Debug.Log($"DK2 tile light probe: {(result.Passed ? "PASS" : "FAIL")}; {result.Cases} cases; {output}");
            if (Application.isBatchMode) EditorApplication.Exit(result.Passed ? 0 : 1);
        }

        private static void Verify(Result result)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play mode before this transient probe.");
            int previousCount = Shader.GetGlobalInt("_DK2TileLightGridCount");
            int previousWidth = Shader.GetGlobalInt("_DK2TileLightGridWidth");
            int previousTorchCount = Shader.GetGlobalInt("_DK2TorchGridCount");
            int previousTorchWidth = Shader.GetGlobalInt("_DK2TorchGridWidth");
            bool previousKeyword = Shader.IsKeywordEnabled(ClassicTileLightBuffer.Keyword);
            if (previousCount != 0 || previousTorchCount != 0 || previousKeyword)
                throw new InvalidOperationException("A live classic light buffer is published; the probe will not replace it.");
            Shader shader = Resources.Load<Shader>("Shaders/ClassicVertexLit");
            result.Pipeline = RenderPipelineManager.currentPipeline?.GetType().FullName;
            result.ShaderSupported = shader != null && shader.isSupported;
            if (shader != null) result.ShaderMessages = string.Join("\n", ShaderUtil.GetShaderMessages(shader).Select(message => message.message));
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("Classic shader unavailable.");
            Light[] lights = Resources.FindObjectsOfTypeAll<Light>().Where(light => light.gameObject.scene.IsValid()).ToArray();
            bool[] enabled = lights.Select(light => light.enabled).ToArray();
            float previousPreview = Shader.GetGlobalFloat("_DK2InitialAmbientPreview");
            Vector4 previousAmbient = Shader.GetGlobalVector("_DK2AmbientColour");
            RenderTexture previousTarget = RenderTexture.active;
            bool previousAsync = ShaderUtil.allowAsyncCompilation;
            GameObject root = null;
            Material material = null;
            Material unlitControl = null;
            Mesh mesh = null;
            RenderTexture target = null;
            Texture2D readback = null;
            ClassicTileLightBuffer buffer = null;
            IDisposable legacyBuffer = null;
            try
            {
                foreach (Light light in lights) light.enabled = false;
                ShaderUtil.allowAsyncCompilation = false;
                legacyBuffer = (IDisposable)Activator.CreateInstance(
                    typeof(ClassicMeshes).Assembly.GetType("DK2.Unity.ClassicTorchLightBuffer", throwOnError: true), 3, 3);
                Shader.SetGlobalFloat("_DK2InitialAmbientPreview", 1);
                Shader.SetGlobalVector("_DK2AmbientColour", Vector4.zero);
                root = new GameObject("Transient tile light table probe") { hideFlags = HideFlags.HideAndDontSave };
                var cameraObject = new GameObject("Camera"); cameraObject.transform.SetParent(root.transform);
                Camera camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
                camera.transform.SetPositionAndRotation(new Vector3(2.5f, 3, -1.5f), Quaternion.Euler(90, 0, 0));
                camera.orthographic = true; camera.orthographicSize = .02f;
                camera.aspect = 1;
                camera.nearClipPlane = .1f; camera.farClipPlane = 5;
                camera.cullingMask = 1 << 31; camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.magenta; camera.allowHDR = true; camera.allowMSAA = false;
                var surface = new GameObject("Anchored surface") { layer = 31 };
                surface.transform.SetParent(root.transform);
                mesh = new Mesh { name = "Probe anchored quad", hideFlags = HideFlags.HideAndDontSave };
                Vector3[] corners = { new Vector3(2.49f, 0, -1.51f), new Vector3(2.49f, 0, -1.49f),
                    new Vector3(2.51f, 0, -1.49f), new Vector3(2.51f, 0, -1.51f) };
                mesh.vertices = corners;
                mesh.normals = Enumerable.Repeat(Vector3.up, 4).ToArray();
                mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
                mesh.uv2 = Enumerable.Repeat(new Vector2(4, 1), 4).ToArray();
                mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                surface.AddComponent<MeshFilter>().sharedMesh = mesh;
                material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                material.SetColor("_BaseColor", Color.white); material.SetTexture("_BaseMap", Texture2D.whiteTexture);
                MeshRenderer renderer = surface.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
                target = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                target.Create(); readback = new Texture2D(64, 64, TextureFormat.RGBAFloat, false, true);
                if (!RenderPipeline.SupportsRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target }))
                    throw new NotSupportedException("Configured render pipeline does not support standard requests.");
                result.RendererBoundsBefore = renderer.bounds;
                mesh.RecalculateBounds();
                result.RendererBoundsAfter = renderer.bounds;
                result.CentreViewport = camera.WorldToViewportPoint(new Vector3(2.5f, 0, -1.5f));
                result.FrustumVisible = GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(camera), renderer.bounds);
                result.VertexCount = mesh.vertexCount; result.SubmeshCount = mesh.subMeshCount;
                result.RendererEnabled = renderer.enabled; result.RendererActive = surface.activeInHierarchy;
                result.RendererLayer = surface.layer; result.CameraMask = camera.cullingMask;
                unlitControl = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                unlitControl.SetColor("_BaseColor", Color.white);
                unlitControl.SetFloat("_Cull", (float)CullMode.Off);
                renderer.sharedMaterial = unlitControl;
                result.UnlitControl = Draw(camera, target, readback);
                renderer.sharedMaterial = material;
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", new Color(.2f, .4f, .6f, 0));
                result.OriginalCull = material.GetFloat("_Cull");
                result.EmissionBackCull = Draw(camera, target, readback);
                material.SetFloat("_Cull", (float)CullMode.Off);
                result.EmissionCullOff = Draw(camera, target, readback);
                Check(result, result.UnlitControl, Color.white);
                Check(result, result.EmissionCullOff, new Color(Pack(.2f), Pack(.4f), Pack(.6f), 1));
                material.DisableKeyword("_EMISSION"); material.SetColor("_EmissionColor", Color.black);
                ClassicPointLight red = Source(180, 50, 20), blue = Source(20, 50, 180);
                var redTable = new ClassicLightTable(new TilePos(1, 1)); redTable.RebuildStatic(new[] { red });
                var blueTable = new ClassicLightTable(new TilePos(0, 0)); blueTable.RebuildStatic(new[] { blue });
                buffer = new ClassicTileLightBuffer(3, 3);
                buffer.Set(redTable); buffer.Set(blueTable); buffer.Flush();
                RecordPublication(result, buffer, 4);
                Check(result, Draw(camera, target, readback), Expected(red, corners[0]));
                var duplicateSource = new GameObject("URP point must not double count") { layer = 31 };
                duplicateSource.transform.SetParent(root.transform);
                duplicateSource.transform.position = new Vector3(2.5f, 1, -1.5f);
                Light point = duplicateSource.AddComponent<Light>(); point.type = LightType.Point;
                point.range = 3; point.intensity = 1; point.cullingMask = 1 << 31;
                Check(result, Draw(camera, target, readback), Expected(red, corners[0]));
                // Unpublished tiles stay without punctual light even when URP has a real point source.
                mesh.uv2 = Enumerable.Repeat(new Vector2(8, 1), 4).ToArray();
                Check(result, Draw(camera, target, readback), new Color(0, 0, 0, 1));
                mesh.uv2 = Enumerable.Repeat(new Vector2(4, 1), 4).ToArray();
                point.enabled = false;
                ClassicPointLight green = Source(20, 180, 50);
                redTable.RebuildDynamic(new[] { green }); buffer.Set(redTable); buffer.Flush();
                NVector3 combined = red.ContributionAt(new NVector3(corners[0].x, corners[0].y, corners[0].z), NVector3.UnitY)
                    + green.ContributionAt(new NVector3(corners[0].x, corners[0].y, corners[0].z), NVector3.UnitY);
                Check(result, Draw(camera, target, readback), new Color(Pack(combined.X), Pack(combined.Y), Pack(combined.Z), 1));
                redTable.RebuildDynamic(Array.Empty<ClassicPointLight>()); buffer.Set(redTable); buffer.Flush();
                mesh.uv2 = Enumerable.Repeat(new Vector2(0, 1), 4).ToArray();
                Check(result, Draw(camera, target, readback), Expected(blue, corners[0]));
                var fullTable = new ClassicLightTable(new TilePos(2, 2));
                // Only the last slot contributes, exposing truncation or a highest-tile offset error.
                fullTable.RebuildDynamic(Enumerable.Repeat(Source(0, 0, 0), 23).Append(red).ToArray());
                buffer.Set(fullTable); buffer.Flush();
                mesh.uv2 = Enumerable.Repeat(new Vector2(8, 1), 4).ToArray();
                Check(result, Draw(camera, target, readback), Expected(red, corners[0]));
                // No anchor: select tile(1,1) from object origin even though its vertices are in tile(2,1).
                mesh.uv2 = new Vector2[4];
                surface.transform.position = new Vector3(1.5f, 0, -1.5f);
                mesh.vertices = corners.Select(point => point - surface.transform.position).ToArray();
                mesh.RecalculateBounds();
                Check(result, Draw(camera, target, readback), Expected(red, corners[0]));
                // Moving the same world geometry's object origin changes table selection to tile(0,0).
                surface.transform.position = new Vector3(.5f, 0, -.5f);
                mesh.vertices = corners.Select(point => point - surface.transform.position).ToArray();
                mesh.RecalculateBounds();
                Check(result, Draw(camera, target, readback), Expected(blue, corners[0]));
                blueTable.RebuildStatic(Array.Empty<ClassicPointLight>()); buffer.Set(blueTable); buffer.Flush();
                Check(result, Draw(camera, target, readback), new Color(0, 0, 0, 1));
                buffer.Set(redTable); buffer.Clear();
                Check(result, Draw(camera, target, readback), new Color(0, 0, 0, 1));
                buffer.Set(redTable); buffer.Flush();
                surface.transform.position = new Vector3(1.5f, 0, -1.5f);
                mesh.vertices = corners.Select(point => point - surface.transform.position).ToArray();
                mesh.RecalculateBounds();
                Check(result, Draw(camera, target, readback), Expected(red, corners[0]));
                buffer.Dispose(); buffer = null;
                Check(result, Draw(camera, target, readback), new Color(0, 0, 0, 1));
                point.enabled = true;
                Color fallback = Draw(camera, target, readback);
                RequireFinite(fallback);
                if (Mathf.Max(fallback.r, fallback.g, fallback.b) < .01f)
                    throw new InvalidOperationException("Disposal did not restore the real URP point fallback; double-count test was not exercised.");
                result.Cases++;
                point.enabled = false;
                VerifySceneLifecycle(result, root.transform, camera, target, readback, redTable, red, corners[0]);
                legacyBuffer.GetType().GetMethod("Flush").Invoke(legacyBuffer, null);
            }
            finally
            {
                buffer?.Dispose();
                legacyBuffer?.Dispose();
                Shader.SetGlobalInt("_DK2TileLightGridCount", previousCount);
                Shader.SetGlobalInt("_DK2TileLightGridWidth", previousWidth);
                Shader.SetGlobalInt("_DK2TorchGridCount", previousTorchCount);
                Shader.SetGlobalInt("_DK2TorchGridWidth", previousTorchWidth);
                if (previousKeyword) Shader.EnableKeyword(ClassicTileLightBuffer.Keyword);
                else Shader.DisableKeyword(ClassicTileLightBuffer.Keyword);
                Shader.SetGlobalFloat("_DK2InitialAmbientPreview", previousPreview);
                Shader.SetGlobalVector("_DK2AmbientColour", previousAmbient);
                RenderTexture.active = previousTarget;
                ShaderUtil.allowAsyncCompilation = previousAsync;
                if (root != null) Object.DestroyImmediate(root);
                if (mesh != null) Object.DestroyImmediate(mesh);
                if (material != null) Object.DestroyImmediate(material);
                if (unlitControl != null) Object.DestroyImmediate(unlitControl);
                if (readback != null) Object.DestroyImmediate(readback);
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
                for (int i = 0; i < lights.Length; i++) if (lights[i] != null) lights[i].enabled = enabled[i];
            }
        }

        private static void EnsurePipeline()
        {
            if (RenderPipelineManager.currentPipeline != null) return;
            if (GraphicsSettings.currentRenderPipeline == null)
                throw new InvalidOperationException("No render pipeline asset is configured.");
            var bootstrap = new GameObject("Transient tile-light pipeline bootstrap") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                Camera camera = bootstrap.AddComponent<Camera>(); camera.enabled = false;
                if (!RenderPipeline.SupportsRenderRequest(camera, new RenderPipeline.StandardRequest())
                    || RenderPipelineManager.currentPipeline == null)
                    throw new NotSupportedException("Configured pipeline did not initialize a standard render request.");
            }
            finally { Object.DestroyImmediate(bootstrap); }
        }

        private static void RecordPublication(Result result, ClassicTileLightBuffer buffer, int tile)
        {
            result.PublishedWidth = Shader.GetGlobalInt("_DK2TileLightGridWidth");
            result.PublishedTiles = Shader.GetGlobalInt("_DK2TileLightGridCount");
            result.TableKeyword = Shader.IsKeywordEnabled(ClassicTileLightBuffer.Keyword);
            var counts = new uint[result.PublishedTiles];
            var countBuffer = (GraphicsBuffer)typeof(ClassicTileLightBuffer)
                .GetField("_tableCounts", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(buffer);
            countBuffer.GetData(counts);
            result.PublishedSelectedSources = (int)counts[tile];
            var source = new SourceEntry[1];
            var sourceBuffer = (GraphicsBuffer)typeof(ClassicTileLightBuffer)
                .GetField("_sources", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(buffer);
            sourceBuffer.GetData(source, 0, tile * ClassicLightTable.MaximumSources, 1);
            result.FirstSourcePositionRadius = source[0].PositionRadiusSquared;
            result.FirstSourceColourIntensity = source[0].ColourIntensity;
            if (result.PublishedWidth != 3 || result.PublishedTiles != 9 || result.PublishedSelectedSources != 1 || !result.TableKeyword)
                throw new InvalidOperationException("Selected table publication does not match the supplied fixture.");
        }

        private static void VerifySceneLifecycle(Result result, Transform root, Camera camera,
            RenderTexture target, Texture2D readback, ClassicLightTable table, ClassicPointLight light, Vector3 corner)
        {
            DungeonScene scene = null;
            try
            {
                scene = new DungeonScene(root, 3, 3,
                    new ClassicMeshes(null, classicVertexLighting: true, nativeInitialAmbient: true), creatureShadows: false);
                Action[] invalid = {
                    () => scene.SetPointLightTable(null),
                    () => scene.SetPointLightTable(new ClassicLightTable(new TilePos(-1, 0))),
                    () => scene.SetPointLightTable(new ClassicLightTable(new TilePos(3, 0))),
                    () => scene.SetPointLightTable(new ClassicLightTable(new TilePos(0, 3))),
                };
                foreach (Action action in invalid)
                {
                    bool rejected = false;
                    try { action(); } catch (ArgumentException) { rejected = true; }
                    if (!rejected || Shader.IsKeywordEnabled(ClassicTileLightBuffer.Keyword)
                        || Shader.GetGlobalInt("_DK2TileLightGridCount") != 0)
                        throw new InvalidOperationException("Invalid scene table input activated supplied lighting.");
                    result.SceneLifecycleChecks++;
                }
                scene.SetPointLightTable(table); scene.Flush();
                if (!Shader.IsKeywordEnabled(ClassicTileLightBuffer.Keyword)
                    || Shader.GetGlobalInt("_DK2TileLightGridCount") != 9)
                    throw new InvalidOperationException("Scene did not publish supplied lighting.");
                Check(result, Draw(camera, target, readback), Expected(light, corner));
                result.SceneLifecycleChecks++;
                scene.Clear(); scene.Flush();
                if (!Shader.IsKeywordEnabled(ClassicTileLightBuffer.Keyword))
                    throw new InvalidOperationException("Scene Clear changed lighting mode.");
                Check(result, Draw(camera, target, readback), new Color(0, 0, 0, 1));
                result.SceneLifecycleChecks++;
                scene.DisposeLightResources(); scene = null;
                if (Shader.IsKeywordEnabled(ClassicTileLightBuffer.Keyword)
                    || Shader.GetGlobalInt("_DK2TileLightGridCount") != 0)
                    throw new InvalidOperationException("Scene disposal left supplied lighting active.");
                result.SceneLifecycleChecks++;
            }
            finally { scene?.DisposeLightResources(); }
        }

        private static ClassicPointLight Source(int red, int green, int blue) => ClassicPointLight.FromTorch(
            new TorchView(0, default, 10240, 6144, 4096, 0, red, green, blue, 64, 8192, default));

        private static Color Expected(ClassicPointLight source, Vector3 corner)
        {
            NVector3 value = source.ContributionAt(new NVector3(corner.x, corner.y, corner.z), NVector3.UnitY);
            return new Color(Pack(value.X), Pack(value.Y), Pack(value.Z), 1);
        }

        private static float Pack(float value) => Mathf.Min(255, Mathf.Floor(Mathf.Max(0, value) * 255f + .00001f)) / 255f;

        private static Color Draw(Camera camera, RenderTexture target, Texture2D readback)
        {
            RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
            RenderTexture.active = target; readback.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            readback.Apply(); return readback.GetPixel(target.width / 2, target.height / 2);
        }

        private static void Check(Result result, Color actual, Color expected)
        {
            RequireFinite(actual); RequireFinite(expected);
            float error = Mathf.Max(Mathf.Abs(actual.r - expected.r), Mathf.Abs(actual.g - expected.g), Mathf.Abs(actual.b - expected.b));
            result.MaximumError = Mathf.Max(result.MaximumError, error);
            if (error > .012f || Mathf.Abs(actual.a - 1) > .001f)
                throw new InvalidOperationException($"Case {result.Cases}: expected {expected}, got {actual}.");
            result.Cases++;
        }

        private static void RequireFinite(Color value)
        {
            if (float.IsNaN(value.r) || float.IsInfinity(value.r) || float.IsNaN(value.g) || float.IsInfinity(value.g)
                || float.IsNaN(value.b) || float.IsInfinity(value.b) || float.IsNaN(value.a) || float.IsInfinity(value.a))
                throw new InvalidOperationException("Probe colour contains NaN or infinity: " + value);
        }
    }
}
