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
        private static readonly ProfilerMarker OrderMarker = new ProfilerMarker("DK2.TransparentTerrain.DepthOrder");
        private static readonly ProfilerMarker UploadMarker = new ProfilerMarker("DK2.TransparentTerrain.UploadRuns");

        private sealed class Source
        {
            public Vector3[] Positions, Normals;
            public Vector2[] Uvs;
            public Vector2[] LightAnchors;
            public Material[] Materials;
            public int[][] Triangles;
        }

        private sealed class Run
        {
            public GameObject Object;
            public Mesh Mesh;
            public MeshRenderer Renderer;
            public bool Used;
            public readonly List<RenderTriangle> Triangles = new List<RenderTriangle>();
            // Triangles tracks drawn order; offsets tracks the retained vertex layout, which
            // stays unchanged when only indices are reordered or a subset is drawn.
            public readonly Dictionary<RenderTriangle, int> VertexOffsets = new Dictionary<RenderTriangle, int>();
        }

        private readonly SortedDictionary<int, Source> _sources = new SortedDictionary<int, Source>();
        private readonly List<Run> _runs = new List<Run>();
        private readonly List<Run> _selectedRuns = new List<Run>();
        private readonly Dictionary<int, Run> _runMatches = new Dictionary<int, Run>();
        private readonly List<Vector3> _positions = new List<Vector3>();
        private readonly List<Vector3> _normals = new List<Vector3>();
        private readonly List<Vector2> _uvs = new List<Vector2>();
        private readonly List<Vector2> _lightAnchors = new List<Vector2>();
        private readonly List<Material> _materials = new List<Material>();
        private readonly List<RenderTriangle> _triangles = new List<RenderTriangle>();
        private readonly List<NumericsVector3> _worldPositions = new List<NumericsVector3>();
        private readonly List<Vector3> _runPositions = new List<Vector3>();
        private readonly List<Vector3> _runNormals = new List<Vector3>();
        private readonly List<Vector2> _runUvs = new List<Vector2>();
        private readonly List<Vector2> _runLightAnchors = new List<Vector2>();
        private readonly List<Vector4> _runStaticLightInputs = new List<Vector4>();
        private readonly List<int> _runIndices = new List<int>();
        private readonly TriangleDepthOrderBuffer _sort = new TriangleDepthOrderBuffer();
        private bool _dirty = true;
        private Vector3 _lastForward;
        private Matrix4x4 _lastMatrix;

        private void OnEnable() => RenderPipelineManager.beginCameraRendering += BeforeCamera;
        private void OnDisable() => RenderPipelineManager.beginCameraRendering -= BeforeCamera;

        public void SetChunk(int chunk, List<Vector3> positions, List<Vector3> normals,
            List<Vector2> uvs, List<Vector2> lightAnchors, Dictionary<Material, List<int>> byMaterial)
        {
            using ProfilerMarker.AutoScope scope = SetChunkMarker.Auto();
            var materials = new List<Material>();
            var triangles = new List<int[]>();

            // Only the vertices the blended triangles use, renumbered in first-use order: a chunk's
            // opaque bulk would otherwise be copied here and transformed on every camera turn. The
            // depth sort breaks ties by triangle order, not vertex number, so drawing is unchanged.
            _remap.Clear();
            _keptPositions.Clear(); _keptNormals.Clear(); _keptUvs.Clear();
            _keptLightAnchors.Clear();
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
                            _keptLightAnchors.Add(lightAnchors[vertex]);
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
                LightAnchors = _keptLightAnchors.ToArray(),
                Materials = materials.ToArray(), Triangles = triangles.ToArray(),
            };
            _dirty = true;
        }

        private readonly Dictionary<int, int> _remap = new Dictionary<int, int>();
        private readonly List<Vector3> _keptPositions = new List<Vector3>();
        private readonly List<Vector3> _keptNormals = new List<Vector3>();
        private readonly List<Vector2> _keptUvs = new List<Vector2>();
        private readonly List<Vector2> _keptLightAnchors = new List<Vector2>();

        public void Clear()
        {
            _sources.Clear();
            _dirty = true;
            foreach (Run run in _runs) run.Object.SetActive(false);
        }

        private void Collect()
        {
            _positions.Clear(); _normals.Clear(); _uvs.Clear(); _materials.Clear(); _triangles.Clear();
            _lightAnchors.Clear();
            var slots = new Dictionary<Material, int>();
            foreach (Source source in _sources.Values)
            {
                int start = _positions.Count;
                _positions.AddRange(source.Positions);
                _normals.AddRange(source.Normals);
                _uvs.AddRange(source.Uvs);
                _lightAnchors.AddRange(source.LightAnchors);
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
            bool geometryChanged = _dirty;
            if (geometryChanged) Collect();
            if (geometryChanged || matrix != _lastMatrix)
            {
                _worldPositions.Clear();
                foreach (Vector3 local in _positions)
                {
                    Vector3 world = matrix.MultiplyPoint3x4(local);
                    _worldPositions.Add(new NumericsVector3(world.x, world.y, world.z));
                }
            }
            IReadOnlyList<TriangleDepthOrderBuffer.Run> ordered;
            using (OrderMarker.Auto())
                ordered = _sort.Sort(_worldPositions, _triangles,
                    new NumericsVector3(forward.x, forward.y, forward.z));
            using ProfilerMarker.AutoScope uploadScope = UploadMarker.Auto();
            if (ordered.Count > short.MaxValue)
                throw new InvalidOperationException("Transparent terrain exceeds Unity's 32767 ordered-run limit.");
            SelectRuns(ordered, geometryChanged);
            for (int i = 0; i < ordered.Count; i++)
            {
                Run run = _selectedRuns[i];
                if (!run.Object.activeSelf) run.Object.SetActive(true);
                Material material = _materials[ordered[i].MaterialIndex];
                if (run.Renderer.sharedMaterial != material) run.Renderer.sharedMaterial = material;
                if (run.Renderer.sortingOrder != i + 1) run.Renderer.sortingOrder = i + 1;
                var triangles = ordered[i].Triangles;
                if (!geometryChanged && SameTriangles(run.Triangles, triangles)) continue;
                if (!geometryChanged && ReorderIndices(run, triangles)) continue;
                run.Triangles.Clear();
                run.VertexOffsets.Clear();
                _runPositions.Clear(); _runNormals.Clear(); _runUvs.Clear(); _runIndices.Clear();
                _runLightAnchors.Clear();
                _runStaticLightInputs.Clear();
                for (int t = 0; t < triangles.Count; t++)
                {
                    RenderTriangle triangle = triangles[t];
                    run.VertexOffsets[triangle] = _runPositions.Count;
                    run.Triangles.Add(triangle);
                    AddVertex(triangle.A); AddVertex(triangle.B); AddVertex(triangle.C);
                }
                run.Mesh.Clear();
                run.Mesh.indexFormat = _runPositions.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                run.Mesh.SetVertices(_runPositions);
                run.Mesh.SetNormals(_runNormals);
                run.Mesh.SetUVs(0, _runUvs);
                run.Mesh.SetUVs(1, _runLightAnchors);
                run.Mesh.SetUVs(2, _runStaticLightInputs);
                run.Mesh.SetUVs(3, _runStaticLightInputs);
                run.Mesh.SetTriangles(_runIndices, 0, calculateBounds: true);
            }
            foreach (Run run in _runs)
                if (!run.Used && run.Object.activeSelf) run.Object.SetActive(false);
            _dirty = false;
            _lastForward = forward;
            _lastMatrix = matrix;
        }

        private static bool SameTriangles(List<RenderTriangle> previous, IReadOnlyList<RenderTriangle> next)
        {
            if (previous.Count != next.Count) return false;
            for (int i = 0; i < previous.Count; i++)
                if (previous[i] != next[i]) return false;
            return true;
        }

        private void SelectRuns(IReadOnlyList<TriangleDepthOrderBuffer.Run> ordered, bool geometryChanged)
        {
            _runMatches.Clear();
            foreach (Run run in _runs)
            {
                run.Used = false;
                if (geometryChanged)
                {
                    run.Triangles.Clear();
                    run.VertexOffsets.Clear();
                }
                else _runMatches[TriangleHash(run.Triangles)] = run;
            }
            _selectedRuns.Clear();
            for (int i = 0; i < ordered.Count; i++)
            {
                Run match = null;
                var triangles = ordered[i].Triangles;
                if (!geometryChanged && _runMatches.TryGetValue(TriangleHash(triangles), out Run candidate)
                    && !candidate.Used && SameTriangles(candidate.Triangles, triangles))
                {
                    match = candidate;
                    match.Used = true;
                }
                _selectedRuns.Add(match);
            }
            // Reserve all exact matches before repurposing a mesh another run still needs.
            int available = 0;
            for (int i = 0; i < _selectedRuns.Count; i++)
            {
                if (_selectedRuns[i] != null) continue;
                while (available < _runs.Count && _runs[available].Used) available++;
                if (available == _runs.Count) _runs.Add(CreateRun(available));
                Run run = _runs[available++];
                run.Used = true;
                _selectedRuns[i] = run;
            }
        }

        private static int TriangleHash(IReadOnlyList<RenderTriangle> triangles)
        {
            unchecked
            {
                int hash = triangles.Count;
                for (int i = 0; i < triangles.Count; i++)
                {
                    RenderTriangle t = triangles[i];
                    hash = ((hash * 31 + t.A) * 31 + t.B) * 31 + t.C;
                    hash = hash * 31 + t.MaterialIndex;
                }
                return hash;
            }
        }

        private bool ReorderIndices(Run run, IReadOnlyList<RenderTriangle> triangles)
        {
            _runIndices.Clear();
            for (int i = 0; i < triangles.Count; i++)
            {
                if (!run.VertexOffsets.TryGetValue(triangles[i], out int vertex)) return false;
                _runIndices.Add(vertex); _runIndices.Add(vertex + 1); _runIndices.Add(vertex + 2);
            }
            run.Mesh.SetTriangles(_runIndices, 0, calculateBounds: false);
            run.Triangles.Clear();
            for (int i = 0; i < triangles.Count; i++) run.Triangles.Add(triangles[i]);
            return true;
        }

        private void AddVertex(int vertex)
        {
            _runIndices.Add(_runPositions.Count);
            _runPositions.Add(_positions[vertex]);
            _runNormals.Add(_normals[vertex]);
            _runUvs.Add(_uvs[vertex]);
            _runLightAnchors.Add(_lightAnchors[vertex]);
            _runStaticLightInputs.Add(Vector4.zero);
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
