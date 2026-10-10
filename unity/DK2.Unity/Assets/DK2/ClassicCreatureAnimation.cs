// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art;
using UnityEngine;

namespace DK2.Unity
{
    public sealed class ClassicCreatureAnimation : MonoBehaviour
    {
        private RenderAnimation _clip;
        private Mesh _mesh;
        private System.Numerics.Vector3[] _sample;
        private Vector3[] _vertices;
        private double _start;
        private double _duration;
        private bool _loop;

        public void Configure(RenderAnimation clip, Mesh template, Material[] materials,
            double duration, bool loop, double start)
        {
            _clip = clip;
            _duration = duration;
            _loop = loop;
            _start = start;
            _sample = new System.Numerics.Vector3[clip.Mesh.Positions.Count];
            _vertices = new Vector3[_sample.Length];
            if (_mesh != null) Object.Destroy(_mesh);
            _mesh = Object.Instantiate(template);
            _mesh.MarkDynamic();
            GetComponent<MeshFilter>().sharedMesh = _mesh;
            GetComponent<MeshRenderer>().sharedMaterials = materials;
        }

        public void Draw(double seconds)
        {
            if (_clip == null) return;
            _clip.Sample(_duration > 0 ? (float)((seconds - _start) / _duration) : 0f, _loop, _sample);
            for (int i = 0; i < _vertices.Length; i++)
                _vertices[i] = new Vector3(_sample[i].X, _sample[i].Y, _sample[i].Z);
            _mesh.SetVertices(_vertices);
            _mesh.RecalculateBounds();
        }

        public void Stop()
        {
            _clip = null;
            if (_mesh != null) Object.Destroy(_mesh);
            _mesh = null;
        }

        private void OnDestroy()
        {
            Stop();
        }
    }
}
