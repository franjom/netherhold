// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using DK2.Core;
using DK2.Presentation;
using UnityEngine;
using UnityEngine.Rendering;

namespace DK2.Unity
{
    public sealed class ClassicTileHoverOutline : IDisposable
    {
        private readonly GameObject _root;
        private readonly Mesh _mesh;
        private readonly Material _material;
        private readonly MeshRenderer _renderer;
        private readonly TerrainLattice _lattice;
        private readonly System.Numerics.Vector3[] _grid = new System.Numerics.Vector3[27];
        private readonly Vector3[] _points = new Vector3[27], _vertices = new Vector3[96];
        private readonly int[] _endpoints = new int[48];
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
            int[] triangles = new int[144];
            var uv = new Vector2[96];
            for (int i = 0; i < 24; i++)
            {
                int v = i * 4, t = i * 6;
                triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
                triangles[t + 3] = v + 2; triangles[t + 4] = v + 1; triangles[t + 5] = v + 3;
                // 0x0059DD30 runs the ribbon's UVs 0..1 across its width.
                uv[v] = new Vector2(0, 0); uv[v + 1] = new Vector2(0, 1);
                uv[v + 2] = new Vector2(1, 0); uv[v + 3] = new Vector2(1, 1);
            }
            _mesh.vertices = _vertices; _mesh.uv = uv; _mesh.triangles = triangles;
            TileHoverOutline.Indices(TileHoverEdges.All | TileHoverEdges.Blue, _endpoints);
            _renderer.enabled = false;
        }

        public void Update(TilePos? tile, Camera camera, double nowSeconds)
        {
            _renderer.enabled = tile.HasValue && camera != null;
            if (!_renderer.enabled) return;
            long milliseconds = (long)(nowSeconds * 1000);
            int delta = _lastMilliseconds == 0 ? 0 : (int)Math.Min(int.MaxValue, Math.Max(0, milliseconds - _lastMilliseconds));
            _lastMilliseconds = milliseconds;
            float brightness = _pulse.Level * TileHoverOutline.BluePeak;
            _pulse.Advance(delta);
            TilePos at = tile.Value;
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
            for (int i = 0; i < 24; i++)
            {
                Vector3 a = _points[_endpoints[i * 2]], b = _points[_endpoints[i * 2 + 1]];
                Vector3 side = Vector3.Cross(b - a, camera.transform.position - (a + b) * .5f).normalized * TileHoverOutline.Radius;
                _vertices[i * 4] = a - side; _vertices[i * 4 + 1] = a + side;
                _vertices[i * 4 + 2] = b - side; _vertices[i * 4 + 3] = b + side;
            }
            _mesh.vertices = _vertices; _mesh.RecalculateBounds();
            _material.SetColor("_Color", new Color(0, 0, brightness, 1));
        }

        public void Dispose()
        {
            UnityEngine.Object.Destroy(_root); UnityEngine.Object.Destroy(_mesh); UnityEngine.Object.Destroy(_material);
        }
    }
}
