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

    [Fact]
    public void EveryMapBorderIsAConstraint()
    {
        var mesh = new NavMesh();
        mesh.Reset(8, 6);

        int borders = 0;
        for (int t = 0; t < mesh.TriangleSlots; t++)
            for (int i = 0; i < 3; i++)
                if (mesh.Neighbour(t, i) == NavMesh.NoNeighbour)
                {
                    Assert.True(mesh.IsConstraint(t, i));
                    borders++;
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
        // The two halves of the border edge are both still constraints.
        int constrainedBorders = 0;
        for (int t = 0; t < mesh.TriangleSlots; t++)
            for (int i = 0; i < 3; i++)
                if (mesh.Neighbour(t, i) == NavMesh.NoNeighbour && mesh.IsConstraint(t, i)) constrainedBorders++;
        Assert.Equal(5, constrainedBorders);
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

    /// <summary>Neighbours point back at each other across the reversed edge, every live triangle
    /// is wound positively, and a constraint bit is shared by both sides of its edge.</summary>
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
                if (u == NavMesh.NoNeighbour)
                {
                    Assert.True(mesh.IsConstraint(t, i), $"triangle {t} edge {i} borders nothing but is not a constraint");
                    continue;
                }
                Assert.True(mesh.IsTriangleLive(u), $"triangle {t} edge {i} points at dead triangle {u}");
                int from = mesh.Vertex(t, i), to = mesh.Vertex(t, (i + 1) % 3);
                int j = mesh.EdgeIndexOf(u, to, from);
                Assert.Equal(t, mesh.Neighbour(u, j));
                Assert.Equal(mesh.IsConstraint(t, i), mesh.IsConstraint(u, j));
            }
        }
    }
}
