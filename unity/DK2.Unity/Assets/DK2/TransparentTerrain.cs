// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Reflection;
using DK2.Art;
using DK2.Presentation;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using NumericsMatrix = System.Numerics.Matrix4x4;
using NumericsVector2 = System.Numerics.Vector2;
using NumericsVector3 = System.Numerics.Vector3;

namespace DK2.Unity
{
    /// <summary>Blended terrain faces share one camera-depth stream across chunks and materials: what is collected,
    /// culled, sorted and split is <see cref="BlendedTerrainStream{TMaterial}"/>'s (plan step 3d); this class uploads
    /// its runs, each a <see cref="GameObject"/> drawn at the stream's sorting order.</summary>
    public sealed class TransparentTerrain : MonoBehaviour
    {
        private static readonly ProfilerMarker SetChunkMarker = new ProfilerMarker("DK2.TransparentTerrain.SetChunk");
        private static readonly ProfilerMarker SortMarker = new ProfilerMarker("DK2.TransparentTerrain.Sort");
        private static readonly ProfilerMarker OrderMarker = new ProfilerMarker("DK2.TransparentTerrain.DepthOrder");
        private static readonly ProfilerMarker UploadMarker = new ProfilerMarker("DK2.TransparentTerrain.UploadRuns");
        private static readonly ProfilerMarker CullMarker = new ProfilerMarker("DK2.TransparentTerrain.Cull");

        private sealed class Run
        {
            public GameObject Object;
            public Mesh Mesh;
            public MeshRenderer Renderer;
            public bool Used;
            public long BoundsRevision = -1;
            public int VertexCapacity, IndexCapacity, SortingOrder = int.MinValue;
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
        private RenderPipelineAsset _checkedPipeline;
        private PropertyInfo _dynamicBatching;

        private readonly BlendedTerrainStream<Material> _stream = new BlendedTerrainStream<Material>(ClassicMeshes.IsTagOverlay)
        {
            Profiler = StreamMarkers.Instance,
        };
        private readonly ChunkVertices _chunkVertices = new ChunkVertices();

        private readonly List<Vector3> _effectPositions = new List<Vector3>(), _effectNormals = new List<Vector3>();
        private readonly List<Vector2> _effectUvs = new List<Vector2>();
        private readonly List<Color32> _effectColours = new List<Color32>();
        private readonly List<int> _effectIndices = new List<int>();
        private BlendedVertex[] _effectVertices = Array.Empty<BlendedVertex>();

        // Each effect's inputs as last added, compared before any vertex is placed, as before the move.
        private sealed class EffectInputs
        {
            public Material Material;
            public Matrix4x4 Transform, WorldToLocal;
            public Vector3[] Positions = Array.Empty<Vector3>(), Normals = Array.Empty<Vector3>();
            public Vector2[] Uvs = Array.Empty<Vector2>();
            public Color32[] Colours = Array.Empty<Color32>();
            public int[] Indices = Array.Empty<int>();
        }

        private readonly List<EffectInputs> _effectInputs = new List<EffectInputs>();
        private int _effectInputCount;
        private int[] _effectIndexData = Array.Empty<int>();

        private readonly List<Run> _runs = new List<Run>();
        private readonly List<Run> _selectedRuns = new List<Run>();
        private readonly Dictionary<int, Run> _runMatches = new Dictionary<int, Run>();
        private readonly Plane[] _frustum = new Plane[6];
        private readonly List<NumericsVector3> _itemWorlds = new List<NumericsVector3>();
        private int[] _runIndexData = Array.Empty<int>();

        private void OnEnable() => RenderPipelineManager.beginCameraRendering += BeforeCamera;
        private void OnDisable() => RenderPipelineManager.beginCameraRendering -= BeforeCamera;

        public void BeginEffectFrame()
        {
            _effectInputCount = 0;
            _stream.BeginEffectFrame();
        }

