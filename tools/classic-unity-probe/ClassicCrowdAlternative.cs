// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using DK2.Art;
using DK2.Assets;
using DK2.Data.Model;
using UnityEngine;

namespace DK2.Unity
{
    public sealed class ClassicCrowdBank
    {
        public sealed class Segment
        {
            public Mesh Template;
            public Mesh[] Frames;
            public Material[] Materials;
            public int FrameCount;
        }
        public readonly Segment[] Segments = new Segment[3];
        public CreatureAnimationTiming Timing;
        public long PositionStorageBytes;
        public long DecodedPositionBytes;
        public int Vertices;

        public ClassicCrowdBank(InstallArtProvider art, ClassicMeshes meshes, ResolvedAsset asset, string representation, Transform parent)
        {
            if (!art.TryGetResource(asset, out Dk2ArtResource resource)) throw new InvalidOperationException("Missing resource timing");
            Timing = new CreatureAnimationTiming
            {
                ResourceType = resource.Type, FrameCount = resource.FrameCount, FramesPerSecond = resource.FramesPerSecond,
                Flags = resource.AnimationFlags, StartSegmentFrames = resource.StartSegmentFrames, EndSegmentFrames = resource.EndSegmentFrames,
            };
            for (int part = 0; part < 3; part++)
            {
                if (part == (int)AnimationSegment.Start && (resource.AnimationFlags & CreatureAnimationFlags.StartSegment) == 0) continue;
                if (part == (int)AnimationSegment.End && (resource.AnimationFlags & CreatureAnimationFlags.EndSegment) == 0) continue;
                if (!art.TryResolveSegment(asset, (AnimationSegment)part, out ResolvedAsset resolved)
                    || !art.TryLoadModel(resolved, out var model)) throw new InvalidOperationException("Missing candidate segment");
                var clip = new RenderAnimation(model);
                GameObject source = meshes.TryCreate(resolved, "Hidden candidate source", parent);
                source.SetActive(false);
                Mesh template = source.GetComponent<MeshFilter>().sharedMesh;
                Material[] materials = source.GetComponent<MeshRenderer>().sharedMaterials;
                var bank = new Segment { Template = template, Materials = materials, FrameCount = clip.FrameCount };
                Segments[part] = bank;
                int count = clip.Mesh.Positions.Count;
                Vertices += count;
                DecodedPositionBytes += clip.PositionBytes;
                var positions = new System.Numerics.Vector3[count];
                var vertices = new Vector3[count];
                var pixels = representation == "vat" ? new Color[count * clip.FrameCount] : null;
                if (representation == "frames") bank.Frames = new Mesh[clip.FrameCount];
                Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
                bool firstPosition = true;
                for (int frame = 0; frame < clip.FrameCount; frame++)
                {
                    clip.Sample(clip.FrameCount == 1 ? 0 : frame / (float)(clip.FrameCount - 1), false, positions);
                    for (int vertex = 0; vertex < count; vertex++)
                    {
                        vertices[vertex] = new Vector3(positions[vertex].X, positions[vertex].Y, positions[vertex].Z);
                        if (firstPosition) { bounds = new Bounds(vertices[vertex], Vector3.zero); firstPosition = false; }
                        else bounds.Encapsulate(vertices[vertex]);
                        if (pixels != null) pixels[frame * count + vertex] = new Color(vertices[vertex].x, vertices[vertex].y, vertices[vertex].z, 1);
                    }
                    if (bank.Frames != null)
                    {
                        bank.Frames[frame] = UnityEngine.Object.Instantiate(template);
                        bank.Frames[frame].SetVertices(vertices);
                        bank.Frames[frame].RecalculateBounds();
                        bank.Frames[frame].UploadMeshData(true);
                    }
                }
                if (pixels != null)
                {
                    if (count > SystemInfo.maxTextureSize || clip.FrameCount > SystemInfo.maxTextureSize)
                        throw new InvalidOperationException("VAT exceeds texture size");
                    var texture = new Texture2D(count, clip.FrameCount, TextureFormat.RGBAFloat, false, true)
                    { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Decoded KMF vertex positions" };
                    texture.SetPixels(pixels);
                    texture.Apply(false, true);
                    Shader shader = Shader.Find("DK2/Probe/VAT Lit");
                    if (shader == null || !shader.isSupported) throw new InvalidOperationException("VAT Lit shader unavailable");
                    bank.Template = UnityEngine.Object.Instantiate(template);
                    bank.Template.bounds = bounds;
                    bank.Materials = new Material[materials.Length];
                    for (int material = 0; material < materials.Length; material++)
                    {
                        if (materials[material].renderQueue > 2500) throw new InvalidOperationException("VAT prototype supports opaque materials only");
                        var replacement = new Material(materials[material]) { shader = shader };
                        replacement.SetTexture("_VatPositions", texture);
                        replacement.SetFloat("_VatFrameCount", clip.FrameCount);
                        bank.Materials[material] = replacement;
                    }
                    PositionStorageBytes += (long)count * clip.FrameCount * 16;
                }
                else PositionStorageBytes += (long)count * clip.FrameCount * 12;
            }
        }
    }

    public sealed class ClassicCrowdAlternative
    {
        private readonly ClassicCrowdBank _bank;
        private readonly RenderAnimationClock _clock;
        private readonly MeshFilter _filter;
        private readonly MeshRenderer _renderer;
        private readonly MaterialPropertyBlock _properties = new MaterialPropertyBlock();
        private readonly bool _vat;
        private long _advanced;
        private int _segment = -1, _frame = -1;
        private static readonly int FrameProperty = Shader.PropertyToID("_VatFrame");

        public ClassicCrowdAlternative(GameObject go, ClassicCrowdBank bank, bool vat)
        {
            _bank = bank;
            _vat = vat;
            _clock = new RenderAnimationClock(bank.Timing, true);
            _filter = go.GetComponent<MeshFilter>();
            _renderer = go.GetComponent<MeshRenderer>();
            Draw(0);
        }

        public void Draw(double seconds)
        {
            double tickTime = seconds * 4;
            long tick = (long)Math.Floor(tickTime);
            while (_advanced <= tick) { _clock.Advance(); _advanced++; }
            AnimationPose pose = _clock.Sample((float)(tickTime - tick));
            int segment = (int)pose.Segment;
            ClassicCrowdBank.Segment bank = _bank.Segments[segment];
            if (segment != _segment)
            {
                _renderer.sharedMaterials = bank.Materials;
                _filter.sharedMesh = bank.Template;
                _segment = segment;
                _frame = -1;
            }
            float frame = Mathf.Clamp01(pose.Phase) * (bank.FrameCount - 1);
            if (_vat)
            {
                _properties.SetFloat(FrameProperty, frame);
                _renderer.SetPropertyBlock(_properties);
            }
            else
            {
                int selected = Mathf.RoundToInt(frame);
                if (_frame != selected) { _filter.sharedMesh = bank.Frames[selected]; _frame = selected; }
            }
        }
    }
}
