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
    /// The classic look's terrain (plan steps 1 and 2): plain <see cref="TerrainPieces"/>, a room's
    /// floor and <see cref="RoomWalls"/>, and a liquid's bed, merged per 16×16-tile chunk.
    /// </summary>
    /// <remarks>
    /// A piece is turned a quarter per step about its origin, clockwise from above (side N 0 to E 1,
    /// as DK2's side table runs), then moved to the tile's centre and, for a quarter, its quadrant's.
    /// Heights are the models' own: floor 1, walls 1 to 2 (ADR-0411). A <see cref="TerrainLattice"/>
    /// then bends each vertex as DK2 does, keeping the straight normals (terrain-lattice.md §1); a
    /// change rebuilds the 5×5 tiles' chunks around it, where DK2 rebuilds the 3×3.
    /// </remarks>
    public sealed class TerrainChunks
    {
        private const int ChunkSize = 16;

        private static readonly ProfilerMarker RebuildMarker = new ProfilerMarker("DK2.TerrainChunks.Rebuild");
        private static readonly ProfilerMarker GeometryMarker = new ProfilerMarker("DK2.TerrainChunks.Geometry");
        private static readonly ProfilerMarker UploadMarker = new ProfilerMarker("DK2.TerrainChunks.Upload");

        // Quadrant centres NW, NE, SE, SW in renderer X (east) and Z (north).
        private static readonly Vector2[] QuadrantCentres =
        {
            new Vector2(-0.25f, 0.25f), new Vector2(0.25f, 0.25f),
            new Vector2(0.25f, -0.25f), new Vector2(-0.25f, -0.25f),
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
        private readonly System.Diagnostics.Stopwatch _flushClock = new System.Diagnostics.Stopwatch();

        /// <summary>How long one <see cref="Flush"/> may spend rebuilding chunks before it leaves the
        /// rest for the next frame, in milliseconds; at least one chunk is rebuilt per flush, and the
        /// first build after a <see cref="Clear"/> takes every chunk. A tag wave that touches many
        /// chunks then costs a few frames of a few milliseconds instead of one frame of a hundred,
        /// and a chunk not yet rebuilt shows its previous mesh until its turn (ADR-0457). Zero or
        /// less rebuilds everything at once.</summary>
        public double FlushBudgetMilliseconds { get; set; } = 4.0;

        /// <summary>How many chunks the last <see cref="Flush"/> left for a later one.</summary>
        public int PendingChunks => _dirty.Count;

        // Each drawn tile's pieces as AddTile last built them. A rebuild regenerates only the tiles
        // whose inputs changed (_stale) and copies the rest, so a dig costs its 5x5 window rather
        // than every tile of the chunks that window touches. The chunk comes out the same, vertex
        // for vertex and triangle for triangle.
        private sealed class TileGeometry
        {
            public Vector3[] Positions, Normals;
            public Vector2[] Uvs;
            public Material[] Materials;
            public int[][] Triangles; // per material, indexed from the tile's first vertex
        }

        private readonly TileGeometry[] _geometry;
        private readonly bool[] _stale;

        // One tile's build, reused so a tile costs no list allocations beyond its own arrays.
        private readonly List<Vector3> _tilePositions = new List<Vector3>();
        private readonly List<Vector3> _tileNormals = new List<Vector3>();
        private readonly List<Vector2> _tileUvs = new List<Vector2>();
        private readonly Dictionary<Material, List<int>> _tileByMaterial = new Dictionary<Material, List<int>>();

        // One chunk's assembly, reused across rebuilds.
        private readonly List<Vector3> _positions = new List<Vector3>();
        private readonly List<Vector3> _normals = new List<Vector3>();
        private readonly List<Vector2> _uvs = new List<Vector2>();
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
            bool draws = (!tile.Pieces.IsEmpty && tile.PieceKeys != null)
                || tile.RoomKeys != null || tile.BedKeys != null;
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

        /// <summary>Rebuilds the chunks a tile changed in since the last flush, lowest chunk first,
        /// within <see cref="FlushBudgetMilliseconds"/>; what does not fit stays dirty for the next
        /// flush. Everything is rebuilt when nothing is drawn yet, so a level appears whole.</summary>
        public void Flush()
        {
            if (_dirty.Count == 0) return;
            _dirtyOrder.Clear();
            _dirtyOrder.AddRange(_dirty);
            _dirtyOrder.Sort();
            bool budgeted = FlushBudgetMilliseconds > 0 && _chunks.Count > 0;
            _flushClock.Restart();
            foreach (int chunk in _dirtyOrder)
            {
                if (budgeted && _dirty.Count < _dirtyOrder.Count && _flushClock.Elapsed.TotalMilliseconds >= FlushBudgetMilliseconds)
                    break;
                using (RebuildMarker.Auto())
                    Rebuild(chunk);
                _dirty.Remove(chunk);
            }
        }

        public void Clear()
        {
            foreach (GameObject go in _chunks.Values) DestroyChunk(go);
            _chunks.Clear();
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
        private static void DestroyChunk(GameObject go)
        {
            if (go == null) return;
            Object.Destroy(go.GetComponent<MeshFilter>().sharedMesh);
            Object.Destroy(go);
        }

        private int ChunkOf(int tileIndex) =>
            (tileIndex / _width / ChunkSize * _chunksX) + (tileIndex % _width / ChunkSize);

        private void Rebuild(int chunk)
        {
            _positions.Clear();
            _normals.Clear();
            _uvs.Clear();
            foreach (List<int> list in _byMaterial.Values) list.Clear();

            int height = _tiles.Length / _width;
            int x0 = chunk % _chunksX * ChunkSize, y0 = chunk / _chunksX * ChunkSize;
            for (int y = y0; y < Mathf.Min(y0 + ChunkSize, height); y++)
                for (int x = x0; x < Mathf.Min(x0 + ChunkSize, _width); x++)
                {
                    int index = (y * _width) + x;
                    if (!_drawn[index])
                    {
                        _geometry[index] = null;
                        _stale[index] = false;
                        continue;
                    }
                    if (_stale[index] || _geometry[index] == null)
                    {
                        using (GeometryMarker.Auto()) _geometry[index] = BuildTile(x, y, _tiles[index]);
                        _stale[index] = false;
                    }
                    Append(_geometry[index]);
                }

            _transparent.SetChunk(chunk, _positions, _normals, _uvs, _byMaterial);
            _chunks.TryGetValue(chunk, out GameObject go);
            if (_positions.Count == 0)
            {
                DestroyChunk(go);
                _chunks.Remove(chunk);
                return;
            }

            using ProfilerMarker.AutoScope upload = UploadMarker.Auto();
            var mesh = new Mesh
            {
                name = $"terrain chunk {chunk}",
                indexFormat = _positions.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16,
            };
            mesh.SetVertices(_positions);
            mesh.SetNormals(_normals);
            mesh.SetUVs(0, _uvs);
            _opaque.Clear();
            foreach (KeyValuePair<Material, List<int>> entry in _byMaterial)
                if (entry.Value.Count > 0 && entry.Key.renderQueue < (int)RenderQueue.Transparent) _opaque.Add(entry.Key);
            mesh.subMeshCount = _opaque.Count;
            for (int i = 0; i < _opaque.Count; i++)
                mesh.SetTriangles(_byMaterial[_opaque[i]], i, calculateBounds: false);
            mesh.RecalculateBounds();

            // The chunk keeps its object; its previous mesh is destroyed rather than left to leak.
            if (go == null)
            {
                go = new GameObject(mesh.name);
                go.transform.SetParent(_root, worldPositionStays: false);
                go.AddComponent<MeshFilter>();
                go.AddComponent<MeshRenderer>();
                _chunks[chunk] = go;
            }
            var filter = go.GetComponent<MeshFilter>();
            if (filter.sharedMesh != null) Object.Destroy(filter.sharedMesh);
            filter.sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterials = _opaque.ToArray();
        }

        /// <summary>One tile's pieces, built as the chunk used to build them in place.</summary>
        private TileGeometry BuildTile(int x, int y, in TileView tile)
        {
            _tilePositions.Clear();
            _tileNormals.Clear();
            _tileUvs.Clear();
            foreach (List<int> list in _tileByMaterial.Values) list.Clear();
            _tileMaterialOrder.Clear();
            _tileMaterialSeen.Clear();
            AddTile(x, y, tile);

            // In the tile's own first-use order, so Append meets materials in the order the old
            // in-place build did and the chunk's submeshes come out in the same order.
            var geometry = new TileGeometry
            {
                Positions = _tilePositions.ToArray(), Normals = _tileNormals.ToArray(), Uvs = _tileUvs.ToArray(),
                Materials = _tileMaterialOrder.ToArray(), Triangles = new int[_tileMaterialOrder.Count][],
            };
            for (int m = 0; m < _tileMaterialOrder.Count; m++)
                geometry.Triangles[m] = _tileByMaterial[_tileMaterialOrder[m]].ToArray();
            return geometry;
        }

        private readonly List<Material> _tileMaterialOrder = new List<Material>();
        private readonly HashSet<Material> _tileMaterialSeen = new HashSet<Material>();

        private void Append(TileGeometry geometry)
        {
            int start = _positions.Count;
            _positions.AddRange(geometry.Positions);
            _normals.AddRange(geometry.Normals);
            _uvs.AddRange(geometry.Uvs);
            for (int m = 0; m < geometry.Materials.Length; m++)
            {
                if (!_byMaterial.TryGetValue(geometry.Materials[m], out List<int> list))
                    _byMaterial[geometry.Materials[m]] = list = new List<int>();
                foreach (int index in geometry.Triangles[m]) list.Add(start + index);
            }
        }

        private void AddTile(int x, int y, in TileView tile)
        {
            if (tile.BedKeys != null) AddBed(x, y, tile);
            if (tile.RoomKeys != null)
            {
                AddRoom(x, y, tile);
                return;
            }

            TerrainPieces pieces = tile.Pieces;
            TerrainPieceKeys keys = tile.PieceKeys;
            Vector3 centre = DungeonScene.Ground(x + 0.5f, y + 0.5f);

            // A Reinforced Wall's face (the one player-coloured wall) takes its plain brick. A tagged
            // tile tints every piece it draws (tag-highlight.md).
            bool undamaged = pieces.Quarters == QuarterKind.Top;
            bool tagged = tile.Tagged;
            for (int side = 0; side < 4; side++)
                if (pieces.HasFace(side)) Add(_classic.Piece(keys.Side, undamaged), side, centre, x, y, tagged);
            if (pieces.HasTop) Add(_classic.Piece(keys.Top), 0, centre, x, y, tagged);
            if (pieces.HasFloor) Add(_classic.Piece(keys.Floor), 0, centre, x, y, tagged);
            if (pieces.Quarters == QuarterKind.None) return;

            for (int q = 0; q < 4; q++)
            {
                Vector2 offset = QuadrantCentres[q];
                Add(_classic.Piece(keys.Quarter(pieces.QuarterSet, pieces.QuarterPiece(q))),
                    TerrainPieces.QuarterRotation(q), centre + new Vector3(offset.x, 0f, offset.y), x, y, tagged);
            }
        }

        /// <summary>A room tile: its floor's quarters and whole piece, then each side's wall, whole
        /// at the centre or as two halves in the side's two quadrants, all turned to the side.</summary>
        private void AddRoom(int x, int y, in TileView tile)
        {
            RoomPieceKeys keys = tile.RoomKeys;
            Vector3 centre = DungeonScene.Ground(x + 0.5f, y + 0.5f);
            AddSet(tile.RoomFloor, keys, null, centre, x, y, tile.AmbientTint);

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
                    Add(_classic.Piece(keys.Piece(walls.WholePiece(side))), walls.WholeRotation(side), centre, x, y, tagged, tint);
                else if (walls.HasWall(side))
                {
                    Add(_classic.Piece(keys.Piece(walls.HalfAPiece(side))), side, Quadrant(centre, side), x, y, tagged, tint);
                    Add(_classic.Piece(keys.Piece(walls.HalfBPiece(side))), side, Quadrant(centre, (side + 1) & 3), x, y,
                        tagged, tint);
                }
            }
        }

        /// <summary>A liquid tile's bed, or a bridge's. The surface is not drawn: DK2's is an alpha
        /// texture laid by code not yet read.</summary>
        private void AddBed(int x, int y, in TileView tile)
        {
            TerrainPieceKeys keys = tile.BedKeys;
            Vector3 centre = DungeonScene.Ground(x + 0.5f, y + 0.5f);
            AddSet(tile.Bed, null, keys, centre, x, y);
        }

        /// <summary>A piece list's quarters and whole piece, from a room's pieces or a liquid's bed.</summary>
        private void AddSet(PieceSet set, RoomPieceKeys room, TerrainPieceKeys bed, Vector3 centre, int x, int y,
            DK2.Data.Model.TerrainLightColour wholeTint = default)
        {
            if (set.HasQuarters)
                for (int q = 0; q < 4; q++)
                    if (set.HasQuarter(q))
                        Add(_classic.Piece(Key(room, bed, set.QuarterPiece(q))), set.QuarterRotation(q),
                            Quadrant(centre, q), x, y);
            if (set.HasWhole) Add(_classic.Piece(Key(room, bed, set.WholePiece)), set.WholeRotation, centre, x, y,
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

        private void Add(ClassicMeshes.PieceSource piece, int rotation, Vector3 at, int x, int y, bool tagged = false,
            DK2.Data.Model.TerrainLightColour tint = default)
        {
            if (piece == null) return;
            Quaternion turn = Quaternion.Euler(0f, 90f * rotation, 0f);
            int start = _tilePositions.Count;
            for (int i = 0; i < piece.Positions.Length; i++)
            {
                Vector3 v = (turn * piece.Positions[i]) + at;
                if (_lattice != null)
                {
                    // DK2's frame is (x, -z, -y) of this one (DungeonScene.Ground, RenderMesh).
                    System.Numerics.Vector3 bent = _lattice.Deform(new System.Numerics.Vector3(v.x, -v.z, -v.y), x, y);
                    v = new Vector3(bent.X, -bent.Z, -bent.Y);
                }
                _tilePositions.Add(v);
                _tileNormals.Add(turn * piece.Normals[i]);
                _tileUvs.Add(piece.Uvs[i]);
            }
            for (int m = 0; m < piece.Triangles.Count; m++)
            {
                Material material = tagged ? _classic.Tagged(piece.Materials[m])
                    : _classic.TerrainTinted(piece.Materials[m], tint);
                AddTriangles(material, piece.Triangles[m], start);
                // DK2 draws a tagged piece a second time, additively (tag-highlight.md).
                if (tagged) AddTriangles(_classic.TagOverlay(), piece.Triangles[m], start);
            }
        }

        private void AddTriangles(Material material, int[] triangles, int start)
        {
            if (!_tileByMaterial.TryGetValue(material, out List<int> list))
                _tileByMaterial[material] = list = new List<int>();
            if (_tileMaterialSeen.Add(material)) _tileMaterialOrder.Add(material);
            foreach (int index in triangles) list.Add(start + index);
        }
    }
}
