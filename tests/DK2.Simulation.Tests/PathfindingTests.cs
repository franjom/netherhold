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
        // Four-connected, so the length is the Manhattan detour: across to the
        // gap, down through it, and back.
        Assert.Equal(8, path.Count);
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
            Assert.Equal(1, previous.ManhattanDistance(step));
            previous = step;
        }
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
