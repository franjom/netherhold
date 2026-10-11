// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Presentation;
using UnityEngine;
using NumericsVector3 = System.Numerics.Vector3;

namespace DK2.Unity
{
    public sealed class ClassicTorchGeometry : MonoBehaviour
    {
        private Mesh _mesh;
        private NumericsVector3[] _source;
        private Vector3[] _sourceNormals;
        private Vector3[] _vertices;
        private Vector3[] _normals;
        private TorchView _torch;
        private TerrainLattice _lattice;

        public void Configure(in TorchView torch, TerrainLattice lattice)
        {
            if (_mesh == null)
            {
                MeshFilter filter = GetComponent<MeshFilter>();
                Mesh template = filter.sharedMesh;
                Vector3[] source = template.vertices;
                _source = new NumericsVector3[source.Length];
                for (int i = 0; i < source.Length; i++)
                    _source[i] = new NumericsVector3(source[i].x, source[i].y, source[i].z);
                _sourceNormals = template.normals;
                _vertices = new Vector3[source.Length];
                _normals = new Vector3[_sourceNormals.Length];
                _mesh = Object.Instantiate(template);
                _mesh.MarkDynamic();
                filter.sharedMesh = _mesh;
            }
            _torch = torch;
            _lattice = lattice;
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            Quaternion turn = Quaternion.Euler(0f, torch.Yaw * 360f / 2048f, 0f);
            for (int i = 0; i < _normals.Length; i++) _normals[i] = turn * _sourceNormals[i];
            _mesh.SetNormals(_normals);
            Refresh();
        }

        public void Refresh()
        {
            if (_mesh == null) return;
            var origin = new NumericsVector3(_torch.PosX / (float)Fp32.One,
                _torch.Height / (float)Fp32.One, -_torch.PosY / (float)Fp32.One);
            for (int i = 0; i < _vertices.Length; i++)
            {
                NumericsVector3 point = TorchGeometry.Vertex(_torch, _source[i], _lattice) - origin;
                _vertices[i] = new Vector3(point.X, point.Y, point.Z);
            }
            _mesh.SetVertices(_vertices);
            _mesh.RecalculateBounds();
        }

        private void OnDestroy()
        {
            if (_mesh != null) Object.Destroy(_mesh);
        }
    }
}
