// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Pathfinding;

namespace DK2.Simulation.Tests;

/// <summary>
/// The navigation mesh's storage, reset and point insertion (navigation-mesh-and-search.md §2.1,
/// ADR-0157 phase 1). Every test ends by checking the mesh's invariants, because a triangulation
/// bug that leaves neighbours asymmetric shows up later as a route through a wall.
/// </summary>
public class NavMeshTests
{
    private const int U = NavMesh.UnitsPerTile;

    [Fact]
    public void ResetIsFourCornersAndTwoTriangles()
    {
        var mesh = new NavMesh();
        mesh.Reset(8, 6);

        Assert.Equal(4, mesh.PointSlots);
        Assert.Equal(2, mesh.LiveTriangleCount());
        AssertInvariants(mesh);
    }

    /// <summary>The original's reset sets no constraint bit (+0xD = 7: every edge bit, no
    /// constraint); only the partial rebuild's side marker ever sets one (§2.2).</summary>
    [Fact]
    public void TheResetHasFourBordersAndNoConstraint()
    {
        var mesh = new NavMesh();
        mesh.Reset(8, 6);

        int borders = 0;
        for (int t = 0; t < mesh.TriangleSlots; t++)
            for (int i = 0; i < 3; i++)
            {
                Assert.False(mesh.IsConstraint(t, i));
                if (mesh.Neighbour(t, i) == NavMesh.NoNeighbour) borders++;
            }

        Assert.Equal(4, borders);
    }

    [Fact]
    public void LocateFindsTheTriangleUnderAPosition()
    {
        var mesh = new NavMesh();
        mesh.Reset(8, 6);

        int nearOrigin = mesh.Locate(U / 2, U / 2);
        int nearFarCorner = mesh.Locate((8 * U) - (U / 2), (6 * U) - (U / 2));
        Assert.True(nearOrigin >= 0);
        Assert.True(nearFarCorner >= 0);
        Assert.Equal(-1, mesh.Locate(-1, 0));
        Assert.Equal(-1, mesh.Locate((8 * U) + 1, U));
    }

    [Fact]
    public void AnInteriorPointSplitsOneTriangleIntoThree()
    {
        var mesh = new NavMesh();
        mesh.Reset(8, 8);

        // (1, 6) lies strictly inside the lower-left triangle (0,0)-(8,8)-(0,8).
        mesh.InsertPoint(1, 6);

        Assert.Equal(4, mesh.LiveTriangleCount());
        Assert.Equal(5, mesh.PointSlots);
        AssertInvariants(mesh);
    }

    [Fact]
    public void APointOnTheSharedEdgeSplitsBothTrianglesIntoFour()
    {
        var mesh = new NavMesh();
        mesh.Reset(8, 8);

        // (4, 4) lies on the diagonal shared by the two reset triangles.
        mesh.InsertPoint(4, 4);

        Assert.Equal(4, mesh.LiveTriangleCount());
        AssertInvariants(mesh);
    }

    [Fact]
    public void APointOnTheMapBorderSplitsOnlyOneTriangle()
    {
        var mesh = new NavMesh();
        mesh.Reset(8, 8);

        mesh.InsertPoint(3, 0);

        Assert.Equal(3, mesh.LiveTriangleCount());
        AssertInvariants(mesh);
        // The border edge is now two edges; both still border nothing.
        int borders = 0;
        for (int t = 0; t < mesh.TriangleSlots; t++)
            for (int i = 0; i < 3; i++)
                if (mesh.Neighbour(t, i) == NavMesh.NoNeighbour) borders++;
        Assert.Equal(5, borders);
    }

    /// <summary>The original does nothing when the corner is already a vertex, and neither do we
    /// — inserting the same corner twice must not grow the mesh.</summary>
    [Fact]
    public void ReinsertingAnExistingCornerReturnsItAndChangesNothing()
    {
        var mesh = new NavMesh();
        mesh.Reset(8, 8);
        int p = mesh.InsertPoint(2, 5);
        int before = mesh.LiveTriangleCount();

        Assert.Equal(p, mesh.InsertPoint(2, 5));
        Assert.Equal(before, mesh.LiveTriangleCount());
        Assert.Equal(1, mesh.InsertPoint(8, 0));   // a reset corner, by index
        AssertInvariants(mesh);
    }

