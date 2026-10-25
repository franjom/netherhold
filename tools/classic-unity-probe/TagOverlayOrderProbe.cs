// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DK2.Unity.Editor
{
    public static class TagOverlayOrderProbe
    {
        public static void Run()
        {
            if (!Application.isBatchMode || !File.Exists(".tag-overlay-probe"))
                throw new InvalidOperationException("Requires an isolated marked batch project.");
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var camera = new GameObject("probe camera").AddComponent<Camera>();
                camera.transform.position = new Vector3(0, 0, -10);
                camera.orthographic = true; camera.orthographicSize = 2;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                camera.allowHDR = false; camera.allowMSAA = false;
                var target = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGB32);
                target.Create(); camera.targetTexture = target;
                var terrain = new GameObject("tagged terrain").AddComponent<TransparentTerrain>();
                var meshes = new ClassicMeshes(null);
                Material tag = meshes.TagOverlay(); tag.color = Color.blue;
                var positions = new List<Vector3> { new Vector3(-1,-1,1), new Vector3(-1,1,1),
                    new Vector3(1,1,1), new Vector3(1,-1,1) };
                terrain.SetChunk(0, positions, new List<Vector3> { Vector3.back, Vector3.back, Vector3.back, Vector3.back },
                    new List<Vector2> { Vector2.zero, Vector2.up, Vector2.one, Vector2.right },
                    new List<Vector2> { Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero },
                    new Dictionary<Material, List<int>> { [tag] = new List<int> { 0,1,2,0,2,3 } });
                var before = typeof(TransparentTerrain).GetMethod("BeforeCamera", BindingFlags.NonPublic | BindingFlags.Instance);
                before.Invoke(terrain, new object[] { default(ScriptableRenderContext), camera });
                var actor = GameObject.CreatePrimitive(PrimitiveType.Quad);
                actor.name = "creature or flower";
                var renderer = actor.GetComponent<MeshRenderer>();
                var solid = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                solid.SetColor("_BaseColor", Color.red);
                var flower = new Material(Shader.Find("DK2/ClassicEffect")) { renderQueue = 3200 };
                flower.SetColor("_Tint", Color.red);
                flower.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                var faded = new Material(solid) { renderQueue = 3000 };
                faded.SetFloat("_Surface", 1); faded.SetFloat("_ZWrite", 0);
                faded.SetFloat("_SrcBlend", (int)BlendMode.SrcAlpha);
                faded.SetFloat("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                faded.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                var readback = new Texture2D(64, 64, TextureFormat.RGBA32, false);
                Verify("tag visible", Color.blue, false, solid, 0);
                Verify("opaque creature", Color.red, true, solid, 0);
                Verify("transparent creature", Color.red, true, faded, 0);
                Verify("flower", Color.red, true, flower, 0);
                SetTag(meshes.TagOverlay(true));
                Verify("tutorial overlay", new Color(1, 64f / 255f, 65f / 255f), false, solid, 0);
                Verify("creature above tutorial overlay", Color.red, true, faded, 0);
                Verify("flower above tutorial overlay", Color.red, true, flower, 0);
                SetTag(tag);
                faded.SetColor("_BaseColor", new Color(1, 0, 0, .5f));
                Verify("faded creature", new Color(.5f, 0, .5f), true, faded, 0);
                var veil = new Material(faded); veil.SetColor("_BaseColor", new Color(0, 1, 0, .5f));
                var veilPositions = new List<Vector3>();
                foreach (Vector3 position in positions) veilPositions.Add(position + Vector3.back * 1.5f);
                terrain.SetChunk(1, veilPositions, new List<Vector3> { Vector3.back, Vector3.back, Vector3.back, Vector3.back },
                    new List<Vector2> { Vector2.zero, Vector2.up, Vector2.one, Vector2.right },
                    new List<Vector2> { Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero },
                    new Dictionary<Material, List<int>> { [veil] = new List<int> { 0,1,2,0,2,3 } });
                before.Invoke(terrain, new object[] { default(ScriptableRenderContext), camera });
                faded.SetColor("_BaseColor", Color.red);
                Verify("ordinary transparent terrain in front", new Color(.5f, .5f, 0), true, faded, 0);
                renderer.sharedMaterial = solid;
                var blocker = GameObject.CreatePrimitive(PrimitiveType.Quad);
                blocker.transform.position = new Vector3(0, 0, -1);
                var blockerMaterial = new Material(solid); blockerMaterial.SetColor("_BaseColor", Color.green);
                blocker.GetComponent<MeshRenderer>().sharedMaterial = blockerMaterial;
                Verify("flower behind solid wall", Color.green, true, flower, 0);
                blocker.SetActive(false); terrain.Clear();
                var effectMesh = UnityEngine.Object.Instantiate(actor.GetComponent<MeshFilter>().sharedMesh);
                effectMesh.colors32 = new[] { new Color32(0,255,0,255), new Color32(0,255,0,255),
                    new Color32(0,255,0,255), new Color32(0,255,0,255) };
                flower.SetColor("_Tint", Color.white);
                var backdrop = new Material(faded); backdrop.SetColor("_BaseColor", Color.blue);
                SetBackdrop(1);
                terrain.BeginEffectFrame(); terrain.AddEffectMesh(effectMesh, 0, flower, Matrix4x4.identity);
                terrain.EndEffectFrame();
                Verify("effect in front of blended terrain", Color.green, false, solid, 0);
                effectMesh.colors32 = new[] { new Color32(255,0,0,255), new Color32(255,0,0,255),
                    new Color32(255,0,0,255), new Color32(255,0,0,255) };
                terrain.BeginEffectFrame(); terrain.AddEffectMesh(effectMesh, 0, flower, Matrix4x4.identity);
                terrain.AddEffectMesh(effectMesh, 0, flower, Matrix4x4.Translate(new Vector3(2, 0, 0)));
                terrain.EndEffectFrame();
                Verify("mutated effect mesh colours", Color.red, false, solid, 0);
                terrain.BeginEffectFrame(); terrain.AddEffectMesh(effectMesh, 0, flower, Matrix4x4.identity);
                terrain.EndEffectFrame();
                Verify("fewer effect submissions", Color.red, false, solid, 0);
                int effectVertices = 0;
                foreach (MeshRenderer batch in terrain.GetComponentsInChildren<MeshRenderer>())
                    if (batch.sharedMaterial == flower) effectVertices += batch.GetComponent<MeshFilter>().sharedMesh.triangles.Length;
                if (effectVertices != 6) throw new Exception("Removed effect retained geometry.");
                terrain.transform.rotation = Quaternion.Euler(0, 25, 0);
                Verify("world effect under rotated terrain root", Color.red, false, solid, 0);
                foreach (MeshRenderer batch in terrain.GetComponentsInChildren<MeshRenderer>())
                    if (batch.sharedMaterial == flower)
                    {
                        int first = batch.GetComponent<MeshFilter>().sharedMesh.triangles[0];
                        Vector3 point = batch.transform.TransformPoint(batch.additionalVertexStreams.vertices[first]);
                        if ((point - effectMesh.vertices[effectMesh.triangles[0]]).sqrMagnitude > .000001f)
                            throw new Exception("World effect moved with terrain root.");
                    }
                terrain.transform.rotation = Quaternion.identity;
                SetBackdrop(-1);
                Verify("blended terrain in front of effect", Color.blue, false, solid, 0);
                SetBackdrop(1); terrain.BeginEffectFrame();
                terrain.EndEffectFrame();
                Verify("expired effect removed", Color.blue, false, solid, 0);
                backdrop.SetColor("_BaseColor", Color.black);
                terrain.gameObject.SetActive(false);
                var lowAlpha = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                lowAlpha.SetPixel(0, 0, new Color(1, 1, 1, .25f)); lowAlpha.Apply();
                flower.SetTexture("_MainTex", lowAlpha);
                flower.SetColor("_Tint", Color.red);
                flower.SetInt("_SrcBlend", (int)BlendMode.One);
                flower.SetInt("_DstBlend", (int)BlendMode.One);
                Verify("additive sprite ignores texture alpha", Color.red, true, flower, 0);
                var batching = typeof(ShaderUtil).GetMethod("GetSRPBatcherCompatibilityCode",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (batching == null) throw new Exception("Cannot verify SRP batcher shader compatibility.");
                Shader effectShader = Shader.Find("DK2/ClassicEffect");
                int compatibility = (int)batching.Invoke(null, new object[] { effectShader, 0 });
                if (compatibility != 0)
                {
                    var reason = typeof(ShaderUtil).GetMethod("GetSRPBatcherCompatibilityIssueReason",
                        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    object detail = reason == null ? "Unavailable reason" : reason.Invoke(null,
                        new object[] { effectShader, 0, compatibility });
                    throw new Exception("Classic effect shader bypasses the enabled SRP batcher: " + compatibility + " " + detail);
                }
                Debug.Log("Tag overlay order verification passed: seventeen GPU pixel cases.");
                EditorApplication.Exit(0);

                void SetTag(Material material)
                {
                    terrain.SetChunk(0, positions, new List<Vector3> { Vector3.back, Vector3.back, Vector3.back, Vector3.back },
                        new List<Vector2> { Vector2.zero, Vector2.up, Vector2.one, Vector2.right },
                        new List<Vector2> { Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero },
                        new Dictionary<Material, List<int>> { [material] = new List<int> { 0,1,2,0,2,3 } });
                }

                void SetBackdrop(float z)
                {
                    var points = new List<Vector3>();
                    foreach (Vector3 position in positions) points.Add(new Vector3(position.x, position.y, z));
                    terrain.SetChunk(0, points, new List<Vector3> { Vector3.back, Vector3.back, Vector3.back, Vector3.back },
                        new List<Vector2> { Vector2.zero, Vector2.up, Vector2.one, Vector2.right },
                        new List<Vector2> { Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero },
                        new Dictionary<Material, List<int>> { [backdrop] = new List<int> { 0,1,2,0,2,3 } });
                }

                void Verify(string name, Color expected, bool showActor, Material material, float depth)
                {
                    actor.SetActive(showActor); actor.transform.position = new Vector3(0, 0, depth);
                    renderer.sharedMaterial = material;
                    before.Invoke(terrain, new object[] { default(ScriptableRenderContext), camera });
                    var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
                    RenderPipeline.SubmitRenderRequest(camera, request);
                    RenderTexture.active = target;
                    readback.ReadPixels(new Rect(0, 0, 64, 64), 0, 0); readback.Apply();
                    Color actual = readback.GetPixel(32, 32);
                    if (QualitySettings.activeColorSpace == ColorSpace.Linear) actual = actual.linear;
                    RenderTexture.active = null;
                    if (Mathf.Abs(actual.r - expected.r) > .08f || Mathf.Abs(actual.g - expected.g) > .08f
                        || Mathf.Abs(actual.b - expected.b) > .08f)
                    {
                        foreach (MeshRenderer batch in terrain.GetComponentsInChildren<MeshRenderer>())
                        {
                            Mesh drawn = batch.GetComponent<MeshFilter>().sharedMesh;
                            Debug.Log("batch " + batch.sharedMaterial.name + " order " + batch.sortingOrder
                                + " vertices " + drawn.vertexCount + " colour " + drawn.colors32[0]
                                + " point " + drawn.vertices[0]);
                        }
                        throw new Exception(name + ": expected " + expected + ", got " + actual);
                    }
                    Debug.Log(name + ": " + actual);
                }
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }
    }
}
