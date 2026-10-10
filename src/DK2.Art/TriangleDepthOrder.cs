// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;

namespace DK2.Art;

public readonly record struct RenderTriangle(int A, int B, int C, int MaterialIndex);
public sealed record RenderTriangleRun(int MaterialIndex, IReadOnlyList<RenderTriangle> Triangles);

/// <summary>Back-to-front blended triangles, batching only consecutive equal materials.</summary>
public static class TriangleDepthOrder
{
    public static IReadOnlyList<RenderTriangleRun> Sort(IReadOnlyList<Vector3> positions,
        IReadOnlyList<RenderTriangle> triangles, Vector3 cameraForward)
    {
        var sorted = triangles.Select((triangle, order) => (triangle, order,
            depth: Vector3.Dot(positions[triangle.A] + positions[triangle.B] + positions[triangle.C], cameraForward)))
            .OrderByDescending(entry => entry.depth).ThenBy(entry => entry.order);
        var runs = new List<RenderTriangleRun>();
        List<RenderTriangle>? current = null;
        int material = -1;
        foreach (var entry in sorted)
        {
            if (current is null || material != entry.triangle.MaterialIndex)
            {
                material = entry.triangle.MaterialIndex;
                current = [];
                runs.Add(new RenderTriangleRun(material, current));
            }
            current.Add(entry.triangle);
        }
        return runs;
    }
}
