// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using DK2.Core;
using DK2.Presentation;
using UnityEngine;
using UnityEngine.Rendering;

namespace DK2.Unity
{
    /// <summary>The selection cage (0x0043CC30, hand-cursor-rendering.md): each boundary tile of the
    /// rectangle draws the kind3 grid's segments whose edge mask it holds, red (pulse, 0, 0), or blue
    /// (0, 0, pulse * 0.613) when the placement is valid.</summary>
    public sealed class ClassicTileHoverOutline : IDisposable
    {
        private readonly GameObject _root;
        private readonly Mesh _mesh;
        private readonly Material _material;
        private readonly MeshRenderer _renderer;
        private readonly TerrainLattice _lattice;
        private readonly System.Numerics.Vector3[] _grid = new System.Numerics.Vector3[27];
        private readonly Vector3[] _points = new Vector3[27];
        private readonly int[] _endpoints = new int[48];
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Vector2> _uvs = new List<Vector2>();
        private readonly List<int> _triangles = new List<int>();
        private readonly TileHoverPulse _pulse = new TileHoverPulse();
        private long _lastMilliseconds;

        public ClassicTileHoverOutline(Transform parent, TerrainLattice lattice)
        {
            _lattice = lattice;
            _root = new GameObject("Hand tile outline"); _root.transform.SetParent(parent, false);
            _mesh = new Mesh { name = "Terrain-deformed hover cage" }; _mesh.MarkDynamic();
            _root.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = _root.AddComponent<MeshRenderer>();
            Shader shader = Resources.Load<Shader>("Shaders/ClassicHoverOutline");
            if (shader == null) throw new InvalidOperationException("Hover outline shader unavailable.");
            _material = new Material(shader); _renderer.sharedMaterial = _material;
            _renderer.shadowCastingMode = ShadowCastingMode.Off; _renderer.receiveShadows = false;
            _renderer.enabled = false;
        }

        /// <summary>The Hand's dig-hover cage: one tile, blue.</summary>
        public void Update(TilePos? tile, Camera camera, double nowSeconds) =>
            Update(tile is TilePos at ? new TileRectangle(at.X, at.Y, at.X + 1, at.Y + 1) : null, true, camera, nowSeconds);

        public void Update(TileRectangle? area, bool blue, Camera camera, double nowSeconds)
        {
            _renderer.enabled = area.HasValue && camera != null;
            if (!_renderer.enabled) return;
            long milliseconds = (long)(nowSeconds * 1000);
            int delta = _lastMilliseconds == 0 ? 0 : (int)Math.Min(int.MaxValue, Math.Max(0, milliseconds - _lastMilliseconds));
            _lastMilliseconds = milliseconds;
            float level = _pulse.Level;
            _pulse.Advance(delta);
            TileRectangle rect = area.Value;
            _vertices.Clear(); _uvs.Clear(); _triangles.Clear();
            for (int y = rect.Y0; y < rect.Y1; y++)
                for (int x = rect.X0; x < rect.X1; x++)
                {
                    TileHoverEdges mask = TileHoverEdges.Lower
                        | (y == rect.Y0 ? TileHoverEdges.North : 0) | (y == rect.Y1 - 1 ? TileHoverEdges.South : 0)
                        | (x == rect.X0 ? TileHoverEdges.West : 0) | (x == rect.X1 - 1 ? TileHoverEdges.East : 0);
                    if (mask == TileHoverEdges.Lower) continue;
                    AddTile(new TilePos(x, y), mask, camera);
                }
            _mesh.Clear();
            _mesh.SetVertices(_vertices); _mesh.SetUVs(0, _uvs); _mesh.SetTriangles(_triangles, 0);
            _mesh.RecalculateBounds();
            _material.SetColor("_Color", blue ? new Color(0, 0, level * TileHoverOutline.BluePeak, 1) : new Color(level, 0, 0, 1));
        }

        private void AddTile(TilePos at, TileHoverEdges mask, Camera camera)
        {
            TileHoverOutline.Grid(at, _grid);
            for (int i = 0; i < _grid.Length; i++)
            {
                System.Numerics.Vector3 p = _grid[i];
                if (_lattice != null)
                {
                    p.Z += .05f;
                    p = _lattice.Deform(p, at.X, at.Y);
                    p.Z -= .05f;
                }
                _points[i] = new Vector3(p.X, -p.Z, -p.Y);
            }
            int count = TileHoverOutline.Indices(mask, _endpoints);
            for (int i = 0; i < count; i += 2)
            {
                Vector3 a = _points[_endpoints[i]], b = _points[_endpoints[i + 1]];
                Vector3 side = Vector3.Cross(b - a, camera.transform.position - (a + b) * .5f).normalized * TileHoverOutline.Radius;
                int v = _vertices.Count;
                _vertices.Add(a - side); _vertices.Add(a + side); _vertices.Add(b - side); _vertices.Add(b + side);
                // 0x0059DD30 runs the ribbon's UVs 0..1 across its width.
                _uvs.Add(new Vector2(0, 0)); _uvs.Add(new Vector2(0, 1)); _uvs.Add(new Vector2(1, 0)); _uvs.Add(new Vector2(1, 1));
                _triangles.Add(v); _triangles.Add(v + 1); _triangles.Add(v + 2);
                _triangles.Add(v + 2); _triangles.Add(v + 1); _triangles.Add(v + 3);
            }
        }

        public void Dispose()
        {
            UnityEngine.Object.Destroy(_root); UnityEngine.Object.Destroy(_mesh); UnityEngine.Object.Destroy(_material);
        }
    }
}
