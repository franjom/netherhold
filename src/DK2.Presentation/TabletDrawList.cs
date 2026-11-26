// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Art.Kmf;

namespace DK2.Presentation;

/// <summary><c>Tablet.kmf</c> as the reward tablet draws it: the model, its sparkle points (the mesh's control points;
/// 0x00577DC0 reads the CTRL table, and Tablet.kmf's sixteen ring its edge) and the screen winding of its front at rest
/// (the sign of the y-down cross product of the first triangle of the material with the item frames).</summary>
public sealed class TabletMesh
{
    public TabletMesh(KmfModel model)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        KmfMesh mesh = model.Mesh ?? throw new ArgumentException("The tablet model has no mesh.", nameof(model));
        var points = new List<Vector3>(model.ControlPoints.Count);
        foreach (KmfControlPoint control in model.ControlPoints)
        {
            if (control.Vertex < model.Geometry.Count)
            {
                points.Add(model.Geometry[(int)control.Vertex]);
            }
        }

        Points = points.ToArray();
        bool front = false;
        foreach (KmfPrimitive primitive in mesh.Primitives)
        {
            IReadOnlyList<byte> indices = primitive.IndicesByDetailLevel[0];
            VertexCapacity += indices.Count / 3 * 3;
            // The front is the material with the item frames; at rest it faces the viewer. The first one counts.
            if (front || model.Materials[(int)primitive.MaterialIndex].TextureNames.Count < 2 || indices.Count < 3)
            {
                continue;
            }

            Vector3 a = primitive.Vertices[indices[0]].Position, b = primitive.Vertices[indices[1]].Position,
                c = primitive.Vertices[indices[2]].Position;
            FrontWinding = Math.Sign((b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X));
            front = true;
        }
        Primitives = mesh.Primitives;
    }

    public KmfModel Model { get; }
    public IReadOnlyList<KmfPrimitive> Primitives { get; }
    public Vector3[] Points { get; }
    public int FrontWinding { get; }

    /// <summary>The most vertices one frame can draw: every whole triangle of detail level 0.</summary>
    public int VertexCapacity { get; }
}

/// <summary>One drawn vertex: screen pixels from the top left, and the texture coordinate.</summary>
public readonly record struct TabletVertex(float X, float Y, float U, float V);

/// <summary>A run of <see cref="TabletDrawList.Vertices"/>, three per triangle, drawn with one texture.</summary>
public readonly record struct TabletBatch(string Texture, int First, int Count);

/// <summary>A sparkle's square: its centre in screen pixels and half its side; the texture is <c>TabletSparkle</c>,
/// drawn (0,0) top left to (1,1) bottom right.</summary>
public readonly record struct TabletSparkleQuad(float X, float Y, float Half);

/// <summary>The tablet in screen pixels (0x0042F040), as a draw list the adapter blits: turned and scaled about its
/// centre, the origin plus its position (the win screen's is ½ width, 5/12 height with position 0; the fly-in's origin
/// is the top left), DK2's pixels taken as 768-high reference units; faces wound away from the viewer are left out. Then
/// its sparkles, each a TabletSparkle square as wide as its frames left (HYPOTHESIS). One batch per textured primitive, in
/// the mesh's order, even when every face of it is culled. Reuses its buffers, so a frame allocates nothing.</summary>
public sealed class TabletDrawList
{
    internal const float ReferenceHeight = 768;
    private readonly TabletVertex[] _vertices;
    private readonly TabletBatch[] _batches;
    private TabletSparkleQuad[] _sparkles;
    private int _vertexCount, _batchCount, _sparkleCount;

    public TabletDrawList(TabletMesh mesh)
    {
        Mesh = mesh ?? throw new ArgumentNullException(nameof(mesh));
        _vertices = new TabletVertex[mesh.VertexCapacity];
        _batches = new TabletBatch[mesh.Primitives.Count];
        _sparkles = new TabletSparkleQuad[RewardTablet.SparkleCapacityFor(mesh.Points.Length)];
    }

    public TabletMesh Mesh { get; }
    public ReadOnlySpan<TabletVertex> Vertices => _vertices.AsSpan(0, _vertexCount);
    public ReadOnlySpan<TabletBatch> Batches => _batches.AsSpan(0, _batchCount);
    public ReadOnlySpan<TabletSparkleQuad> Sparkles => _sparkles.AsSpan(0, _sparkleCount);

    /// <param name="itemId">The reward item, which picks the front's texture frame.</param>
    /// <param name="originX">The tablet's origin in screen pixels from the left.</param>
    /// <param name="originY">The tablet's origin in screen pixels from the top.</param>
    /// <param name="screenHeight">The screen's height in pixels, which sets the reference unit.</param>
    public void Build(RewardTablet tablet, byte itemId, float originX, float originY, int screenHeight)
    {
        if (tablet is null)
        {
            throw new ArgumentNullException(nameof(tablet));
        }

        _vertexCount = 0; _batchCount = 0; _sparkleCount = 0;
        float unit = screenHeight / ReferenceHeight;
        float cx = originX + tablet.X * unit, cy = originY + tablet.Y * unit;
        Matrix4x4 turn = tablet.Turn();
        IReadOnlyList<KmfPrimitive> primitives = Mesh.Primitives;
        for (int p = 0; p < primitives.Count; p++)
        {
            KmfPrimitive primitive = primitives[p];
            IReadOnlyList<string> textures = Mesh.Model.Materials[(int)primitive.MaterialIndex].TextureNames;
            if (textures.Count == 0)
            {
                continue;
            }

            int first = _vertexCount;
            IReadOnlyList<byte> indices = primitive.IndicesByDetailLevel[0];
            for (int i = 0; i + 2 < indices.Count; i += 3)
            {
                KmfVertex a = primitive.Vertices[indices[i]], b = primitive.Vertices[indices[i + 1]], c = primitive.Vertices[indices[i + 2]];
                Vector3 pa = tablet.Project(a.Position, turn), pb = tablet.Project(b.Position, turn), pc = tablet.Project(c.Position, turn);
                if (Math.Sign((pb.X - pa.X) * (pc.Y - pa.Y) - (pb.Y - pa.Y) * (pc.X - pa.X)) != Mesh.FrontWinding)
                {
                    continue;
                }

                Add(a, pa, cx, cy, unit);
                Add(b, pb, cx, cy, unit);
                Add(c, pc, cx, cy, unit);
            }
            _batches[_batchCount++] = new TabletBatch(textures[RewardTablet.TextureFrame(textures.Count, itemId)], first, _vertexCount - first);
        }
        ReadOnlySpan<RewardTablet.Sparkle> sparkles = tablet.Sparkles;
        if (sparkles.Length > _sparkles.Length)
        {
            _sparkles = new TabletSparkleQuad[sparkles.Length];
        }

        foreach (RewardTablet.Sparkle s in sparkles)
        {
            _sparkles[_sparkleCount++] = new TabletSparkleQuad(originX + s.X * unit, originY + s.Y * unit, s.Life * unit / 2);
        }
    }

    private void Add(KmfVertex vertex, Vector3 at, float cx, float cy, float unit) =>
        _vertices[_vertexCount++] = new TabletVertex(cx + at.X * unit, cy + at.Y * unit, vertex.TextureCoordinate.X, vertex.TextureCoordinate.Y);
}
