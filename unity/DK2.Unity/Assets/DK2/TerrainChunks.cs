// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Collections.Generic;
using DK2.Assets;
using DK2.Presentation;
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

        // Quadrant centres NW, NE, SE, SW in renderer X (east) and Z (north).
        private static readonly Vector2[] QuadrantCentres =
        {
            new Vector2(-0.25f, 0.25f), new Vector2(0.25f, 0.25f),
            new Vector2(0.25f, -0.25f), new Vector2(-0.25f, -0.25f),
        };

        private readonly Transform _root;
        private readonly ClassicMeshes _classic;
        private readonly TerrainLattice _lattice;
        private readonly int _width;
        private readonly int _chunksX;
        private readonly TileView[] _tiles;
        private readonly bool[] _drawn;
        private readonly HashSet<int> _dirty = new HashSet<int>();
        private readonly Dictionary<int, GameObject> _chunks = new Dictionary<int, GameObject>();

        // Reused across rebuilds so a chunk costs no list allocations.
        private readonly List<Vector3> _positions = new List<Vector3>();
        private readonly List<Vector3> _normals = new List<Vector3>();
        private readonly List<Vector2> _uvs = new List<Vector2>();
        private readonly Dictionary<Material, List<int>> _byMaterial = new Dictionary<Material, List<int>>();

        public TerrainChunks(Transform root, ClassicMeshes classic, int width, int height,
            TerrainLattice lattice = null)
        {
            _root = root;
            _classic = classic;
            _lattice = lattice;
            _width = width;
            _chunksX = (width + ChunkSize - 1) / ChunkSize;
            _tiles = new TileView[width * height];
            _drawn = new bool[width * height];
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
                        _dirty.Add(ChunkOf((ty * _width) + tx));
            }
            else if (draws || _drawn[tileIndex]) _dirty.Add(ChunkOf(tileIndex));
            _tiles[tileIndex] = tile;
            _drawn[tileIndex] = draws;
            return draws;
        }

        /// <summary>Rebuilds every chunk a tile changed in since the last flush.</summary>
        public void Flush()
        {
            foreach (int chunk in _dirty) Rebuild(chunk);
            _dirty.Clear();
        }

        public void Clear()
        {
            foreach (GameObject go in _chunks.Values) if (go != null) Object.Destroy(go);
            _chunks.Clear();
            _dirty.Clear();
            System.Array.Clear(_drawn, 0, _drawn.Length);
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
                    if (_drawn[(y * _width) + x]) AddTile(x, y, _tiles[(y * _width) + x]);

            if (_chunks.TryGetValue(chunk, out GameObject old) && old != null) Object.Destroy(old);
            _chunks.Remove(chunk);
            if (_positions.Count == 0) return;

            var mesh = new Mesh
            {
                name = $"terrain chunk {chunk}",
                indexFormat = _positions.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16,
            };
            mesh.SetVertices(_positions);
            mesh.SetNormals(_normals);
            mesh.SetUVs(0, _uvs);
            var materials = new List<Material>();
            foreach (KeyValuePair<Material, List<int>> entry in _byMaterial)
                if (entry.Value.Count > 0) materials.Add(entry.Key);
            mesh.subMeshCount = materials.Count;
            for (int i = 0; i < materials.Count; i++)
                mesh.SetTriangles(_byMaterial[materials[i]], i, calculateBounds: false);
            mesh.RecalculateBounds();

            var go = new GameObject(mesh.name);
            go.transform.SetParent(_root, worldPositionStays: false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = materials.ToArray();
            _chunks[chunk] = go;
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

            // A Reinforced Wall's face (the one player-coloured wall) takes its plain brick.
            bool undamaged = pieces.Quarters == QuarterKind.Top;
            for (int side = 0; side < 4; side++)
                if (pieces.HasFace(side)) Add(_classic.Piece(keys.Side, undamaged), side, centre, x, y);
            if (pieces.HasTop) Add(_classic.Piece(pieces.TopTagged ? keys.TopTagged : keys.Top), 0, centre, x, y);
            if (pieces.HasFloor) Add(_classic.Piece(keys.Floor), 0, centre, x, y);
            if (pieces.Quarters == QuarterKind.None) return;

            for (int q = 0; q < 4; q++)
            {
                Vector2 offset = QuadrantCentres[q];
                Add(_classic.Piece(keys.Quarter(pieces.QuarterSet, pieces.QuarterPiece(q))),
                    TerrainPieces.QuarterRotation(q), centre + new Vector3(offset.x, 0f, offset.y), x, y);
            }
        }

        /// <summary>A room tile: its floor's quarters and whole piece, then each side's wall, whole
        /// at the centre or as two halves in the side's two quadrants, all turned to the side.</summary>
        private void AddRoom(int x, int y, in TileView tile)
        {
            RoomPieceKeys keys = tile.RoomKeys;
            Vector3 centre = DungeonScene.Ground(x + 0.5f, y + 0.5f);
            AddSet(tile.RoomFloor, keys, null, centre, x, y);

            RoomWalls walls = tile.Walls;
            for (int side = 0; side < 4; side++)
            {
                if (walls.IsWhole(side))
                    Add(_classic.Piece(keys.Piece(walls.WholePiece(side))), side, centre, x, y);
                else if (walls.HasWall(side))
                {
                    Add(_classic.Piece(keys.Piece(walls.HalfAPiece(side))), side, Quadrant(centre, side), x, y);
                    Add(_classic.Piece(keys.Piece(walls.HalfBPiece(side))), side, Quadrant(centre, (side + 1) & 3), x, y);
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
        private void AddSet(PieceSet set, RoomPieceKeys room, TerrainPieceKeys bed, Vector3 centre, int x, int y)
        {
            if (set.HasQuarters)
                for (int q = 0; q < 4; q++)
                    if (set.HasQuarter(q))
                        Add(_classic.Piece(Key(room, bed, set.QuarterPiece(q))), set.QuarterRotation(q),
                            Quadrant(centre, q), x, y);
            if (set.HasWhole) Add(_classic.Piece(Key(room, bed, set.WholePiece)), set.WholeRotation, centre, x, y);
        }

        private static AssetRef Key(RoomPieceKeys room, TerrainPieceKeys bed, int k) =>
            room != null ? room.Piece(k) : bed.Bed(k);

        private static Vector3 Quadrant(Vector3 centre, int quadrant) =>
            centre + new Vector3(QuadrantCentres[quadrant].x, 0f, QuadrantCentres[quadrant].y);

        private void Add(ClassicMeshes.PieceSource piece, int rotation, Vector3 at, int x, int y)
        {
            if (piece == null) return;
            Quaternion turn = Quaternion.Euler(0f, 90f * rotation, 0f);
            int start = _positions.Count;
            for (int i = 0; i < piece.Positions.Length; i++)
            {
                Vector3 v = (turn * piece.Positions[i]) + at;
                if (_lattice != null)
                {
                    // DK2's frame is (x, -z, -y) of this one (DungeonScene.Ground, RenderMesh).
                    System.Numerics.Vector3 bent = _lattice.Deform(new System.Numerics.Vector3(v.x, -v.z, -v.y), x, y);
                    v = new Vector3(bent.X, -bent.Z, -bent.Y);
                }
                _positions.Add(v);
                _normals.Add(turn * piece.Normals[i]);
                _uvs.Add(piece.Uvs[i]);
            }
            for (int m = 0; m < piece.Triangles.Count; m++)
            {
                if (!_byMaterial.TryGetValue(piece.Materials[m], out List<int> list))
                    _byMaterial[piece.Materials[m]] = list = new List<int>();
                foreach (int index in piece.Triangles[m]) list.Add(start + index);
            }
        }
    }
}