        /// <summary>Unlit blended effect geometry, sorted together with the terrain this frame. An effect whose inputs
        /// and this object's transform are unchanged is repeated; otherwise it is placed in this object's space now,
        /// absent normals up, UVs zero, colours white.</summary>
        public void AddEffectMesh(Mesh mesh, int submesh, Material material, Matrix4x4 worldTransform)
        {
            mesh.GetVertices(_effectPositions); mesh.GetNormals(_effectNormals);
            mesh.GetUVs(0, _effectUvs); mesh.GetColors(_effectColours); mesh.GetTriangles(_effectIndices, submesh);
            if (_effectInputCount == _effectInputs.Count) _effectInputs.Add(new EffectInputs());
            EffectInputs inputs = _effectInputs[_effectInputCount++];
            Matrix4x4 worldToLocal = transform.worldToLocalMatrix;
            if (inputs.Material == material && inputs.Transform.Equals(worldTransform) && inputs.WorldToLocal.Equals(worldToLocal)
                && Same(_effectPositions, inputs.Positions) && Same(_effectNormals, inputs.Normals)
                && Same(_effectUvs, inputs.Uvs) && Same(_effectColours, inputs.Colours)
                && Same(_effectIndices, inputs.Indices))
            {
                _stream.RepeatEffect();
                return;
            }
            inputs.Material = material; inputs.Transform = worldTransform; inputs.WorldToLocal = worldToLocal;
            Copy(_effectPositions, ref inputs.Positions); Copy(_effectNormals, ref inputs.Normals);
            Copy(_effectUvs, ref inputs.Uvs); Copy(_effectColours, ref inputs.Colours);
            Copy(_effectIndices, ref inputs.Indices);
            Matrix4x4 local = worldToLocal * worldTransform;
            Matrix4x4 normals = local.inverse.transpose;
            int count = _effectPositions.Count;
            if (_effectVertices.Length < count) _effectVertices = new BlendedVertex[Mathf.NextPowerOfTwo(count)];
            for (int i = 0; i < count; i++)
            {
                Vector3 position = local.MultiplyPoint3x4(_effectPositions[i]);
                Vector3 normal = i < _effectNormals.Count ? normals.MultiplyVector(_effectNormals[i]).normalized : Vector3.up;
                Vector2 uv = i < _effectUvs.Count ? _effectUvs[i] : Vector2.zero;
                Color32 colour = i < _effectColours.Count ? _effectColours[i] : new Color32(255, 255, 255, 255);
                _effectVertices[i] = new BlendedVertex(Numerics(position), Numerics(normal), new NumericsVector2(uv.x, uv.y),
                    NumericsVector2.Zero, new VertexColour(colour.r, colour.g, colour.b, colour.a));
            }
            _stream.AddEffect(material, new ReadOnlySpan<BlendedVertex>(_effectVertices, 0, count),
                IndicesOf(_effectIndices));
        }

