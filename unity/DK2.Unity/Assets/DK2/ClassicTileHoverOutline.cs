// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
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
        private readonly TileHoverRibbon _ribbon = new TileHoverRibbon();
        private Vector3[] _vertices = new Vector3[64];
        private Vector2[] _uvs = new Vector2[64];
        private int[] _triangles = new int[96];

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

        /// <summary>Draws <see cref="TileHoverOutline.Build"/>'s ribbon for <paramref name="area"/>, turned to the camera.</summary>
        public void Update(TileRectangle? area, bool blue, Camera camera, double nowSeconds)
        {
            _renderer.enabled = area.HasValue && camera != null;
            if (!_renderer.enabled) return;
            Vector3 eye = camera.transform.position;
            TileHoverOutline.Build(_ribbon, area.Value, blue, new System.Numerics.Vector3(eye.x, eye.y, eye.z), _lattice,
                (long)(nowSeconds * 1000));
            int vertices = _ribbon.VertexCount, indices = _ribbon.IndexCount;
            if (_vertices.Length < vertices)
            {
                Array.Resize(ref _vertices, Math.Max(vertices, _vertices.Length * 2));
                Array.Resize(ref _uvs, _vertices.Length);
            }
            if (_triangles.Length < indices) Array.Resize(ref _triangles, Math.Max(indices, _triangles.Length * 2));
            ReadOnlySpan<System.Numerics.Vector3> positions = _ribbon.Positions;
            ReadOnlySpan<System.Numerics.Vector2> uvs = _ribbon.Uvs;
            for (int i = 0; i < vertices; i++)
            {
                _vertices[i] = new Vector3(positions[i].X, positions[i].Y, positions[i].Z);
                _uvs[i] = new Vector2(uvs[i].X, uvs[i].Y);
            }
            _ribbon.Indices.CopyTo(_triangles);
            _mesh.Clear();
            _mesh.SetVertices(_vertices, 0, vertices); _mesh.SetUVs(0, _uvs, 0, vertices);
            _mesh.SetTriangles(_triangles, 0, indices, 0);
            _mesh.RecalculateBounds();
            System.Numerics.Vector3 colour = _ribbon.Colour;
            _material.SetColor("_Color", new Color(colour.X, colour.Y, colour.Z, 1));
        }

        public void Dispose()
        {
            UnityEngine.Object.Destroy(_root); UnityEngine.Object.Destroy(_mesh); UnityEngine.Object.Destroy(_material);
        }
    }
}
