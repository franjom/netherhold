// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using DK2.Art.Textures;
using DK2.Core;
using DK2.Data.Model;
using NumericsMatrix = System.Numerics.Matrix4x4;
using DK2.Presentation;
using DK2.Simulation;
using UnityEngine;
using UnityEngine.Rendering;
using NumericsVector = System.Numerics.Vector3;

namespace DK2.Unity
{
    public sealed class ClassicLightning : IDisposable, ILightningCameraTransform
    {
        private readonly GameObject _root;
        private readonly Mesh _mesh;
        private readonly MeshRenderer _renderer;
        private readonly Material _material;
        private readonly Texture2D _texture;
        private readonly LightningDrawList _drawList;
        private readonly List<LightningControlPoint> _controls = new List<LightningControlPoint>();
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Vector2> _uvs = new List<Vector2>();
        private readonly List<int> _triangles = new List<int>();
        private Transform _cameraTransform;
        private bool _hasCamera;

        public ClassicLightning(Transform parent, Func<string, RgbaImage> loadTexture,
            RoomConstructionParticles effects, uint seed)
        {
            _drawList = new LightningDrawList(effects, seed, this);
            _root = new GameObject("Procedural lightning"); _root.transform.SetParent(parent, false);
            _mesh = new Mesh { name = "Lightning ribbons", indexFormat = IndexFormat.UInt32 }; _mesh.MarkDynamic();
            _root.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = _root.AddComponent<MeshRenderer>();
            _renderer.shadowCastingMode = ShadowCastingMode.Off; _renderer.receiveShadows = false;
            Shader shader = Resources.Load<Shader>("Shaders/ClassicLightning");
            if (shader == null) throw new InvalidOperationException("Lightning shader unavailable.");
            _material = new Material(shader);
            RgbaImage image = loadTexture("Lightning");
            _texture = new Texture2D(image.Width, image.Height, TextureFormat.RGBA32, false)
            { name = "Lightning", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            _texture.LoadRawTextureData(image.Pixels); _texture.Apply(false, true);
            _material.SetTexture("_MainTex", _texture); _renderer.sharedMaterial = _material;
            _renderer.enabled = false;
        }

        public void BeginFrame(Camera camera)
        {
            _hasCamera = camera != null;
            _cameraTransform = _hasCamera ? camera.transform : null;
            _drawList.BeginFrame(_hasCamera ? Matrix(camera.transform.worldToLocalMatrix) : NumericsMatrix.Identity,
                _hasCamera ? Matrix(camera.transform.localToWorldMatrix) : NumericsMatrix.Identity, _hasCamera);
        }
        public void DrawShots(WorldState world, GameDatabase database, WorldView view, float fraction) =>
            _drawList.DrawShots(world, database, view, fraction);
        public void DrawEffects() => _drawList.DrawEffects();
        NumericsVector ILightningCameraTransform.ToCamera(NumericsVector point)
        {
            Vector3 p = _cameraTransform.InverseTransformPoint(new Vector3(point.X, point.Y, point.Z));
            return new NumericsVector(p.x, p.y, p.z);
        }
        NumericsVector ILightningCameraTransform.ToWorld(NumericsVector point)
        {
            Vector3 p = _cameraTransform.TransformPoint(new Vector3(point.X, point.Y, point.Z));
            return new NumericsVector(p.x, p.y, p.z);
        }
        public void DrawCreature(CreatureId id, GameTick tick, ClassicCreatureAnimation animation)
        {
            if (animation == null || !_drawList.NeedsCreatureCrackle(id, tick, animation.InstallationRevision)) return;
            animation.CollectLightningControlPoints(_controls);
            _drawList.DrawCrackle(_controls);
        }
        private static NumericsMatrix Matrix(Matrix4x4 m) => new NumericsMatrix(
            m.m00, m.m10, m.m20, m.m30, m.m01, m.m11, m.m21, m.m31,
            m.m02, m.m12, m.m22, m.m32, m.m03, m.m13, m.m23, m.m33);
        public void EndFrame()
        {
            _vertices.Clear(); _uvs.Clear(); _triangles.Clear();
            for (int i = 0; i < _drawList.Vertices.Count; i++)
            {
                NumericsVector v = _drawList.Vertices[i];
                _vertices.Add(new Vector3(v.X, v.Y, v.Z));
            }
            for (int i = 0; i < _drawList.Uvs.Count; i++)
            {
                System.Numerics.Vector2 uv = _drawList.Uvs[i];
                _uvs.Add(new Vector2(uv.X, uv.Y));
            }
            for (int i = 0; i < _drawList.Triangles.Count; i++) _triangles.Add(_drawList.Triangles[i]);
            _renderer.enabled = _hasCamera && _vertices.Count != 0;
            _mesh.Clear();
            if (!_renderer.enabled) return;
            _mesh.SetVertices(_vertices); _mesh.SetUVs(0, _uvs); _mesh.SetTriangles(_triangles, 0); _mesh.RecalculateBounds();
        }

        public void Dispose()
        {
            UnityEngine.Object.Destroy(_root); UnityEngine.Object.Destroy(_mesh);
            UnityEngine.Object.Destroy(_material); UnityEngine.Object.Destroy(_texture);
        }
    }
}
