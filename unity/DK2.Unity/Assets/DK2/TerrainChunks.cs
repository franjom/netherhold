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
    /// The classic look's terrain (plan steps 1 and 2): plain <see cref="TerrainPieces"/>, a room's
    /// floor and <see cref="RoomWalls"/>, and a liquid's bed, merged per 16×16-tile chunk.
    /// </summary>
    /// <remarks>
    /// A piece is turned a quarter per step about its origin, clockwise from above (side N 0 to E 1,
    /// as DK2's side table runs), then moved to the tile's centre and, for a quarter, its quadrant's.
    /// Heights are the models' own: floor 1, walls 1 to 2 (ADR-0411). A <see cref="TerrainLattice"/>
    /// then bends each vertex as DK2 does, keeping the straight normals (terrain-lattice.md §1); a
    /// change rebuilds the 5×5 tiles' chunks around it, where DK2 rebuilds the 3×3. A tile's
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

        // Quadrant centres NW, NE, SE, SW in renderer X (east) and Z (north).
        private static readonly Vector2[] QuadrantCentres =
        {
            new Vector2(-0.25f, 0.25f), new Vector2(0.25f, 0.25f),
            new Vector2(0.25f, -0.25f), new Vector2(-0.25f, -0.25f),
        };

        // The four quarter turns, made on the main thread: the build thread only multiplies by them.
        private static readonly Quaternion[] Turns =
        {
            Quaternion.identity, Quaternion.Euler(0f, 90f, 0f), Quaternion.Euler(0f, 180f, 0f), Quaternion.Euler(0f, 270f, 0f),
        };

        private readonly Transform _root;
        private readonly ClassicMeshes _classic;
        private readonly TransparentTerrain _transparent;
        private readonly TerrainLattice _lattice;
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
        private sealed class TileGeometry
        {
            // Phase one, the frame's: the pieces placed in the tile and, per triangle group, the
            // material slot it draws with.
            public readonly List<PlacedPiece> Pieces = new List<PlacedPiece>();
            public readonly List<TriangleGroup> Groups = new List<TriangleGroup>();
            public Material[] Materials = new Material[4];
            public int MaterialCount;
            public Vector2 LightAnchor;
            public int LatticeX, LatticeY;

            // Phase two, any thread's: the vertices and, per material slot, the triangles from the
            // tile's first vertex.
            public Vector3[] Positions = new Vector3[256], Normals = new Vector3[256];
            public Vector2[] Uvs = new Vector2[256];
            public int VertexCount;
            public IndexList[] Triangles = new IndexList[4];
            public int[] PieceStarts = new int[8];

            public int SlotOf(Material material)
            {
                for (int i = 0; i < MaterialCount; i++)
                    if (ReferenceEquals(Materials[i], material)) return i;
                if (MaterialCount == Materials.Length)
                {
                    System.Array.Resize(ref Materials, MaterialCount * 2);
                    System.Array.Resize(ref Triangles, MaterialCount * 2);
                }
                Materials[MaterialCount] = material;
                Triangles[MaterialCount] ??= new IndexList();
                return MaterialCount++;
            }
        }

        private sealed class IndexList
        {
            public int[] Items = new int[64];
            public int Count;

            public void Add(int value)
            {
                if (Count == Items.Length) System.Array.Resize(ref Items, Count * 2);
                Items[Count++] = value;
            }
        }

        private struct PlacedPiece
        {
            public ClassicMeshes.PieceSource Piece;
            public int Rotation;
            public Vector3 At;
            public bool InvertNormals;
        }

        private struct TriangleGroup
        {
            public int Piece, Group, Slot;
        }

        private readonly TileGeometry[] _geometry;
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
            _width = width;
            _chunksX = (width + ChunkSize - 1) / ChunkSize;
            _tiles = new TileView[width * height];
            _drawn = new bool[width * height];
            _geometry = new TileGeometry[width * height];
            _stale = new bool[width * height];
        }

        /// <summary>Takes the tile when it has plain or room pieces; false leaves it to the placeholder.</summary>
        public bool TrySet(int tileIndex, in TileView tile)
        {
            // The build task reads the tiles and the lattice; a change waits for it (rarely: it is a
            // millisecond or two, started a frame or more before the next tick's tiles arrive).
            JoinBuild();
            bool draws = (!tile.Pieces.IsEmpty && tile.PieceKeys != null)
                || tile.RoomKeys != null || tile.BedKeys != null || !tile.SightFogOverlay.IsEmpty;
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
                        TileGeometry geometry = _geometry[index] ??= new TileGeometry();
                        Prepare(x, y, _tiles[index], geometry);
                        _lattice?.Prepare(x, y);
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
                foreach (int index in _building) Build(_geometry[index]);
            });
        }

        private void BuildInline()
        {
            PrepareStale();
            using (GeometryMarker.Auto())
                foreach (int index in _building) Build(_geometry[index]);
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

        /// <summary>Phase one of one tile: its pieces placed and their materials resolved, in the
        /// tile's own first-use order so the chunk's submeshes come out as the in-place build's did.
        /// Main thread: the materials are Unity objects, made on first use.</summary>
        private void Prepare(int x, int y, in TileView tile, TileGeometry geometry)
        {
            geometry.Pieces.Clear();
            geometry.Groups.Clear();
            geometry.MaterialCount = 0;
            geometry.LightAnchor = new Vector2(y * _width + x, 1);
            geometry.LatticeX = x;
            geometry.LatticeY = y;
            AddTile(geometry, x, y, tile);
            if (!tile.SightFogOverlay.IsEmpty)
                Add(geometry, _classic.Piece(tile.SightFogOverlay), 0, DungeonScene.Ground(x + .5f, y + .5f),
                    x, y, fogOpacity: tile.SightFogOpacity);
        }

        /// <summary>Phase two of one tile: the vertices turned, placed and bent, and the triangles
        /// gathered per material slot. Arrays and arithmetic only, so any thread may run it; the
        /// lattice cells it reads were built by <see cref="Prepare"/>.</summary>
        private void Build(TileGeometry geometry)
        {
            geometry.VertexCount = 0;
            for (int s = 0; s < geometry.MaterialCount; s++) geometry.Triangles[s].Count = 0;
            if (geometry.PieceStarts.Length < geometry.Pieces.Count) geometry.PieceStarts = new int[geometry.Pieces.Count * 2];
            int x = geometry.LatticeX, y = geometry.LatticeY;
            for (int p = 0; p < geometry.Pieces.Count; p++)
            {
                PlacedPiece placed = geometry.Pieces[p];
                ClassicMeshes.PieceSource piece = placed.Piece;
                Quaternion turn = Turns[placed.Rotation & 3];
                int start = geometry.VertexCount;
                geometry.PieceStarts[p] = start;
                int needed = start + piece.Positions.Length;
                if (geometry.Positions.Length < needed)
                {
                    int size = Mathf.Max(needed, geometry.Positions.Length * 2);
                    System.Array.Resize(ref geometry.Positions, size);
                    System.Array.Resize(ref geometry.Normals, size);
                    System.Array.Resize(ref geometry.Uvs, size);
                }
                for (int i = 0; i < piece.Positions.Length; i++)
                {
                    Vector3 v = (turn * piece.Positions[i]) + placed.At;
                    if (_lattice != null)
                    {
                        // DK2's frame is (x, -z, -y) of this one (DungeonScene.Ground, RenderMesh).
                        System.Numerics.Vector3 bent = _lattice.Deform(new System.Numerics.Vector3(v.x, -v.z, -v.y), x, y);
                        v = new Vector3(bent.X, -bent.Z, -bent.Y);
                    }
                    geometry.Positions[start + i] = v;
                    // Ordinary tops carry native drawflag0x80 (static-mesh-lighting.md section1).
                    Vector3 normal = placed.InvertNormals ? -piece.Normals[i] : piece.Normals[i];
                    geometry.Normals[start + i] = turn * normal;
                    geometry.Uvs[start + i] = piece.Uvs[i];
                }
                geometry.VertexCount = needed;
            }
            for (int g = 0; g < geometry.Groups.Count; g++)
            {
                TriangleGroup group = geometry.Groups[g];
                int start = geometry.PieceStarts[group.Piece];
                IndexList list = geometry.Triangles[group.Slot];
                foreach (int index in geometry.Pieces[group.Piece].Piece.Triangles[group.Group]) list.Add(start + index);
            }
        }

        private void Append(TileGeometry geometry)
        {
            if (geometry == null) return;
            int start = _positions.Count;
            for (int i = 0; i < geometry.VertexCount; i++)
            {
                _positions.Add(geometry.Positions[i]);
                _normals.Add(geometry.Normals[i]);
                _uvs.Add(geometry.Uvs[i]);
                _lightAnchors.Add(geometry.LightAnchor);
            }
            for (int s = 0; s < geometry.MaterialCount; s++)
            {
                if (!_byMaterial.TryGetValue(geometry.Materials[s], out List<int> list))
                    _byMaterial[geometry.Materials[s]] = list = new List<int>();
                IndexList triangles = geometry.Triangles[s];
                for (int i = 0; i < triangles.Count; i++) list.Add(start + triangles.Items[i]);
            }
        }

        private void AddTile(TileGeometry geometry, int x, int y, in TileView tile)
        {
            if (tile.BedKeys != null) AddBed(geometry, x, y, tile);
            if (tile.RoomKeys != null)
            {
                AddRoom(geometry, x, y, tile);
                return;
            }

            TerrainPieces pieces = tile.Pieces;
            TerrainPieceKeys keys = tile.PieceKeys;
            Vector3 centre = DungeonScene.Ground(x + 0.5f, y + 0.5f);

            // A Reinforced Wall's face takes the texture of its damage stage (Brick_DECAY3 to Brick,
            // tile.FaceTexture; Q617). A tagged tile tints every piece it draws (tag-highlight.md).
            bool tagged = tile.Tagged;
            for (int side = 0; side < 4; side++)
                if (pieces.HasFace(side)) Add(geometry, _classic.Piece(keys.Side, tile.FaceTexture), side, centre, x, y, tagged, tile.AmbientTint);
            if (pieces.HasTop) Add(geometry, _classic.Piece(keys.Top), 0, centre, x, y, tagged, tile.AmbientTint,
                invertLightingNormals: true);
            if (pieces.HasFloor) Add(geometry, _classic.Piece(keys.Floor), 0, centre, x, y, tagged, tile.AmbientTint);
            AddHeroFaces(geometry, x, y, tile, keys, centre, tagged);
            if (pieces.Quarters == QuarterKind.None) return;

            for (int q = 0; q < 4; q++)
            {
                Vector2 offset = QuadrantCentres[q];
                Add(geometry, _classic.Piece(keys.Quarter(pieces.QuarterSet, pieces.QuarterPiece(q))),
                    TerrainPieces.QuarterRotation(q), centre + new Vector3(offset.x, 0f, offset.y), x, y, tagged, tile.AmbientTint,
                    invertLightingNormals: pieces.Quarters == QuarterKind.Top);
            }
        }

        /// <summary>A room tile: its floor's quarters and whole piece, then each side's wall, whole
        /// at the centre or as two halves in the side's two quadrants, all turned to the side.</summary>
        private void AddRoom(TileGeometry geometry, int x, int y, in TileView tile)
        {
            RoomPieceKeys keys = tile.RoomKeys;
            Vector3 centre = DungeonScene.Ground(x + 0.5f, y + 0.5f);
            AddSet(geometry, tile.RoomFloor, keys, null, centre, x, y, tile.AmbientTint, tile.QuarterAmbientTint);
            if (!tile.ExtraRoomPiece.IsEmpty)
                Add(geometry, _classic.Piece(tile.ExtraRoomPiece), 0, centre, x, y, tint: tile.AmbientTint);

            // A wall facing a tagged tile is tinted with it (0x00444F10's per-piece flag, which
            // 0x0043C310 reads).
            RoomWalls walls = tile.Walls;
            for (int side = 0; side < 4; side++)
            {
                bool tagged = FacesTag(x, y, side);
                // Special gate walls have a separate native builder; its tint is not traced here.
                DK2.Data.Model.TerrainLightColour tint = walls.IsWhole(side) && walls.WholePiece(side) < 7
                    ? default : NeighbourTint(x, y, side);
                if (walls.IsWhole(side))
                    Add(geometry, _classic.Piece(keys.Piece(walls.WholePiece(side))), walls.WholeRotation(side), centre, x, y, tagged, tint);
                else if (walls.HasWall(side))
                {
                    Add(geometry, _classic.Piece(keys.Piece(walls.HalfAPiece(side))), side, Quadrant(centre, side), x, y, tagged, tint);
                    Add(geometry, _classic.Piece(keys.Piece(walls.HalfBPiece(side))), side, Quadrant(centre, (side + 1) & 3), x, y,
                        tagged, tint);
                }
            }
        }

        /// <summary>A hero-owned wall's faces, carried in <see cref="TileView.Walls"/>: a whole piece
        /// at the centre or two halves in the side's two quadrants, turned to the side, with the room
        /// scheme's halves 4 and 5 swapped (0x0043E860).</summary>
        private void AddHeroFaces(TileGeometry geometry, int x, int y, in TileView tile, TerrainPieceKeys keys, Vector3 centre, bool tagged)
        {
            RoomWalls faces = tile.Walls;
            if (faces.IsEmpty) return;
            for (int side = 0; side < 4; side++)
            {
                if (faces.IsWhole(side))
                    Add(geometry, _classic.Piece(keys.HeroFace(faces.WholePiece(side))), side, centre, x, y, tagged, tile.AmbientTint);
                else if (faces.HasWall(side))
                {
                    Add(geometry, _classic.Piece(keys.HeroFace(SwapHeroHalf(faces.HalfAPiece(side)))), side,
                        Quadrant(centre, side), x, y, tagged, tile.AmbientTint);
                    Add(geometry, _classic.Piece(keys.HeroFace(SwapHeroHalf(faces.HalfBPiece(side)))), side,
                        Quadrant(centre, (side + 1) & 3), x, y, tagged, tile.AmbientTint);
                }
            }
        }

        private static int SwapHeroHalf(int piece) => piece == 4 ? 5 : piece == 5 ? 4 : piece;

        /// <summary>A liquid tile's bed, or a bridge's. The surface is not drawn: DK2's is an alpha
        /// texture laid by code not yet read.</summary>
        private void AddBed(TileGeometry geometry, int x, int y, in TileView tile)
        {
            TerrainPieceKeys keys = tile.BedKeys;
            Vector3 centre = DungeonScene.Ground(x + 0.5f, y + 0.5f);
            AddSet(geometry, tile.Bed, null, keys, centre, x, y);
        }

        /// <summary>A piece list's quarters and whole piece, from a room's pieces or a liquid's bed.</summary>
        private void AddSet(TileGeometry geometry, PieceSet set, RoomPieceKeys room, TerrainPieceKeys bed, Vector3 centre, int x, int y,
            DK2.Data.Model.TerrainLightColour wholeTint = default,
            DK2.Data.Model.TerrainLightColour quarterTint = default)
        {
            if (set.HasQuarters)
                for (int q = 0; q < 4; q++)
                    if (set.HasQuarter(q))
                        Add(geometry, _classic.Piece(Key(room, bed, set.QuarterPiece(q))), set.QuarterRotation(q),
                            Quadrant(centre, q), x, y, tint: quarterTint);
            if (set.HasWhole) Add(geometry, _classic.Piece(Key(room, bed, set.WholePiece)), set.WholeRotation, centre, x, y,
                tint: wholeTint);
        }

        private static readonly int[] SideDx = { 0, 1, 0, -1 }, SideDy = { -1, 0, 1, 0 };

        private bool FacesTag(int x, int y, int side)
        {
            int nx = x + SideDx[side], ny = y + SideDy[side];
            return nx >= 0 && ny >= 0 && nx < _width && ny < _tiles.Length / _width && _tiles[(ny * _width) + nx].Tagged;
        }

        private DK2.Data.Model.TerrainLightColour NeighbourTint(int x, int y, int side)
        {
            int nx = x + SideDx[side], ny = y + SideDy[side];
            return nx >= 0 && ny >= 0 && nx < _width && ny < _tiles.Length / _width
                ? _tiles[ny * _width + nx].AmbientTint : default;
        }

        private static AssetRef Key(RoomPieceKeys room, TerrainPieceKeys bed, int k) =>
            room != null ? room.Piece(k) : bed.Bed(k);

        private static Vector3 Quadrant(Vector3 centre, int quadrant) =>
            centre + new Vector3(QuadrantCentres[quadrant].x, 0f, QuadrantCentres[quadrant].y);

        /// <summary>Places one piece and resolves the material of each of its triangle groups: the
        /// sight fog's faded twin, the tag's or the red highlight's lit twin, or the terrain light's
        /// tinted twin; a tagged piece draws a second time, additively (tag-highlight.md).</summary>
        private void Add(TileGeometry geometry, ClassicMeshes.PieceSource piece, int rotation, Vector3 at, int x, int y, bool tagged = false,
            DK2.Data.Model.TerrainLightColour tint = default, byte? fogOpacity = null, bool invertLightingNormals = false)
        {
            if (piece == null) return;
            bool red = _tiles[y * _width + x].RedHighlighted;
            int placed = geometry.Pieces.Count;
            geometry.Pieces.Add(new PlacedPiece { Piece = piece, Rotation = rotation, At = at, InvertNormals = invertLightingNormals });
            for (int m = 0; m < piece.Triangles.Count; m++)
            {
                Material material = fogOpacity is byte opacity ? _classic.SightFog(piece.Materials[m], opacity)
                    : red ? _classic.Tagged(piece.Materials[m], red: true)
                    : tagged ? _classic.Tagged(piece.Materials[m])
                    : _classic.TerrainTinted(piece.Materials[m], tint);
                geometry.Groups.Add(new TriangleGroup { Piece = placed, Group = m, Slot = geometry.SlotOf(material) });
                if (red || tagged)
                    geometry.Groups.Add(new TriangleGroup { Piece = placed, Group = m, Slot = geometry.SlotOf(_classic.TagOverlay(red)) });
            }
        }

        /// <summary>One piece's emitted triangles per material, for the editor's tag material probe
        /// (reflection): both phases of a one-tile build with the piece alone.</summary>
        private Dictionary<Material, List<int>> ProbeEmitted(ClassicMeshes.PieceSource piece, bool tagged,
            DK2.Data.Model.TerrainLightColour tint)
        {
            var geometry = new TileGeometry();
            Add(geometry, piece, 0, Vector3.zero, 0, 0, tagged, tint);
            Build(geometry);
            var emitted = new Dictionary<Material, List<int>>();
            for (int s = 0; s < geometry.MaterialCount; s++)
            {
                var list = new List<int>();
                for (int i = 0; i < geometry.Triangles[s].Count; i++) list.Add(geometry.Triangles[s].Items[i]);
                emitted[geometry.Materials[s]] = list;
            }
            return emitted;
        }
    }
}
