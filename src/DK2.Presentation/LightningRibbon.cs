// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;

namespace DK2.Presentation;

public sealed record LightningRibbon(IReadOnlyList<Vector3> Vertices, IReadOnlyList<Vector2> Uvs,
    IReadOnlyList<int> Triangles)
{
    public static LightningRibbon Build(IReadOnlyList<Vector3> nodes)
    {
        var sides = new Vector3[nodes.Count];
        for (int i = 1; i < nodes.Count - 1; i++)
        {
            Vector3 previous = Project(nodes[i - 1]), current = Project(nodes[i]), next = Project(nodes[i + 1]);
            Vector3 before = Normal(new(previous.Y - current.Y, current.X - previous.X, 0));
            Vector3 after = Normal(new(current.Y - next.Y, next.X - current.X, 0));
            Vector3 sum = before + after;
            sides[i] = (sum.LengthSquared() > .01f ? Normal(sum) : before) * LightningBolts.RibbonHalfWidth;
        }
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        for (int i = 0; i < nodes.Count - 1; i++)
        {
            int v = vertices.Count;
            vertices.Add(nodes[i] - sides[i]); vertices.Add(nodes[i] + sides[i]);
            vertices.Add(nodes[i + 1] - sides[i + 1]); vertices.Add(nodes[i + 1] + sides[i + 1]);
            uvs.Add(new(0, 0)); uvs.Add(new(0, 1)); uvs.Add(new(1, 0)); uvs.Add(new(1, 1));
            triangles.AddRange([v, v + 1, v + 2, v + 2, v + 1, v + 3,
                v + 2, v + 1, v, v + 3, v + 1, v + 2]);
        }
        return new(vertices, uvs, triangles);
    }

    private static Vector3 Normal(Vector3 value) => value.LengthSquared() > 1e-12f
        ? Vector3.Normalize(value) : Vector3.Zero;
    private static Vector3 Project(Vector3 point) => new(point.X / Math.Max(.1f, point.Z),
        point.Y / Math.Max(.1f, point.Z), 0);
}
