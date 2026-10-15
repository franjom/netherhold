// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Pathfinding;

namespace DK2.Simulation.Tests;

/// <summary>The gate re-walk after the funnel (navigation-and-steering.md §2.1, Q344): which
/// gates survive, which endpoint is selected, the sixteen-tile cap and its two adjusters, the
/// line intersection they rely on, and the point selector that turns records into waypoints.</summary>
public class GateWalkTests
{
    private const int U = NavMesh.UnitsPerTile;

    private sealed class Grid : INavByteGrid
    {
        private readonly string[] _rows;
        public Grid(string[] rows) { _rows = rows; Height = rows.Length; Width = rows[0].Length; }
        public int Width { get; }
        public int Height { get; }
        public byte NavByte(int x, int y) => _rows[y][x] == '#' ? NavBytes.Solid : NavBytes.Floor;
    }

    private static NavPoint P(int xTiles, int yTiles) => new(xTiles * U, yTiles * U);

    private static GateRecord Record(NavPoint a, NavPoint b, int selector = 0, NavPoint crossing = default) =>
        new(a, b, selector) { Crossing = crossing };

    [Theory]
    [InlineData(0, 0, 4, 0, 4)]
    [InlineData(0, 0, 4, 2, 5)]
    [InlineData(0, 0, -2, 4, 5)]
    public void OctileIsTheLongerAxisPlusHalfTheShorter(int ax, int ay, int bx, int by, int expected) =>
        Assert.Equal(expected, GateWalk.Octile(new NavPoint(ax, ay), new NavPoint(bx, by)));

    /// <summary>An L one tile wide: the route leaves the horizontal arm across the edge from the
    /// L's outer corner (4,1) to its inner corner (3,2). The arm's own diagonal (1,1)-(3,2) is dropped
    /// because both sight-lines from the start to that edge's ends pass through it, and the vertical
    /// arm's diagonal (3,2)-(4,3), which the build in the original's order adds, is dropped too; this edge is kept
    /// because the sight-line to the destination clips the inner corner instead. Its first
    /// endpoint (4,1) is on the walker's right going down, its second (3,2) is the funnel corner
    /// and so the nearer, selected one.</summary>
    [Fact]
    public void AGateTheSightLinesPassThroughIsDroppedAndOneTheyMissIsKept()
    {
        NavMesh mesh = NavMesh.Build(new Grid(["#####", "#...#", "###.#", "#####"]));
        var router = new MeshRouter(mesh);
        NavPoint from = NavPoint.FromTileCentre(1, 1), to = NavPoint.FromTileCentre(3, 2);
        var corridor = new List<int>();
        Assert.True(router.TrySearch(mesh.Locate(to.X, to.Y), mesh.Locate(from.X, from.Y), from, MoverProfile.Walker, corridor));
        // Four triangles in the original's build order (reset diagonal, corner order, splits:
        // ADR-0486); the builder before it made three. Nothing below depends on the count.
        Assert.Equal(4, corridor.Count);
        var funnel = new List<NavPoint>();
        Funnel.Reduce(mesh, from, to, corridor, funnel);
        Assert.Equal([P(3, 2), to], funnel);
        var records = new GateRecord[GateWalk.MaxRecords + 1];

        int count = GateWalk.Run(mesh, from, to, corridor, funnel, records, GateWalk.Cap);

        Assert.Equal(2, count);
        Assert.Equal(P(4, 1), records[0].A);
        Assert.Equal(P(3, 2), records[0].B);
        Assert.Equal(1, records[0].Selector);
        Assert.Equal(to, records[1].A);
        var waypoints = new List<NavPoint>();
        GateWalk.Select(records, count, to, GateWalk.Midpoint, waypoints);
        Assert.Equal([new NavPoint((7 * U) / 2, (3 * U) / 2), to], waypoints);
    }

    [Fact]
    public void WithoutGatesTheOnlyRecordIsTheDestination()
    {
        NavMesh mesh = NavMesh.Build(new Grid(["###", "#.#", "###"]));
        var records = new GateRecord[GateWalk.MaxRecords + 1];
        NavPoint to = NavPoint.FromTileCentre(1, 1);

        Assert.Equal(1, GateWalk.Run(mesh, to, to, [mesh.Locate(to.X, to.Y)], [to], records, GateWalk.Cap));
        Assert.Equal(to, records[0].A);
        Assert.Equal(to, records[0].B);
    }

    [Fact]
    public void AGateUnderSixteenTilesIsLeftAlone()
    {
        GateRecord r = Record(P(0, 0), P(15, 0));

        GateWalk.Collapse(ref r, GateWalk.Cap);

        Assert.Equal(P(15, 0), r.B);
    }

