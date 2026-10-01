// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Pathfinding;

namespace DK2.AI.Tests;

/// <summary>The keeper's mesh cache: one build per change of the navigation bytes, and a reused
/// router that answers exactly as a freshly built one.</summary>
public class KeeperRouteCacheTests
{
    private const int U = NavMesh.UnitsPerTile;

    private sealed class Grid(string[] rows) : INavByteGrid
    {
        private readonly char[][] _rows = [.. rows.Select(r => r.ToCharArray())];
        public int Width => _rows[0].Length;
        public int Height => _rows.Length;
        public byte NavByte(int x, int y) => _rows[y][x] == '#' ? NavBytes.Solid : NavBytes.Floor;
        public void Set(int x, int y, char c) => _rows[y][x] = c;
    }

    private static Grid Walled() => new([
        "..........",
        "..........",
        "....#.....",
        "....#.....",
        "....#.....",
        "..........",
    ]);

    [Fact]
    public void TheSameBytesReuseTheRouterAndBuildOnce()
    {
        var cache = new KeeperRouteCache();
        MeshRouter first = cache.RouterFor(Walled());
        MeshRouter second = cache.RouterFor(Walled());

        Assert.Same(first, second);
        Assert.Equal(1, cache.Builds);
    }

    [Fact]
    public void AChangedByteOrSizeBuildsAgainAndOnlyTheLastMeshIsKept()
    {
        var cache = new KeeperRouteCache();
        Grid grid = Walled();
        MeshRouter before = cache.RouterFor(grid);

        grid.Set(4, 3, '.');
        MeshRouter opened = cache.RouterFor(grid);
        Assert.NotSame(before, opened);
        Assert.Equal(2, cache.Builds);

        grid.Set(4, 3, '#');
        Assert.NotSame(before, cache.RouterFor(grid));
        Assert.Equal(3, cache.Builds);

        cache.RouterFor(new Grid(["..........", ".........."]));
        Assert.Equal(4, cache.Builds);
    }

    [Fact]
    public void AReusedRouterAnswersEveryQueryAsAFreshOneDoes()
    {
        var cache = new KeeperRouteCache();
        Grid grid = Walled();
        MoverProfile mover = KeeperView.StagingWalker(1);
        (int X, int Y)[] ends = [(1, 3), (8, 3), (4, 0), (4, 5), (9, 5), (0, 0)];

        // Ask every pair twice through the cache, so the second round runs on a router that has
        // already answered queries, and compare each answer with a router built for that query.
        for (int round = 0; round < 2; round++)
            foreach ((int X, int Y) from in ends)
                foreach ((int X, int Y) to in ends)
                {
                    var cached = new List<NavPoint>();
                    var fresh = new List<NavPoint>();
                    NavPoint a = new((from.X * U) + (U / 2), (from.Y * U) + (U / 2));
                    NavPoint b = new((to.X * U) + (U / 2), (to.Y * U) + (U / 2));
                    bool cachedRoutes = cache.RouterFor(grid).TryRoute(a, b, mover, cached);
                    bool freshRoutes = new MeshRouter(NavMesh.Build(grid)).TryRoute(a, b, mover, fresh);
                    Assert.Equal(freshRoutes, cachedRoutes);
                    Assert.Equal(fresh, cached);
                }
        Assert.Equal(1, cache.Builds);
    }
}
