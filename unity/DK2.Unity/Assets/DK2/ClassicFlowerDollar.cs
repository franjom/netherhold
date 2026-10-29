// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using DK2.Art;
using DK2.Data.Model;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace DK2.Unity
{
    public sealed class ClassicFlowerDollar : IDisposable
    {
        private readonly RenderAnimation _clip;
        private readonly Mesh _template;
        private readonly Material[] _materials;
        private readonly Dictionary<int, Mesh> _meshes = new Dictionary<int, Mesh>();
        private readonly System.Numerics.Vector3[] _sample;
        private readonly Vector3[] _vertices;
        private RenderAnimationClock _clock;

        public ClassicFlowerDollar(InstallArtProvider art, ClassicMeshes meshes)
        {
            if (art == null || meshes == null || !art.TryResolve(StatusFlowerArt.Dollar, out var asset)
                || !art.TryLoadModel(asset, out var model) || model.Animation?.IsComplete != true
                || !meshes.TryGetMesh(StatusFlowerArt.Dollar, out _template, out _materials)) return;
            _clip = new RenderAnimation(model);
            _sample = new System.Numerics.Vector3[_clip.Mesh.Positions.Count];
            _vertices = new Vector3[_sample.Length];
        }

        public void Reset() => _clock = null;

        public void Draw(int creatureId, Vector3 position, float fraction)
        {
            if (_clip == null) return;
            if (!_meshes.TryGetValue(creatureId, out Mesh mesh))
            {
                mesh = Object.Instantiate(_template); mesh.MarkDynamic(); _meshes.Add(creatureId, mesh);
            }
            if (_clock == null) _clock = new RenderAnimationClock(new CreatureAnimationTiming
                { ResourceType = 6, FrameCount = 30, FramesPerSecond = 30 }, loop: true);
            _clip.Sample(_clock.Sample(fraction).Phase, false, _sample);
            for (int i = 0; i < _vertices.Length; i++)
                _vertices[i] = new Vector3(_sample[i].X, _sample[i].Y, _sample[i].Z);
            mesh.SetVertices(_vertices); mesh.RecalculateBounds();
            for (int i = 0; i < _materials.Length; i++)
                Graphics.RenderMesh(new RenderParams(_materials[i])
                    { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false }, mesh, i,
                    Matrix4x4.TRS(position, Quaternion.Euler(0, 180, 0), Vector3.one));
            // Native005961A0 advances the shared overlay animation after each winner draw.
            _clock.Advance();
        }

        public void Dispose()
        {
            foreach (Mesh mesh in _meshes.Values) Object.Destroy(mesh);
            _meshes.Clear(); _clock = null;
        }
    }
}
