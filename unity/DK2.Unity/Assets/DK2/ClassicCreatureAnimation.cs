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
    /// <summary>Unity's side of <see cref="CreatureAnimationPlayer{TResources}"/>: the mesh copies, the materials and the
    /// vertex upload. The playback rules are the player's.</summary>
    public sealed class ClassicCreatureAnimation : MonoBehaviour, ICreatureAnimationSurface<ClassicCreatureAnimation.MeshSet>
    {
        /// <summary>A clip set's mesh templates and materials, by <see cref="AnimationSegment"/>.</summary>
        public readonly struct MeshSet
        {
            public MeshSet(Mesh[] templates, Material[][] materials)
            {
                Templates = templates;
                Materials = materials;
            }

            public Mesh[] Templates { get; }
            public Material[][] Materials { get; }
        }

        private static readonly ProfilerMarker ClockMarker = new ProfilerMarker("DK2.Animation.Clock");
        private static readonly ProfilerMarker SampleMarker = new ProfilerMarker("DK2.Animation.Sample");
        private static readonly ProfilerMarker UploadMarker = new ProfilerMarker("DK2.Animation.MeshUpload");
        private CreatureAnimationPlayer<MeshSet> _player;
        private RenderAnimation[] _clips;
        private Mesh[] _meshes;
        private Mesh[] _templates;
        private Material[][] _materials;
        private Vector3[][] _vertices;
        private readonly System.Collections.Generic.List<AnimationPoint> _lightningPoints =
            new System.Collections.Generic.List<AnimationPoint>();

        private CreatureAnimationPlayer<MeshSet> Player => _player ??= new CreatureAnimationPlayer<MeshSet>(this);

        /// <summary>What the last <see cref="Draw"/> found of the clip's footstep marks: 0 none, 1 before the clip's wrap, 2 after it
        /// (<c>0x004B4630</c>, asked at each drawn frame by <c>0x0043CC30</c>).</summary>
        public int FootstepCrossing => Player.FootstepCrossing;
        public bool BeatPending => Player.BeatPending;
        public int InstallationRevision => Player.InstallationRevision;

        public void ConsumeBeat() => Player.ConsumeBeat();

        public void AdvanceHealthPhase(Fp32 phaseStep) => Player.AdvanceHealthPhase(phaseStep);

        public void Configure(RenderAnimation[] clips, Mesh[] templates, Material[][] materials,
            CreatureAnimationTiming timing, bool loop, double start, Fp32? authoredMovementScale = null,
            Fp32? movementSpeed = null, AnimationReplacement replacement = AnimationReplacement.Immediate,
            bool hold = false, Fp32? initialMovementSpeed = null, Fp32? phaseStepOverride = null, Fp32 initialPhase = default,
            FootstepMarkPair footstepMarks = default)
        {
            using (ClockMarker.Auto())
                Player.Configure(clips, new MeshSet(templates, materials), timing, loop, start, authoredMovementScale,
                    movementSpeed, replacement, hold, initialMovementSpeed, phaseStepOverride, initialPhase, footstepMarks);
        }

        void ICreatureAnimationSurface<MeshSet>.Install(RenderAnimation[] clips, MeshSet resources)
        {
            Mesh[] templates = resources.Templates;
            if (SameClips(clips, templates))
            {
                // The copies differ from their templates only in the vertices and bounds Draw writes
                // before a segment is shown, so a clip started again keeps them.
                _clips = clips;
                _materials = resources.Materials;
                return;
            }
            ReleaseMeshes();
            _clips = clips;
            _templates = templates;
            _materials = resources.Materials;
            _meshes = new Mesh[3];
            _vertices = new Vector3[3][];
            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i] == null) continue;
                _vertices[i] = new Vector3[clips[i].Mesh.Positions.Count];
                _meshes[i] = Object.Instantiate(templates[i]);
                _meshes[i].MarkDynamic();
            }
        }

        void ICreatureAnimationSurface<MeshSet>.Show(AnimationSegment segment)
        {
            GetComponent<MeshFilter>().sharedMesh = _meshes[(int)segment];
            GetComponent<MeshRenderer>().sharedMaterials = _materials[(int)segment];
        }

        void ICreatureAnimationSurface<MeshSet>.Upload(AnimationSegment segment, System.Numerics.Vector3[] sample)
        {
            Vector3[] vertices = _vertices[(int)segment];
            using (SampleMarker.Auto())
                for (int i = 0; i < vertices.Length; i++)
                    vertices[i] = new Vector3(sample[i].X, sample[i].Y, sample[i].Z);
            using (UploadMarker.Auto())
            {
                _meshes[(int)segment].SetVertices(vertices);
                _meshes[(int)segment].RecalculateBounds();
            }
        }

        public void SetMainMaterials(Material[] materials)
        {
            if (_materials == null) return;
            _materials[(int)AnimationSegment.Main] = materials;
            if (Player.DrawnSegment == AnimationSegment.Main)
                GetComponent<MeshRenderer>().sharedMaterials = materials;
        }

        public void Draw(double seconds, Fp32? movementSpeed = null, bool hold = false, Fp32? phaseStepOverride = null)
        {
            using (ClockMarker.Auto()) Player.Draw(seconds, movementSpeed, hold, phaseStepOverride);
        }

        /// <summary>The drawn clip's tagged point <paramref name="id"/> (a KMF control point) in world
        /// space, as last drawn: the point world slot +0x10C finds (0x0059D260, ADR-0480).</summary>
        public bool TryGetControlPoint(uint id, out Vector3 world)
        {
            world = default;
            if (_vertices == null || !Player.TryGetControlPoint(id, out System.Numerics.Vector3 local)) return false;
            world = transform.TransformPoint(new Vector3(local.X, local.Y, local.Z));
            return true;
        }

        public void CollectLightningControlPoints(System.Collections.Generic.List<DK2.Presentation.LightningControlPoint> points)
        {
            points.Clear();
            if (_vertices == null) return;
            Player.CollectLightningPoints(_lightningPoints);
            foreach (AnimationPoint control in _lightningPoints)
            {
                Vector3 point = transform.TransformPoint(new Vector3(control.Local.X, control.Local.Y, control.Local.Z));
                points.Add(DK2.Presentation.LightningControlPoint.FromRenderWorld(control.Id,
                    new System.Numerics.Vector3(point.x, point.y, point.z)));
            }
        }

        public void Stop()
        {
            _player?.Stop();
            ReleaseMeshes();
        }

        private bool SameClips(RenderAnimation[] clips, Mesh[] templates)
        {
            if (_meshes == null || _clips == null || _templates == null
                || clips.Length != _clips.Length || templates.Length != _templates.Length) return false;
            for (int i = 0; i < clips.Length; i++)
                if (!ReferenceEquals(clips[i], _clips[i]) || templates[i] != _templates[i]
                    || (clips[i] != null && _meshes[i] == null)) return false;
            return true;
        }

        private void ReleaseMeshes()
        {
            if (_meshes != null)
                foreach (Mesh mesh in _meshes) if (mesh != null) Object.Destroy(mesh);
            _meshes = null;
            _templates = null;
        }

        private void OnDestroy()
        {
            Stop();
        }
    }
}