        public void EndEffectFrame()
        {
            if (_effectInputCount < _effectInputs.Count) _effectInputs.RemoveRange(_effectInputCount, _effectInputs.Count - _effectInputCount);
            _stream.EndEffectFrame();
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

        /// <summary>The chunk's blended materials (render queue Transparent and later) join the stream.</summary>
        public void SetChunk(int chunk, List<Vector3> positions, List<Vector3> normals,
            List<Vector2> uvs, List<Vector2> lightAnchors, Dictionary<Material, List<int>> byMaterial)
        {
            using ProfilerMarker.AutoScope scope = SetChunkMarker.Auto();
            _chunkVertices.Set(positions, normals, uvs, lightAnchors);
            _stream.BeginChunk(chunk, _chunkVertices);
            foreach (KeyValuePair<Material, List<int>> entry in byMaterial)
                if (entry.Value.Count != 0 && entry.Key.renderQueue >= (int)RenderQueue.Transparent)
                    _stream.AddChunkTriangles(entry.Key, entry.Value);
            _stream.EndChunk();
            _chunkVertices.Set(null, null, null, null);
        }

        public void Clear()
        {
            _stream.Clear();
            _effectInputs.Clear(); _effectInputCount = 0;
            // A teardown on scene unload can find a run's object already gone with its parent.
            foreach (Run run in _runs)
            {
                if (run.Object != null && run.Active) run.Object.SetActive(false);
                run.Active = false;
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
            CheckInstancing();
            Refresh(camera);
        }

        /// <summary>Joins blended items (status flowers) at <paramref name="worlds"/> into this frame's back-to-front
        /// stream for <paramref name="camera"/> (<see cref="BlendedTerrainStream{TMaterial}.PlaceItems"/>) and writes each
        /// item's sorting order to <paramref name="orders"/>. Call once per frame after the camera is placed and before
        /// the render; the render's own refresh then finds nothing changed and keeps these runs.</summary>
        public void PlaceItems(Camera camera, List<Vector3> worlds, List<int> orders)
        {
            _itemWorlds.Clear();
            foreach (Vector3 world in worlds) _itemWorlds.Add(Numerics(world));
            _stream.SetItems(Numerics(camera.transform.forward), _itemWorlds);
            Refresh(camera);
            _stream.ItemOrders(orders);
        }

        /// <summary>The frustum's planes are taken only when the stream will re-sort, as before the move.</summary>
        private void Refresh(Camera camera)
        {
            NumericsVector3 forward = Numerics(camera.transform.forward);
            NumericsMatrix matrix = Numerics(transform.localToWorldMatrix);
            Matrix4x4 culling = camera.cullingMatrix;
            if (!_stream.NeedsRefresh(forward, matrix, Numerics(culling))) return;
            if (_stream.Refresh(forward, matrix, Frustum(culling))) Upload();
        }

        private ViewFrustum Frustum(Matrix4x4 culling)
        {
            GeometryUtility.CalculateFrustumPlanes(culling, _frustum);
            return new ViewFrustum(Numerics(culling), Numerics(_frustum[0]), Numerics(_frustum[1]), Numerics(_frustum[2]),
                Numerics(_frustum[3]), Numerics(_frustum[4]), Numerics(_frustum[5]));
        }

        private void CheckInstancing()
        {
            for (int i = 0; i < _stream.MaterialCount; i++)
                if (_stream.Material(i).enableInstancing)
                    throw new InvalidOperationException("Shared transparent vertex streams require GPU instancing disabled.");
        }

        private void Upload()
        {
            CheckInstancing();
            using ProfilerMarker.AutoScope uploadScope = UploadMarker.Auto();
            IReadOnlyList<TriangleDepthOrderBuffer.Run> ordered = _stream.Runs;
            if (ordered.Count > short.MaxValue)
                throw new InvalidOperationException("Transparent terrain exceeds Unity's 32767 ordered-run limit.");
            IReadOnlyList<BlendedVertex> vertices = _stream.Vertices;
            UploadSharedVertices(_stream.GeometryChanged);
            SelectRuns(ordered);
            for (int i = 0; i < ordered.Count; i++)
            {
                Run run = _selectedRuns[i];
                if (!run.Active) { run.Object.SetActive(true); run.Active = true; }
                Material material = _stream.Material(ordered[i].MaterialIndex);
                if (run.Material != material) { run.Renderer.sharedMaterial = material; run.Material = material; }
                // Sorting order precedes material queue (BlendedTerrainStream.SortingOrder).
                int sortingOrder = _stream.SortingOrder(i);
                if (run.SortingOrder != sortingOrder) { run.Renderer.sortingOrder = sortingOrder; run.SortingOrder = sortingOrder; }
                var triangles = ordered[i].Triangles;
                bool layoutChanged = run.VertexCapacity != _vertexData.Length;
                if (layoutChanged)
                {
                    // Index-only meshes retain the shared vertex count without allocating duplicate attributes.
                    // Bounds come from the shared positions; recalculating from the primary mesh is invalid.
                    run.Mesh.SetVertexBufferParams(_vertexData.Length, IndexMeshLayout);
                    run.VertexCapacity = _vertexData.Length;
                }
                if (!run.SharedBound) { run.Renderer.additionalVertexStreams = _sharedVertices; run.SharedBound = true; }
                bool sameTriangles = SameTriangles(run.Triangles, triangles);
                bool boundsChanged = false;
                if (run.BoundsRevision != _stream.GeometryRevision || layoutChanged || !sameTriangles)
                {
                    Bounds bounds = new Bounds(Unity(vertices[triangles[0].A].Position), Vector3.zero);
                    foreach (RenderTriangle triangle in triangles)
                    {
                        bounds.Encapsulate(Unity(vertices[triangle.A].Position));
                        bounds.Encapsulate(Unity(vertices[triangle.B].Position));
                        bounds.Encapsulate(Unity(vertices[triangle.C].Position));
                    }
                    if (layoutChanged || bounds != run.Bounds) { run.Mesh.bounds = bounds; run.Bounds = bounds; boundsChanged = true; }
                    run.BoundsRevision = _stream.GeometryRevision;
                }
                if (!layoutChanged && sameTriangles)
                {
                    if (boundsChanged) SetRunDrawRange(run, triangles.Count * 3);
                    continue;
                }
                run.Triangles.Clear();
                int indexCount = triangles.Count * 3;
                if (_runIndexData.Length < indexCount) _runIndexData = new int[Mathf.NextPowerOfTwo(indexCount)];
                for (int t = 0; t < triangles.Count; t++)
                {
                    RenderTriangle triangle = triangles[t];
                    run.Triangles.Add(triangle);
                    _runIndexData[t * 3] = triangle.A; _runIndexData[t * 3 + 1] = triangle.B; _runIndexData[t * 3 + 2] = triangle.C;
                }
                if (run.IndexCapacity < indexCount)
                {
                    // Changing triangle counts must not resize native backing storage every frame.
                    run.IndexCapacity = Math.Max(16, Mathf.NextPowerOfTwo(indexCount));
                    run.Mesh.SetIndexBufferParams(run.IndexCapacity, IndexFormat.UInt32);
                }
                run.Mesh.SetIndexBufferData(_runIndexData, 0, 0, indexCount, MeshUpdateFlags.DontRecalculateBounds);
                SetRunDrawRange(run, indexCount);
            }
            foreach (Run run in _runs)
                if (!run.Used && run.Active) { run.Object.SetActive(false); run.Active = false; }
        }

        private static void SetRunDrawRange(Run run, int indexCount)
        {
            run.Mesh.SetSubMesh(0, new SubMeshDescriptor(0, indexCount, MeshTopology.Triangles)
            { bounds = run.Bounds, firstVertex = 0, vertexCount = run.VertexCapacity }, MeshUpdateFlags.DontRecalculateBounds);
        }

        private void UploadSharedVertices(bool geometryChanged)
        {
            IReadOnlyList<BlendedVertex> vertices = _stream.Vertices;
            if (vertices.Count == 0 || (!geometryChanged && _sharedVertices != null)) return;
            if (_sharedVertices == null)
            {
                _sharedVertices = new Mesh { name = "shared transparent vertices" };
                _sharedVertices.MarkDynamic();
            }
            bool grew = _vertexData.Length < vertices.Count;
            if (grew)
            {
                _vertexData = new SharedVertex[Math.Max(1024, Mathf.NextPowerOfTwo(vertices.Count))];
                _sharedVertices.SetVertexBufferParams(_vertexData.Length, SharedLayout);
            }
            for (int i = 0; i < vertices.Count; i++)
            {
                BlendedVertex vertex = vertices[i];
                _vertexData[i] = new SharedVertex
                {
                    Position = Unity(vertex.Position), Normal = Unity(vertex.Normal),
                    Colour = new Color32(vertex.Colour.R, vertex.Colour.G, vertex.Colour.B, vertex.Colour.A),
                    Uv = new Vector2(vertex.Uv.X, vertex.Uv.Y), LightAnchor = new Vector2(vertex.LightAnchor.X, vertex.LightAnchor.Y),
                };
            }
            _sharedVertices.SetVertexBufferData(_vertexData, 0, 0, grew ? _vertexData.Length : vertices.Count, 0, MeshUpdateFlags.DontRecalculateBounds);
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

        // List<T> has no span view on Unity's profile: the indices go through a reused buffer.
        private ReadOnlySpan<int> IndicesOf(List<int> list)
        {
            if (_effectIndexData.Length < list.Count) _effectIndexData = new int[Mathf.NextPowerOfTwo(list.Count)];
            list.CopyTo(_effectIndexData);
            return new ReadOnlySpan<int>(_effectIndexData, 0, list.Count);
        }

        private static NumericsVector3 Numerics(Vector3 v) => new NumericsVector3(v.x, v.y, v.z);

        private static Vector3 Unity(NumericsVector3 v) => new Vector3(v.X, v.Y, v.Z);

        private static System.Numerics.Plane Numerics(Plane plane) => new System.Numerics.Plane(Numerics(plane.normal), plane.distance);

        /// <summary>Unity's layout: <c>M11</c> is <c>m00</c>, <c>M14</c> is <c>m03</c>.</summary>
        private static NumericsMatrix Numerics(Matrix4x4 m) => new NumericsMatrix(
            m.m00, m.m01, m.m02, m.m03, m.m10, m.m11, m.m12, m.m13, m.m20, m.m21, m.m22, m.m23, m.m30, m.m31, m.m32, m.m33);

        /// <summary>A chunk's vertex lists read as <see cref="BlendedVertex"/> where the stream asks, so only the blended
        /// triangles' vertices are converted.</summary>
        private sealed class ChunkVertices : IReadOnlyList<BlendedVertex>
        {
            private List<Vector3> _positions, _normals;
            private List<Vector2> _uvs, _lightAnchors;

            public void Set(List<Vector3> positions, List<Vector3> normals, List<Vector2> uvs, List<Vector2> lightAnchors)
            {
                _positions = positions; _normals = normals; _uvs = uvs; _lightAnchors = lightAnchors;
            }

            public int Count => _positions.Count;

            public BlendedVertex this[int index] => new BlendedVertex(Numerics(_positions[index]), Numerics(_normals[index]),
                new NumericsVector2(_uvs[index].x, _uvs[index].y),
                new NumericsVector2(_lightAnchors[index].x, _lightAnchors[index].y), VertexColour.White);

            public IEnumerator<BlendedVertex> GetEnumerator()
            {
                for (int i = 0; i < Count; i++) yield return this[i];
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        /// <summary>The stream's markers mapped to this class's Unity markers.</summary>
        private sealed class StreamMarkers : DK2.Simulation.ISimulationProfiler
        {
            public static readonly StreamMarkers Instance = new StreamMarkers();

            public void Begin(DK2.Simulation.SimulationMarker marker) => Of(marker)?.Begin();

            public void End(DK2.Simulation.SimulationMarker marker) => Of(marker)?.End();

            private static ProfilerMarker? Of(DK2.Simulation.SimulationMarker marker) =>
                marker == BlendedTerrainStream.Markers.Sort ? SortMarker
                : marker == BlendedTerrainStream.Markers.Cull ? CullMarker
                : marker == BlendedTerrainStream.Markers.DepthOrder ? OrderMarker
                : (ProfilerMarker?)null;
        }
    }
}
