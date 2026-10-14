// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Pathfinding;

/// <summary>Eight-connected grid search: A* for a known destination, Dijkstra for "the nearest
/// tile satisfying a predicate". Connectivity and step cost are DK2's; the algorithm is not.
/// <para><b>DK2 does not route on a grid.</b> It searches a triangulated navigation mesh and follows
/// the result with a steering system (Q5, ADR-0155, docs/research/navigation-and-steering.md). This
/// class stands in until that is rebuilt or the divergence is accepted; it is not the original.</para>
/// <para>Eight-connected; a corner is cut only past an open side, as DK2's edge-only region fill and our mesh refuse a corner-only gap (Q28, ADR-0339). Octile step cost,
/// <b>10 orthogonal and 14 diagonal</b> — STRONGLY INFERRED (Q48): a diagonal covers root-2
/// tile-widths, and <c>CreatureMovementSystem</c> charges the same 14/10, because a router and a
/// mover that disagree produce creatures that arrive early. No terrain weighting — CONFIRMED (Q30).
/// Tie-breaking is a fixed neighbour order then tile index: reproducible, but ours.</para>
/// <para>Determinism: no floating point, no hashing, no unordered iteration; a generation stamp makes
/// a reused scratch buffer indistinguishable from a fresh one. Not thread-safe.</para></summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/imp-tasks.md")]
public sealed class GridPathfinder : IPathfinder
{
    /// <summary>What one orthogonal step costs. The unit is arbitrary; the ratio is not.</summary>
    public const int StraightCost = 10;

    /// <summary>
    /// What one diagonal step costs: <b>14</b>, the usual integer stand-in for
    /// 10 x root-2 (14.142...). See Q48.
    /// </summary>
    public const int DiagonalCost = 14;

    /// <summary>
    /// The order neighbours are expanded in — four orthogonal, then four
    /// diagonal. Fixed, because it decides which of several equally short routes
    /// is returned.
    /// </summary>
    internal static readonly int[] NeighbourDx = [0, -1, 1, 0, -1, 1, -1, 1];
    internal static readonly int[] NeighbourDy = [-1, 0, 0, 1, -1, -1, 1, 1];
    internal static readonly int[] NeighbourCost =
        [StraightCost, StraightCost, StraightCost, StraightCost,
         DiagonalCost, DiagonalCost, DiagonalCost, DiagonalCost];


    private int _width;
    private int _generation;
    private int[] _stamp = [];
    private int[] _cameFrom = [];
    private int[] _cost = [];

    private readonly TileHeap _heap = new();

    public bool TryFindPath(IPathGrid grid, TilePos from, TilePos to, List<TilePos> path)
    {
        if (grid is null) throw new ArgumentNullException(nameof(grid));
        if (path is null) throw new ArgumentNullException(nameof(path));

        path.Clear();
        if (!InBounds(grid, from) || !InBounds(grid, to)) return false;
        if (from == to) return true;
        if (!grid.IsWalkable(to.X, to.Y)) return false;

        Begin(grid);

        int start = Index(from);
        int goal = Index(to);
        _stamp[start] = _generation;
        _cameFrom[start] = -1;
        _cost[start] = 0;
        _heap.Push(start, Heuristic(from, to));

        while (_heap.Count > 0)
        {
            int current = _heap.Pop();
            if (current == goal)
            {
                BuildPath(current, path);
                return true;
            }

            int cx = current % _width;
            int cy = current / _width;

            for (int i = 0; i < NeighbourDx.Length; i++)
            {
                int nx = cx + NeighbourDx[i];
                int ny = cy + NeighbourDy[i];
                if (!InBounds(grid, nx, ny) || !grid.IsWalkable(nx, ny)) continue;
                if (i >= 4 && CornerOnly(grid, cx, cy, i)) continue;

                int nextCost = _cost[current] + NeighbourCost[i];
                int n = (ny * _width) + nx;
                bool seen = _stamp[n] == _generation;
                if (seen && _cost[n] <= nextCost) continue;

                _stamp[n] = _generation;
                _cameFrom[n] = current;
                _cost[n] = nextCost;
                _heap.Push(n, nextCost + Heuristic(new TilePos(nx, ny), to));
            }
        }

        return false;
    }

