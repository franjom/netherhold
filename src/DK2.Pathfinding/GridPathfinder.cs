using DK2.Core;

namespace DK2.Pathfinding;

/// <summary>
/// Four-connected grid search: A* for a known destination, breadth-first for
/// "the nearest tile that satisfies a predicate".
///
/// <para><b>This is ours, not DK2's (Q5).</b> Three choices here are visible in
/// gameplay and none of them is evidence about the original:</para>
/// <list type="bullet">
/// <item><b>Four-connected.</b> Movement is north/west/east/south only. Chosen
/// because it is the conservative option: an eight-connected search can return a
/// route that squeezes diagonally between two solid tiles, and whether DK2
/// permits that is UNKNOWN. A four-connected route is always walkable by an
/// eight-connected mover, so replacing this later can only widen the set of
/// paths, never invalidate one.</item>
/// <item><b>Uniform step cost.</b> Every walkable tile costs the same to enter.
/// DK2 may weight terrain (water is visibly slower to cross); UNKNOWN.</item>
/// <item><b>Tie-breaking.</b> Several routes are usually equally short, and
/// which one comes back is decided by a fixed neighbour order and then by tile
/// index. That makes the result reproducible, which is what the determinism gate
/// needs, but it is an arbitrary rule rather than a reconstructed one.</item>
/// </list>
///
/// <para>Determinism: no floating point, no hashing, no unordered iteration.
/// The instance keeps scratch buffers between calls purely to avoid allocating,
/// and a generation stamp makes a reused buffer indistinguishable from a fresh
/// one — asserted by <c>PathfindingTests.RepeatedSearches_DoNotContaminateEachOther</c>.
/// Not thread-safe; the simulation is single-threaded by design.</para>
/// </summary>
[Dk2Behavior(Confidence.Unknown, "docs/research/imp-tasks.md")]
public sealed class GridPathfinder : IPathfinder
{
    /// <summary>
    /// The order neighbours are expanded in. Fixed, because it decides which of
    /// several equally short routes is returned.
    /// </summary>
    private static readonly int[] NeighbourDx = [0, -1, 1, 0];
    private static readonly int[] NeighbourDy = [-1, 0, 0, 1];

    private int _width;
    private int _generation;
    private int[] _stamp = [];
    private int[] _cameFrom = [];
    private int[] _cost = [];

    private readonly List<int> _queue = [];
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

            int nextCost = _cost[current] + 1;
            int cx = current % _width;
            int cy = current / _width;

            for (int i = 0; i < NeighbourDx.Length; i++)
            {
                int nx = cx + NeighbourDx[i];
                int ny = cy + NeighbourDy[i];
                if (!InBounds(grid, nx, ny) || !grid.IsWalkable(nx, ny)) continue;

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
        _queue.Add(start);

        for (int head = 0; head < _queue.Count; head++)
        {
            int current = _queue[head];
            int cx = current % _width;
            int cy = current / _width;

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

                int n = (ny * _width) + nx;
                if (_stamp[n] == _generation) continue;

                _stamp[n] = _generation;
                _cameFrom[n] = current;
                _cost[n] = _cost[current] + 1;
                _queue.Add(n);
            }
        }

        return false;
    }

    /// <summary>Manhattan distance — admissible and consistent for four-connected uniform steps.</summary>
    private static int Heuristic(TilePos a, TilePos b) => a.ManhattanDistance(b);

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
        _queue.Clear();
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
