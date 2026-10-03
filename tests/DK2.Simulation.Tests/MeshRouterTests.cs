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

    /// <summary>DK2's endpoint rule (`0x004C9DF0`, navigation-mesh-and-search.md §2.3): a target on a
    /// tile corner or edge belongs to the triangle toward the far point (257, 257), so a corner
    /// shared with a solid tile routes when the south-east tile is floor and is refused when it is
    /// solid; 93 of Level 7's replayed corner targets turned on this (Q354).</summary>
    [Theory]
    [InlineData(2.0, 2.0, true)]   // corner: solid to the north-west, floor south-east
    [InlineData(1.0, 1.0, false)]  // corner: floor around, the solid tile south-east
    [InlineData(1.5, 2.0, true)]   // edge below the solid tile: floor lies south
    [InlineData(1.5, 1.0, false)]  // edge above the solid tile: the solid tile lies south
    [InlineData(2.0, 1.5, true)]   // edge east of the solid tile: floor lies east
    [InlineData(1.0, 1.5, false)]  // edge west of the solid tile: the solid tile lies east
    public void AnEndpointOnACornerOrEdgeLeansTowardTheSouthEast(double tx, double ty, bool routes)
    {
        (MeshRouter router, NavMesh mesh) = Make(
            "....",
            ".#..",
            "....",
            "....");
        var to = new NavPoint((int)(tx * NavMesh.UnitsPerTile), (int)(ty * NavMesh.UnitsPerTile));

        int t = mesh.LocateEndpoint(to.X, to.Y);
        Assert.True(t >= 0);
        Assert.Equal(routes, !NavBytes.IsSolid(mesh.NavByte(t)));

        var from = new NavPoint(NavMesh.UnitsPerTile / 2, (3 * NavMesh.UnitsPerTile) + (NavMesh.UnitsPerTile / 2));
        Assert.Equal(routes, router.TryRoute(from, to, MoverProfile.Walker, new List<NavPoint>()));
    }

    /// <summary>The door arms of the cost function (`0x004C8BD0`; navigation-mesh-and-search.md §4).
    /// Owner 3's mover meets its own door and player 4's, closed, locked or tagged, with each of
    /// the state option bits; the tile left is judged by the same rule.</summary>
    [Theory]
    [InlineData(3, false, false, 0x80, 3, true)]   // own unlocked door, respecting doors: pass
    [InlineData(3, true, false, 0x80, 3, false)]   // own locked door: stop
    [InlineData(4, false, false, 0x80, 3, false)]  // foreign door, respecting doors: stop
    [InlineData(4, false, false, 0x00, 3, true)]   // foreign door, not respecting: pass
    [InlineData(4, true, false, 0x00, 3, true)]    // foreign locked door, not respecting: pass
    [InlineData(4, false, true, 0x00, 3, false)]   // foreign tagged door: stop whatever the word
    [InlineData(3, true, false, 0x00, 3, false)]   // own locked door, not respecting: stop
    [InlineData(3, true, false, 0x02, 3, true)]    // own locked door, pass-own-locked: pass
    [InlineData(3, true, true, 0x02, 3, true)]     // own tagged locked door, pass-own-locked: pass
    public void TheDoorArmsFollowTheOwnerTheLockAndTheOptionWord(int doorOwner, bool locked, bool tagged, int options, int moverOwner, bool passes)
    {
        byte door = NavBytes.Door((byte)doorOwner, locked, tagged);
        var mover = new MoverProfile(true, false, (byte)moverOwner,
            RespectDoors: (options & 0x80) != 0, PassOwnLockedDoors: (options & 0x02) != 0, EntersPortalHole: (options & 0x04) != 0);

        int entering = MeshRouter.TransitionCost(door, NavBytes.Floor, mover);
        Assert.Equal(passes, entering != 0);
        if (passes) Assert.Equal(NavBytes.Category(door) + 1, entering);

        // Leaving a door tile applies the same arms without the tag test.
        bool leaves = passes || (tagged && doorOwner != moverOwner && (options & 0x80) == 0 && !locked);
        Assert.Equal(leaves, MeshRouter.TransitionCost(NavBytes.Floor, door, mover) != 0);
    }

    /// <summary>The cross-check behind ADR-0339: the mesh has no route through a corner-only gap, as
    /// DK2's edge-only region fill (<c>0x004C91A0</c>) gives none, and routes once one side of the
    /// corner is open, which is the rule <c>GridPathfinder</c> now follows.</summary>
    [Fact]
    public void ACornerOnlyGapHasNoRoute_AndOneOpenSideRestoresIt()
    {
        var waypoints = new List<NavPoint>();
        var cornerOnly = new MeshRouter(NavMesh.Build(new Grid(["######", "#..###", "#..###", "###..#", "###..#", "######"])));
        Assert.False(cornerOnly.TryRoute(NavPoint.FromTileCentre(1, 1), NavPoint.FromTileCentre(4, 4), MoverProfile.Walker, waypoints));

        var oneSideOpen = new MeshRouter(NavMesh.Build(new Grid(["######", "#..###", "#..###", "##...#", "###..#", "######"])));
        Assert.True(oneSideOpen.TryRoute(NavPoint.FromTileCentre(1, 1), NavPoint.FromTileCentre(4, 4), MoverProfile.Walker, waypoints));
    }

    [Fact]
    public void APortalHoleIsEnteredOnlyWithOptionBitTwo()
    {
        Assert.Equal(0, MeshRouter.TransitionCost(NavBytes.PortalHole, NavBytes.Floor, MoverProfile.Walker));
        Assert.Equal(1, MeshRouter.TransitionCost(NavBytes.PortalHole, NavBytes.Floor, new MoverProfile(false, false, EntersPortalHole: true)));
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
    /// the funnel turns twice — at the lower corner, then the upper. The mover does not aim at
    /// those corners: the re-walk keeps the two gates the sight-lines miss, both of them diagonals
    /// drawn from the wall's free end across the open floor, and aims at their midpoints, so the
    /// route runs through the middle of the gap and never crosses solid ground.</summary>
    [Fact]
    public void AroundAWallTheRouteAimsAtTheMiddleOfTheGatesItTurnsThrough()
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
        Assert.Equal([new NavPoint(7 * U, 5 * U), new NavPoint(7 * U, 2 * U), to], path);
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
