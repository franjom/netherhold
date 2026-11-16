// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Collections.Generic;
using DK2.Assets;
using DK2.Presentation;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace DK2.Unity
{
    /// <summary>
    /// The classic look's terrain (plan steps 1 and 2): each tile's pieces as <see cref="TerrainTileBuilder"/>
    /// places, bends and keys them, merged per 16×16-tile chunk into one mesh, a submesh per opaque
    /// material; blended triangles go to <see cref="TransparentTerrain"/>.
    /// </summary>
    /// <remarks>
    /// Which tiles go stale and when a chunk is rebuilt is <see cref="TerrainChunkScheduler"/>'s (the 5×5 window,
    /// the flush budget, the thread-pool build, ADR-0457 and ADR-0696); this class uploads what it hands over.
    /// </remarks>
    public sealed class TerrainChunks : ITerrainChunkSink
    {
        private static readonly ProfilerMarker UploadMarker = new ProfilerMarker("DK2.TerrainChunks.Upload");
        // The scheduler's spans, made at type init as before the move, so the runner's recorders find them.
        private static readonly ProfilerMarker RebuildMarker = new ProfilerMarker("DK2.TerrainChunks.Rebuild");
        private static readonly ProfilerMarker GeometryMarker = new ProfilerMarker("DK2.TerrainChunks.Geometry");
        private static readonly ProfilerMarker PrepareMarker = new ProfilerMarker("DK2.TerrainChunks.Prepare");
        private static readonly ProfilerMarker JoinMarker = new ProfilerMarker("DK2.TerrainChunks.Join");

        private readonly Transform _root;
        private readonly ClassicMeshes _classic;
        private readonly TransparentTerrain _transparent;
        private readonly TerrainChunkScheduler _scheduler;
        private readonly Dictionary<int, GameObject> _chunks = new Dictionary<int, GameObject>();
        // Each chunk's mesh, held apart from its object so a teardown frees it even when the object
        // went first with its parent.
        private readonly Dictionary<int, Mesh> _chunkMeshes = new Dictionary<int, Mesh>();
        private readonly Dictionary<int, Material[]> _chunkMaterials = new Dictionary<int, Material[]>();
        private readonly List<Vector4> _staticLightInputs = new List<Vector4>();
        // Bounds are recalculated once at the end; nothing here changes an index out of range.
        private const MeshUpdateFlags UploadFlags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices
            | MeshUpdateFlags.DontNotifyMeshUsers;

        /// <summary>How long one <see cref="Flush"/> may spend before it leaves the rest for the next frame
        /// (<see cref="TerrainChunkScheduler.FlushBudgetMilliseconds"/>, ADR-0457).</summary>
        public double FlushBudgetMilliseconds
        {
            get => _scheduler.FlushBudgetMilliseconds;
            set => _scheduler.FlushBudgetMilliseconds = value;
        }

        /// <summary>How many chunks the last <see cref="Flush"/> left for a later one.</summary>
        public int PendingChunks => _scheduler.PendingChunks;

        // One chunk's assembly, reused across rebuilds.
        private readonly List<Vector3> _positions = new List<Vector3>();
        private readonly List<Vector3> _normals = new List<Vector3>();
        private readonly List<Vector2> _uvs = new List<Vector2>();
        private readonly List<Vector2> _lightAnchors = new List<Vector2>();
        private readonly Dictionary<Material, List<int>> _byMaterial = new Dictionary<Material, List<int>>();
        private readonly List<Material> _opaque = new List<Material>();

        public TerrainChunks(Transform root, ClassicMeshes classic, int width, int height,
            TerrainLattice lattice = null)
        {
            _root = root;
            _classic = classic;
            var blended = new GameObject("transparent terrain");
            blended.transform.SetParent(root, worldPositionStays: false);
            _transparent = blended.AddComponent<TransparentTerrain>();
            _scheduler = new TerrainChunkScheduler(classic, width, height, lattice)
            {
                Profiler = SchedulerMarkers.Instance,
            };
        }

        /// <summary>Takes the tile when it has plain or room pieces; false leaves it to the placeholder.</summary>
        public bool TrySet(int tileIndex, in TileView tile) => _scheduler.Set(tileIndex, tile);

        /// <summary>Uploads the chunks the scheduler hands over this frame (<see cref="TerrainChunkScheduler.Flush"/>).</summary>
        public void Flush()
        {
            if (_scheduler.PendingChunks == 0)
            {
                _scheduler.Flush(this);
                return;
            }
            using ClassicMeshes.LoadBreakdown.Scope _ = ClassicMeshes.LoadBreakdown.Span(
                _classic?.Breakdown, ClassicMeshes.LoadBreakdown.Work.Terrain);
            _scheduler.Flush(this);
        }

        /// <summary>What the last <see cref="Flush"/> spent on the frame's thread, for the frame recorder.</summary>
        public double LastFlushMilliseconds => _scheduler.LastFlushMilliseconds;

        /// <summary>Waits for the thread-pool build, if one runs; call it before changing the lattice's latches, which
        /// forget cells the build may be reading (<see cref="TerrainChunkScheduler.JoinBuild"/>).</summary>
        public void JoinBuild() => _scheduler.JoinBuild();

        public void Clear()
        {
            _scheduler.Clear();
            foreach (GameObject go in _chunks.Values) if (go != null) Object.Destroy(go);
            foreach (Mesh mesh in _chunkMeshes.Values) if (mesh != null) Object.Destroy(mesh);
            _chunks.Clear();
            _chunkMeshes.Clear();
            _transparent.Clear();
        }

        // The mesh is the chunk's own: destroying the object alone would leak it.
        private void DestroyChunk(int chunk)
        {
            if (_chunks.TryGetValue(chunk, out GameObject go) && go != null) Object.Destroy(go);
            if (_chunkMeshes.TryGetValue(chunk, out Mesh mesh) && mesh != null) Object.Destroy(mesh);
            _chunks.Remove(chunk);
            _chunkMeshes.Remove(chunk);
            _chunkMaterials.Remove(chunk);
        }

        void ITerrainChunkSink.BeginChunk(int chunk)
        {
            _positions.Clear();
            _normals.Clear();
            _uvs.Clear();
            _lightAnchors.Clear();
            foreach (List<int> list in _byMaterial.Values) list.Clear();
        }

        void ITerrainChunkSink.AddTile(int tileIndex, TerrainTileGeometry geometry) => Append(geometry);

        void ITerrainChunkSink.EndChunk(int chunk) => Rebuild(chunk);

        private void Rebuild(int chunk)
        {
            _transparent.SetChunk(chunk, _positions, _normals, _uvs, _lightAnchors, _byMaterial);
            _chunks.TryGetValue(chunk, out GameObject go);
            if (_positions.Count == 0)
            {
                DestroyChunk(chunk);
                return;
            }

            using ProfilerMarker.AutoScope upload = UploadMarker.Auto();
            // The chunk keeps its object, its mesh and its material array: a rebuild refills them in
            // place, so a tick's rebuild leaves nothing for the collector (ADR-0695).
            if (!_chunkMeshes.TryGetValue(chunk, out Mesh mesh) || mesh == null)
            {
                mesh = new Mesh { name = $"terrain chunk {chunk}" };
                mesh.MarkDynamic();
                _chunkMeshes[chunk] = mesh;
            }
            mesh.Clear(keepVertexLayout: true);
            mesh.indexFormat = _positions.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(_positions, 0, _positions.Count, UploadFlags);
            mesh.SetNormals(_normals, 0, _normals.Count, UploadFlags);
            mesh.SetUVs(0, _uvs, 0, _uvs.Count, UploadFlags);
            mesh.SetUVs(1, _lightAnchors, 0, _lightAnchors.Count, UploadFlags);
            // Missing vertex channels can arrive as ones and select the animated lighting arm.
            while (_staticLightInputs.Count < _positions.Count) _staticLightInputs.Add(default);
            mesh.SetUVs(2, _staticLightInputs, 0, _positions.Count, UploadFlags);
            mesh.SetUVs(3, _staticLightInputs, 0, _positions.Count, UploadFlags);
            _opaque.Clear();
            foreach (KeyValuePair<Material, List<int>> entry in _byMaterial)
                if (entry.Value.Count > 0 && entry.Key.renderQueue < (int)RenderQueue.Transparent) _opaque.Add(entry.Key);
            mesh.subMeshCount = _opaque.Count;
            for (int i = 0; i < _opaque.Count; i++)
                mesh.SetTriangles(_byMaterial[_opaque[i]], i, calculateBounds: false);
            mesh.RecalculateBounds();

            if (go == null)
            {
                go = new GameObject(mesh.name);
                go.transform.SetParent(_root, worldPositionStays: false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>();
                _chunks[chunk] = go;
            }
            if (!_chunkMaterials.TryGetValue(chunk, out Material[] materials) || !SameMaterials(materials))
            {
                _chunkMaterials[chunk] = materials = _opaque.ToArray();
                go.GetComponent<MeshRenderer>().sharedMaterials = materials;
            }
        }

        private bool SameMaterials(Material[] materials)
        {
            if (materials.Length != _opaque.Count) return false;
            for (int i = 0; i < materials.Length; i++)
                if (!ReferenceEquals(materials[i], _opaque[i])) return false;
            return true;
        }

        /// <summary>Copies one tile's vertices into the chunk and its triangles into each key's
        /// material list, numbered from the chunk's vertices. Main thread: a material is made on first use.</summary>
        private void Append(TerrainTileGeometry geometry)
        {
            if (geometry == null) return;
            int start = _positions.Count;
            var anchor = new Vector2(geometry.LightAnchor.X, geometry.LightAnchor.Y);
            System.Numerics.Vector3[] positions = geometry.Positions, normals = geometry.Normals;
            System.Numerics.Vector2[] uvs = geometry.Uvs;
            for (int i = 0; i < geometry.VertexCount; i++)
            {
                _positions.Add(new Vector3(positions[i].X, positions[i].Y, positions[i].Z));
                _normals.Add(new Vector3(normals[i].X, normals[i].Y, normals[i].Z));
                _uvs.Add(new Vector2(uvs[i].X, uvs[i].Y));
                _lightAnchors.Add(anchor);
            }
            for (int s = 0; s < geometry.KeyCount; s++)
            {
                Material material = _classic.Terrain(geometry.Keys[s]);
                if (!_byMaterial.TryGetValue(material, out List<int> list))
                    _byMaterial[material] = list = new List<int>();
                TerrainIndexList triangles = geometry.Triangles[s];
                int[] items = triangles.Items;
                for (int i = 0; i < triangles.Count; i++) list.Add(start + items[i]);
            }
        }

        /// <summary>One piece's emitted triangles per material, for the editor's tag material probe
        /// (reflection): both phases of a one-tile build with the piece alone.</summary>
        private Dictionary<Material, List<int>> ProbeEmitted(TerrainPiece piece, bool tagged,
            DK2.Data.Model.TerrainLightColour tint)
        {
            var geometry = new TerrainTileGeometry();
            _scheduler.Builder.PrepareSinglePiece(geometry, piece, tagged, tint);
            _scheduler.Builder.Build(geometry);
            var emitted = new Dictionary<Material, List<int>>();
            for (int s = 0; s < geometry.KeyCount; s++)
            {
                var list = new List<int>();
                for (int i = 0; i < geometry.Triangles[s].Count; i++) list.Add(geometry.Triangles[s].Items[i]);
                emitted[_classic.Terrain(geometry.Keys[s])] = list;
            }
            return emitted;
        }

        /// <summary>The scheduler's markers mapped to this class's Unity markers.</summary>
        private sealed class SchedulerMarkers : DK2.Simulation.ISimulationProfiler
        {
            public static readonly SchedulerMarkers Instance = new SchedulerMarkers();

            public void Begin(DK2.Simulation.SimulationMarker marker) => Of(marker)?.Begin();

            public void End(DK2.Simulation.SimulationMarker marker) => Of(marker)?.End();

            private static ProfilerMarker? Of(DK2.Simulation.SimulationMarker marker) =>
                marker == TerrainChunkScheduler.Markers.Rebuild ? RebuildMarker
                : marker == TerrainChunkScheduler.Markers.Geometry ? GeometryMarker
                : marker == TerrainChunkScheduler.Markers.Prepare ? PrepareMarker
                : marker == TerrainChunkScheduler.Markers.Join ? JoinMarker
                : (ProfilerMarker?)null;
        }
    }
}
