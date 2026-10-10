// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Art.Kmf;

namespace DK2.Art;

/// <summary>
/// One KMF mesh in the renderer's frame, ready to copy into an engine: X east, Y up, Z north,
/// one unit a tile, left-handed (Unity's). Triangles are grouped by material, one list per
/// <see cref="KmfModel.Materials"/> entry, empty where a material is unused.
/// </summary>
/// <remarks>
/// A DK2 point maps as <c>(x, -z, -y)</c> and DK2's own vertex order is kept (ADR-0411). UVs are
/// as decoded, V counted down from the image's top row, so an engine that counts V up must take
/// the image's rows top first.
/// </remarks>
public sealed class RenderMesh
{
    private RenderMesh(Vector3[] positions, Vector3[] normals, Vector2[] uvs, int[][] triangles,
        KmfBlend[] blends)
    {
        Positions = positions;
        Normals = normals;
        Uvs = uvs;
        Triangles = triangles;
        Blends = blends;
    }

    public IReadOnlyList<Vector3> Positions { get; }
    public IReadOnlyList<Vector3> Normals { get; }
    public IReadOnlyList<Vector2> Uvs { get; }

    /// <summary>Vertex indices, three a triangle, per material.</summary>
    public IReadOnlyList<int[]> Triangles { get; }

    /// <summary>Each triangle list's blend, from its material's flags
    /// (<see cref="KmfMaterial.Blend"/>); opaque where the model has no material.</summary>
    public IReadOnlyList<KmfBlend> Blends { get; }

    /// <summary>DK2's frame to the renderer's: <c>(x, -z, -y)</c>.</summary>
    public static Vector3 ToRenderFrame(Vector3 dk2) => new(dk2.X, -dk2.Z, -dk2.Y);

    /// <summary>The model's static mesh, or frame <paramref name="frame"/> of its animation, at
    /// detail level 0 (each primitive's first index list), as <c>GlbWriter</c> takes.</summary>
    public static RenderMesh FromModel(KmfModel model, int frame = 0)
    {
        if (model is null) throw new ArgumentNullException(nameof(model));
        if (frame < 0) throw new ArgumentOutOfRangeException(nameof(frame));
        KmfMesh mesh = model.Animation is { IsComplete: true } animation && frame < animation.Frames.Count
            ? animation.Frames[frame]
            : model.Mesh ?? throw new ArgumentException("A KMF group has no mesh.", nameof(model));

        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>[Math.Max(model.Materials.Count, 1)];
        for (int i = 0; i < triangles.Length; i++) triangles[i] = [];

        foreach (KmfPrimitive primitive in mesh.Primitives)
        {
            int first = positions.Count;
            foreach (KmfVertex vertex in primitive.Vertices)
            {
                positions.Add(ToRenderFrame(vertex.Position));
                normals.Add(ToRenderFrame(vertex.Normal));
                uvs.Add(vertex.TextureCoordinate);
            }
            int material = (int)Math.Min(primitive.MaterialIndex, (uint)(triangles.Length - 1));
            if (primitive.IndicesByDetailLevel.Count == 0) continue;
            foreach (byte index in primitive.IndicesByDetailLevel[0])
                triangles[material].Add(first + index);
        }
        var blends = new KmfBlend[triangles.Length];
        for (int i = 0; i < blends.Length && i < model.Materials.Count; i++)
            blends[i] = model.Materials[i].Blend;
        return new RenderMesh([.. positions], [.. normals], [.. uvs],
            triangles.Select(list => list.ToArray()).ToArray(), blends);
    }
}
