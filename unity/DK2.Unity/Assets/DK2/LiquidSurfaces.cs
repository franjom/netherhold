// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Presentation;
using UnityEngine;

namespace DK2.Unity
{
    /// <summary>
    /// The classic look's water and lava surfaces: <see cref="LiquidSurfaceField"/> lays them out and waves them,
    /// and this uploads each liquid as one mesh, its vertices and normals rewritten every frame.
    /// </summary>
    public sealed class LiquidSurfaces
    {
        private readonly Transform _root;
        private readonly ClassicMeshes _classic;
        private readonly LiquidSurfaceField _field;
        private readonly Layer _water;
        private readonly Layer _lava;

        public LiquidSurfaces(Transform root, ClassicMeshes classic, int width, int height)
        {
            _root = root;
            _classic = classic;
            _field = new LiquidSurfaceField(width, height);
            _water = new Layer(_field.Water);
            _lava = new Layer(_field.Lava);
        }

        public void Set(int tileIndex, in TileView tile) => _field.Set(tileIndex, tile);

        /// <summary>Rebuilds a layer whose tiles changed, then waves both.</summary>
        public void Animate(float time)
        {
            _field.Animate(time);
            Upload(_water);
            Upload(_lava);
        }

        public void Clear()
        {
            Clear(_water);
            Clear(_lava);
            _field.Clear();
        }

        // The mesh is the layer's own (Rebuild destroys the previous one too); the object alone would leak it.
        private static void Clear(Layer layer)
        {
            if (layer.Object != null) Object.Destroy(layer.Object);
            if (layer.Mesh != null) Object.Destroy(layer.Mesh);
            layer.Object = null;
            layer.Mesh = null;
            layer.Version = layer.Wave.Version;
        }

        private void Upload(Layer layer)
        {
            LiquidSurfaceWave wave = layer.Wave;
            if (layer.Version != wave.Version) Rebuild(layer);
            if (layer.Mesh == null) return;
            int count = wave.VertexCount;
            for (int i = 0; i < count; i++)
            {
                System.Numerics.Vector3 p = wave.Positions[i], n = wave.Normals[i];
                layer.Positions[i] = new Vector3(p.X, p.Y, p.Z);
                layer.Normals[i] = new Vector3(n.X, n.Y, n.Z);
            }
            layer.Mesh.SetVertices(layer.Positions, 0, count);
            // The shared face normals, where Unity used to recalculate its own: a deliberate deviation of at most 1.5°.
            layer.Mesh.SetNormals(layer.Normals, 0, count);
        }

        private void Rebuild(Layer layer)
        {
            LiquidSurfaceWave wave = layer.Wave;
            Clear(layer);
            if (wave.TileCount == 0) return;

            int count = wave.VertexCount;
            layer.Positions = new Vector3[count];
            layer.Normals = new Vector3[count];
            var uvs = new Vector2[count];
            var lightAnchors = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                uvs[i] = new Vector2(wave.Uvs[i].X, wave.Uvs[i].Y);
                lightAnchors[i] = new Vector2(wave.LightAnchors[i].X, wave.LightAnchors[i].Y);
                System.Numerics.Vector3 p = wave.Positions[i];
                layer.Positions[i] = new Vector3(p.X, p.Y, p.Z);
            }

            var mesh = new Mesh
            {
                name = $"{wave.Kind} surface",
                indexFormat = count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16,
            };
            mesh.MarkDynamic();
            layer.Mesh = mesh;
            mesh.SetVertices(layer.Positions);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, lightAnchors);
            var staticLightInputs = new Vector4[count];
            mesh.SetUVs(2, staticLightInputs);
            mesh.SetUVs(3, staticLightInputs);
            mesh.SetTriangles(wave.Triangles, 0, wave.IndexCount, 0);
            mesh.RecalculateBounds();

            var go = new GameObject(mesh.name);
            go.transform.SetParent(_root, worldPositionStays: false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = _classic.ForTexture(wave.Texture);
            layer.Object = go;
        }

        private sealed class Layer
        {
            public Layer(LiquidSurfaceWave wave)
            {
                Wave = wave;
                Version = wave.Version;
            }

            public LiquidSurfaceWave Wave { get; }
            public int Version { get; set; }
            public Vector3[] Positions { get; set; }
            public Vector3[] Normals { get; set; }
            public Mesh Mesh { get; set; }
            public GameObject Object { get; set; }
        }
    }
}
