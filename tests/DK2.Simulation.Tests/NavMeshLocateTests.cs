// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Pathfinding;

namespace DK2.Simulation.Tests;

/// <summary>The original's locate (`0x004C98B0`, ADR-0486): a walk from a cell cache of last
/// results that returns the first triangle it reaches holding the point, so a vertex names
/// whichever of its fan triangles the walk meets first, which depends on where the last locate
/// in that cell ended. The cache is therefore state: saved, and hashed.</summary>
public sealed class NavMeshLocateTests
{
    private const int U = NavMesh.UnitsPerTile;

    [Fact]
    public void ALocateAtAVertexNamesTheFanTriangleReachedFirst_WhichTheLastLocateDecides()
    {
        var mesh = new NavMesh();
        mesh.Reset(0, 0, 8, 8);
        mesh.InsertPoint(4, 4);
        // Four triangles meet at (4,4); approached from near each canvas corner the walk ends in
        // the one on that side.
        var reached = new HashSet<int>();
        foreach ((int x, int y) in new[] { (1, 1), (7, 1), (7, 7), (1, 7) })
        {
            int near = mesh.Locate(x * U, y * U);
            Assert.True(near >= 0);
            int atVertex = mesh.Locate(4 * U, 4 * U);
            Assert.Equal(near, atVertex);
            reached.Add(atVertex);
        }
        Assert.Equal(4, reached.Count);
    }

    [Fact]
    public void TheLocateCacheIsSavedAndHashed()
    {
        var mesh = new NavMesh();
        mesh.Reset(0, 0, 8, 8);
        mesh.InsertPoint(4, 4);
        mesh.Locate(1 * U, 1 * U);
        ulong left = HashOf(mesh);
        NavMesh copy = RoundTrip(mesh);
        Assert.Equal(left, HashOf(copy));
        Assert.Equal(mesh.Locate(4 * U, 4 * U), copy.Locate(4 * U, 4 * U));

        mesh.Locate(7 * U, 7 * U);
        Assert.NotEqual(left, HashOf(mesh));
        Assert.NotEqual(copy.Locate(4 * U, 4 * U), mesh.Locate(4 * U, 4 * U));
    }

    /// <summary>The reset's diagonal runs from (x1, y0) to (x0, y1), as the original's
    /// (`0x004C8D10`) does; ours ran the other way before ADR-0486.</summary>
    [Fact]
    public void TheResetSplitsTheCanvasAlongTheOriginalsDiagonal()
    {
        var mesh = new NavMesh();
        mesh.Reset(0, 0, 8, 8);
        Assert.Equal(2, mesh.LiveTriangleCount());
        Assert.True(HasEdge(mesh, 8, 0, 0, 8));
        Assert.False(HasEdge(mesh, 0, 0, 8, 8));
        // A point on that diagonal splits both triangles: four, and the diagonal is gone.
        mesh.InsertPoint(4, 4);
        Assert.Equal(4, mesh.LiveTriangleCount());
        Assert.False(HasEdge(mesh, 8, 0, 0, 8));
        Assert.True(HasEdge(mesh, 8, 0, 4, 4));
        NavMeshTests.AssertInvariants(mesh);
    }

    private static bool HasEdge(NavMesh mesh, int ax, int ay, int bx, int by)
    {
        for (int t = 0; t < mesh.TriangleSlots; t++)
        {
            if (!mesh.IsTriangleLive(t)) continue;
            for (int i = 0; i < 3; i++)
            {
                int p = mesh.Vertex(t, i), q = mesh.Vertex(t, (i + 1) % 3);
                bool pa = mesh.PointX(p) == ax && mesh.PointY(p) == ay, qb = mesh.PointX(q) == bx && mesh.PointY(q) == by;
                bool pb = mesh.PointX(p) == bx && mesh.PointY(p) == by, qa = mesh.PointX(q) == ax && mesh.PointY(q) == ay;
                if ((pa && qb) || (pb && qa)) return true;
            }
        }
        return false;
    }

    private static ulong HashOf(NavMesh mesh)
    {
        DK2.Core.Hash64 hash = DK2.Core.Hash64.Start();
        mesh.HashInto(ref hash);
        return hash.Value;
    }

    private static NavMesh RoundTrip(NavMesh mesh)
    {
        using var ms = new MemoryStream();
        mesh.Write(new BinaryWriter(ms));
        ms.Position = 0;
        return NavMesh.Read(new BinaryReader(ms));
    }
}
