// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Reflection;
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
        private static readonly ProfilerMarker CullMarker = new ProfilerMarker("DK2.TransparentTerrain.Cull");

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
            public long BoundsRevision = -1;
            public int VertexCapacity, SortingOrder = int.MinValue;
            public bool Active = true, SharedBound;
            public Material Material;
            public Bounds Bounds;
            public readonly List<RenderTriangle> Triangles = new List<RenderTriangle>();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SharedVertex
        {
            public Vector3 Position, Normal;
            public Color32 Colour;
            public Vector2 Uv, LightAnchor;
            public Vector4 StaticLight, LightKind;
        }

        private static readonly VertexAttributeDescriptor[] SharedLayout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 2),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord3, VertexAttributeFormat.Float32, 4),
        };
        private static readonly VertexAttributeDescriptor[] IndexMeshLayout = Array.Empty<VertexAttributeDescriptor>();
        private Mesh _sharedVertices;
        private SharedVertex[] _vertexData = Array.Empty<SharedVertex>();
        private long _geometryRevision;
        private RenderPipelineAsset _checkedPipeline;
        private PropertyInfo _dynamicBatching;

        private sealed class EffectMesh
        {
            public Material Material;
            public Matrix4x4 Transform;
            public Vector3[] Positions = Array.Empty<Vector3>(), Normals = Array.Empty<Vector3>();
            public Vector2[] Uvs = Array.Empty<Vector2>();
            public Color32[] Colours = Array.Empty<Color32>();
            public int[] Indices = Array.Empty<int>();
        }

        private readonly List<EffectMesh> _effects = new List<EffectMesh>();
        private int _effectCount;
        private readonly List<Vector3> _effectPositions = new List<Vector3>(), _effectNormals = new List<Vector3>();
        private readonly List<Vector2> _effectUvs = new List<Vector2>();
        private readonly List<Color32> _effectColours = new List<Color32>();
        private readonly List<int> _effectIndices = new List<int>();
        private readonly List<Color32> _colours = new List<Color32>();

        public void BeginEffectFrame() => _effectCount = 0;

        /// <summary>Unlit blended effect geometry, sorted together with the terrain this frame.</summary>
        public void AddEffectMesh(Mesh mesh, int submesh, Material material, Matrix4x4 worldTransform)
        {
            mesh.GetVertices(_effectPositions); mesh.GetNormals(_effectNormals);
            mesh.GetUVs(0, _effectUvs); mesh.GetColors(_effectColours); mesh.GetTriangles(_effectIndices, submesh);
            if (_effectCount == _effects.Count) _effects.Add(new EffectMesh());
            EffectMesh effect = _effects[_effectCount++];
            if (effect.Material == material && effect.Transform.Equals(worldTransform)
                && Same(_effectPositions, effect.Positions) && Same(_effectNormals, effect.Normals)
                && Same(_effectUvs, effect.Uvs) && Same(_effectColours, effect.Colours)
                && Same(_effectIndices, effect.Indices)) return;
            effect.Material = material; effect.Transform = worldTransform;
            Copy(_effectPositions, ref effect.Positions); Copy(_effectNormals, ref effect.Normals);
            Copy(_effectUvs, ref effect.Uvs); Copy(_effectColours, ref effect.Colours);
            Copy(_effectIndices, ref effect.Indices);
            _dirty = true;
        }

        public void EndEffectFrame()
        {
            if (_effectCount == _effects.Count) return;
            _effects.RemoveRange(_effectCount, _effects.Count - _effectCount);
            _dirty = true;
        }

        private static bool Same<T>(List<T> values, T[] previous)
        {
            if (values.Count != previous.Length) return false;
            for (int i = 0; i < previous.Length; i++)
                if (!EqualityComparer<T>.Default.Equals(values[i], previous[i])) return false;
            return true;
        }

        private static void Copy<T>(List<T> values, ref T[] target)
        {
            if (target.Length != values.Count) target = new T[values.Count];
            values.CopyTo(target);
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
        private readonly List<RenderTriangle> _visibleTriangles = new List<RenderTriangle>();
        private readonly Plane[] _frustum = new Plane[6];
        private readonly List<NumericsVector3> _worldPositions = new List<NumericsVector3>();
        private readonly List<int> _runIndices = new List<int>();
        private readonly TriangleDepthOrderBuffer _sort = new TriangleDepthOrderBuffer();
        private bool _dirty = true;
        private Vector3 _lastForward;
        private Matrix4x4 _lastMatrix;
        private Matrix4x4 _lastCullingMatrix;

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
            _effects.Clear(); _effectCount = 0;
            _dirty = true;
            // A teardown on scene unload can find a run's object already gone with its parent.
            foreach (Run run in _runs)
            {
                if (run.Object != null && run.Active) run.Object.SetActive(false);
                run.Active = false;
            }
        }

        private void Collect()
        {
            _positions.Clear(); _normals.Clear(); _uvs.Clear(); _materials.Clear(); _triangles.Clear();
            _lightAnchors.Clear(); _colours.Clear();
            var slots = new Dictionary<Material, int>();
            foreach (Source source in _sources.Values)
            {
                int start = _positions.Count;
                _positions.AddRange(source.Positions);
                for (int i = 0; i < source.Positions.Length; i++) _colours.Add(new Color32(255, 255, 255, 255));
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
            foreach (EffectMesh effect in _effects)
            {
                int start = _positions.Count;
                Matrix4x4 local = transform.worldToLocalMatrix * effect.Transform;
                Matrix4x4 normals = local.inverse.transpose;
                for (int i = 0; i < effect.Positions.Length; i++)
                {
                    _positions.Add(local.MultiplyPoint3x4(effect.Positions[i]));
                    _normals.Add(i < effect.Normals.Length ? normals.MultiplyVector(effect.Normals[i]).normalized : Vector3.up);
                    _uvs.Add(i < effect.Uvs.Length ? effect.Uvs[i] : Vector2.zero);
                    _colours.Add(i < effect.Colours.Length ? effect.Colours[i] : new Color32(255, 255, 255, 255));
                    _lightAnchors.Add(Vector2.zero);
                }
                if (!slots.TryGetValue(effect.Material, out int slot))
                {
                    slot = _materials.Count; slots.Add(effect.Material, slot); _materials.Add(effect.Material);
                }
                for (int i = 0; i < effect.Indices.Length; i += 3)
                    _triangles.Add(new RenderTriangle(start + effect.Indices[i], start + effect.Indices[i + 1],
                        start + effect.Indices[i + 2], slot));
            }
        }

        private void BeforeCamera(ScriptableRenderContext context, Camera camera)
        {
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline != _checkedPipeline)
            {
                _checkedPipeline = pipeline;
                _dynamicBatching = pipeline != null ? pipeline.GetType().GetProperty("supportsDynamicBatching") : null;
            }
            if (_dynamicBatching != null && (bool)_dynamicBatching.GetValue(pipeline))
                throw new InvalidOperationException("Shared transparent vertex streams require dynamic batching disabled.");
            foreach (Material material in _materials)
                if (material.enableInstancing)
                    throw new InvalidOperationException("Shared transparent vertex streams require GPU instancing disabled.");
            Vector3 forward = camera.transform.forward;
            Matrix4x4 matrix = transform.localToWorldMatrix;
            Matrix4x4 culling = camera.cullingMatrix;
            if (!_dirty && forward == _lastForward && matrix == _lastMatrix && culling == _lastCullingMatrix) return;
            using ProfilerMarker.AutoScope scope = SortMarker.Auto();
            bool geometryChanged = _dirty || (_effects.Count != 0 && matrix != _lastMatrix);
            if (geometryChanged) { Collect(); _geometryRevision++; }
            foreach (Material material in _materials)
                if (material.enableInstancing)
                    throw new InvalidOperationException("Shared transparent vertex streams require GPU instancing disabled.");
            if (geometryChanged || matrix != _lastMatrix)
            {
                _worldPositions.Clear();
                foreach (Vector3 local in _positions)
                {
                    Vector3 world = matrix.MultiplyPoint3x4(local);
                    _worldPositions.Add(new NumericsVector3(world.x, world.y, world.z));
                }
            }
            using (CullMarker.Auto())
            {
                GeometryUtility.CalculateFrustumPlanes(culling, _frustum);
                _visibleTriangles.Clear();
                foreach (RenderTriangle triangle in _triangles)
                {
                    NumericsVector3 a = _worldPositions[triangle.A], b = _worldPositions[triangle.B], c = _worldPositions[triangle.C];
                    bool outside = false;
                    foreach (Plane plane in _frustum)
                    {
                        Vector3 n = plane.normal;
                        if (n.x * a.X + n.y * a.Y + n.z * a.Z + plane.distance < -.0001f
                            && n.x * b.X + n.y * b.Y + n.z * b.Z + plane.distance < -.0001f
                            && n.x * c.X + n.y * c.Y + n.z * c.Z + plane.distance < -.0001f)
                        { outside = true; break; }
                    }
                    if (!outside) _visibleTriangles.Add(triangle);
                }
            }
            IReadOnlyList<TriangleDepthOrderBuffer.Run> ordered;
            using (OrderMarker.Auto())
                ordered = _sort.Sort(_worldPositions, _visibleTriangles,
                    new NumericsVector3(forward.x, forward.y, forward.z));
            using ProfilerMarker.AutoScope uploadScope = UploadMarker.Auto();
            if (ordered.Count > short.MaxValue)
                throw new InvalidOperationException("Transparent terrain exceeds Unity's 32767 ordered-run limit.");
            UploadSharedVertices(geometryChanged);
            SelectRuns(ordered);
            for (int i = 0; i < ordered.Count; i++)
            {
                Run run = _selectedRuns[i];
                if (!run.Active) { run.Object.SetActive(true); run.Active = true; }
                Material material = _materials[ordered[i].MaterialIndex];
                if (run.Material != material) { run.Renderer.sharedMaterial = material; run.Material = material; }
                // Sorting order precedes material queue. Tag passes must precede actors/flowers;
                // other blended terrain retains its existing ordering (including water and fog).
                int sortingOrder = ClassicMeshes.IsTagOverlay(material) ? i - short.MaxValue : i + 1;
                if (run.SortingOrder != sortingOrder) { run.Renderer.sortingOrder = sortingOrder; run.SortingOrder = sortingOrder; }
                var triangles = ordered[i].Triangles;
                bool layoutChanged = run.VertexCapacity != _vertexData.Length;
                if (layoutChanged)
                {
                    // Index-only meshes retain the shared vertex count without allocating duplicate attributes.
                    // Bounds come from the shared positions; recalculating from the primary mesh is invalid.
                    run.Mesh.SetVertexBufferParams(_vertexData.Length, IndexMeshLayout);
                    run.Mesh.indexFormat = IndexFormat.UInt32;
                    run.VertexCapacity = _vertexData.Length;
                }
                if (!run.SharedBound) { run.Renderer.additionalVertexStreams = _sharedVertices; run.SharedBound = true; }
                if (run.BoundsRevision != _geometryRevision || layoutChanged || !SameTriangles(run.Triangles, triangles))
                {
                    Bounds bounds = new Bounds(_positions[triangles[0].A], Vector3.zero);
                    foreach (RenderTriangle triangle in triangles)
                    {
                        bounds.Encapsulate(_positions[triangle.A]);
                        bounds.Encapsulate(_positions[triangle.B]);
                        bounds.Encapsulate(_positions[triangle.C]);
                    }
                    if (layoutChanged || bounds != run.Bounds) { run.Mesh.bounds = bounds; run.Bounds = bounds; }
                    run.BoundsRevision = _geometryRevision;
                }
                if (!layoutChanged && SameTriangles(run.Triangles, triangles)) continue;
                run.Triangles.Clear();
                _runIndices.Clear();
                for (int t = 0; t < triangles.Count; t++)
                {
                    RenderTriangle triangle = triangles[t];
                    run.Triangles.Add(triangle);
                    _runIndices.Add(triangle.A); _runIndices.Add(triangle.B); _runIndices.Add(triangle.C);
                }
                run.Mesh.SetTriangles(_runIndices, 0, calculateBounds: false);
            }
            foreach (Run run in _runs)
                if (!run.Used && run.Active) { run.Object.SetActive(false); run.Active = false; }
            _dirty = false;
            _lastForward = forward;
            _lastMatrix = matrix;
            _lastCullingMatrix = culling;
        }

        private void UploadSharedVertices(bool geometryChanged)
        {
            if (_positions.Count == 0 || (!geometryChanged && _sharedVertices != null)) return;
            if (_sharedVertices == null)
            {
                _sharedVertices = new Mesh { name = "shared transparent vertices" };
                _sharedVertices.MarkDynamic();
            }
            bool grew = _vertexData.Length < _positions.Count;
            if (grew)
            {
                _vertexData = new SharedVertex[Math.Max(1024, Mathf.NextPowerOfTwo(_positions.Count))];
                _sharedVertices.SetVertexBufferParams(_vertexData.Length, SharedLayout);
            }
            for (int i = 0; i < _positions.Count; i++)
                _vertexData[i] = new SharedVertex
                {
                    Position = _positions[i], Normal = _normals[i], Colour = _colours[i],
                    Uv = _uvs[i], LightAnchor = _lightAnchors[i],
                };
            _sharedVertices.SetVertexBufferData(_vertexData, 0, 0, grew ? _vertexData.Length : _positions.Count, 0, MeshUpdateFlags.DontRecalculateBounds);
        }

        private static bool SameTriangles(List<RenderTriangle> previous, IReadOnlyList<RenderTriangle> next)
        {
            if (previous.Count != next.Count) return false;
            for (int i = 0; i < previous.Count; i++)
                if (previous[i] != next[i]) return false;
            return true;
        }

        private void SelectRuns(IReadOnlyList<TriangleDepthOrderBuffer.Run> ordered)
        {
            _runMatches.Clear();
            foreach (Run run in _runs)
            {
                run.Used = false;
                _runMatches[TriangleHash(run.Triangles)] = run;
            }
            _selectedRuns.Clear();
            for (int i = 0; i < ordered.Count; i++)
            {
                Run match = null;
                var triangles = ordered[i].Triangles;
                if (_runMatches.TryGetValue(TriangleHash(triangles), out Run candidate)
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
            foreach (Run run in _runs)
            {
                if (run.Renderer != null) run.Renderer.additionalVertexStreams = null;
                ReleaseMesh(run.Mesh);
            }
            ReleaseMesh(_sharedVertices);
        }

        private static void ReleaseMesh(Mesh mesh)
        {
            if (mesh == null) return;
            if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh);
        }
    }
}
