// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DK2.Art;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using NVector3 = System.Numerics.Vector3;
using Object = UnityEngine.Object;

namespace DK2.Unity.Editor
{
    public static class TransparentTerrainReuseProbe
    {
        public static void Run()
        {
            if (!Application.isBatchMode || !File.Exists(".classic-window-frame-probe"))
                throw new InvalidOperationException("Requires the isolated marked batch project.");
            GameObject root = null, cameraObject = null;
            var materials = new Material[2];
            int cases = 0;
            try
            {
                root = new GameObject("transparent reuse verification");
                var terrain = root.AddComponent<TransparentTerrain>();
                cameraObject = new GameObject("verification camera");
                var camera = cameraObject.AddComponent<Camera>();
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                for (int i = 0; i < materials.Length; i++) materials[i] = new Material(shader) { renderQueue = 3000 };
                var before = typeof(TransparentTerrain).GetMethod("BeforeCamera", BindingFlags.NonPublic | BindingFlags.Instance);
                for (int revision = 0; revision < 4; revision++)
                {
                    var positions = new List<Vector3>();
                    var normals = new List<Vector3>();
                    var uv = new List<Vector2>();
                    var anchors = new List<Vector2>();
                    var byMaterial = new Dictionary<Material, List<int>>();
                    for (int m = 0; m < 2; m++) byMaterial[materials[m]] = new List<int>();
                    for (int triangle = 0; triangle < 8; triangle++)
                        for (int v = 0; v < 3; v++)
                        {
                            int index = positions.Count;
                            positions.Add(new Vector3(triangle % 3 + v * .1f, revision * .2f, triangle / 3));
                            normals.Add(Vector3.up);
                            uv.Add(new Vector2(index * .01f, revision));
                            anchors.Add(new Vector2(triangle, revision));
                            byMaterial[materials[(triangle + revision) % 2]].Add(index);
                        }
                    terrain.SetChunk(0, positions, normals, uv, anchors, byMaterial);
                    root.transform.rotation = Quaternion.Euler(0, revision * 17, 0);
                    var sourceTriangles = new List<RenderTriangle>();
                    foreach (var entry in byMaterial)
                        for (int i = 0; i < entry.Value.Count; i += 3)
                            sourceTriangles.Add(new RenderTriangle(entry.Value[i], entry.Value[i + 1], entry.Value[i + 2],
                                Array.IndexOf(materials, entry.Key)));
                    var world = new List<NVector3>();
                    foreach (Vector3 position in positions)
                    {
                        Vector3 p = root.transform.TransformPoint(position);
                        world.Add(new NVector3(p.x, p.y, p.z));
                    }
                    for (int step = 0; step < 24; step++)
                    {
                        if (step == 12)
                        {
                            root.transform.Rotate(0, 13, 0);
                            world.Clear();
                            foreach (Vector3 position in positions)
                            {
                                Vector3 p = root.transform.TransformPoint(position);
                                world.Add(new NVector3(p.x, p.y, p.z));
                            }
                        }
                        // Repeated and tiny angle changes exercise unchanged runs as well as reorderings.
                        camera.transform.rotation = Quaternion.Euler(65, step == 23 ? 0 : (step / 2) * 30 + (step % 2) * .01f, 0);
                        before.Invoke(terrain, new object[] { default(ScriptableRenderContext), camera });
                        Vector3 forward = camera.transform.forward;
                        var expected = TriangleDepthOrder.Sort(world, sourceTriangles, new NVector3(forward.x, forward.y, forward.z));
                        var renderers = new List<MeshRenderer>(root.GetComponentsInChildren<MeshRenderer>());
                        renderers.Sort((a, b) => a.sortingOrder.CompareTo(b.sortingOrder));
                        if (renderers.Count != expected.Count) throw new Exception("Run count mismatch.");
                        for (int run = 0; run < expected.Count; run++)
                        {
                            MeshRenderer renderer = renderers[run];
                            if (renderer.sortingOrder != run + 1) throw new Exception("Ordinary terrain ordering changed.");
                            if (renderer.sharedMaterial != materials[expected[run].MaterialIndex]) throw new Exception("Material mismatch.");
                            Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                            Vector3[] vertices = mesh.vertices, meshNormals = mesh.normals;
                            Vector2[] meshUvs = mesh.uv, meshAnchors = mesh.uv2;
                            int[] indices = mesh.triangles;
                            var light = new List<Vector4>(); mesh.GetUVs(2, light);
                            var light2 = new List<Vector4>(); mesh.GetUVs(3, light2);
                            int at = 0;
                            foreach (RenderTriangle triangle in expected[run].Triangles)
                                foreach (int vertex in new[] { triangle.A, triangle.B, triangle.C })
                                {
                                    int drawn = indices[at];
                                    if (vertices[drawn] != positions[vertex] || meshNormals[drawn] != normals[vertex]
                                        || meshUvs[drawn] != uv[vertex] || meshAnchors[drawn] != anchors[vertex]
                                        || light[drawn] != Vector4.zero || light2[drawn] != Vector4.zero)
                                        throw new Exception("Vertex stream mismatch.");
                                    at++;
                                }
                            if (at != indices.Length) throw new Exception("Stale triangles.");
                        }
                        cases++;
                    }
                    terrain.Clear();
                    before.Invoke(terrain, new object[] { default(ScriptableRenderContext), camera });
                    if (root.GetComponentsInChildren<MeshRenderer>().Length != 0) throw new Exception("Clear retained active geometry.");
                }
                VerifyIndexReuse(terrain, camera, materials[0], before);
                VerifyInactivePoolInvalidation(terrain, camera, materials, before);
                Debug.Log("Transparent terrain reuse verification passed: " + cases + " cases.");
                EditorApplication.Exit(0);
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
            finally
            {
                if (root != null) Object.DestroyImmediate(root);
                if (cameraObject != null) Object.DestroyImmediate(cameraObject);
                foreach (Material material in materials) if (material != null) Object.DestroyImmediate(material);
            }
        }

        private static void VerifyIndexReuse(TransparentTerrain terrain, Camera camera, Material material, MethodInfo before)
        {
            var positions = new List<Vector3>(); var normals = new List<Vector3>();
            var uv = new List<Vector2>(); var anchors = new List<Vector2>(); var indices = new List<int>();
            for (int i = 0; i < 12; i++)
            {
                positions.Add(new Vector3(i % 3, 0, i / 3)); normals.Add(Vector3.up);
                uv.Add(Vector2.zero); anchors.Add(Vector2.one); indices.Add(i);
            }
            terrain.transform.rotation = Quaternion.identity;
            terrain.SetChunk(0, positions, normals, uv, anchors, new Dictionary<Material, List<int>> { [material] = indices });
            camera.transform.rotation = Quaternion.identity;
            before.Invoke(terrain, new object[] { default(ScriptableRenderContext), camera });
            Mesh mesh = terrain.GetComponentInChildren<MeshFilter>().sharedMesh;
            Vector3[] retained = mesh.vertices;
            int[] original = mesh.triangles;
            camera.transform.rotation = Quaternion.Euler(0, 180, 0);
            before.Invoke(terrain, new object[] { default(ScriptableRenderContext), camera });
            AssertRetained(mesh, retained);
            if (mesh.triangles[0] == original[0]) throw new Exception("Expected an index-only reversal.");
            var runs = (System.Collections.IList)typeof(TransparentTerrain).GetField("_runs", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(terrain);
            var reorder = typeof(TransparentTerrain).GetMethod("ReorderIndices", BindingFlags.NonPublic | BindingFlags.Instance);
            if (!(bool)reorder.Invoke(terrain, new object[] { runs[0], new[] { new RenderTriangle(0, 1, 2, 0) } }))
                throw new Exception("Expected retained subset.");
            if (mesh.triangles.Length != 3) throw new Exception("Subset retained extra triangles.");
            AssertRetained(mesh, retained);
            camera.transform.rotation = Quaternion.identity;
            before.Invoke(terrain, new object[] { default(ScriptableRenderContext), camera });
            AssertRetained(mesh, retained);
            if (mesh.triangles.Length != original.Length) throw new Exception("Restore lost triangles.");
        }

        private static void AssertRetained(Mesh mesh, Vector3[] expected)
        {
            Vector3[] actual = mesh.vertices;
            if (actual.Length != expected.Length) throw new Exception("Vertex buffer was rebuilt.");
            for (int i = 0; i < actual.Length; i++)
                if (actual[i] != expected[i]) throw new Exception("Expected unchanged vertex layout.");
        }

        private static void VerifyInactivePoolInvalidation(TransparentTerrain terrain, Camera camera, Material[] materials, MethodInfo before)
        {
            var positions = new List<Vector3>(); var normals = new List<Vector3>();
            var uv = new List<Vector2>(); var anchors = new List<Vector2>();
            var byMaterial = new Dictionary<Material, List<int>> { [materials[0]] = new List<int>(), [materials[1]] = new List<int>() };
            for (int t = 0; t < 8; t++)
                for (int v = 0; v < 3; v++)
                {
                    int vertex = positions.Count;
                    positions.Add(new Vector3(t, v * .1f, 0)); normals.Add(Vector3.up);
                    uv.Add(Vector2.zero); anchors.Add(Vector2.zero); byMaterial[materials[t % 2]].Add(vertex);
                }
            terrain.transform.rotation = Quaternion.identity;
            terrain.SetChunk(0, positions, normals, uv, anchors, byMaterial);
            camera.transform.rotation = Quaternion.Euler(0, 90, 0);
            before.Invoke(terrain, new object[] { default(ScriptableRenderContext), camera });
            if (terrain.GetComponentsInChildren<MeshRenderer>().Length != 8) throw new Exception("Expected eight warm runs.");
            for (int i = 0; i < positions.Count; i++)
            {
                int triangle = i / 3;
                positions[i] = new Vector3(100 + triangle, (i % 3) * .1f, triangle % 2 * 100 + triangle / 2);
                uv[i] = Vector2.one; anchors[i] = new Vector2(3, 4);
            }
            terrain.SetChunk(0, positions, normals, uv, anchors, byMaterial);
            camera.transform.rotation = Quaternion.identity;
            before.Invoke(terrain, new object[] { default(ScriptableRenderContext), camera });
            if (terrain.GetComponentsInChildren<MeshRenderer>().Length != 2) throw new Exception("Expected two replacement runs.");
            camera.transform.rotation = Quaternion.Euler(0, 90, 0);
            before.Invoke(terrain, new object[] { default(ScriptableRenderContext), camera });
            var renderers = terrain.GetComponentsInChildren<MeshRenderer>();
            if (renderers.Length != 8) throw new Exception("Expected eight restored runs.");
            foreach (MeshRenderer renderer in renderers)
            {
                Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                Vector3[] vertices = mesh.vertices; Vector2[] meshUv = mesh.uv, meshAnchors = mesh.uv2;
                foreach (int vertex in mesh.triangles)
                    if (vertices[vertex].x < 100 || meshUv[vertex] != Vector2.one || meshAnchors[vertex] != new Vector2(3, 4))
                        throw new Exception("Inactive pool resurrected stale geometry.");
            }
        }
    }
}
