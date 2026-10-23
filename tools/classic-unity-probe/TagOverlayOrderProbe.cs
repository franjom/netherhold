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
                Debug.Log("Tag overlay order verification passed: seven GPU pixel cases.");
                EditorApplication.Exit(0);

                void Verify(string name, Color expected, bool showActor, Material material, float depth)
                {
                    actor.SetActive(showActor); actor.transform.position = new Vector3(0, 0, depth);
                    renderer.sharedMaterial = material;
                    var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
                    RenderPipeline.SubmitRenderRequest(camera, request);
                    RenderTexture.active = target;
                    readback.ReadPixels(new Rect(0, 0, 64, 64), 0, 0); readback.Apply();
                    Color actual = readback.GetPixel(32, 32);
                    if (QualitySettings.activeColorSpace == ColorSpace.Linear) actual = actual.linear;
                    RenderTexture.active = null;
                    if (Mathf.Abs(actual.r - expected.r) > .08f || Mathf.Abs(actual.g - expected.g) > .08f
                        || Mathf.Abs(actual.b - expected.b) > .08f)
                        throw new Exception(name + ": expected " + expected + ", got " + actual);
                    Debug.Log(name + ": " + actual);
                }
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }
    }
}
