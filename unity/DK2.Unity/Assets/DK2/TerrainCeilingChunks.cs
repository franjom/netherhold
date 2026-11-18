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
    /// The possession view's roof, drawn: <see cref="TerrainCeilingScheduler"/>'s chunks, with the one
    /// <c>Ceiling</c> material and the terrain's vertex lighting. Every rule is the scheduler's
    /// (ADR-0805); this uploads its geometry as meshes, shows them while the possession camera
    /// holds the view, and rebuilds the chunks a change reaches.
    /// </summary>
    public sealed class TerrainCeilingChunks
    {
        private const MeshUpdateFlags UploadFlags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices
            | MeshUpdateFlags.DontNotifyMeshUsers;

        private static readonly ProfilerMarker RebuildMarker = new ProfilerMarker("DK2.TerrainCeiling.Rebuild");

        private readonly GameObject _root;
        private readonly ClassicMeshes _classic;
        private readonly TerrainCeilingScheduler _scheduler;
        private readonly GameObject[] _chunks;
        private readonly Mesh[] _meshes;
        private Material _material;

        // One chunk's assembly, reused across rebuilds.
        private readonly TerrainCeilingGeometry _geometry = new TerrainCeilingGeometry();
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
            _scheduler = new TerrainCeilingScheduler(width, height, lattice);
            _chunks = new GameObject[_scheduler.ChunkCount];
            _meshes = new Mesh[_scheduler.ChunkCount];
        }

        /// <summary>A presented tile changed, which may have changed the lattice around it.</summary>
        public void TileChanged(int x, int y) => _scheduler.TileChanged(x, y);

        /// <summary>Shows the roof over the presented world, rebuilding what changed since it was
        /// last shown: the first show builds every chunk. Call where the world may be read.</summary>
        public void Show(WorldState world, GameDatabase database)
        {
            if (_scheduler.Refresh(world, database))
            {
                using ProfilerMarker.AutoScope _ = RebuildMarker.Auto();
                _material ??= _classic.ForTexture(TerrainCeiling.TextureName);
                for (int chunk = 0; chunk < _chunks.Length; chunk++)
                    if (_scheduler.TryBuild(chunk, _geometry)) Upload(chunk);
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
            _scheduler.Clear();
        }

        /// <summary>The chunk the scheduler just built into <see cref="_geometry"/>, as its mesh.
        /// The triangles keep DK2's order: clockwise seen from below, the side Unity's back-face
        /// culling keeps.</summary>
        private void Upload(int chunk)
        {
            _positions.Clear();
            _normals.Clear();
            _uvs.Clear();
            _lightAnchors.Clear();
            _triangles.Clear();
            for (int i = 0; i < _geometry.Positions.Count; i++)
            {
                System.Numerics.Vector3 p = _geometry.Positions[i], n = _geometry.Normals[i];
                System.Numerics.Vector2 uv = _geometry.Uvs[i], anchor = _geometry.LightAnchors[i];
                _positions.Add(new Vector3(p.X, p.Y, p.Z));
                _normals.Add(new Vector3(n.X, n.Y, n.Z));
                _uvs.Add(new Vector2(uv.X, uv.Y));
                _lightAnchors.Add(new Vector2(anchor.X, anchor.Y));
            }
            _triangles.AddRange(_geometry.Triangles);

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
