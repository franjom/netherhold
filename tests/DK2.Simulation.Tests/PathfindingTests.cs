// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;

namespace DK2.Simulation.Tests;

/// <summary>
/// The grid search, on grids drawn as text so the shape being tested is
/// visible.
///
/// These prove the search is correct and reproducible. They prove nothing about
/// Dungeon Keeper 2 — how the original routes creatures is Q5, and
/// <see cref="GridPathfinder"/> is an admitted placeholder for it. What is worth
/// pinning is exactly the part a replacement must also provide: shortest routes,
/// no route through walls, and the same answer every time.
/// </summary>
public class PathfindingTests
{
    /// <summary>A grid from rows of characters; '#' is wall, anything else is floor.</summary>
    private sealed class TextGrid : IPathGrid
    {
        private readonly string[] _rows;

        public TextGrid(params string[] rows) => _rows = rows;

        public int Width => _rows[0].Length;
        public int Height => _rows.Length;

        public bool IsWalkable(int x, int y) =>
            (uint)x < (uint)Width && (uint)y < (uint)Height && _rows[y][x] != '#';
    }

    private static readonly TextGrid Open = new(
        ".....",
        ".....",
        ".....",
        ".....",
        ".....");

    /// <summary>A wall with a single gap, so there is exactly one way through.</summary>
    private static readonly TextGrid OneDoor = new(
        ".....",
        ".....",
        "##.##",
        ".....",
        ".....");

    private static readonly TextGrid Sealed = new(
        ".....",
        ".....",
        "#####",
        ".....",
        ".....");

    // ---- point to point --------------------------------------------------

    [Fact]
    public void AStraightWalk_IsAsLongAsTheDistance()
    {
        var path = new List<TilePos>();

        Assert.True(new GridPathfinder().TryFindPath(Open, new TilePos(0, 0), new TilePos(4, 0), path));

        Assert.Equal(4, path.Count);
        Assert.Equal(new TilePos(4, 0), path[path.Count - 1]);
    }

    [Fact]
    public void AWalkToWhereYouAlreadyStand_IsEmptyAndSucceeds()
    {
        var path = new List<TilePos>();

        Assert.True(new GridPathfinder().TryFindPath(Open, new TilePos(2, 2), new TilePos(2, 2), path));

        Assert.Empty(path);
    }

    [Fact]
    public void ARouteAroundAWall_GoesThroughTheGap()
    {
        var path = new List<TilePos>();

        Assert.True(new GridPathfinder().TryFindPath(OneDoor, new TilePos(0, 0), new TilePos(0, 4), path));

        Assert.Contains(new TilePos(2, 2), path);

        // FOUR steps, all of them diagonal: (1,1), (2,2), (1,3), (0,4). The gap
        // at (2,2) is the only way through and a creature can reach it on the
        // bias (Q28), so the detour costs 4 x 14 = 56 rather than the eight
        // orthogonal steps a four-connected search had to take.
        Assert.Equal(4, path.Count);
        Assert.Equal(
            [new TilePos(1, 1), new TilePos(2, 2), new TilePos(1, 3), new TilePos(0, 4)],
            path);
    }

    [Fact]
    public void EveryStepOfAPath_IsWalkableAndAdjacentToTheLast()
    {
        var path = new List<TilePos>();
        Assert.True(new GridPathfinder().TryFindPath(OneDoor, new TilePos(0, 0), new TilePos(4, 4), path));

        TilePos previous = new(0, 0);
        foreach (TilePos step in path)
        {
            Assert.True(OneDoor.IsWalkable(step.X, step.Y), $"{step} is a wall");

            // Eight-connected now (Q28): a step moves at most one tile on each
            // axis and at least one somewhere, so Chebyshev distance is 1 and
            // Manhattan is 1 or 2. Asserting Manhattan == 1 was the
            // four-connected assumption written into a test.
            int dx = Math.Abs(step.X - previous.X);
            int dy = Math.Abs(step.Y - previous.Y);
            Assert.Equal(1, Math.Max(dx, dy));
            previous = step;
        }
    }

    // ---- eight-connected movement (Q28) and octile cost (Q48) --------------

    /// <summary>Two floors touching only at a corner, both orthogonal neighbours solid.</summary>
    private static readonly TextGrid CornerOnly = new(
        "..##",
        "..##",
        "##..",
        "##..");

    /// <summary>The same, with one of the two corner tiles opened.</summary>
    private static readonly TextGrid OneSideOpen = new(
        "..##",
        "..##",
        "#...",
        "##..");