    public bool TryFindNearest(IPathGrid grid, TilePos from, Func<TilePos, bool> isGoal, List<TilePos> path)
    {
        if (grid is null) throw new ArgumentNullException(nameof(grid));
        if (isGoal is null) throw new ArgumentNullException(nameof(isGoal));
        if (path is null) throw new ArgumentNullException(nameof(path));

        path.Clear();
        if (!InBounds(grid, from)) return false;

        Begin(grid);

        // The starting tile is entered without being tested for walkability: a
        // creature standing somewhere can always leave it, even if the terrain
        // under it has since become something it could not have walked onto.
        int start = Index(from);
        _stamp[start] = _generation;
        _cameFrom[start] = -1;
        _cost[start] = 0;
        _heap.Push(start, 0);

        // Dijkstra, not breadth-first: steps stopped costing the same when movement went
        // eight-connected (Q48, 10 orthogonal and 14 diagonal), and a breadth-first sweep would
        // return the goal reachable in the fewest STEPS rather than the nearest one. The
        // distinction is observed — the owner confirmed an imp takes the marked tile closest by
        // WALKING DISTANCE (Q25), and three diagonal steps are further than four straight ones.
        while (_heap.Count > 0)
        {
            int current = _heap.Pop();
            int cx = current % _width;
            int cy = current / _width;

            // Popped in cost order, so the first goal off the heap is the
            // cheapest to reach and the search can stop there.
            if (isGoal(new TilePos(cx, cy)))
            {
                BuildPath(current, path);
                return true;
            }

            for (int i = 0; i < NeighbourDx.Length; i++)
            {
                int nx = cx + NeighbourDx[i];
                int ny = cy + NeighbourDy[i];
                if (!InBounds(grid, nx, ny) || !grid.IsWalkable(nx, ny)) continue;
                if (i >= 4 && CornerOnly(grid, cx, cy, i)) continue;

                int nextCost = _cost[current] + NeighbourCost[i];
                int n = (ny * _width) + nx;
                if (_stamp[n] == _generation && _cost[n] <= nextCost) continue;

                _stamp[n] = _generation;
                _cameFrom[n] = current;
                _cost[n] = nextCost;
                _heap.Push(n, nextCost);
            }
        }

        return false;
    }

    /// <summary>
    /// A diagonal step whose two orthogonal neighbours are both unwalkable is no step: DK2's region
    /// fill (<c>0x004C91A0</c>) walks edge neighbours only, so its route pre-check (<c>0x004C9400</c>)
    /// refuses two floors that touch at one vertex; the Level 2 capture's dig sides at 3063-3082 agree,
    /// and so does our mesh router (ADR-0157). One open side is enough, as for the mesh (ADR-0339).
    /// </summary>
    internal static bool CornerOnly(IPathGrid grid, int cx, int cy, int i) =>
        !grid.IsWalkable(cx + NeighbourDx[i], cy) && !grid.IsWalkable(cx, cy + NeighbourDy[i]);

    /// <summary>
    /// Octile distance: the exact cost of the cheapest unobstructed route, so it
    /// is admissible and consistent for these step costs and A* stays optimal.
    /// Take as many diagonals as the shorter axis allows, then go straight.
    /// </summary>
    private static int Heuristic(TilePos a, TilePos b)
    {
        int dx = Math.Abs(a.X - b.X);
        int dy = Math.Abs(a.Y - b.Y);
        int diagonal = Math.Min(dx, dy);
        int straight = Math.Max(dx, dy) - diagonal;
        return (diagonal * DiagonalCost) + (straight * StraightCost);
    }

    private void Begin(IPathGrid grid)
    {
        _width = grid.Width;

        int needed = _width * grid.Height;
        if (_stamp.Length < needed)
        {
            _stamp = new int[needed];
            _cameFrom = new int[needed];
            _cost = new int[needed];
            _generation = 0;
        }

        // A fresh generation invalidates every stamp left by earlier searches,
        // so a reused buffer behaves exactly like a newly allocated one.
        _generation++;
        _heap.Clear();
    }

    private int Index(TilePos p) => (p.Y * _width) + p.X;

    private static bool InBounds(IPathGrid grid, TilePos p) => InBounds(grid, p.X, p.Y);

    private static bool InBounds(IPathGrid grid, int x, int y) =>
        (uint)x < (uint)grid.Width && (uint)y < (uint)grid.Height;

    /// <summary>Walks the parent chain back to the start, then reverses it. The start tile is not included.</summary>
    private void BuildPath(int goal, List<TilePos> path)
    {
        for (int at = goal; at >= 0 && _cameFrom[at] >= 0; at = _cameFrom[at])
            path.Add(new TilePos(at % _width, at / _width));

        path.Reverse();
    }
}
