// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using DK2.Art;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using NumericsVector3 = System.Numerics.Vector3;

namespace DK2.Unity
{
    /// <summary>Blended terrain faces share one camera-depth stream across chunks and materials.</summary>
    public sealed class TransparentTerrain : MonoBehaviour
    {
        private static readonly ProfilerMarker SetChunkMarker = new ProfilerMarker("DK2.TransparentTerrain.SetChunk");
        private static readonly ProfilerMarker SortMarker = new ProfilerMarker("DK2.TransparentTerrain.Sort");

        private sealed class Source
        {
            public Vector3[] Positions, Normals;
            public Vector2[] Uvs;
            public Material[] Materials;
            public int[][] Triangles;
        }

        private sealed class Run
        {
            public GameObject Object;
            public Mesh Mesh;
            public MeshRenderer Renderer;
        }

        private readonly SortedDictionary<int, Source> _sources = new SortedDictionary<int, Source>();
        private readonly List<Run> _runs = new List<Run>();
        private readonly List<Vector3> _positions = new List<Vector3>();
        private readonly List<Vector3> _normals = new List<Vector3>();
        private readonly List<Vector2> _uvs = new List<Vector2>();
        private readonly List<Material> _materials = new List<Material>();
        private readonly List<RenderTriangle> _triangles = new List<RenderTriangle>();
        private readonly List<NumericsVector3> _worldPositions = new List<NumericsVector3>();
        private readonly List<Vector3> _runPositions = new List<Vector3>();
        private readonly List<Vector3> _runNormals = new List<Vector3>();
        private readonly List<Vector2> _runUvs = new List<Vector2>();
        private readonly List<int> _runIndices = new List<int>();
        private bool _dirty = true;
        private Vector3 _lastForward;
        private Matrix4x4 _lastMatrix;

        private void OnEnable() => RenderPipelineManager.beginCameraRendering += BeforeCamera;
        private void OnDisable() => RenderPipelineManager.beginCameraRendering -= BeforeCamera;

        public void SetChunk(int chunk, List<Vector3> positions, List<Vector3> normals,
            List<Vector2> uvs, Dictionary<Material, List<int>> byMaterial)
        {
            using ProfilerMarker.AutoScope scope = SetChunkMarker.Auto();
            var materials = new List<Material>();
            var triangles = new List<int[]>();

            // Only the vertices the blended triangles use, renumbered in first-use order: a chunk's
            // opaque bulk would otherwise be copied here and transformed on every camera turn. The
            // depth sort breaks ties by triangle order, not vertex number, so drawing is unchanged.
            _remap.Clear();
            _keptPositions.Clear(); _keptNormals.Clear(); _keptUvs.Clear();
            foreach (KeyValuePair<Material, List<int>> entry in byMaterial)
                if (entry.Value.Count != 0 && entry.Key.renderQueue >= (int)RenderQueue.Transparent)
                {
                    var kept = new int[entry.Value.Count];
                    for (int i = 0; i < kept.Length; i++)
                    {
                        int vertex = entry.Value[i];
                        if (!_remap.TryGetValue(vertex, out int local))
                        {
                            local = _keptPositions.Count;
                            _remap.Add(vertex, local);
                            _keptPositions.Add(positions[vertex]);
                            _keptNormals.Add(normals[vertex]);
                            _keptUvs.Add(uvs[vertex]);
                        }
                        kept[i] = local;
                    }
                    materials.Add(entry.Key);
                    triangles.Add(kept);
                }
            if (triangles.Count == 0)
            {
                // A chunk with no blended faces before or after changes nothing drawn here.
                if (!_sources.Remove(chunk)) return;
            }
            else _sources[chunk] = new Source
            {
                Positions = _keptPositions.ToArray(), Normals = _keptNormals.ToArray(), Uvs = _keptUvs.ToArray(),
                Materials = materials.ToArray(), Triangles = triangles.ToArray(),
            };
            _dirty = true;
        }

        private readonly Dictionary<int, int> _remap = new Dictionary<int, int>();
        private readonly List<Vector3> _keptPositions = new List<Vector3>();
        private readonly List<Vector3> _keptNormals = new List<Vector3>();
        private readonly List<Vector2> _keptUvs = new List<Vector2>();

