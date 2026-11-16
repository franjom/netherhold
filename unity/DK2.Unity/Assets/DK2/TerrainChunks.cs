// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Collections.Generic;
using System.Threading.Tasks;
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
    /// A change rebuilds the 5×5 tiles' chunks around it, where DK2 rebuilds the 3×3. A tile's
    /// vertices are built on a thread-pool task between frames (ADR-0696; <see cref="Flush"/>).
    /// </remarks>
    public sealed class TerrainChunks
    {
        private const int ChunkSize = 16;

        private static readonly ProfilerMarker RebuildMarker = new ProfilerMarker("DK2.TerrainChunks.Rebuild");
        private static readonly ProfilerMarker GeometryMarker = new ProfilerMarker("DK2.TerrainChunks.Geometry");
        private static readonly ProfilerMarker PrepareMarker = new ProfilerMarker("DK2.TerrainChunks.Prepare");
        private static readonly ProfilerMarker JoinMarker = new ProfilerMarker("DK2.TerrainChunks.Join");
        private static readonly ProfilerMarker UploadMarker = new ProfilerMarker("DK2.TerrainChunks.Upload");

        private readonly Transform _root;
        private readonly ClassicMeshes _classic;
        private readonly TransparentTerrain _transparent;
        private readonly TerrainLattice _lattice;
        private readonly TerrainTileBuilder _builder;
        private readonly int _width;
        private readonly int _chunksX;
        private readonly TileView[] _tiles;
        private readonly bool[] _drawn;
        private readonly HashSet<int> _dirty = new HashSet<int>();
        private readonly List<int> _dirtyOrder = new List<int>();
        private readonly Dictionary<int, GameObject> _chunks = new Dictionary<int, GameObject>();
        // Each chunk's mesh, held apart from its object so a teardown frees it even when the object
        // went first with its parent.
        private readonly Dictionary<int, Mesh> _chunkMeshes = new Dictionary<int, Mesh>();
        private readonly Dictionary<int, Material[]> _chunkMaterials = new Dictionary<int, Material[]>();
        private readonly List<Vector4> _staticLightInputs = new List<Vector4>();
        // Bounds are recalculated once at the end; nothing here changes an index out of range.
        private const MeshUpdateFlags UploadFlags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices
            | MeshUpdateFlags.DontNotifyMeshUsers;
        private readonly System.Diagnostics.Stopwatch _flushClock = new System.Diagnostics.Stopwatch();

        /// <summary>How long one <see cref="Flush"/> may spend uploading chunks before it leaves the
        /// rest for the next frame, in milliseconds; at least one chunk is uploaded per flush, and the
        /// first build after a <see cref="Clear"/> takes every chunk. A tag wave that touches many
        /// chunks then costs a few frames of a few milliseconds instead of one frame of a hundred,
        /// and a chunk not yet rebuilt shows its previous mesh until its turn (ADR-0457). Zero or
        /// less builds and uploads everything at once, on the calling thread.</summary>
        public double FlushBudgetMilliseconds { get; set; } = 4.0;

        /// <summary>How many chunks the last <see cref="Flush"/> left for a later one.</summary>
        public int PendingChunks => _dirty.Count;

        // Each drawn tile's pieces as the last build left them, in buffers the tile keeps: a rebuild
        // regenerates only the tiles whose inputs changed (_stale) and copies the rest, so a dig costs
        // its 5x5 window rather than every tile of the chunks that window touches, and the arrays are
        // refilled rather than replaced (a tick's rebuild was 0.5 MB of them, ADR-0696). The chunk
        // comes out the same, vertex for vertex and triangle for triangle.
        private readonly TerrainTileGeometry[] _geometry;
        private readonly bool[] _stale;
        // The tiles the running build task owns; nothing of theirs is read until it is joined.
        private readonly List<int> _building = new List<int>();
        private Task _buildTask;

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
            _lattice = lattice;
            _builder = new TerrainTileBuilder(classic, width, height, lattice);
            _width = width;
            _chunksX = (width + ChunkSize - 1) / ChunkSize;
            _tiles = new TileView[width * height];
            _drawn = new bool[width * height];
            _geometry = new TerrainTileGeometry[width * height];
            _stale = new bool[width * height];
        }

        /// <summary>Takes the tile when it has plain or room pieces; false leaves it to the placeholder.</summary>
        public bool TrySet(int tileIndex, in TileView tile)
        {
            // The build task reads the tiles and the lattice; a change waits for it (rarely: it is a
            // millisecond or two, started a frame or more before the next tick's tiles arrive).
            JoinBuild();
            bool draws = TerrainTileBuilder.Draws(tile);
            if (_lattice != null)
            {
                // A change reaches its 3×3 cells, and they bend the pieces one tile further.
                int x = tileIndex % _width, y = tileIndex / _width, height = _tiles.Length / _width;
                _lattice.Invalidate(x, y);
                for (int ty = Mathf.Max(0, y - 2); ty <= Mathf.Min(height - 1, y + 2); ty++)
                    for (int tx = Mathf.Max(0, x - 2); tx <= Mathf.Min(_width - 1, x + 2); tx++)
                        MarkStale((ty * _width) + tx);
            }
            else if (draws || _drawn[tileIndex]) MarkStale(tileIndex);
            if (tile.Tagged != _tiles[tileIndex].Tagged || tile.AmbientTint != _tiles[tileIndex].AmbientTint)
                DirtyNeighbours(tileIndex);
            _tiles[tileIndex] = tile;
            _drawn[tileIndex] = draws;
            return draws;
        }

        /// <summary>Takes a finished build over, starts one for the tiles that changed since, and
        /// uploads the chunks whose tiles are all built, lowest chunk first, within
        /// <see cref="FlushBudgetMilliseconds"/>; what does not fit stays dirty for the next flush.
        /// Everything is built inline and uploaded when nothing is drawn yet, so a level appears whole.</summary>
        public void Flush()
        {
            LastFlushMilliseconds = 0;
            if (_dirty.Count == 0) return;
            using ClassicMeshes.LoadBreakdown.Scope _ = ClassicMeshes.LoadBreakdown.Span(
                _classic?.Breakdown, ClassicMeshes.LoadBreakdown.Work.Terrain);
            _flushClock.Restart();
            bool budgeted = FlushBudgetMilliseconds > 0 && _chunks.Count > 0;
            Task build = _buildTask;
            if (build != null)
            {
                if (budgeted && !build.IsCompleted)
                {
                    LastFlushMilliseconds = _flushClock.Elapsed.TotalMilliseconds;
                    return;
                }
                JoinBuild();
            }

            _dirtyOrder.Clear();
            _dirtyOrder.AddRange(_dirty);
            _dirtyOrder.Sort();
            if (budgeted) StartBuild();
            else BuildInline();

            foreach (int chunk in _dirtyOrder)
            {
                if (budgeted && _dirty.Count < _dirtyOrder.Count && _flushClock.Elapsed.TotalMilliseconds >= FlushBudgetMilliseconds)
                    break;
                if (AnyStale(chunk)) continue;
                using (RebuildMarker.Auto())
                    Rebuild(chunk);
                _dirty.Remove(chunk);
            }
            LastFlushMilliseconds = _flushClock.Elapsed.TotalMilliseconds;
        }

        /// <summary>What the last <see cref="Flush"/> spent on the frame's thread, for the frame recorder.</summary>
        public double LastFlushMilliseconds { get; private set; }

        public void Clear()
        {
            JoinBuild();
            foreach (GameObject go in _chunks.Values) if (go != null) Object.Destroy(go);
            foreach (Mesh mesh in _chunkMeshes.Values) if (mesh != null) Object.Destroy(mesh);
            _chunks.Clear();
            _chunkMeshes.Clear();
            _transparent.Clear();
            _dirty.Clear();
            System.Array.Clear(_drawn, 0, _drawn.Length);
            System.Array.Clear(_geometry, 0, _geometry.Length);
            System.Array.Clear(_stale, 0, _stale.Length);
        }

        /// <summary>A tag tints the room walls facing the tile, so its four neighbours redraw.</summary>
        private void DirtyNeighbours(int tileIndex)
        {
            int x = tileIndex % _width, y = tileIndex / _width, height = _tiles.Length / _width;
            if (x > 0) MarkStale(tileIndex - 1);
            if (x < _width - 1) MarkStale(tileIndex + 1);
            if (y > 0) MarkStale(tileIndex - _width);
            if (y < height - 1) MarkStale(tileIndex + _width);
        }

        private void MarkStale(int tileIndex)
        {
            _stale[tileIndex] = true;
            _dirty.Add(ChunkOf(tileIndex));
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

        private int ChunkOf(int tileIndex) =>
            (tileIndex / _width / ChunkSize * _chunksX) + (tileIndex % _width / ChunkSize);

        private bool AnyStale(int chunk)
        {
            int height = _tiles.Length / _width;
            int x0 = chunk % _chunksX * ChunkSize, y0 = chunk / _chunksX * ChunkSize;
            for (int y = y0; y < Mathf.Min(y0 + ChunkSize, height); y++)
                for (int x = x0; x < Mathf.Min(x0 + ChunkSize, _width); x++)
                    if (_stale[(y * _width) + x]) return true;
            return false;
        }

        /// <summary>Phase one for every stale tile of the dirty chunks, on this thread; a stale tile
        /// no longer drawn just stops being stale.</summary>
        private void PrepareStale()
        {
            using ProfilerMarker.AutoScope _ = PrepareMarker.Auto();
            int height = _tiles.Length / _width;
            foreach (int chunk in _dirtyOrder)
            {
                int x0 = chunk % _chunksX * ChunkSize, y0 = chunk / _chunksX * ChunkSize;
                for (int y = y0; y < Mathf.Min(y0 + ChunkSize, height); y++)
                    for (int x = x0; x < Mathf.Min(x0 + ChunkSize, _width); x++)
                    {
                        int index = (y * _width) + x;
                        if (!_stale[index]) continue;
                        if (!_drawn[index])
                        {
                            _stale[index] = false;
                            continue;
                        }
                        _builder.Prepare(x, y, _tiles, _geometry[index] ??= new TerrainTileGeometry());
                        _building.Add(index);
                    }
            }
        }

        private void StartBuild()
        {
            PrepareStale();
            if (_building.Count == 0) return;
            _buildTask = Task.Run(() =>
            {
                foreach (int index in _building) _builder.Build(_geometry[index]);
            });
        }

        private void BuildInline()
        {
            PrepareStale();
            using (GeometryMarker.Auto())
                foreach (int index in _building) _builder.Build(_geometry[index]);
            foreach (int index in _building) _stale[index] = false;
            _building.Clear();
        }

        /// <summary>Waits for the build task, if one runs, and takes its tiles over.</summary>
        private void JoinBuild()
        {
            Task build = _buildTask;
            if (build == null) return;
            using (JoinMarker.Auto())
            {
                _buildTask = null;
                try
                {
                    build.GetAwaiter().GetResult();
                }
                finally
                {
                    foreach (int index in _building) _stale[index] = false;
                    _building.Clear();
                }
            }
        }

        private void Rebuild(int chunk)
        {
            _positions.Clear();
            _normals.Clear();
            _uvs.Clear();
            _lightAnchors.Clear();
            foreach (List<int> list in _byMaterial.Values) list.Clear();

            int height = _tiles.Length / _width;
            int x0 = chunk % _chunksX * ChunkSize, y0 = chunk / _chunksX * ChunkSize;
            for (int y = y0; y < Mathf.Min(y0 + ChunkSize, height); y++)
                for (int x = x0; x < Mathf.Min(x0 + ChunkSize, _width); x++)
                {
                    int index = (y * _width) + x;
                    if (_drawn[index]) Append(_geometry[index]);
                }

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
            _builder.PrepareSinglePiece(geometry, piece, tagged, tint);
            _builder.Build(geometry);
            var emitted = new Dictionary<Material, List<int>>();
            for (int s = 0; s < geometry.KeyCount; s++)
            {
                var list = new List<int>();
                for (int i = 0; i < geometry.Triangles[s].Count; i++) list.Add(geometry.Triangles[s].Items[i]);
                emitted[_classic.Terrain(geometry.Keys[s])] = list;
            }
            return emitted;
        }
    }
}