    /// <summary>A grid of corners in a fixed order gives a fixed mesh: the determinism the router
    /// inherits from insertion order.</summary>
    [Fact]
    public void TheSameInsertionsGiveTheSameMesh()
    {
        NavMesh a = Build(), b = Build();
        Assert.Equal(a.TriangleSlots, b.TriangleSlots);
        for (int t = 0; t < a.TriangleSlots; t++)
        {
            Assert.Equal(a.NavByte(t), b.NavByte(t));
            for (int i = 0; i < 3; i++)
            {
                Assert.Equal(a.Vertex(t, i), b.Vertex(t, i));
                Assert.Equal(a.Neighbour(t, i), b.Neighbour(t, i));
            }
        }

        static NavMesh Build()
        {
            var m = new NavMesh();
            m.Reset(12, 12);
            for (int y = 2; y <= 10; y += 4)
                for (int x = 2; x <= 10; x += 4)
                    m.InsertPoint(x, y);
            AssertInvariants(m);
            return m;
        }
    }

    // ---- vertex removal (navigation-partial-rebuild.md, ADR-0461) -------------------------

    /// <summary>An interior point splits its triangle into three; removing it (degree 3, `0x004CB8C0`)
    /// merges them back, frees two triangle slots and the point, and the next insertion reuses the
    /// freed slots last in, first out, as the original's allocators pop the head of each list.</summary>
    [Fact]
    public void RemovingADegreeThreeVertexFreesItsSlotsForReuse()
    {
        var mesh = new NavMesh();
        mesh.Reset(8, 6);
        int p = mesh.InsertPoint(3, 2);
        Assert.Equal(4, mesh.LiveTriangleCount());
        Assert.True(mesh.TryLocateVertex(3, 2, out int t, out int s));

        Assert.True(mesh.RemoveVertex(t, s));
        Assert.Equal(2, mesh.LiveTriangleCount());
        Assert.False(mesh.IsPointLive(p));
        Assert.Equal(4, mesh.TriangleSlots);
        AssertInvariants(mesh);

        // The removal pushed the second then the third triangle of the fan, so the third comes
        // back first: the next split's first new triangle takes the higher of the two freed slots.
        int freedHigh = -1, freedLow = -1;
        for (int i = 0; i < mesh.TriangleSlots; i++)
            if (!mesh.IsTriangleLive(i)) { if (i > freedHigh) { freedLow = freedHigh; freedHigh = i; } else freedLow = i; }
        int again = mesh.InsertPoint(5, 3);
        Assert.Equal(p, again);
        Assert.Equal(4, mesh.TriangleSlots);
        Assert.Equal(4, mesh.LiveTriangleCount());
        Assert.True(freedHigh >= 0 && freedLow >= 0 && mesh.IsTriangleLive(freedHigh) && mesh.IsTriangleLive(freedLow));
        AssertInvariants(mesh);
    }

    /// <summary>A point on the reset's diagonal splits both triangles, so it has degree 4; removing it
    /// (`0x004CB170`) re-joins the four across one diagonal.</summary>
    [Fact]
    public void RemovingADegreeFourVertexRejoinsItsFan()
    {
        var mesh = new NavMesh();
        mesh.Reset(0, 0, 8, 8);
        int p = mesh.InsertPoint(4, 4);
        Assert.Equal(4, mesh.LiveTriangleCount());
        Assert.True(mesh.TryLocateVertex(4, 4, out int t, out int s));
        int before = mesh.TriangleSlots;

        int degree = t >= 0 ? mesh.ReduceVertexDegree(ref t, ref s) : -1;
        Assert.Equal(4, degree);
        Assert.True(mesh.RemoveDegree4Vertex(t, s));
        Assert.Equal(2, mesh.LiveTriangleCount());
        Assert.False(mesh.IsPointLive(p));
        Assert.Equal(before, mesh.TriangleSlots);
        AssertInvariants(mesh);
    }