    [Theory]
    [InlineData(0, 15, 20)]
    [InlineData(1, 5, 20)]
    public void ALongerGateIsCutToFifteenTilesFromTheSelectedEnd(int selector, int aTiles, int bTiles)
    {
        GateRecord r = Record(P(0, 0), P(20, 0), selector);

        GateWalk.Collapse(ref r, GateWalk.Cap);

        Assert.Equal(P(aTiles == 15 ? 0 : aTiles, 0), r.A);
        Assert.Equal(P(bTiles == 20 && selector == 1 ? 20 : 15, 0), r.B);
    }

    [Fact]
    public void ADiagonalGateIsCutAlongItsOwnDirection()
    {
        GateRecord r = Record(P(0, 0), P(20, 20));

        GateWalk.Collapse(ref r, GateWalk.Cap);

        Assert.Equal(r.B.X, r.B.Y);
        Assert.InRange(r.B.X, 2700, 2730); // 15 tiles / √2 = 10.6 tiles, through the integer root
    }

    [Fact]
    public void ACrossingWithBothEndsWithinEightTilesChangesNothing()
    {
        GateRecord r = Record(P(0, 0), P(10, 0), 0, P(5, 0));

        GateWalk.AdjustAroundCrossing(ref r, GateWalk.Cap);

        Assert.Equal((P(0, 0), P(10, 0)), (r.A, r.B));
    }

    [Fact]
    public void ACrossingNearOneEndCollapsesTowardThatEnd()
    {
        GateRecord r = Record(P(0, 0), P(40, 0), 1, P(4, 0));

        GateWalk.AdjustAroundCrossing(ref r, GateWalk.Cap);

        Assert.Equal(0, r.Selector);
        Assert.Equal((P(0, 0), P(15, 0)), (r.A, r.B));
    }

    [Fact]
    public void ACrossingFarFromBothEndsPullsThemToEightTilesEitherSide()
    {
        GateRecord r = Record(P(0, 0), P(40, 0), 0, P(20, 0));

        GateWalk.AdjustAroundCrossing(ref r, GateWalk.Cap);

        Assert.Equal((P(12, 0), P(28, 0)), (r.A, r.B));
    }

    /// <summary>Three tenths of the way up a ten-tile gate lands one unit short of the exact
    /// point: the Q14 parameter and the product are both truncated.</summary>
    [Fact]
    public void TheCrossingIsFoundByTruncatingArithmetic()
    {
        GateRecord r = Record(P(0, 0), P(0, 10));

        Assert.True(GateWalk.Intersect(ref r, P(-5, 3), P(5, 3)));
        Assert.Equal(new NavPoint(0, (3 * U) - 1), r.Crossing);
    }

    [Fact]
    public void TheLineNotTheSegmentIsIntersected()
    {
        GateRecord r = Record(P(0, 0), P(0, 10));

        Assert.True(GateWalk.Intersect(ref r, P(-10, 3), P(-5, 3)));
        Assert.Equal(new NavPoint(0, (3 * U) - 1), r.Crossing);
    }

    [Fact]
    public void ALineBeyondTheGatesEndMisses()
    {
        GateRecord r = Record(P(0, 0), P(0, 10));

        Assert.False(GateWalk.Intersect(ref r, P(-5, 11), P(5, 11)));
        Assert.Equal(default, r.Crossing);
    }

    /// <summary>A parameter just below zero passes the ±1 range test and fails the box test, but
    /// the crossing has been written by then. Reproduced.</summary>
    [Fact]
    public void ANearMissStillWritesTheCrossing()
    {
        GateRecord r = Record(P(0, 0), P(0, 10));

        Assert.False(GateWalk.Intersect(ref r, P(-5, -1), P(5, -1)));
        Assert.Equal(new NavPoint(0, -U), r.Crossing);
    }

    [Fact]
    public void AParallelLineMisses()
    {
        GateRecord r = Record(P(0, 0), P(0, 10));

        Assert.False(GateWalk.Intersect(ref r, P(-5, 0), P(-5, 10)));
    }

    [Fact]
    public void TheSelectorMeasuresFromTheChosenEndSoOddGatesRoundApart()
    {
        GateRecord[] records = [Record(P(0, 0), new NavPoint(0, 769), 0), Record(P(0, 0), new NavPoint(0, 769), 1), Record(P(9, 9), P(9, 9))];
        var waypoints = new List<NavPoint>();

        GateWalk.Select(records, 3, P(9, 9), GateWalk.Midpoint, waypoints);

        Assert.Equal([new NavPoint(0, 384), new NavPoint(0, 385), P(9, 9)], waypoints);
    }

    [Fact]
    public void TheFractionIsClampedBelowOne()
    {
        GateRecord[] records = [Record(P(0, 0), new NavPoint(0, 769)), Record(P(9, 9), P(9, 9))];
        var waypoints = new List<NavPoint>();

        GateWalk.Select(records, 2, P(9, 9), 0x7000, waypoints);

        Assert.Equal(new NavPoint(0, 768), waypoints[0]);
    }
}
