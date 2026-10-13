// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art;
using DK2.Data.Model;
using DK2.Core;
using UnityEngine;
using Unity.Profiling;

namespace DK2.Unity
{
    public sealed class ClassicCreatureAnimation : MonoBehaviour
    {
        private static readonly ProfilerMarker ClockMarker = new ProfilerMarker("DK2.Animation.Clock");
        private static readonly ProfilerMarker SampleMarker = new ProfilerMarker("DK2.Animation.Sample");
        private static readonly ProfilerMarker UploadMarker = new ProfilerMarker("DK2.Animation.MeshUpload");
        private RenderAnimation[] _clips;
        private Mesh[] _meshes;
        private Material[][] _materials;
        private System.Numerics.Vector3[][] _samples;
        private Vector3[][] _vertices;
        private RenderAnimationClock _clock;
        private long _startTick;
        private long _advanced;
        private AnimationSegment? _drawnSegment;
        private (RenderAnimation[] Clips, Mesh[] Templates, Material[][] Materials)? _pending;
        private int _resourceRevision;

        public void Configure(RenderAnimation[] clips, Mesh[] templates, Material[][] materials,
            CreatureAnimationTiming timing, bool loop, double start, Fp32? authoredMovementScale = null,
            Fp32? movementSpeed = null, AnimationReplacement replacement = AnimationReplacement.Immediate,
            bool hold = false, Fp32? initialMovementSpeed = null)
        {
            if (_clock != null && replacement == AnimationReplacement.Deferred)
            {
                Draw(start, movementSpeed, hold);
                int revision = _clock.ResourceRevision;
                _clock.Replace(timing, loop, replacement, authoredMovementScale, initialMovementSpeed);
                if (_clock.ResourceRevision == revision)
                {
                    _pending = (clips, templates, materials);
                    return;
                }
            }
            Stop();
            _clock = new RenderAnimationClock(timing, loop, authoredMovementScale, initialMovementSpeed);
            _resourceRevision = _clock.ResourceRevision;
            _startTick = (long)System.Math.Floor(start * 4);
            _advanced = 0;
            InstallMeshes(clips, templates, materials);
            Draw(start, movementSpeed, hold);
        }

        private void InstallMeshes(RenderAnimation[] clips, Mesh[] templates, Material[][] materials)
        {
            ReleaseMeshes();
            _clips = clips;
            _materials = materials;
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
        }

        public void Draw(double seconds, Fp32? movementSpeed = null, bool hold = false)
        {
            if (_clock == null) return;
            double tickTime = seconds * 4;
            long tick = (long)System.Math.Floor(tickTime);
            AnimationPose pose;
            using (ClockMarker.Auto())
            {
                while (_advanced <= tick - _startTick) { _clock.Advance(movementSpeed, hold); _advanced++; }
                pose = _clock.Sample((float)(tickTime - tick));
            }
            if (_clock.ResourceRevision != _resourceRevision && _pending.HasValue)
            {
                var pending = _pending.Value;
                InstallMeshes(pending.Clips, pending.Templates, pending.Materials);
                _pending = null;
                _resourceRevision = _clock.ResourceRevision;
            }
            int segment = (int)pose.Segment;
            if (_drawnSegment != (AnimationSegment)segment)
            {
                GetComponent<MeshFilter>().sharedMesh = _meshes[segment];
                GetComponent<MeshRenderer>().sharedMaterials = _materials[segment];
                _drawnSegment = (AnimationSegment)segment;
            }
            System.Numerics.Vector3[] sample = _samples[segment];
            Vector3[] vertices = _vertices[segment];
            using (SampleMarker.Auto())
            {
                _clips[segment].Sample(pose.Phase, false, sample);
                for (int i = 0; i < vertices.Length; i++)
                    vertices[i] = new Vector3(sample[i].X, sample[i].Y, sample[i].Z);
            }
            using (UploadMarker.Auto())
            {
                _meshes[segment].SetVertices(vertices);
                _meshes[segment].RecalculateBounds();
            }
        }

        public void Stop()
        {
            _clock = null;
            _pending = null;
            ReleaseMeshes();
        }

        private void ReleaseMeshes()
        {
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