    /// <summary>
    /// A corner-only gap is not a route: DK2's region fill (<c>0x004C91A0</c>) walks edge neighbours
    /// only, so its pre-check refuses the pair; our mesh router refuses it; and the Level 2 capture
    /// agrees: six imps at ticks 3063-3082 pass a tagged wall whose spare slots lay only behind such a
    /// gap and dig the next wall instead. The grid search used to let a creature squeeze through
    /// (Q28); it agrees with the mesh now (ADR-0339).
    /// </summary>
    [Fact]
    public void ACornerOnlyGap_IsNotARoute_AsTheMeshRefusesIt()
    {
        var path = new List<TilePos>();
        var finder = new GridPathfinder();

        Assert.False(finder.TryFindPath(CornerOnly, new TilePos(0, 0), new TilePos(3, 3), path));
        Assert.False(finder.TryFindNearest(CornerOnly, new TilePos(0, 0), p => p == new TilePos(3, 3), path));

        // One open side beside the corner and the diagonal step is allowed again, as the mesh routes it.
        Assert.True(finder.TryFindPath(OneSideOpen, new TilePos(0, 0), new TilePos(3, 3), path));
        Assert.Equal([new TilePos(1, 1), new TilePos(2, 2), new TilePos(3, 3)], path);
    }

    /// <summary>
    /// <b>A creature crosses on the bias.</b> CONFIRMED (Q28, owner): DK2
    /// creatures are eight-connected. Four diagonal steps beat the eight
    /// orthogonal ones a four-connected search was obliged to take.
    /// </summary>
    [Fact]
    public void AcrossOpenFloor_TheRouteIsDiagonal()
    {
        var path = new List<TilePos>();

        Assert.True(new GridPathfinder().TryFindPath(Open, new TilePos(0, 0), new TilePos(4, 4), path));

        Assert.Equal(
            [new TilePos(1, 1), new TilePos(2, 2), new TilePos(3, 3), new TilePos(4, 4)],
            path);
    }

    /// <summary>
    /// <b>A diagonal is not a free shortcut.</b> Three diagonal steps cost 42 and
    /// four orthogonal ones cost 40, so the four-step goal is the nearer one —
    /// and a search that counted steps instead of distance would return the
    /// other. That distinction is observable and observed: the owner confirmed an
    /// imp takes the marked tile nearest by <i>walking distance</i> (Q25).
    /// </summary>
    [Fact]
    public void TheNearestGoal_IsNearestByDistanceAndNotByStepCount()
    {
        var path = new List<TilePos>();
        var goals = new HashSet<TilePos> { new(3, 3), new(4, 0) };

        Assert.True(new GridPathfinder().TryFindNearest(
            Open, new TilePos(0, 0), goals.Contains, path));

        Assert.Equal(new TilePos(4, 0), path[^1]);
        Assert.Equal(4, path.Count);
    }

    /// <summary>
    /// The heuristic must never overestimate, or A* stops returning shortest
    /// routes. Checked against the search's own answer on an open grid, where
    /// the two have to agree exactly: with nothing in the way, the cheapest
    /// route IS the octile distance.
    /// </summary>
    [Theory]
    [InlineData(0, 0, 4, 4, 4 * GridPathfinder.DiagonalCost)]
    [InlineData(0, 0, 4, 0, 4 * GridPathfinder.StraightCost)]
    [InlineData(0, 0, 4, 2, (2 * GridPathfinder.DiagonalCost) + (2 * GridPathfinder.StraightCost))]
    [InlineData(0, 0, 0, 3, 3 * GridPathfinder.StraightCost)]
    public void OnOpenFloor_TheCheapestRouteIsTheOctileDistance(
        int fromX, int fromY, int toX, int toY, int expectedCost)
    {
        var path = new List<TilePos>();
        Assert.True(new GridPathfinder().TryFindPath(
            Open, new TilePos(fromX, fromY), new TilePos(toX, toY), path));

        int cost = 0;
        TilePos previous = new(fromX, fromY);
        foreach (TilePos step in path)
        {
            cost += step.X != previous.X && step.Y != previous.Y
                ? GridPathfinder.DiagonalCost
                : GridPathfinder.StraightCost;
            previous = step;
        }

        Assert.Equal(expectedCost, cost);
    }

    [Fact]
    public void NoRouteExists_WhenTheWallIsUnbroken()
    {
        var path = new List<TilePos>();

        Assert.False(new GridPathfinder().TryFindPath(Sealed, new TilePos(0, 0), new TilePos(0, 4), path));
        Assert.Empty(path);
    }

