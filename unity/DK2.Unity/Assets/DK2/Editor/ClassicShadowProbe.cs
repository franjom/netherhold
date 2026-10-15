// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using DK2.Core;
using DK2.Presentation;
using Object = UnityEngine.Object;

namespace DK2.Unity.Editor
{
    public static class ClassicShadowProbe
    {
        [Serializable] private sealed class Result
        {
            public bool Passed;
            public int ShadowPixels, ChangedAnimationPixels, MultiSourcePixels;
            public float MaximumDarkening;
            public Vector3 ShadowCentroid;
            public string Error;
        }
        [MenuItem("DK2/Classic look/Verify creature silhouettes on GPU")]
        public static void Run()
        {
            var result = new Result();
            string output = Environment.GetEnvironmentVariable("DK2_SHADOW_PROBE_OUTPUT")
                ?? Path.GetFullPath(Path.Combine(Application.dataPath, "../../../build/step5-shadows"));
            Directory.CreateDirectory(output);
            try { Verify(result, output); result.Passed = true; }
            catch (Exception error) { result.Error = error.ToString(); }
            File.WriteAllText(Path.Combine(output, "result.json"), JsonUtility.ToJson(result, true));
            Debug.Log($"DK2 shadow probe: {(result.Passed ? "PASS" : "FAIL")}; {output}");
            if (Application.isBatchMode) EditorApplication.Exit(result.Passed ? 0 : 1);
        }
        private static void Verify(Result result, string output)
        {
            foreach (string name in new[] { "ClassicShadowMask", "ClassicShadowReduce", "ClassicShadowReceiver" })
                if (Resources.Load<Shader>("Shaders/" + name) == null)
                    throw new InvalidOperationException("Missing shadow shader: " + name);
            GameObject root = new GameObject("Transient shadow probe") { hideFlags = HideFlags.HideAndDontSave };
            ClassicCreatureShadows shadows = null;
            Mesh actorMesh = null;
            Material white = null;
            RenderTexture target = null;
            Texture2D readback = null;
            RenderTexture previous = RenderTexture.active;
            bool asyncCompilation = ShaderUtil.allowAsyncCompilation;
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                Camera camera = new GameObject("Camera").AddComponent<Camera>();
                camera.transform.SetParent(root.transform);
                camera.enabled = false;
                camera.transform.SetPositionAndRotation(new Vector3(.5f, 3, -.5f), Quaternion.Euler(90, 0, 0));
                camera.orthographic = true; camera.orthographicSize = .8f; camera.aspect = 1;
                camera.nearClipPlane = .1f; camera.farClipPlane = 5;
                camera.cullingMask = 1 << 31; camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.white; camera.allowHDR = false; camera.allowMSAA = false;
                white = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                white.SetColor("_BaseColor", Color.white);
                GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
                floor.transform.SetParent(root.transform); floor.layer = 31;
                floor.transform.position = new Vector3(.5f, 0, -.5f);
                floor.GetComponent<MeshRenderer>().sharedMaterial = white;
                GameObject actor = new GameObject("Animated triangle") { layer = 31 };
                actor.transform.SetParent(root.transform);
                actor.transform.position = new Vector3(.5f, 0, -.5f);
                actorMesh = new Mesh();
                actorMesh.vertices = new[] { new Vector3(-.15f, 0, -.2f), new Vector3(.15f, 0, -.2f), new Vector3(0, .6f, .2f) };
                actorMesh.triangles = new[] { 0, 2, 1 }; actorMesh.RecalculateBounds();
                actor.AddComponent<MeshFilter>().sharedMesh = actorMesh;
                MeshRenderer renderer = actor.AddComponent<MeshRenderer>(); renderer.sharedMaterial = white;
                target = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                target.Create(); readback = new Texture2D(256, 256, TextureFormat.RGBA32, false, true);
                var request = new RenderPipeline.StandardRequest { destination = target };
                if (!RenderPipeline.SupportsRenderRequest(camera, request)) throw new NotSupportedException("URP request unavailable.");
                Color32[] baseline = Draw(camera, target, readback);
                shadows = new ClassicCreatureShadows(root.transform);
                shadows.Register(actor); shadows.SetGroundHeight(actor, .01f);
                shadows.SetTorch(new TorchView(0, new TilePos(0, 0), -2048, 2048, 4096, 0, 255, 180, 80, 128, 8192, default));
                shadows.Render(camera); renderer.enabled = false;
                Color32[] first = Draw(camera, target, readback);
                File.WriteAllBytes(Path.Combine(output, "silhouette.png"), readback.EncodeToPNG());
                for (int i = 0; i < first.Length; i++)
                {
                    float difference = (baseline[i].r - first[i].r) / 255f;
                    if (difference > .02f) result.ShadowPixels++;
                    result.MaximumDarkening = Mathf.Max(result.MaximumDarkening, difference);
                    if (first[i].a != baseline[i].a) throw new InvalidOperationException("Shadow changed framebuffer alpha.");
                }
                if (result.ShadowPixels < 16 || result.MaximumDarkening < .2f || result.MaximumDarkening > .72f)
                    throw new InvalidOperationException($"Invalid silhouette: {result.ShadowPixels} pixels, darkening {result.MaximumDarkening}.");
                double weight = 0, sumX = 0, sumZ = 0;
                for (int i = 0; i < first.Length; i++)
                {
                    int darkening = baseline[i].r - first[i].r;
                    if (darkening <= 0) continue;
                    Vector3 point = camera.ViewportToWorldPoint(new Vector3(
                        ((i % target.width) + .5f) / target.width, ((i / target.width) + .5f) / target.height, 3f));
                    weight += darkening; sumX += point.x * darkening; sumZ += point.z * darkening;
                }
                result.ShadowCentroid = new Vector3((float)(sumX / weight), .01f, (float)(sumZ / weight));
                float angle = Mathf.PI * .25f * 1.5f / 2.5f;
                Vector3 expectedCentroid = new Vector3(.5f + (.2f - .01f) * Mathf.Tan(angle), .01f, -.5666667f);
                if (Vector3.Distance(result.ShadowCentroid, expectedCentroid) > .03f)
                    throw new InvalidOperationException($"Silhouette orientation mismatch: {result.ShadowCentroid}, expected {expectedCentroid}.");
                renderer.enabled = true;
                shadows.SetTorch(new TorchView(1, new TilePos(1, 0), 6144, 2048, 4096, 0, 255, 180, 80, 128, 8192, default));
                shadows.Render(camera); renderer.enabled = false;
                Color32[] multiple = Draw(camera, target, readback);
                for (int i = 0; i < multiple.Length; i++) if (baseline[i].r - multiple[i].r > 5) result.MultiSourcePixels++;
                if (result.MultiSourcePixels <= result.ShadowPixels)
                    throw new InvalidOperationException("Second light did not add a separate projected silhouette.");
                shadows.RemoveTorch(1); renderer.enabled = true; shadows.Render(camera); renderer.enabled = false;
                Color32[] restored = Draw(camera, target, readback);
                for (int i = 0; i < restored.Length; i++)
                    if (Mathf.Abs(restored[i].r - first[i].r) > 2) throw new InvalidOperationException("Atlas growth changed the first silhouette.");
                renderer.enabled = true;
                actorMesh.vertices = new[] { new Vector3(-.3f, 0, .15f), new Vector3(.3f, 0, .15f), new Vector3(0, .7f, -.2f) };
                actorMesh.RecalculateBounds(); shadows.Render(camera); renderer.enabled = false;
                Color32[] animated = Draw(camera, target, readback);
                for (int i = 0; i < first.Length; i++) if (Mathf.Abs(first[i].r - animated[i].r) > 4) result.ChangedAnimationPixels++;
                if (result.ChangedAnimationPixels < 8) throw new InvalidOperationException("Shadow did not follow changed mesh vertices.");
                shadows.Remove(actor); shadows.Render(camera);
                Color32[] removed = Draw(camera, target, readback);
                for (int i = 0; i < removed.Length; i++)
                    if (Mathf.Abs(removed[i].r - baseline[i].r) > 2) throw new InvalidOperationException("Removed caster retained its shadow.");
            }
            finally
            {
                RenderTexture.active = previous;
                shadows?.Dispose();
                Object.DestroyImmediate(root);
                if (actorMesh != null) Object.DestroyImmediate(actorMesh);
                if (white != null) Object.DestroyImmediate(white);
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
                if (readback != null) Object.DestroyImmediate(readback);
                ShaderUtil.allowAsyncCompilation = asyncCompilation;
            }
        }
        private static Color32[] Draw(Camera camera, RenderTexture target, Texture2D readback)
        {
            RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
            RenderTexture.active = target; readback.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            readback.Apply(); return readback.GetPixels32();
        }
    }
}
