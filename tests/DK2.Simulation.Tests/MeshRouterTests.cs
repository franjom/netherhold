// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Pathfinding;

namespace DK2.Simulation.Tests;

/// <summary>
/// The greedy two-ended search and the funnel together (navigation-mesh-and-search.md §3–§4,
/// navigation-and-steering.md §2.1). The first test is the owner's observation of 2026-09-16,
/// turned into a pin: a long off-diagonal walk over open floor is one straight segment.
/// </summary>
public class MeshRouterTests
{
    private const int U = NavMesh.UnitsPerTile;

    private sealed class Grid : INavByteGrid
    {
        private readonly string[] _rows;
        public Grid(string[] rows) { _rows = rows; Height = rows.Length; Width = rows[0].Length; }
        public int Width { get; }
        public int Height { get; }
        public byte NavByte(int x, int y) => _rows[y][x] switch
        {
            '#' => NavBytes.Solid, '~' => NavBytes.Water, 'L' => NavBytes.Lava, _ => NavBytes.Floor,
        };
    }

    private static (MeshRouter router, NavMesh mesh) Make(params string[] rows)
    {
        NavMesh mesh = NavMesh.Build(new Grid(rows));
        return (new MeshRouter(mesh), mesh);
    }

    [Fact]
    public void TenAcrossAndThreeUpOverOpenFloorIsOneStraightSegment()
    {
        (MeshRouter router, _) = Make(
            "############",
            "#..........#",
            "#..........#",
            "#..........#",
            "#..........#",
            "############");
        var path = new List<NavPoint>();
        NavPoint from = NavPoint.FromTileCentre(1, 4), to = NavPoint.FromTileCentre(10, 1);

        Assert.True(router.TryRoute(from, to, MoverProfile.Walker, path));
        Assert.Equal([to], path);
    }

    [Fact]
    public void AnUnreachableDestinationHasNoRoute()
    {
        (MeshRouter router, _) = Make(
            "#########",
            "#...#...#",
            "#...#...#",
            "#########");
        var path = new List<NavPoint>();
        Assert.False(router.TryRoute(NavPoint.FromTileCentre(1, 1), NavPoint.FromTileCentre(7, 2), MoverProfile.Walker, path));
        Assert.Empty(path);
    }

    /// <summary>Around a wall one tile thick a taut string wraps both corners of its free end, so
    /// the funnel turns twice — at the lower corner, then the upper — and the route never crosses
    /// solid ground.</summary>
    [Fact]
    public void AroundAWallTheRouteTurnsAtBothCornersOfItsEnd()
    {
        (MeshRouter router, NavMesh mesh) = Make(
            "##########",
            "#........#",
            "#........#",
            "#####....#",
            "#........#",
            "#........#",
            "##########");
        var path = new List<NavPoint>();
        NavPoint from = NavPoint.FromTileCentre(1, 5), to = NavPoint.FromTileCentre(1, 1);

        Assert.True(router.TryRoute(from, to, MoverProfile.Walker, path));
        Assert.Equal([new NavPoint(5 * U, 4 * U), new NavPoint(5 * U, 3 * U), to], path);
        AssertOnFloor(mesh, from, path);
    }

    [Fact]
    public void WaterBlocksAWalkerAndNotASwimmer()
    {
        (MeshRouter router, _) = Make(
            "#######",
            "#..~..#",
            "#..~..#",
            "#..~..#",
            "#######");
        var path = new List<NavPoint>();
        NavPoint from = NavPoint.FromTileCentre(1, 2), to = NavPoint.FromTileCentre(5, 2);

        Assert.False(router.TryRoute(from, to, MoverProfile.Walker, path));
        Assert.True(router.TryRoute(from, to, new MoverProfile(CanWalkOnWater: true, CanWalkOnLava: false), path));
        Assert.Equal([to], path);
    }

    [Fact]
    public void TheSameRequestGivesTheSameRoute()
    {
        string[] rows =
        [
            "############",
            "#....#.....#",
            "#....#.###.#",
            "#....#...#.#",
            "#......###.#",
            "#..........#",
            "############",
        ];
        var a = new List<NavPoint>();
        var b = new List<NavPoint>();
        NavPoint from = NavPoint.FromTileCentre(1, 1), to = NavPoint.FromTileCentre(10, 1);
        Assert.True(Make(rows).router.TryRoute(from, to, MoverProfile.Walker, a));
        Assert.True(Make(rows).router.TryRoute(from, to, MoverProfile.Walker, b));
        Assert.Equal(a, b);
        Assert.True(a.Count >= 2);
    }

    [Theory]
    [InlineData(NavBytes.Floor, 1)]
    [InlineData(NavBytes.Water, 2)]
    [InlineData(NavBytes.Lava, 4)]
    public void TheTransitionCostIsOnePlusTheCategory(byte next, int expected)
    {
        var swimmerAndWader = new MoverProfile(CanWalkOnWater: true, CanWalkOnLava: true);
        Assert.Equal(expected, MeshRouter.TransitionCost(next, NavBytes.Floor, swimmerAndWader));
    }

    [Fact]
    public void SolidIsImpassableToEveryone()
    {
        var everything = new MoverProfile(true, true);
        Assert.Equal(0, MeshRouter.TransitionCost(NavBytes.Solid, NavBytes.Floor, everything));
    }

    /// <summary>Leaving water onto floor is always allowed; entering it is what the flag gates.
    /// Judged from the expansion side, so the two search directions see different worlds.</summary>
    [Fact]
    public void TheWaterGateIsOnEnteringNotLeaving()
    {
        Assert.Equal(1, MeshRouter.TransitionCost(NavBytes.Floor, NavBytes.Water, MoverProfile.Walker));
        Assert.Equal(0, MeshRouter.TransitionCost(NavBytes.Water, NavBytes.Floor, MoverProfile.Walker));
    }

    /// <summary>Every sample along the route lies in some floor triangle. A taut string runs
    /// along wall edges, and a point on an edge is in both triangles, so any floor triangle
    /// containing it counts.</summary>
    private static void AssertOnFloor(NavMesh mesh, NavPoint from, List<NavPoint> path)
    {
        NavPoint prev = from;
        foreach (NavPoint p in path)
        {
            for (int s = 1; s < 16; s++)
            {
                int x = prev.X + ((p.X - prev.X) * s / 16), y = prev.Y + ((p.Y - prev.Y) * s / 16);
                bool onFloor = false;
                for (int t = 0; t < mesh.TriangleSlots && !onFloor; t++)
                    onFloor = mesh.IsTriangleLive(t) && !NavBytes.IsSolid(mesh.NavByte(t)) && mesh.Contains(t, x, y);
                Assert.True(onFloor, $"route crosses solid at ({x},{y})");
            }
            prev = p;
        }
    }
}