    [Fact]
    public void AWallIsNotADestination()
    {
        var path = new List<TilePos>();

        Assert.False(new GridPathfinder().TryFindPath(Sealed, new TilePos(0, 0), new TilePos(0, 2), path));
    }

    [Fact]
    public void TilesOutsideTheGrid_AreNotReachable()
    {
        var finder = new GridPathfinder();
        var path = new List<TilePos>();

        Assert.False(finder.TryFindPath(Open, new TilePos(-1, 0), new TilePos(0, 0), path));
        Assert.False(finder.TryFindPath(Open, new TilePos(0, 0), new TilePos(5, 0), path));
    }

    // ---- nearest goal ----------------------------------------------------

    [Fact]
    public void TheNearestGoal_IsTheOneFewestStepsAway_NotTheOneNearestInSpace()
    {
        // (4,0) is 4 steps away in the open. (0,4) is 4 steps away too as the
        // crow flies but 8 through the one gap, so the search must prefer the
        // first even though both look equally close on a map.
        var path = new List<TilePos>();
        var goals = new[] { new TilePos(4, 0), new TilePos(0, 4) };

        Assert.True(new GridPathfinder().TryFindNearest(
            OneDoor, new TilePos(0, 0), p => goals.Contains(p), path));

        Assert.Equal(new TilePos(4, 0), path[path.Count - 1]);
    }

    [Fact]
    public void AGoalYouAreStandingOn_NeedsNoWalking()
    {
        var path = new List<TilePos>();

        Assert.True(new GridPathfinder().TryFindNearest(
            Open, new TilePos(2, 2), p => p == new TilePos(2, 2), path));

        Assert.Empty(path);
    }

    [Fact]
    public void AnUnreachableGoal_IsNotFoundAtAll()
    {
        var path = new List<TilePos>();

        Assert.False(new GridPathfinder().TryFindNearest(
            Sealed, new TilePos(0, 0), p => p == new TilePos(0, 4), path));
    }

    // ---- reproducibility -------------------------------------------------

    [Fact]
    public void TheSameQuestion_AlwaysGetsTheSameAnswer()
    {
        // Not "a shortest path" but "the same shortest path": several routes are
        // equally short here, and the determinism gate needs the choice between
        // them to be fixed rather than merely optimal.
        var first = new List<TilePos>();
        var second = new List<TilePos>();

        Assert.True(new GridPathfinder().TryFindPath(Open, new TilePos(0, 0), new TilePos(4, 4), first));
        Assert.True(new GridPathfinder().TryFindPath(Open, new TilePos(0, 0), new TilePos(4, 4), second));

        Assert.Equal(first, second);
    }

    [Fact]
    public void RepeatedSearches_DoNotContaminateEachOther()
    {
        // The instance keeps its working buffers between calls. This is the test
        // that keeps that an optimisation rather than a source of divergence:
        // a reused finder must answer exactly like a fresh one, whatever it was
        // asked before, and whatever size grid it saw.
        var reused = new GridPathfinder();
        var scratch = new List<TilePos>();

        reused.TryFindPath(Open, new TilePos(0, 0), new TilePos(4, 4), scratch);
        reused.TryFindNearest(OneDoor, new TilePos(4, 4), p => p == new TilePos(0, 0), scratch);
        reused.TryFindPath(Sealed, new TilePos(0, 0), new TilePos(0, 4), scratch);

        var afterUse = new List<TilePos>();
        var fresh = new List<TilePos>();
        Assert.True(reused.TryFindPath(OneDoor, new TilePos(0, 0), new TilePos(4, 4), afterUse));
        Assert.True(new GridPathfinder().TryFindPath(OneDoor, new TilePos(0, 0), new TilePos(4, 4), fresh));

        Assert.Equal(fresh, afterUse);
    }

    // ---- the world as a grid ---------------------------------------------

    [Fact]
    public void SolidTerrain_IsNotWalkable_AndFloorIs()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = Dk2TerrainFixture.World();
        var grid = new WorldPathGrid(database, world, Dk2TerrainFixture.Imp());

        Assert.True(grid.IsWalkable(2, 2));   // claimed path, inside the room
        Assert.False(grid.IsWalkable(5, 5));  // a gold seam
        Assert.False(grid.IsWalkable(0, 0));  // impenetrable border
        Assert.False(grid.IsWalkable(-1, 0)); // off the map
    }
}
