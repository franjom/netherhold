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
                        camera.transform.position = new Vector3(1, .5f, 1) - camera.transform.forward * 30;
                        before.Invoke(terrain, new object[] { default(ScriptableRenderContext), camera });
                        Vector3 forward = camera.transform.forward;
                        var expected = TriangleDepthOrder.Sort(world, sourceTriangles, new NVector3(forward.x, forward.y, forward.z));
                        var renderers = new List<MeshRenderer>(root.GetComponentsInChildren<MeshRenderer>());
                        renderers.Sort((a, b) => a.sortingOrder.CompareTo(b.sortingOrder));
                        if (revision == 0 && step == 0)
                        {
                            Mesh shared = renderers[0].additionalVertexStreams;
                            if (shared == null || renderers.Exists(r => r.additionalVertexStreams != shared))
                                throw new Exception("Material runs must share one vertex stream instead of uploading their own copies.");
                            if (renderers.Exists(r => r.GetComponent<MeshFilter>().sharedMesh.vertexBufferCount != 0))
                                throw new Exception("Index runs must not allocate duplicate vertex buffers.");
                        }
                        if (renderers.Count != expected.Count) throw new Exception("Run count mismatch.");
                        for (int run = 0; run < expected.Count; run++)
                        {
                            MeshRenderer renderer = renderers[run];
                            if (renderer.sortingOrder != run + 1) throw new Exception("Ordinary terrain ordering changed.");
                            if (renderer.sharedMaterial != materials[expected[run].MaterialIndex]) throw new Exception("Material mismatch.");
                            Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                            Mesh stream = renderer.additionalVertexStreams;
                            Vector3[] vertices = stream.vertices, meshNormals = stream.normals;
                            Vector2[] meshUvs = stream.uv, meshAnchors = stream.uv2;
                            int[] indices = mesh.triangles;
                            var light = new List<Vector4>(); stream.GetUVs(2, light);
                            var light2 = new List<Vector4>(); stream.GetUVs(3, light2);
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
                VerifyLargeGlobalIndex(terrain, camera, materials, before);
                VerifyCameraCulling(terrain, camera, materials[0], before);
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
            camera.transform.position = new Vector3(1, 0, 1.5f) - camera.transform.forward * 30;
            before.Invoke(terrain, new object[] { default(ScriptableRenderContext), camera });
            MeshRenderer renderer = terrain.GetComponentInChildren<MeshRenderer>();
            Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
            Vector3[] retained = renderer.additionalVertexStreams.vertices;
            int[] original = mesh.triangles;
            camera.transform.rotation = Quaternion.Euler(0, 180, 0);
            camera.transform.position = new Vector3(1, 0, 1.5f) - camera.transform.forward * 30;
            before.Invoke(terrain, new object[] { default(ScriptableRenderContext), camera });
            AssertRetained(renderer.additionalVertexStreams, retained);
            if (mesh.triangles[0] == original[0]) throw new Exception("Expected an index-only reversal.");
            camera.transform.rotation = Quaternion.identity;
            camera.transform.position = new Vector3(1, 0, 1.5f) - camera.transform.forward * 30;
            before.Invoke(terrain, new object[] { default(ScriptableRenderContext), camera });
            AssertRetained(renderer.additionalVertexStreams, retained);
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
            camera.transform.position = new Vector3(53.5f, .1f, 51) - camera.transform.forward * 300;
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
            camera.transform.position = new Vector3(53.5f, .1f, 51) - camera.transform.forward * 300;
            before.Invoke(terrain, new object[] { default(ScriptableRenderContext), camera });
            if (terrain.GetComponentsInChildren<MeshRenderer>().Length != 2) throw new Exception("Expected two replacement runs.");
            camera.transform.rotation = Quaternion.Euler(0, 90, 0);
            camera.transform.position = new Vector3(53.5f, .1f, 51) - camera.transform.forward * 300;
            before.Invoke(terrain, new object[] { default(ScriptableRenderContext), camera });
            var renderers = terrain.GetComponentsInChildren<MeshRenderer>();
            if (renderers.Length != 8) throw new Exception("Expected eight restored runs.");
            foreach (MeshRenderer renderer in renderers)
            {
                Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                Mesh stream = renderer.additionalVertexStreams;
                Vector3[] vertices = stream.vertices; Vector2[] meshUv = stream.uv, meshAnchors = stream.uv2;
                foreach (int vertex in mesh.triangles)
                    if (vertices[vertex].x < 100 || meshUv[vertex] != Vector2.one || meshAnchors[vertex] != new Vector2(3, 4)
                        || !mesh.bounds.Contains(vertices[vertex]))
                        throw new Exception("Inactive pool resurrected stale geometry.");
            }
        }

        private static void VerifyLargeGlobalIndex(TransparentTerrain terrain, Camera camera, Material[] materials, MethodInfo before)
        {
            terrain.Clear();
            var positions = new List<Vector3>(); var normals = new List<Vector3>();
            var uv = new List<Vector2>(); var anchors = new List<Vector2>();
            var first = new List<int>(); var last = new List<int>();
            for (int i = 0; i < 65541; i++)
            {
                positions.Add(new Vector3(i % 3 * .1f, i % 7 * .1f, i % 13 * .1f)); normals.Add(Vector3.up);
                uv.Add(Vector2.zero); anchors.Add(Vector2.zero);
                (i < 65538 ? first : last).Add(i);
            }
            terrain.SetChunk(0, positions, normals, uv, anchors,
                new Dictionary<Material, List<int>> { [materials[0]] = first, [materials[1]] = last });
            camera.transform.rotation = Quaternion.identity;
            camera.transform.position = new Vector3(0, 0, -10);
            before.Invoke(terrain, new object[] { default(ScriptableRenderContext), camera });
            foreach (MeshRenderer renderer in terrain.GetComponentsInChildren<MeshRenderer>())
            {
                Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                if (mesh.vertexCount != renderer.additionalVertexStreams.vertexCount || mesh.indexFormat != IndexFormat.UInt32)
                    throw new Exception("Global vertex capacity was truncated.");
                if (renderer.sharedMaterial == materials[1] && (mesh.triangles.Length != 3 || mesh.triangles[0] <= 65535))
                    throw new Exception("A small run truncated its large global vertex index.");
            }
        }

        private static void VerifyCameraCulling(TransparentTerrain terrain, Camera camera, Material material, MethodInfo before)
        {
            terrain.Clear();
            camera.orthographic = true; camera.orthographicSize = 2; camera.aspect = 1;
            camera.transform.SetPositionAndRotation(new Vector3(0, 0, -10), Quaternion.identity);
            var positions = new List<Vector3>
            {
                new Vector3(-.5f,-.5f,0), new Vector3(.5f,-.5f,0), new Vector3(0,.5f,0),
                new Vector3(100,0,0), new Vector3(101,0,0), new Vector3(100,1,0),
                new Vector3(1.5f,-.5f,0), new Vector3(2.5f,-.5f,0), new Vector3(2.5f,.5f,0),
            };
            var normals = new List<Vector3>(); var uv = new List<Vector2>(); var anchors = new List<Vector2>();
            for (int i = 0; i < positions.Count; i++) { normals.Add(Vector3.up); uv.Add(Vector2.zero); anchors.Add(Vector2.zero); }
            terrain.SetChunk(0, positions, normals, uv, anchors,
                new Dictionary<Material, List<int>> { [material] = new List<int> { 0,1,2,3,4,5,6,7,8 } });
            before.Invoke(terrain, new object[] { default(ScriptableRenderContext), camera });
            Check(6, "Offscreen triangles were retained or a boundary-crossing triangle was lost.");
            camera.transform.position = new Vector3(100, 0, -10);
            before.Invoke(terrain, new object[] { default(ScriptableRenderContext), camera });
            Check(3, "Camera translation did not update culling.");
            camera.farClipPlane = 5;
            before.Invoke(terrain, new object[] { default(ScriptableRenderContext), camera });
            Check(0, "Projection-only change did not update culling.");
            camera.farClipPlane = 1000; camera.orthographic = false;

            void Check(int expected, string message)
            {
                int actual = 0;
                foreach (MeshFilter filter in terrain.GetComponentsInChildren<MeshFilter>()) actual += filter.sharedMesh.triangles.Length;
                if (actual != expected) throw new Exception(message);
            }
        }
    }
}