    /// <summary>A vertex of higher degree is flipped down first (`0x004CAF40`): with four points
    /// around one, the degree is 5 or more and the reduction brings it to 3 or 4 before the removal.</summary>
    [Fact]
    public void AHighDegreeVertexIsFlippedDownAndRemoved()
    {
        var mesh = new NavMesh();
        mesh.Reset(0, 0, 12, 12);
        mesh.InsertRectangle(0, 0, 12, 12, NavBytes.Floor);
        mesh.InsertPoint(6, 6);
        foreach ((int x, int y) in new[] { (3, 3), (9, 3), (9, 9), (3, 9), (6, 2), (2, 6) }) mesh.InsertPoint(x, y);
        mesh.CoarsenRectangle(0, 0, 12, 12);
        Assert.True(mesh.TryLocateVertex(6, 6, out int t, out int s));
        int live = mesh.LiveTriangleCount();
        int degree = 0;
        for (int i = 0; i < mesh.TriangleSlots; i++)
            if (mesh.IsTriangleLive(i))
                for (int k = 0; k < 3; k++)
                    if (mesh.PointX(mesh.Vertex(i, k)) == 6 && mesh.PointY(mesh.Vertex(i, k)) == 6) degree++;
        Assert.True(degree >= 5, $"the centre has degree {degree}, so nothing is reduced");

        Assert.True(mesh.RemoveVertex(t, s));
        Assert.Equal(live - 2, mesh.LiveTriangleCount());
        Assert.False(mesh.TryLocateVertex(6, 6, out _, out _));
        AssertInvariants(mesh);
        for (int i = 0; i < mesh.TriangleSlots; i++)
            if (mesh.IsTriangleLive(i))
                for (int k = 0; k < 3; k++)
                    Assert.False(mesh.PointX(mesh.Vertex(i, k)) == 6 && mesh.PointY(mesh.Vertex(i, k)) == 6);
    }

    /// <summary>The edge finder (`0x004C9C30`) returns the edge that leaves a vertex along a direction,
    /// and nothing when no fan edge points that way or when the point is no vertex. It turns the
    /// fan one way and gives up at the hull, so a vertex on the canvas edge may miss an edge its
    /// fan holds; the rebuild only asks it about points inside the map.</summary>
    [Fact]
    public void TheEdgeTowardATargetLeavesTheVertexAlongIt()
    {
        var mesh = new NavMesh();
        mesh.Reset(0, 0, 8, 8);
        mesh.InsertPoint(4, 4);
        mesh.InsertPoint(6, 4);
        Assert.True(mesh.TryEdgeToward(4, 4, 8, 4, out int t, out int s));
        // The slot names the vertex; the edge leaving it is, in our storage, the one before the slot.
        int q = mesh.Vertex(t, (s + 2) % 3);
        Assert.Equal((6, 4), (mesh.PointX(q), mesh.PointY(q)));
        Assert.Equal((4, 4), (mesh.PointX(mesh.Vertex(t, s)), mesh.PointY(mesh.Vertex(t, s))));
        Assert.True(mesh.TryEdgeToward(4, 4, 0, 0, out t, out s));
        Assert.Equal((0, 0), (mesh.PointX(mesh.Vertex(t, (s + 2) % 3)), mesh.PointY(mesh.Vertex(t, (s + 2) % 3))));
        Assert.False(mesh.TryEdgeToward(4, 4, 8, 3, out _, out _));
        Assert.False(mesh.TryEdgeToward(1, 1, 8, 8, out _, out _));
    }

    /// <summary>Neighbours point back at each other across the reversed edge and every live triangle
    /// is wound positively. Constraint bits are one-sided in the original (the side marker sets a
    /// bit on the triangle it walks), so nothing is asserted about them here.</summary>
    internal static void AssertInvariants(NavMesh mesh)
    {
        for (int t = 0; t < mesh.TriangleSlots; t++)
        {
            if (!mesh.IsTriangleLive(t)) continue;

            int a = mesh.Vertex(t, 0), b = mesh.Vertex(t, 1), c = mesh.Vertex(t, 2);
            Assert.True(NavMesh.Orient(mesh.PointX(a), mesh.PointY(a), mesh.PointX(b), mesh.PointY(b),
                                       mesh.PointX(c), mesh.PointY(c)) > 0, $"triangle {t} is not wound positively");

            for (int i = 0; i < 3; i++)
            {
                int u = mesh.Neighbour(t, i);
                if (u == NavMesh.NoNeighbour) continue;
                Assert.True(mesh.IsTriangleLive(u), $"triangle {t} edge {i} points at dead triangle {u}");
                int from = mesh.Vertex(t, i), to = mesh.Vertex(t, (i + 1) % 3);
                int j = mesh.EdgeIndexOf(u, to, from);
                Assert.Equal(t, mesh.Neighbour(u, j));
            }
        }
    }
}
