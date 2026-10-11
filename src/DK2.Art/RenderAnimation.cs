// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Art.Kmf;

namespace DK2.Art;

/// <summary>A shared decoded clip; each renderer supplies its own mutable position buffer.</summary>
public sealed class RenderAnimation
{
    private readonly Vector3[][] _frames;

    public RenderAnimation(KmfModel model)
    {
        if (model is null) throw new ArgumentNullException(nameof(model));
        if (model.Animation is not { IsComplete: true } animation || animation.Frames.Count == 0)
            throw new ArgumentException("A complete animated KMF is required.", nameof(model));
        Mesh = RenderMesh.FromModel(model);
        _frames = new Vector3[animation.Frames.Count][];
        for (int frame = 0; frame < _frames.Length; frame++)
        {
            _frames[frame] = animation.Frames[frame].Primitives
                .SelectMany(p => p.Vertices).Select(v => RenderMesh.ToRenderFrame(v.Position)).ToArray();
            if (_frames[frame].Length != Mesh.Positions.Count)
                throw new ArgumentException("Animation frame vertex counts differ.", nameof(model));
        }
    }

    public RenderMesh Mesh { get; }
    public int FrameCount => _frames.Length;
    public long PositionBytes => (long)FrameCount * Mesh.Positions.Count * 3 * sizeof(float);

    /// <summary>The animated drawer's fractional frame (kmf-animation-playback.md §2).
    /// Loop time 1 stays on the last frame; only values greater than 1 wrap.</summary>
    public void Sample(float normalizedTime, bool loop, Vector3[] positions)
    {
        if (float.IsNaN(normalizedTime) || float.IsInfinity(normalizedTime))
            throw new ArgumentOutOfRangeException(nameof(normalizedTime));
        if (positions is null) throw new ArgumentNullException(nameof(positions));
        if (positions.Length != Mesh.Positions.Count)
            throw new ArgumentException("The destination must match the clip vertex count.", nameof(positions));
        float time = Math.Max(normalizedTime, 0f);
        if (time > 1f) time = loop ? time - (float)Math.Ceiling(time) + 1f : 1f;
        float frame = time * (FrameCount - 1);
        int first = (int)frame, second = Math.Min(first + 1, FrameCount - 1);
        float fraction = frame - first;
        for (int i = 0; i < positions.Length; i++)
            positions[i] = Vector3.Lerp(_frames[first][i], _frames[second][i], fraction);
    }
}
