// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art;
using DK2.Data.Model;
using UnityEngine;

namespace DK2.Unity
{
    public sealed class ClassicCreatureAnimation : MonoBehaviour
    {
        private RenderAnimation[] _clips;
        private Mesh[] _meshes;
        private Material[][] _materials;
        private System.Numerics.Vector3[][] _samples;
        private Vector3[][] _vertices;
        private RenderAnimationClock _clock;
        private long _startTick;
        private long _advanced;
        private AnimationSegment? _drawnSegment;

        public void Configure(RenderAnimation[] clips, Mesh[] templates, Material[][] materials,
            CreatureAnimationTiming timing, bool loop, double start)
        {
            Stop();
            _clips = clips;
            _materials = materials;
            _clock = new RenderAnimationClock(timing, loop);
            _startTick = (long)System.Math.Floor(start * 4);
            _advanced = 0;
            _drawnSegment = null;
            _meshes = new Mesh[3];
            _samples = new System.Numerics.Vector3[3][];
            _vertices = new Vector3[3][];
            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i] == null) continue;
                _samples[i] = new System.Numerics.Vector3[clips[i].Mesh.Positions.Count];
                _vertices[i] = new Vector3[_samples[i].Length];
                _meshes[i] = Object.Instantiate(templates[i]);
                _meshes[i].MarkDynamic();
            }
            Draw(start);
        }

        public void Draw(double seconds)
        {
            if (_clock == null) return;
            double tickTime = seconds * 4;
            long tick = (long)System.Math.Floor(tickTime);
            while (_advanced <= tick - _startTick) { _clock.Advance(); _advanced++; }
            AnimationPose pose = _clock.Sample((float)(tickTime - tick));
            int segment = (int)pose.Segment;
            if (_drawnSegment != (AnimationSegment)segment)
            {
                GetComponent<MeshFilter>().sharedMesh = _meshes[segment];
                GetComponent<MeshRenderer>().sharedMaterials = _materials[segment];
                _drawnSegment = (AnimationSegment)segment;
            }
            System.Numerics.Vector3[] sample = _samples[segment];
            Vector3[] vertices = _vertices[segment];
            _clips[segment].Sample(pose.Phase, false, sample);
            for (int i = 0; i < vertices.Length; i++)
                vertices[i] = new Vector3(sample[i].X, sample[i].Y, sample[i].Z);
            _meshes[segment].SetVertices(vertices);
            _meshes[segment].RecalculateBounds();
        }

        public void Stop()
        {
            _clock = null;
            if (_meshes != null)
                foreach (Mesh mesh in _meshes) if (mesh != null) Object.Destroy(mesh);
            _meshes = null;
        }

        private void OnDestroy()
        {
            Stop();
        }
    }
}
