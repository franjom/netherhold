// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Collections.Generic;
using DK2.Presentation;
using UnityEngine;

namespace DK2.Unity
{
    /// <summary>
    /// The classic look's water and lava surfaces (room-floors-and-liquids.md §6): each tile a 3×3
    /// vertex grid, waved every frame as DK2's sine mode waves them, one mesh per liquid.
    /// </summary>
    /// <remarks>
    /// Water rests at 0.9, lava at 0.85 (<c>0x005A2570</c>, <c>0x005A2BE0</c>), moved by a sine
    /// along the row plus one along the column: water rises by it, lava sinks. Chosen or not built:
    /// Sine Wave on, Translucent Water off (SOLID textures), opaque, water's first frame only, one
    /// texture repeat a tile, DK2's frame counter at 30 a second, normals recomputed (DK2's are
    /// analytic), no lattice bend, and no fog gate of its own (<c>0x0043D187</c>).
    /// </remarks>
    public sealed class LiquidSurfaces
    {
        private const float WaterRest = 0.9f, WaterAmplitude = 0.03f;
        private const float LavaRest = 0.85f, LavaAmplitude = 0.04f;
        private const float FramesPerSecond = 30f;

        private readonly Transform _root;
        private readonly ClassicMeshes _classic;
        private readonly int _width;
        private readonly LiquidSurface[] _kinds;
        private readonly Layer _water = new Layer(LiquidSurface.Water, "WaterSOLID0");
        private readonly Layer _lava = new Layer(LiquidSurface.Lava, "Lava0SOLID");
        private bool _dirty;

        public LiquidSurfaces(Transform root, ClassicMeshes classic, int width, int height)
        {
            _root = root;
            _classic = classic;
            _width = width;
            _kinds = new LiquidSurface[width * height];
        }

        public void Set(int tileIndex, in TileView tile)
        {
            if (_kinds[tileIndex] == tile.Surface) return;
            _kinds[tileIndex] = tile.Surface;
            _dirty = true;
        }

        /// <summary>Rebuilds a layer whose tiles changed, then waves both.</summary>
        public void Animate(float time)
        {
            if (_dirty)
            {
                Rebuild(_water);
                Rebuild(_lava);
                _dirty = false;
            }
            int frame = (int)(time * FramesPerSecond);
            Wave(_water, frame);
            Wave(_lava, frame);
        }

        public void Clear()
        {
            Clear(_water);
            Clear(_lava);
            System.Array.Clear(_kinds, 0, _kinds.Length);
        }

        // The mesh is the layer's own (Rebuild destroys the previous one too); the object alone would leak it.
        private static void Clear(Layer layer)
        {
            if (layer.Object != null) Object.Destroy(layer.Object);
            if (layer.Mesh != null) Object.Destroy(layer.Mesh);
            layer.Object = null;
            layer.Mesh = null;
            layer.Tiles.Clear();
        }

        private void Rebuild(Layer layer)
        {
            layer.Tiles.Clear();
            for (int i = 0; i < _kinds.Length; i++)
                if (_kinds[i] == layer.Kind) layer.Tiles.Add(i);
            if (layer.Object != null) Object.Destroy(layer.Object);
            if (layer.Mesh != null) Object.Destroy(layer.Mesh);
            layer.Object = null;
            layer.Mesh = null;
            if (layer.Tiles.Count == 0) return;

            int count = layer.Tiles.Count;
            layer.Positions = new Vector3[count * 9];
            var uvs = new Vector2[count * 9];
            var lightAnchors = new Vector2[count * 9];
            var triangles = new int[count * 24];
            for (int k = 0; k < count; k++)
            {
                for (int j = 0; j < 3; j++)
                    for (int i = 0; i < 3; i++)
                    {
                        int vertex = (k * 9) + (j * 3) + i;
                        uvs[vertex] = new Vector2(i * 0.5f, 1f - (j * 0.5f));
                        lightAnchors[vertex] = new Vector2(layer.Tiles[k], 1);
                    }
                int n = k * 24;
                for (int j = 0; j < 2; j++)
                    for (int i = 0; i < 2; i++)
                    {
                        // Clockwise from above: north-west, north-east, south-east; then south-west.
                        int a = (k * 9) + (j * 3) + i, b = a + 1, c = a + 3, d = a + 4;
                        triangles[n++] = a; triangles[n++] = b; triangles[n++] = d;
                        triangles[n++] = a; triangles[n++] = d; triangles[n++] = c;
                    }
            }

            var mesh = new Mesh
            {
                name = $"{layer.Kind} surface",
                indexFormat = count * 9 > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16,
            };
            mesh.MarkDynamic();
            layer.Mesh = mesh;
            Wave(layer, 0);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, lightAnchors);
            var staticLightInputs = new Vector4[mesh.vertexCount];
            mesh.SetUVs(2, staticLightInputs);
            mesh.SetUVs(3, staticLightInputs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();

            var go = new GameObject(mesh.name);
            go.transform.SetParent(_root, worldPositionStays: false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = _classic.ForTexture(layer.Texture);
            layer.Object = go;
        }

        private void Wave(Layer layer, int frame)
        {
            if (layer.Mesh == null || layer.Tiles.Count == 0) return;
            Vector3[] positions = layer.Positions;
            for (int k = 0; k < layer.Tiles.Count; k++)
            {
                int x = layer.Tiles[k] % _width, y = layer.Tiles[k] / _width;
                for (int j = 0; j < 3; j++)
                {
                    float row = Phase(layer.Kind, y, j, frame);
                    for (int i = 0; i < 3; i++)
                    {
                        // DK2's z (up = −z) is −0.9 − 0.03·s for water and −0.85 + 0.04·s for lava.
                        float height = layer.Kind == LiquidSurface.Water
                            ? WaterRest + (WaterAmplitude * (row + Phase(layer.Kind, x, i, frame)))
                            : LavaRest - (LavaAmplitude * (row + Phase(layer.Kind, x, i, frame)));
                        positions[(k * 9) + (j * 3) + i] = DungeonScene.Ground(x + (i * 0.5f), y + (j * 0.5f), height);
                    }
                }
            }
            layer.Mesh.SetVertices(positions);
            layer.Mesh.RecalculateNormals();
        }

        /// <summary>The sine of one axis: water's ((tile·0x10000 + j·0x8000 + 4096·frame) &amp;
        /// 0x1FFFF)·π/65536, lava's ((j·32 − tile·64 + frame) &amp; 127)·π/64; two tiles a period.</summary>
        private static float Phase(LiquidSurface kind, int tile, int vertex, int frame) =>
            kind == LiquidSurface.Water
                ? Mathf.Sin(((tile * 0x10000) + (vertex * 0x8000) + (4096 * frame) & 0x1FFFF) * Mathf.PI / 65536f)
                : Mathf.Sin((((vertex * 32) - (tile * 64) + frame) & 127) * Mathf.PI / 64f);

        private sealed class Layer
        {
            public Layer(LiquidSurface kind, string texture)
            {
                Kind = kind;
                Texture = texture;
            }

            public LiquidSurface Kind { get; }
            public string Texture { get; }
            public List<int> Tiles { get; } = new List<int>();
            public Vector3[] Positions { get; set; }
            public Mesh Mesh { get; set; }
            public GameObject Object { get; set; }
        }
    }
}
