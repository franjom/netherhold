// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Pathfinding;

namespace DK2.Simulation.Tests;

/// <summary>
/// Building the mesh from a grid of navigation bytes (navigation-mesh-and-search.md §2.1). The
/// decisive property is stamping: the triangle under every tile centre carries that tile's byte,
/// which is what a route through a wall would violate.
/// </summary>
public class NavMeshBuildTests
{
    private const int U = NavMesh.UnitsPerTile;

    private sealed class Grid : INavByteGrid
    {
        private readonly byte[,] _nav;
        public Grid(string[] rows)
        {
            Height = rows.Length; Width = rows[0].Length;
            _nav = new byte[Width, Height];
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                    _nav[x, y] = rows[y][x] switch
                    {
                        '#' => NavBytes.Solid, '~' => NavBytes.Water, 'L' => NavBytes.Lava, _ => NavBytes.Floor,
                    };
        }
        public int Width { get; }
        public int Height { get; }
        public byte NavByte(int x, int y) => _nav[x, y];
    }

    /// <summary>Triangles on the map, which is every triangle the reset's canvas did not keep.
    /// The canvas reaches far past any map (−256 … 1280 tiles), so a build always leaves a ring of
    /// <see cref="NavBytes.Outside"/> triangles no route can enter.</summary>
    private static int MapTriangles(NavMesh mesh)
    {
        int count = 0;
        for (int t = 0; t < mesh.TriangleSlots; t++)
            if (mesh.IsTriangleLive(t) && mesh.NavByte(t) != NavBytes.Outside) count++;
        return count;
    }

    /// <summary>A map of one byte needs no rectangle of its own: the build stamps the whole map
    /// as one rectangle of byte 0 and the scan's only rectangle is byte 0 and so skipped.</summary>
    [Fact]
    public void AUniformMapIsTwoTriangles()
    {
        NavMesh mesh = NavMesh.Build(new Grid(["....", "....", "...."]));
        Assert.Equal(2, MapTriangles(mesh));
        NavMeshTests.AssertInvariants(mesh);
    }

    [Fact]
    public void EveryTileCentreLiesInATriangleWithItsOwnByte()
    {
        var grid = new Grid([
            "........",
            "..##....",
            "..##..~~",
            "......~~",
            "LL......",
        ]);
        NavMesh mesh = NavMesh.Build(grid);
        NavMeshTests.AssertInvariants(mesh);

        for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
            {
                int t = mesh.Locate((x * U) + (U / 2), (y * U) + (U / 2));
                Assert.True(t >= 0, $"no triangle under ({x},{y})");
                Assert.Equal(grid.NavByte(x, y), mesh.NavByte(t));
            }
    }

    /// <summary>Two different bytes meet only across a constraint edge, and a constraint edge
    /// between two triangles of one byte is the harmless leftover of a rectangle side.</summary>
    [Fact]
    public void DifferentBytesAreAlwaysSeparatedByAConstraint()
    {
        NavMesh mesh = NavMesh.Build(new Grid([
            "......",
            ".##...",
            ".##.~.",
            "......",
        ]));
        for (int t = 0; t < mesh.TriangleSlots; t++)
        {
            if (!mesh.IsTriangleLive(t)) continue;
            for (int i = 0; i < 3; i++)
            {
                int u = mesh.Neighbour(t, i);
                if (u == NavMesh.NoNeighbour) continue;
                if (mesh.NavByte(t) != mesh.NavByte(u))
                    Assert.True(mesh.IsConstraint(t, i), $"triangles {t} and {u} differ without a constraint");
            }
        }
    }

    /// <summary>A single wall tile in an open floor: the floor around it is a ring, which cannot
    /// coarsen to two triangles but must stay small — the mesh is coarse by design.</summary>
    [Fact]
    public void AnIsolatedWallLeavesAFewTrianglesNotMany()
    {
        NavMesh mesh = NavMesh.Build(new Grid([
            ".......",
            ".......",
            "...#...",
            ".......",
            ".......",
        ]));
        NavMeshTests.AssertInvariants(mesh);
        Assert.InRange(MapTriangles(mesh), 3, 12);
    }

    [Fact]
    public void TheBuildIsDeterministic()
    {
        string[] rows = ["..#...", "..#.~~", "......", "L....."];
        NavMesh a = NavMesh.Build(new Grid(rows)), b = NavMesh.Build(new Grid(rows));
        Assert.Equal(a.TriangleSlots, b.TriangleSlots);
        for (int t = 0; t < a.TriangleSlots; t++)
        {
            Assert.Equal(a.NavByte(t), b.NavByte(t));
            for (int i = 0; i < 3; i++)
            {
                Assert.Equal(a.Vertex(t, i), b.Vertex(t, i));
                Assert.Equal(a.Neighbour(t, i), b.Neighbour(t, i));
                Assert.Equal(a.IsConstraint(t, i), b.IsConstraint(t, i));
            }
        }
    }

