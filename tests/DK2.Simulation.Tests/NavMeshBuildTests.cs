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

    [Fact]
    public void AUniformMapCoarsensBackToTwoTriangles()
    {
        NavMesh mesh = NavMesh.Build(new Grid(["....", "....", "...."]));
        Assert.Equal(2, mesh.LiveTriangleCount());
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
        int live = mesh.LiveTriangleCount();
        Assert.InRange(live, 3, 12);
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
}
