// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DK2.Unity.Editor
{
    public static class HoverOutlineFiniteProbe
    {
        public static void Run()
        {
            if (!Application.isBatchMode || !File.Exists(".tag-overlay-probe"))
                throw new InvalidOperationException("Requires an isolated marked batch project.");
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var camera = new GameObject("hover probe camera").AddComponent<Camera>();
                camera.transform.position = new Vector3(0, 0, -10);
                camera.orthographic = true;
                camera.orthographicSize = 1;
                camera.clearFlags = CameraClearFlags.SolidColor;
                var backdrop = new Color(.12f, .23f, .34f, 1);
                camera.backgroundColor = backdrop;
                camera.allowHDR = true;
                var additional = camera.GetUniversalAdditionalCameraData();
                additional.renderPostProcessing = false;
                additional.stopNaN = false;
                var material = new Material(Shader.Find("DK2/Hand Hover Outline"));
                material.SetColor("_Color", new Color(.2f, .3f, .6f, 1));
                var actor = new GameObject("hover ribbon");
                var mesh = new Mesh();
                actor.AddComponent<MeshFilter>().sharedMesh = mesh;
                actor.AddComponent<MeshRenderer>().sharedMaterial = material;
                mesh.vertices = new[] { new Vector3(-.9f,-.9f,0), new Vector3(-.9f,.9f,0),
                    new Vector3(.9f,.9f,0), new Vector3(.9f,-.9f,0) };
                mesh.triangles = new[] { 0,1,2,0,2,3 };
                var readback = new Texture2D(64, 64, TextureFormat.RGBAFloat, false, true);
                int failures = 0;
                int ribbonFailures = 0;
                foreach (int samples in new[] { 1, 4 })
                {
                    var target = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGBHalf,
                        RenderTextureReadWrite.Linear) { antiAliasing = samples };
                    target.Create();
                    camera.targetTexture = target;
                    camera.allowMSAA = samples > 1;
                    foreach (float across in new[] { .5f, .25f, 0f, 1f, -.0001f, 1.0001f })
                    {
                        mesh.uv = new[] { new Vector2(0, across), new Vector2(0, across),
                            new Vector2(0, across), new Vector2(0, across) };
                        RenderPipeline.SubmitRenderRequest(camera,
                            new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                        RenderTexture.active = target;
                        readback.ReadPixels(new Rect(0, 0, 64, 64), 0, 0);
                        readback.Apply();
                        RenderTexture.active = null;
                        float profile = across < 0 || across > 1 ? 0 :
                            Mathf.Pow(1 - Mathf.Abs(across * 2 - 1), 2.15f);
                        Color expected = backdrop + new Color(.2f, .3f, .6f, 0) * (profile * 1.6f);
                        int invalid = 0;
                        for (int y = 8; y < 56; y++)
                            for (int x = 8; x < 56; x++)
                            {
                                Color actual = readback.GetPixel(x, y);
                                if (!Finite(actual.r) || !Finite(actual.g) || !Finite(actual.b)
                                    || Mathf.Abs(actual.r - expected.r) > .006f
                                    || Mathf.Abs(actual.g - expected.g) > .006f
                                    || Mathf.Abs(actual.b - expected.b) > .006f) invalid++;
                            }
                        Debug.Log($"Hover profile samples={samples} across={across:R} invalid={invalid}/2304 centre={readback.GetPixel(32,32)} expected={expected}");
                        if (invalid != 0) failures++;
                    }
                    for (int phase = 0; phase < 16; phase++)
                    {
                        float offset = phase / 512f;
                        Vector3 start = new Vector3(-.8f, -.7f + offset, 0);
                        Vector3 end = new Vector3(.8f, .7f + offset, 0);
                        Vector3 side = new Vector3(-.7f, .8f, 0).normalized * .008f;
                        mesh.vertices = new[] { start - side, start + side, end + side, end - side };
                        mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
                        mesh.RecalculateBounds();
                        RenderPipeline.SubmitRenderRequest(camera,
                            new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                        RenderTexture.active = target;
                        readback.ReadPixels(new Rect(0, 0, 64, 64), 0, 0);
                        readback.Apply();
                        RenderTexture.active = null;
                        int invalid = 0;
                        foreach (Color actual in readback.GetPixels())
                            if (!Finite(actual.r) || !Finite(actual.g) || !Finite(actual.b)
                                || actual.r < backdrop.r - .006f || actual.g < backdrop.g - .006f
                                || actual.b < backdrop.b - .006f) invalid++;
                        if (invalid > 0) ribbonFailures++;
                        Debug.Log($"Hover raster samples={samples} phase={phase} invalid={invalid}/4096");
                    }
                    mesh.vertices = new[] { new Vector3(-.9f,-.9f,0), new Vector3(-.9f,.9f,0),
                        new Vector3(.9f,.9f,0), new Vector3(.9f,-.9f,0) };
                    mesh.RecalculateBounds();
                    camera.targetTexture = null;
                    target.Release();
                    UnityEngine.Object.DestroyImmediate(target);
                }
                if (failures != 0 || ribbonFailures != 0)
                    throw new Exception($"Hover outline finite/additive verification failed: {failures}/12 profile cases; {ribbonFailures}/32 raster cases.");
                Debug.Log("Hover outline verification passed: twelve GPU profile cases and 32 thin-ribbon raster cases, 158720 checked pixels.");
                EditorApplication.Exit(0);
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
