// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Pathfinding;

namespace DK2.Simulation.Tests;

/// <summary>
/// The per-tile rebuild (navigation-partial-rebuild.md, Q591, ADR-0461): a changed tile edits the
/// mesh in place. What is checked is what any edit must leave: a valid triangulation whose every
/// triangle carries the byte of the tiles under it, no constraint bit left behind, and the map's
/// border intact. Which triangles it leaves is the original's business, unverified without a dump
/// of its mesh after a dig.
/// </summary>
public class NavMeshPartialTests
{
    private sealed class Grid : INavByteGrid
    {
        private readonly byte[] _bytes;

        public Grid(string[] rows)
        {
            Width = rows[0].Length;
            Height = rows.Length;
            _bytes = new byte[Width * Height];
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                    _bytes[(y * Width) + x] = rows[y][x] switch
                    {
                        '#' => NavBytes.Solid,
                        '~' => NavBytes.Water,
                        'L' => NavBytes.Lava,
                        _ => NavBytes.Floor,
                    };
        }

        public int Width { get; }
        public int Height { get; }

        public byte NavByte(int x, int y) =>
            x < 0 || y < 0 || x >= Width || y >= Height ? NavBytes.Solid : _bytes[(y * Width) + x];

        public void Set(int x, int y, byte nav) => _bytes[(y * Width) + x] = nav;
    }

    private static readonly string[] Room =
    [
        "##########",
        "#........#",
        "#..##....#",
        "#..##.~~.#",
        "#.....~~.#",
        "#LL......#",
        "#........#",
        "##########",
    ];

    [Fact]
    public void DiggingOneTileLeavesAValidMeshWithTheNewByte()
    {
        var grid = new Grid(Room);
        NavMesh mesh = NavMesh.Build(grid);
        int before = mesh.LiveTriangleCount();

        grid.Set(3, 2, NavBytes.Floor);
        mesh.Retriangulate(grid, 3, 2, 4, 3);

        AssertEdited(mesh, grid);
        Assert.NotEqual(before, mesh.LiveTriangleCount());
    }

    [Fact]
    public void ClosingATileLeavesAValidMeshWithTheNewByte()
    {
        var grid = new Grid(Room);
        NavMesh mesh = NavMesh.Build(grid);

        grid.Set(5, 5, NavBytes.Solid);
        mesh.Retriangulate(grid, 5, 5, 6, 6);

        AssertEdited(mesh, grid);
    }

    [Fact]
    public void ARectangleOfTilesIsEmptiedAndRefilled()
    {
        var grid = new Grid(Room);
        NavMesh mesh = NavMesh.Build(grid);

        for (int y = 2; y < 5; y++)
            for (int x = 2; x < 5; x++)
                grid.Set(x, y, NavBytes.Water);
        mesh.Retriangulate(grid, 2, 2, 5, 5);

        AssertEdited(mesh, grid);
    }

    [Fact]
    public void ARectangleOnTheMapEdgeRebuildsTheWholeMap()
    {
        var grid = new Grid(Room);
        NavMesh mesh = NavMesh.Build(grid);
        grid.Set(0, 3, NavBytes.Floor);
        mesh.Retriangulate(grid, 0, 3, 1, 4);

        NavMesh fresh = NavMesh.Build(grid);
        Assert.Equal(fresh.LiveTriangleCount(), mesh.LiveTriangleCount());
        Assert.Equal(fresh.TriangleSlots, mesh.TriangleSlots);
        AssertEdited(mesh, grid);
    }

    [Fact]
    public void ASequenceOfEditsKeepsTheMeshValid()
    {
        var grid = new Grid(Room);
        NavMesh mesh = NavMesh.Build(grid);
        uint state = 12345;
        for (int i = 0; i < 60; i++)
        {
            state = (state * 1664525) + 1013904223;
            int x = 1 + (int)((state >> 8) % (uint)(grid.Width - 2));
            state = (state * 1664525) + 1013904223;
            int y = 1 + (int)((state >> 8) % (uint)(grid.Height - 2));
            state = (state * 1664525) + 1013904223;
            byte nav = new[] { NavBytes.Floor, NavBytes.Water, NavBytes.Lava, NavBytes.Solid }[(state >> 16) % 4];
            if (grid.NavByte(x, y) == nav) continue;
            grid.Set(x, y, nav);
            try
            {
                mesh.Retriangulate(grid, x, y, x + 1, y + 1);
                AssertEdited(mesh, grid);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"edit {i} at ({x},{y}) to {nav}: {ex.Message}", ex);
            }
        }
    }

    [Fact]
    public void AnEditThatChangesNothingLeavesTheTrianglesOfTheTile()
    {
        var grid = new Grid(Room);
        NavMesh mesh = NavMesh.Build(grid);
        mesh.Retriangulate(grid, 6, 1, 7, 2);
        AssertEdited(mesh, grid);
    }

    /// <summary>Every live triangle inside the map carries the byte of the tile under its centroid,
    /// neighbours agree, every triangle is wound positively, no constraint bit is left set, and the
    /// four map corners are still vertices.</summary>
    internal static void AssertEdited(NavMesh mesh, INavByteGrid grid)
    {
        NavMeshTests.AssertInvariants(mesh);
        for (int t = 0; t < mesh.TriangleSlots; t++)
        {
            if (!mesh.IsTriangleLive(t)) continue;
            for (int i = 0; i < 3; i++)
                Assert.False(mesh.IsConstraint(t, i), $"triangle {t} edge {i} keeps a constraint bit after the edit");
            long cx = 0, cy = 0;
            for (int i = 0; i < 3; i++)
            {
                int p = mesh.Vertex(t, i);
                cx += mesh.PointX(p) * 3L * NavMesh.UnitsPerTile;
                cy += mesh.PointY(p) * 3L * NavMesh.UnitsPerTile;
            }
            // The centroid in 1/256 tiles, then its tile; outside the map the triangle is the canvas's.
            long ux = cx / 9, uy = cy / 9;
            int tx = (int)Math.Floor(ux / (double)NavMesh.UnitsPerTile), ty = (int)Math.Floor(uy / (double)NavMesh.UnitsPerTile);
            if (tx < 0 || ty < 0 || tx >= grid.Width || ty >= grid.Height)
            {
                Assert.Equal(NavBytes.Outside, mesh.NavByte(t));
                continue;
            }
            Assert.True(grid.NavByte(tx, ty) == mesh.NavByte(t),
                $"triangle {t} carries byte {mesh.NavByte(t)} over tile ({tx},{ty}) whose byte is {grid.NavByte(tx, ty)}");
        }
        foreach ((int x, int y) in new[] { (0, 0), (grid.Width, 0), (grid.Width, grid.Height), (0, grid.Height) })
            Assert.True(mesh.TryLocateVertex(x, y, out _, out _), $"map corner ({x},{y}) is no longer a vertex");
    }
}
