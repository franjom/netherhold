// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Collections.Generic;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace DK2.Unity
{
    /// <summary>
    /// The possession view's roof, drawn: <see cref="TerrainCeilingField"/>'s pieces merged per
    /// 16×16-tile chunk, with the one <c>Ceiling</c> material and the terrain's vertex lighting.
    /// Every rule is the field's (ADR-0805); this turns its points into meshes, shows them while
    /// the possession camera holds the view, and rebuilds the chunks a change reaches.
    /// </summary>
    public sealed class TerrainCeilingChunks
    {
        private const int ChunkSize = 16;
        // The shader's tile anchor kind for a roof vertex: lit from its tile's light table, and
        // given none of Unity's fill, so it is black where no DK2 light reaches.
        private const float RoofAnchor = 2f;
        private const MeshUpdateFlags UploadFlags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices
            | MeshUpdateFlags.DontNotifyMeshUsers;

        private static readonly ProfilerMarker RebuildMarker = new ProfilerMarker("DK2.TerrainCeiling.Rebuild");

        private readonly GameObject _root;
        private readonly ClassicMeshes _classic;
        private readonly TerrainLattice _lattice;
        private readonly TerrainCeilingField _field;
        private readonly int _width, _height, _chunksX;
        private readonly bool[] _dirty;
        private readonly GameObject[] _chunks;
        private readonly Mesh[] _meshes;
        private readonly List<int> _changed = new List<int>();
        private bool _anyDirty = true;
        private long _readTick = long.MinValue;
        private Material _material;

        // One piece from the field, and one chunk's assembly, reused across rebuilds.
        private readonly System.Numerics.Vector3[] _piecePositions = new System.Numerics.Vector3[TerrainCeiling.Vertices];
        private readonly System.Numerics.Vector3[] _pieceNormals = new System.Numerics.Vector3[TerrainCeiling.Vertices];
        private readonly List<Vector3> _positions = new List<Vector3>();
        private readonly List<Vector3> _normals = new List<Vector3>();
        private readonly List<Vector2> _uvs = new List<Vector2>();
        private readonly List<Vector2> _lightAnchors = new List<Vector2>();
        private readonly List<Vector4> _staticLightInputs = new List<Vector4>();
        private readonly List<int> _triangles = new List<int>();

        public TerrainCeilingChunks(Transform root, ClassicMeshes classic, int width, int height, TerrainLattice lattice = null)
        {
            _root = new GameObject("terrain ceiling");
            _root.transform.SetParent(root, worldPositionStays: false);
            _root.SetActive(false);
            _classic = classic;
            _lattice = lattice;
            _field = new TerrainCeilingField(width, height);
            _width = width;
            _height = height;
            _chunksX = (width + ChunkSize - 1) / ChunkSize;
            int chunks = _chunksX * ((height + ChunkSize - 1) / ChunkSize);
            _dirty = new bool[chunks];
            _chunks = new GameObject[chunks];
            _meshes = new Mesh[chunks];
            System.Array.Fill(_dirty, true);
        }

        /// <summary>A presented tile changed, which may have changed the lattice around it.</summary>
        public void TileChanged(int x, int y) => MarkDirty(x, y, TerrainCeiling.BendReach);

        /// <summary>Shows the roof over the presented world, rebuilding what changed since it was
        /// last shown: the first show builds every chunk. Call where the world may be read.</summary>
        public void Show(WorldState world, GameDatabase database)
        {
            long tick = world.CurrentTick.Value;
            if (tick != _readTick)
            {
                _readTick = tick;
                _changed.Clear();
                _field.Refresh(world, database, _changed);
                for (int i = 0; i < _changed.Count; i++)
                    MarkDirty(_changed[i] % _width, _changed[i] / _width, TerrainCeiling.ChangeReach);
            }
            if (_anyDirty)
            {
                using ProfilerMarker.AutoScope _ = RebuildMarker.Auto();
                _material ??= _classic.ForTexture(TerrainCeiling.TextureName);
                for (int chunk = 0; chunk < _dirty.Length; chunk++)
                    if (_dirty[chunk]) Rebuild(chunk);
                _anyDirty = false;
            }
            if (!_root.activeSelf) _root.SetActive(true);
        }

        public void Hide()
        {
            if (_root.activeSelf) _root.SetActive(false);
        }

        public void Clear()
        {
            for (int chunk = 0; chunk < _chunks.Length; chunk++)
            {
                if (_chunks[chunk] != null) Object.Destroy(_chunks[chunk]);
                if (_meshes[chunk] != null) Object.Destroy(_meshes[chunk]);
                _chunks[chunk] = null;
                _meshes[chunk] = null;
            }
            System.Array.Fill(_dirty, true);
            _anyDirty = true;
            _readTick = long.MinValue;
        }

        private void MarkDirty(int x, int y, int reach)
        {
            int x0 = Mathf.Max(0, x - reach) / ChunkSize, x1 = Mathf.Min(_width - 1, x + reach) / ChunkSize;
            int y0 = Mathf.Max(0, y - reach) / ChunkSize, y1 = Mathf.Min(_height - 1, y + reach) / ChunkSize;
            for (int cy = y0; cy <= y1; cy++)
                for (int cx = x0; cx <= x1; cx++)
                    _dirty[(cy * _chunksX) + cx] = true;
            _anyDirty = true;
        }

        private void Rebuild(int chunk)
        {
            _dirty[chunk] = false;
            _positions.Clear();
            _normals.Clear();
            _uvs.Clear();
            _lightAnchors.Clear();
            _triangles.Clear();

            System.ReadOnlySpan<byte> triangles = TerrainCeiling.Triangles;
            int x0 = chunk % _chunksX * ChunkSize, y0 = chunk / _chunksX * ChunkSize;
            for (int y = y0; y < Mathf.Min(y0 + ChunkSize, _height); y++)
                for (int x = x0; x < Mathf.Min(x0 + ChunkSize, _width); x++)
                {
                    if (!_field.Has(x, y)) continue;
                    _field.Piece(x, y, _lattice, _piecePositions, _pieceNormals);
                    int start = _positions.Count;
                    var anchor = new Vector2((y * _width) + x, RoofAnchor);
                    for (int i = 0; i < TerrainCeiling.Vertices; i++)
                    {
                        // DK2's frame is (x, -z, -y) of this one (DungeonScene.Ground).
                        System.Numerics.Vector3 p = _piecePositions[i], n = _pieceNormals[i];
                        _positions.Add(new Vector3(p.X, -p.Z, -p.Y));
                        _normals.Add(new Vector3(n.X, -n.Z, -n.Y));
                        (float u, float v) = TerrainCeiling.Uv(i / 3, i % 3);
                        _uvs.Add(new Vector2(u, v));
                        _lightAnchors.Add(anchor);
                    }
                    // DK2's order as it is: in this frame it turns clockwise seen from below, the
                    // side Unity's back-face culling keeps.
                    for (int i = 0; i < triangles.Length; i++) _triangles.Add(start + triangles[i]);
                }

            GameObject go = _chunks[chunk];
            if (_positions.Count == 0)
            {
                if (go != null) go.SetActive(false);
                return;
            }

            Mesh mesh = _meshes[chunk];
            if (mesh == null)
            {
                mesh = new Mesh { name = $"terrain ceiling chunk {chunk}" };
                mesh.MarkDynamic();
                _meshes[chunk] = mesh;
            }
            mesh.Clear(keepVertexLayout: true);
            mesh.SetVertices(_positions, 0, _positions.Count, UploadFlags);
            mesh.SetNormals(_normals, 0, _normals.Count, UploadFlags);
            mesh.SetUVs(0, _uvs, 0, _uvs.Count, UploadFlags);
            mesh.SetUVs(1, _lightAnchors, 0, _lightAnchors.Count, UploadFlags);
            // Missing vertex channels can arrive as ones and select the animated lighting arm.
            while (_staticLightInputs.Count < _positions.Count) _staticLightInputs.Add(default);
            mesh.SetUVs(2, _staticLightInputs, 0, _positions.Count, UploadFlags);
            mesh.SetUVs(3, _staticLightInputs, 0, _positions.Count, UploadFlags);
            mesh.SetTriangles(_triangles, 0, calculateBounds: false);
            mesh.RecalculateBounds();

            if (go == null)
            {
                go = new GameObject(mesh.name);
                go.transform.SetParent(_root.transform, worldPositionStays: false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = _material;
                // A roof that cast shadows would shade the whole dungeon from Unity's fill light.
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                _chunks[chunk] = go;
            }
            else if (!go.activeSelf) go.SetActive(true);
        }
    }
}