        public void Clear()
        {
            _sources.Clear();
            _dirty = true;
            foreach (Run run in _runs) run.Object.SetActive(false);
        }

        private void Collect()
        {
            _positions.Clear(); _normals.Clear(); _uvs.Clear(); _materials.Clear(); _triangles.Clear();
            var slots = new Dictionary<Material, int>();
            foreach (Source source in _sources.Values)
            {
                int start = _positions.Count;
                _positions.AddRange(source.Positions);
                _normals.AddRange(source.Normals);
                _uvs.AddRange(source.Uvs);
                for (int material = 0; material < source.Materials.Length; material++)
                {
                    if (!slots.TryGetValue(source.Materials[material], out int slot))
                    {
                        slot = _materials.Count;
                        slots.Add(source.Materials[material], slot);
                        _materials.Add(source.Materials[material]);
                    }
                    int[] indices = source.Triangles[material];
                    for (int i = 0; i < indices.Length; i += 3)
                        _triangles.Add(new RenderTriangle(start + indices[i], start + indices[i + 1],
                            start + indices[i + 2], slot));
                }
            }
        }

        private void BeforeCamera(ScriptableRenderContext context, Camera camera)
        {
            Vector3 forward = camera.transform.forward;
            Matrix4x4 matrix = transform.localToWorldMatrix;
            if (!_dirty && forward == _lastForward && matrix == _lastMatrix) return;
            using ProfilerMarker.AutoScope scope = SortMarker.Auto();
            if (_dirty) Collect();
            _worldPositions.Clear();
            foreach (Vector3 local in _positions)
            {
                Vector3 world = matrix.MultiplyPoint3x4(local);
                _worldPositions.Add(new NumericsVector3(world.x, world.y, world.z));
            }
            var ordered = TriangleDepthOrder.Sort(_worldPositions, _triangles,
                new NumericsVector3(forward.x, forward.y, forward.z));
            if (ordered.Count > short.MaxValue)
                throw new InvalidOperationException("Transparent terrain exceeds Unity's 32767 ordered-run limit.");
            for (int i = 0; i < ordered.Count; i++)
            {
                if (i == _runs.Count) _runs.Add(CreateRun(i));
                Run run = _runs[i];
                run.Object.SetActive(true);
                run.Renderer.sharedMaterial = _materials[ordered[i].MaterialIndex];
                run.Renderer.sortingOrder = i + 1;
                _runPositions.Clear(); _runNormals.Clear(); _runUvs.Clear(); _runIndices.Clear();
                foreach (RenderTriangle triangle in ordered[i].Triangles)
                {
                    AddVertex(triangle.A); AddVertex(triangle.B); AddVertex(triangle.C);
                }
                run.Mesh.Clear();
                run.Mesh.indexFormat = _runPositions.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                run.Mesh.SetVertices(_runPositions);
                run.Mesh.SetNormals(_runNormals);
                run.Mesh.SetUVs(0, _runUvs);
                run.Mesh.SetTriangles(_runIndices, 0, calculateBounds: true);
            }
            for (int i = ordered.Count; i < _runs.Count; i++) _runs[i].Object.SetActive(false);
            _dirty = false;
            _lastForward = forward;
            _lastMatrix = matrix;
        }

        private void AddVertex(int vertex)
        {
            _runIndices.Add(_runPositions.Count);
            _runPositions.Add(_positions[vertex]);
            _runNormals.Add(_normals[vertex]);
            _runUvs.Add(_uvs[vertex]);
        }

        private Run CreateRun(int index)
        {
            var go = new GameObject($"transparent terrain run {index}");
            go.transform.SetParent(transform, worldPositionStays: false);
            go.layer = gameObject.layer;
            var mesh = new Mesh { name = go.name };
            mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            return new Run { Object = go, Mesh = mesh, Renderer = renderer };
        }

        private void OnDestroy()
        {
            RenderPipelineManager.beginCameraRendering -= BeforeCamera;
            foreach (Run run in _runs) Destroy(run.Mesh);
        }
    }
}
