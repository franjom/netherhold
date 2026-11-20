// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers;
using DK2.Art;
using DK2.Presentation;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace DK2.GodotAdapter;

public partial class WorldScene
{
    private static readonly StringName AnimationPhaseParameter = "dk2_animation_phase";
    private readonly Dictionary<RenderAnimation, VertexClip> _vertexClips = new();
    private long _vertexTextureBytes, _animationPoseUpdates;
    private const int InitialVertexStagingBytes = 4 * 1024 * 1024;
    private byte[]? _vertexStaging;

    private byte[] VertexStaging(int byteCount)
    {
        if (_vertexStaging == null || _vertexStaging.Length < byteCount)
        {
            byte[] next = ArrayPool<byte>.Shared.Rent(Math.Max(InitialVertexStagingBytes, byteCount));
            if (_vertexStaging != null) ArrayPool<byte>.Shared.Return(_vertexStaging);
            _vertexStaging = next;
        }
        return _vertexStaging;
    }

    private void FreeVertexStaging()
    {
        if (_vertexStaging == null) return;
        ArrayPool<byte>.Shared.Return(_vertexStaging);
        _vertexStaging = null;
    }

    private VertexClip VertexAnimation(RenderAnimation clip, ModelMesh template)
    {
        if (_vertexClips.TryGetValue(clip, out VertexClip? cached)) return cached;
        var built = new VertexClip(clip, template.Box, this);
        _vertexClips.Add(clip, built);
        _vertexTextureBytes += built.TextureBytes;
        return built;
    }

    /// <summary>All positions of a decoded clip, in Godot axes. CUSTOM2.x maps each surface vertex to the source index.
    /// The box contains every frame; linear interpolation cannot leave it, in any camera or shadow viewport.</summary>
    private sealed class VertexClip
    {
        private static readonly StringName VerticesParameter = "animation_vertices", LayoutParameter = "animation_layout",
            FramesParameter = "animation_frames";
        private readonly ImageTexture _texture;
        private readonly int _width, _vertices, _frames;
        public Aabb Box { get; }
        public long TextureBytes { get; }

        public VertexClip(RenderAnimation clip, Aabb firstFrame, WorldScene scene)
        {
            _vertices = clip.Mesh.Positions.Count;
            _frames = clip.FrameCount;
            _width = Math.Min(1024, Math.Max(1, _vertices));
            int height = Math.Max(1, checked((_vertices * _frames + _width - 1) / _width));
            int byteCount = checked(_width * height * 3 * sizeof(float));
            Span<byte> bytes = scene.VertexStaging(byteCount).AsSpan(0, byteCount);
            NVector3 low = new(float.PositiveInfinity), high = new(float.NegativeInfinity);
            for (int f = 0; f < _frames; f++)
            {
                ReadOnlySpan<NVector3> frame = clip.Frame(f);
                for (int v = 0; v < _vertices; v++)
                {
                    NVector3 point = RightHandedFrame.Point(frame[v]);
                    low = NVector3.Min(low, point); high = NVector3.Max(high, point);
                    int offset = (f * _vertices + v) * 12;
                    BitConverter.TryWriteBytes(bytes.Slice(offset, 4), point.X);
                    BitConverter.TryWriteBytes(bytes.Slice(offset + 4, 4), point.Y);
                    BitConverter.TryWriteBytes(bytes.Slice(offset + 8, 4), point.Z);
                }
            }
            // Godot sorts transparents by the box centre in every camera. Keep the first frame's centre while
            // deriving extents from all frames, so culling covers the clip without moving its blended-order key.
            Vector3 centre = firstFrame.GetCenter();
            NVector3 anchor = new(centre.X, centre.Y, centre.Z);
            NVector3 extent = NVector3.Max(NVector3.Abs(low - anchor), NVector3.Abs(high - anchor));
            Box = _vertices == 0 ? new Aabb() : new Aabb(centre - ToGodot(extent), ToGodot(extent * 2f));
            bytes.Slice(checked(_vertices * _frames * 3 * sizeof(float))).Clear();
            TextureBytes = byteCount;
            using Image image = Image.CreateFromData(_width, height, false, Image.Format.Rgbf, bytes);
            _texture = ImageTexture.CreateFromImage(image);
        }

        public void Bind(ShaderMaterial material)
        {
            material.SetShaderParameter(VerticesParameter, _texture);
            material.SetShaderParameter(LayoutParameter, new Vector2I(_width, _vertices));
            material.SetShaderParameter(FramesParameter, _frames);
        }
    }
}
