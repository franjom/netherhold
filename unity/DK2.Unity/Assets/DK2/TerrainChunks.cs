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
    /// The classic look's plain terrain (step 1 of docs/plans/2026-10-09-classic-look.md): each
    /// tile's <see cref="TerrainPieces"/> drawn from the install, merged into one mesh per
    /// 16×16-tile chunk with a submesh per material, and only a changed tile's chunk rebuilt.
    /// </summary>
    /// <remarks>
    /// A piece is rotated a quarter turn per step about its own origin, clockwise seen from above
    /// (side N 0 to E 1, as <see cref="RoomWalls"/> and DK2's side table run), then moved to the
    /// tile's centre and, for a quarter, to its quadrant's centre. Heights are the models' own:
    /// the floor at 1, a wall from 1 to 2 (ADR-0411).
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

        public TerrainChunks(Transform root, ClassicMeshes classic, int width, int height)
        {
            _root = root;
            _classic = classic;
            _width = width;
            _chunksX = (width + ChunkSize - 1) / ChunkSize;
            _tiles = new TileView[width * height];
            _drawn = new bool[width * height];
        }

        /// <summary>Takes the tile when it has plain pieces; false leaves it to the placeholder.</summary>
        public bool TrySet(int tileIndex, in TileView tile)
        {
            bool draws = !tile.Pieces.IsEmpty && tile.PieceKeys != null;
            if (draws || _drawn[tileIndex]) _dirty.Add(ChunkOf(tileIndex));
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
            TerrainPieces pieces = tile.Pieces;
            TerrainPieceKeys keys = tile.PieceKeys;
            Vector3 centre = DungeonScene.Ground(x + 0.5f, y + 0.5f);

            // A Reinforced Wall's face (the one player-coloured wall) takes its plain brick.
            bool undamaged = pieces.Quarters == QuarterKind.Top;
            for (int side = 0; side < 4; side++)
                if (pieces.HasFace(side)) Add(_classic.Piece(keys.Side, undamaged), side, centre);
            if (pieces.HasTop) Add(_classic.Piece(pieces.TopTagged ? keys.TopTagged : keys.Top), 0, centre);
            if (pieces.HasFloor) Add(_classic.Piece(keys.Floor), 0, centre);
            if (pieces.Quarters == QuarterKind.None) return;

            for (int q = 0; q < 4; q++)
            {
                Vector2 offset = QuadrantCentres[q];
                Add(_classic.Piece(keys.Quarter(pieces.QuarterSet, pieces.QuarterPiece(q))),
                    TerrainPieces.QuarterRotation(q), centre + new Vector3(offset.x, 0f, offset.y));
            }
        }

        private void Add(ClassicMeshes.PieceSource piece, int rotation, Vector3 at)
        {
            if (piece == null) return;
            Quaternion turn = Quaternion.Euler(0f, 90f * rotation, 0f);
            int start = _positions.Count;
            for (int i = 0; i < piece.Positions.Length; i++)
            {
                _positions.Add((turn * piece.Positions[i]) + at);
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
