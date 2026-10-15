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

    /// <summary>A whole-map build carries no constraint bit (§2.2: the original's side marker runs on
    /// the partial path only); the byte test alone keeps two bytes apart, and it does.</summary>
    [Fact]
    public void AWholeMapBuildCarriesNoConstraintBits()
    {
        NavMesh mesh = NavMesh.Build(new Grid([
            "......",
            ".##...",
            ".##.~.",
            "......",
        ]));
        for (int t = 0; t < mesh.TriangleSlots; t++)
            for (int i = 0; i < 3; i++)
                Assert.False(mesh.IsConstraint(t, i), $"triangle {t} edge {i} carries a constraint bit");
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

    /// <summary>The flip refuses a constraint edge from either side, as the original tests t's bit
    /// and then u's; a bit set on one side only still refuses both.</summary>
    [Fact]
    public void AFlipAcrossAConstraintIsRefusedFromBothSides()
    {
        var mesh = new NavMesh();
        mesh.Reset(4, 4);
        int t = 0, i = -1;
        for (int k = 0; k < 3; k++) if (mesh.Neighbour(0, k) != NavMesh.NoNeighbour) i = k;
        int u = mesh.Neighbour(t, i);
        int j = mesh.EdgeIndexOf(u, mesh.Vertex(t, (i + 1) % 3), mesh.Vertex(t, i));

        mesh.MarkConstraint(t, i);
        Assert.False(mesh.TryFlip(t, i));
        Assert.False(mesh.TryFlip(u, j));
        Assert.False(mesh.IsConstraint(u, j));

        mesh.ClearConstraint(t, i);
        Assert.True(mesh.TryFlip(t, i));
        Assert.Equal(2, mesh.LiveTriangleCount());
        NavMeshTests.AssertInvariants(mesh);
    }

    /// <summary>The original's flip moves the outer edges' constraint bits before its convexity test
    /// (`0x004C9FC0`, stores at 0x004CA10D..0x004CA164 ahead of the tests), so a flip refused as
    /// non-convex leaves them moved and nothing else written. A non-convex quad: the reset's two
    /// triangles with an extra point pulled into one of them.</summary>
    [Fact]
    public void AFlipRefusedAsNonConvexHasAlreadyMovedTheConstraintBits()
    {
        var mesh = new NavMesh();
        mesh.Reset(0, 0, 8, 8);
        mesh.InsertRectangle(0, 0, 8, 8, NavBytes.Floor);
        mesh.InsertPoint(1, 7);
        // Find a non-convex quad: an edge whose flip the convexity test refuses.
        int t = -1, k = -1, u = -1, j = -1;
        for (int i = 0; i < mesh.TriangleSlots && t < 0; i++)
        {
            if (!mesh.IsTriangleLive(i)) continue;
            for (int e = 0; e < 3 && t < 0; e++)
            {
                int n = mesh.Neighbour(i, e);
                if (n == NavMesh.NoNeighbour) continue;
                int a = mesh.Vertex(i, e), b = mesh.Vertex(i, (e + 1) % 3), c = mesh.Vertex(i, (e + 2) % 3);
                int jj = mesh.EdgeIndexOf(n, b, a);
                int d = mesh.Vertex(n, (jj + 2) % 3);
                if (NavMesh.Orient(mesh.PointX(c), mesh.PointY(c), mesh.PointX(d), mesh.PointY(d), mesh.PointX(a), mesh.PointY(a)) *
                    NavMesh.Orient(mesh.PointX(c), mesh.PointY(c), mesh.PointX(d), mesh.PointY(d), mesh.PointX(b), mesh.PointY(b)) >= 0)
                { t = i; k = e; u = n; j = jj; }
            }
        }
        Assert.True(t >= 0, "the grid has no non-convex quad");

        // The original's next edges are our previous ones (NavMesh.Edit.cs, the translation).
        mesh.MarkConstraint(t, (k + 2) % 3);
        mesh.MarkConstraint(u, (j + 2) % 3);
        int[] verticesBefore = [mesh.Vertex(t, 0), mesh.Vertex(t, 1), mesh.Vertex(t, 2), mesh.Vertex(u, 0), mesh.Vertex(u, 1), mesh.Vertex(u, 2)];

        Assert.False(mesh.TryFlip(t, k));

        int[] verticesAfter = [mesh.Vertex(t, 0), mesh.Vertex(t, 1), mesh.Vertex(t, 2), mesh.Vertex(u, 0), mesh.Vertex(u, 1), mesh.Vertex(u, 2)];
        Assert.Equal(verticesBefore, verticesAfter);
        Assert.False(mesh.IsConstraint(t, (k + 2) % 3));
        Assert.False(mesh.IsConstraint(u, (j + 2) % 3));
        Assert.True(mesh.IsConstraint(t, k));
        Assert.True(mesh.IsConstraint(u, j));
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
    /// diagonal is shorter. Here the reset's own diagonal (6,0)-(0,4) (the original's, from
    /// (x1,y0) to (x0,y1)) spans 52 squared units and the other, (0,0)-(2,4), spans 20, so the
    /// pass takes it.</summary>
    [Fact]
    public void AQuadKeepsItsShorterDiagonal()
    {
        var mesh = new NavMesh();
        mesh.Reset(0, 0, 6, 4);
        mesh.InsertRectangle(0, 0, 6, 4, NavBytes.Floor);
        mesh.InsertPoint(2, 4);
        Assert.True(HasEdge(mesh, 6, 0, 0, 4));

        mesh.CoarsenRectangle(0, 0, 6, 4);

        Assert.False(HasEdge(mesh, 6, 0, 0, 4));
        Assert.True(HasEdge(mesh, 0, 0, 2, 4));
        NavMeshTests.AssertInvariants(mesh);
    }

    /// <summary>The original's coarsening (`0x004CC650`) is a flood that visits each triangle once
    /// per generation and spreads only from a hit, seeded at the rectangle's corners; a flip made
    /// after a neighbour was popped is never revisited, so a build can leave a long diagonal behind.
    /// It is not a fixed-point pass: this grid keeps some, and a further flood over the whole map,
    /// seeded at corners whose fans are settled, makes no hit at all. Solid triangles are never
    /// entered.</summary>
    [Fact]
    public void TheFloodStopsShortOfAFixedPoint_AsTheReadingOfTheOriginalImplies()
    {
        NavMesh mesh = NavMesh.Build(new Grid([
            "........",
            "..##....",
            "..##..~~",
            "......~~",
            "LL......",
        ]));
        int left = Shortenable(mesh);
        Assert.True(left > 0, "the build left no long diagonal, so the flood reached a fixed point here");
        Assert.Equal(0, mesh.CoarsenRectangle(0, 0, 8, 5));
        Assert.Equal(left, Shortenable(mesh));
        NavMeshTests.AssertInvariants(mesh);
    }

    private static int Shortenable(NavMesh mesh)
    {
        int count = 0;
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
                if ((nx * nx) + (ny * ny) < (ox * ox) + (oy * oy)) count++;
            }
        }
        return count;
    }
}
