// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.IO;
using DK2.Art;
using DK2.Assets;
using DK2.Data;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace DK2.Unity.Editor
{
    public static class ClassicSightFogProbe
    {
        [Serializable] private sealed class Result
        {
            public bool Passed;
            public int Cases;
            public string Material, Texture, Error;
            public Color Reference;
            public float MaximumError;
        }

        [MenuItem("DK2/Classic look/Verify Sight fog overlay opacity on GPU")]
        public static void Run()
        {
            EditorApplication.update += RunWhenReady;
            EditorApplication.QueuePlayerLoopUpdate();
        }

        private static void RunWhenReady()
        {
            EditorApplication.update -= RunWhenReady;
            var result = new Result();
            try { Verify(result); result.Passed = true; }
            catch (Exception error) { result.Error = error.ToString(); }
            string output = Environment.GetEnvironmentVariable("DK2_SIGHT_FOG_PROBE_OUTPUT")
                ?? Path.GetFullPath(Path.Combine(Application.dataPath, "../../../build/step5-sight-fog/result.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, JsonUtility.ToJson(result, true));
            Debug.Log($"DK2 Sight fog GPU probe: {(result.Passed ? "PASS" : "FAIL")}; {result.Cases} cases; {output}");
            if (Application.isBatchMode) EditorApplication.Exit(result.Passed ? 0 : 1);
        }

        private static void Verify(Result result)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || Shader.GetGlobalInt("_DK2TorchGridCount") != 0
                || Shader.GetGlobalInt("_DK2TileLightGridCount") != 0)
                throw new InvalidOperationException("Use an idle editor without published classic lighting.");
            string install = Environment.GetEnvironmentVariable("DK2_DIR");
            if (string.IsNullOrEmpty(install)) throw new InvalidOperationException("DK2_DIR is required for original FogOfWar27 art.");
            using var art = InstallArtProvider.Open(install, GameDatabaseLoader.LoadForCampaignLevel(install, "Level1"));
            var classic = new ClassicMeshes(art, classicVertexLighting: true, nativeInitialAmbient: true);
            var owned = new HashSet<Material>();
            var ownedTextures = new HashSet<Texture>();
            GameObject root = null;
            Mesh quad = null, originalMesh = null;
            RenderTexture target = null;
            Texture2D readback = null;
            IDisposable legacyBuffer = null;
            RenderTexture previousTarget = RenderTexture.active;
            bool previousAsync = ShaderUtil.allowAsyncCompilation;
            float previousPreview = Shader.GetGlobalFloat("_DK2InitialAmbientPreview");
            Vector4 previousAmbient = Shader.GetGlobalVector("_DK2AmbientColour");
            bool previousKeyword = Shader.IsKeywordEnabled(ClassicTileLightBuffer.Keyword);
            try
            {
                if (!classic.TryGetMesh(AssetCatalogue.TerrainPiece("Fog Of War", AssetCatalogue.TerrainSide),
                    out originalMesh, out Material[] originals) || originals.Length == 0)
                    throw new InvalidOperationException("Original Fog Of War side-art model/material is unavailable.");
                foreach (Material material in originals)
                {
                    owned.Add(material);
                    if (material.mainTexture != null) ownedTextures.Add(material.mainTexture);
                }
                Material source = originals[0];
                result.Material = source.name; result.Texture = source.mainTexture != null ? source.mainTexture.name : null;
                if (source.mainTexture == null) throw new InvalidOperationException("Original fog texture was not loaded.");
                Color sourceColour = source.GetColor("_BaseColor");
                float sourceBlend = source.GetFloat("_SrcBlend"), destinationBlend = source.GetFloat("_DstBlend");
                float sourceDepthWrite = source.GetFloat("_ZWrite");
                legacyBuffer = (IDisposable)Activator.CreateInstance(typeof(ClassicMeshes).Assembly
                    .GetType("DK2.Unity.ClassicTorchLightBuffer", throwOnError: true), 1, 1);
                Shader.DisableKeyword(ClassicTileLightBuffer.Keyword);
                Shader.SetGlobalFloat("_DK2InitialAmbientPreview", 1);
                Shader.SetGlobalVector("_DK2AmbientColour", new Vector4(.35f, .5f, .65f, 0));
                ShaderUtil.allowAsyncCompilation = false;
                root = new GameObject("Transient original Sight fog material probe") { hideFlags = HideFlags.HideAndDontSave };
                Camera camera = new GameObject("Camera").AddComponent<Camera>(); camera.transform.SetParent(root.transform);
                camera.enabled = false; camera.transform.SetPositionAndRotation(new Vector3(0, 2, 0), Quaternion.Euler(90, 0, 0));
                camera.orthographic = true; camera.orthographicSize = .25f; camera.aspect = 1;
                camera.nearClipPlane = .1f; camera.farClipPlane = 4; camera.cullingMask = 1 << 31;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.allowHDR = true; camera.allowMSAA = false;
                Color background = new Color(.17f, .29f, .41f, 0); camera.backgroundColor = background;
                var surface = new GameObject("Fog overlay control quad") { layer = 31 }; surface.transform.SetParent(root.transform);
                quad = new Mesh { name = "Fog material constant UV control" };
                quad.vertices = new[] { new Vector3(-1, 0, -1), new Vector3(-1, 0, 1), new Vector3(1, 0, 1), new Vector3(1, 0, -1) };
                quad.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
                quad.uv = new[] { Vector2.one * .5f, Vector2.one * .5f, Vector2.one * .5f, Vector2.one * .5f };
                quad.triangles = new[] { 0, 1, 2, 0, 2, 3 }; quad.RecalculateBounds();
                surface.AddComponent<MeshFilter>().sharedMesh = quad;
                MeshRenderer renderer = surface.AddComponent<MeshRenderer>();
                target = new RenderTexture(32, 32, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                target.Create(); readback = new Texture2D(32, 32, TextureFormat.RGBAFloat, false, true);
                if (!RenderPipeline.SupportsRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target }))
                    throw new NotSupportedException("Configured pipeline does not support render requests.");
                Material fullOpacity = classic.SightFog(source, 255); owned.Add(fullOpacity);
                Material reference = new Material(fullOpacity); owned.Add(reference);
                reference.SetFloat("_SrcBlend", (float)BlendMode.One); reference.SetFloat("_DstBlend", (float)BlendMode.Zero);
                renderer.sharedMaterial = reference;
                result.Reference = Draw(camera, target, readback);
                foreach (byte alpha in new byte[] { 63, 254 })
                {
                    Material fog = classic.SightFog(source, alpha); owned.Add(fog);
                    if (classic.SightFog(source, alpha) != fog || fog == source || fog.shader != source.shader
                        || fog.mainTexture != source.mainTexture || fog.GetColor("_EmissionColor") != source.GetColor("_EmissionColor")
                        || fog.IsKeywordEnabled("_EMISSION") != source.IsKeywordEnabled("_EMISSION")
                        || fog.GetFloat("_SrcBlend") != (float)BlendMode.SrcAlpha
                        || fog.GetFloat("_DstBlend") != (float)BlendMode.OneMinusSrcAlpha || fog.GetFloat("_ZWrite") != 0)
                        throw new InvalidOperationException("Sight fog lost original art/material values, caching, or native transparency state.");
                    Color colour = fog.GetColor("_BaseColor");
                    if (colour.r != sourceColour.r || colour.g != sourceColour.g || colour.b != sourceColour.b
                        || Mathf.Abs(colour.a - alpha / 255f) > .00001f)
                        throw new InvalidOperationException("Sight fog changed RGB or did not publish packed opacity.");
                    result.Cases++;
                    Material raw = new Material(fog); owned.Add(raw);
                    raw.SetFloat("_SrcBlend", (float)BlendMode.One); raw.SetFloat("_DstBlend", (float)BlendMode.Zero);
                    renderer.sharedMaterial = raw;
                    Color expected = result.Reference; expected.a *= alpha / 255f;
                    Check(result, Draw(camera, target, readback), expected);
                    renderer.sharedMaterial = fog;
                    float opacity = expected.a;
                    Check(result, Draw(camera, target, readback), expected * opacity + background * (1 - opacity));
                }
                if (source.GetColor("_BaseColor") != sourceColour || source.GetFloat("_SrcBlend") != sourceBlend
                    || source.GetFloat("_DstBlend") != destinationBlend || source.GetFloat("_ZWrite") != sourceDepthWrite)
                    throw new InvalidOperationException("Sight fog mutated the original terrain material.");
                result.Cases++;
            }
            finally
            {
                legacyBuffer?.Dispose();
                Shader.SetGlobalFloat("_DK2InitialAmbientPreview", previousPreview);
                Shader.SetGlobalVector("_DK2AmbientColour", previousAmbient);
                if (previousKeyword) Shader.EnableKeyword(ClassicTileLightBuffer.Keyword);
                RenderTexture.active = previousTarget; ShaderUtil.allowAsyncCompilation = previousAsync;
                if (root != null) Object.DestroyImmediate(root);
                if (quad != null) Object.DestroyImmediate(quad);
                if (originalMesh != null) Object.DestroyImmediate(originalMesh);
                foreach (Material material in owned) if (material != null) Object.DestroyImmediate(material);
                foreach (Texture texture in ownedTextures) Object.DestroyImmediate(texture);
                if (readback != null) Object.DestroyImmediate(readback);
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            }
        }

        private static Color Draw(Camera camera, RenderTexture target, Texture2D readback)
        {
            RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
            RenderTexture.active = target; readback.ReadPixels(new Rect(0, 0, 32, 32), 0, 0); readback.Apply();
            return readback.GetPixel(16, 16);
        }

        private static void Check(Result result, Color actual, Color expected)
        {
            float error = Mathf.Max(Mathf.Abs(actual.r - expected.r), Mathf.Abs(actual.g - expected.g),
                Mathf.Abs(actual.b - expected.b), Mathf.Abs(actual.a - expected.a));
            if (float.IsNaN(error) || float.IsInfinity(error) || error > .003f)
                throw new InvalidOperationException($"Case {result.Cases}: expected {expected}, got {actual}.");
            result.MaximumError = Mathf.Max(result.MaximumError, error); result.Cases++;
        }
    }
}