    [Fact]
    public void AFlipAcrossAConstraintIsRefused()
    {
        var mesh = new NavMesh();
        mesh.Reset(4, 4);
        // Every reset edge is either a border constraint or the free diagonal.
        int refused = 0;
        for (int t = 0; t < mesh.TriangleSlots; t++)
            for (int i = 0; i < 3; i++)
                if (mesh.IsConstraint(t, i)) { Assert.False(mesh.TryFlip(t, i)); refused++; }
        Assert.Equal(4, refused);

        bool flipped = false;
        for (int t = 0; t < mesh.TriangleSlots && !flipped; t++)
            for (int i = 0; i < 3 && !flipped; i++)
                if (!mesh.IsConstraint(t, i)) { Assert.True(mesh.TryFlip(t, i)); flipped = true; }
        Assert.True(flipped);
        Assert.Equal(2, mesh.LiveTriangleCount());
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
                if ((mesh.PointX(p) == ax && mesh.PointY(p) == ay && mesh.PointX(q) == bx && mesh.PointY(q) == by)
                    || (mesh.PointX(p) == bx && mesh.PointY(p) == by && mesh.PointX(q) == ax && mesh.PointY(q) == ay))
                    return true;
            }
        }
        return false;
    }

    /// <summary>The coarsening criterion (`0x004CC190`): a convex quad of one byte keeps whichever
    /// diagonal is shorter. Here the reset's own diagonal (0,0)-(6,4) spans 52 squared units and
    /// the other, (6,0)-(2,4), spans 32, so the pass takes it.</summary>
    [Fact]
    public void AQuadKeepsItsShorterDiagonal()
    {
        var mesh = new NavMesh();
        mesh.Reset(0, 0, 6, 4);
        mesh.InsertRectangle(0, 0, 6, 4, NavBytes.Floor);
        mesh.InsertPoint(2, 4);
        Assert.True(HasEdge(mesh, 0, 0, 6, 4));

        mesh.Coarsen();

        Assert.False(HasEdge(mesh, 0, 0, 6, 4));
        Assert.True(HasEdge(mesh, 6, 0, 2, 4));
        NavMeshTests.AssertInvariants(mesh);
    }

    /// <summary>The pass runs to its fixed point: after a build no edge inside one byte is the
    /// longer diagonal of a convex quad. Solid triangles are exempt — the original's flood fill
    /// never enters one, so rock keeps whatever the insertions left.</summary>
    [Fact]
    public void AfterABuildNoWalkableEdgeCanBeShortened()
    {
        NavMesh mesh = NavMesh.Build(new Grid([
            "........",
            "..##....",
            "..##..~~",
            "......~~",
            "LL......",
        ]));

        for (int t = 0; t < mesh.TriangleSlots; t++)
        {
            if (!mesh.IsTriangleLive(t) || NavBytes.IsSolid(mesh.NavByte(t))) continue;
            for (int i = 0; i < 3; i++)
            {
                int u = mesh.Neighbour(t, i);
                if (u == NavMesh.NoNeighbour || mesh.NavByte(u) != mesh.NavByte(t)) continue;
                int a = mesh.Vertex(t, i), b = mesh.Vertex(t, (i + 1) % 3);
                int c = mesh.Vertex(t, (i + 2) % 3);
                int d = mesh.Vertex(u, (mesh.EdgeIndexOf(u, b, a) + 2) % 3);
                if (NavMesh.Orient(mesh.PointX(c), mesh.PointY(c), mesh.PointX(d), mesh.PointY(d), mesh.PointX(a), mesh.PointY(a)) *
                    NavMesh.Orient(mesh.PointX(c), mesh.PointY(c), mesh.PointX(d), mesh.PointY(d), mesh.PointX(b), mesh.PointY(b)) >= 0)
                    continue;
                long nx = mesh.PointX(d) - mesh.PointX(c), ny = mesh.PointY(d) - mesh.PointY(c);
                long ox = mesh.PointX(b) - mesh.PointX(a), oy = mesh.PointY(b) - mesh.PointY(a);
                Assert.False((nx * nx) + (ny * ny) < (ox * ox) + (oy * oy),
                    $"triangle {t} edge {i} is the longer diagonal of its quad");
            }
        }
    }
}
