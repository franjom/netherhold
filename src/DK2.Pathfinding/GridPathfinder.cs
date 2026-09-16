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
/// <para>Eight-connected and corners may be cut — CONFIRMED by observation (Q28). Octile step cost,
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
    private static readonly int[] NeighbourDx = [0, -1, 1, 0, -1, 1, -1, 1];
    private static readonly int[] NeighbourDy = [-1, 0, 0, 1, -1, -1, 1, 1];
    private static readonly int[] NeighbourCost =
        [StraightCost, StraightCost, StraightCost, StraightCost,
         DiagonalCost, DiagonalCost, DiagonalCost, DiagonalCost];


    private int _width;
    private int _generation;
    private int[] _stamp = [];
    private int[] _cameFrom = [];
    private int[] _cost = [];

    private readonly List<int> _heapTile = [];
    private readonly List<int> _heapKey = [];

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
        HeapPush(start, Heuristic(from, to));

        while (_heapTile.Count > 0)
        {
            int current = HeapPop();
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

                // No corner-cutting check: the original lets a creature pass
                // diagonally between two solid tiles (Q28, owner-confirmed), so a
                // diagonal neighbour is reachable whatever its two orthogonal
                // neighbours are.
                int nextCost = _cost[current] + NeighbourCost[i];
                int n = (ny * _width) + nx;
                bool seen = _stamp[n] == _generation;
                if (seen && _cost[n] <= nextCost) continue;

                _stamp[n] = _generation;
                _cameFrom[n] = current;
                _cost[n] = nextCost;
                HeapPush(n, nextCost + Heuristic(new TilePos(nx, ny), to));
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
        HeapPush(start, 0);

        // Dijkstra, not breadth-first: steps stopped costing the same when movement went
        // eight-connected (Q48, 10 orthogonal and 14 diagonal), and a breadth-first sweep would
        // return the goal reachable in the fewest STEPS rather than the nearest one. The
        // distinction is observed — the owner confirmed an imp takes the marked tile closest by
        // WALKING DISTANCE (Q25), and three diagonal steps are further than four straight ones.
        while (_heapTile.Count > 0)
        {
            int current = HeapPop();
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

                int nextCost = _cost[current] + NeighbourCost[i];
                int n = (ny * _width) + nx;
                if (_stamp[n] == _generation && _cost[n] <= nextCost) continue;

                _stamp[n] = _generation;
                _cameFrom[n] = current;
                _cost[n] = nextCost;
                HeapPush(n, nextCost);
            }
        }

        return false;
    }

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
        _heapTile.Clear();
        _heapKey.Clear();
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

    // A binary min-heap keyed on (estimated total cost, tile index). The second
    // key is what makes the choice between equally promising tiles reproducible.

    private void HeapPush(int tile, int key)
    {
        _heapTile.Add(tile);
        _heapKey.Add(key);

        int child = _heapTile.Count - 1;
        while (child > 0)
        {
            int parent = (child - 1) / 2;
            if (!Less(child, parent)) break;
            Swap(child, parent);
            child = parent;
        }
    }

    private int HeapPop()
    {
        int result = _heapTile[0];
        int last = _heapTile.Count - 1;

        _heapTile[0] = _heapTile[last];
        _heapKey[0] = _heapKey[last];
        _heapTile.RemoveAt(last);
        _heapKey.RemoveAt(last);

        int parent = 0;
        while (true)
        {
            int left = (2 * parent) + 1;
            if (left >= _heapTile.Count) break;

            int right = left + 1;
            int best = right < _heapTile.Count && Less(right, left) ? right : left;
            if (!Less(best, parent)) break;

            Swap(best, parent);
            parent = best;
        }

        return result;
    }

    private bool Less(int a, int b) =>
        _heapKey[a] != _heapKey[b] ? _heapKey[a] < _heapKey[b] : _heapTile[a] < _heapTile[b];

    private void Swap(int a, int b)
    {
        (_heapTile[a], _heapTile[b]) = (_heapTile[b], _heapTile[a]);
        (_heapKey[a], _heapKey[b]) = (_heapKey[b], _heapKey[a]);
    }
}
