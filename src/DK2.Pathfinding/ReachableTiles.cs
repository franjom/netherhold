// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Pathfinding;

/// <summary>Every tile a mover can reach from one start, with the route to each: the Dijkstra of
/// <see cref="GridPathfinder.TryFindNearest"/> run once to exhaustion instead of once per question.
/// <para>A caller asking "can I reach this tile, and how" of many tiles, as a worker testing each
/// candidate job does, would otherwise run one search per candidate, each failure flooding the
/// whole region before it can say no. <see cref="TryPathTo"/> gives exactly what
/// <c>TryFindNearest(grid, from, s =&gt; s == to, path)</c> gives: the same neighbour order, step
/// costs and tie order (<see cref="TileHeap"/>), so the same route, and the start entered without a
/// walkability test. A flood is for one grid and one start (<see cref="IsFor"/>), and the grid must
/// not change its answers between the flood and the questions: the contract <see cref="IPathGrid"/>
/// states for one search, now held across a caller's run of questions.</para>
/// <para>Determinism: integers, fixed orders and a generation stamp, as the pathfinder. Not
/// thread-safe.</para></summary>
public sealed class ReachableTiles
{
    private int _width;
    private int _generation;
    private int[] _stamp = [];
    private int[] _cameFrom = [];
    private int[] _cost = [];
    private readonly TileHeap _heap = new();
    private readonly List<long> _order = [];

    /// <summary>The grid the last flood ran over, or null before the first.</summary>
    public IPathGrid? Grid { get; private set; }

    /// <summary>Where the last flood started.</summary>
    public TilePos From { get; private set; }

    /// <summary>Whether the last flood was over this very grid instance from this tile.</summary>
    public bool IsFor(IPathGrid grid, TilePos from) => ReferenceEquals(Grid, grid) && From == from;

    /// <summary>Reaches every tile <paramref name="from"/> can walk to, recording the cheapest
    /// route to each. A start outside the grid reaches nothing.</summary>
    public void Flood(IPathGrid grid, TilePos from)
    {
        if (grid is null) throw new ArgumentNullException(nameof(grid));

        Grid = grid;
        From = from;
        _width = grid.Width;

        int needed = _width * grid.Height;
        if (_stamp.Length < needed)
        {
            _stamp = new int[needed];
            _cameFrom = new int[needed];
            _cost = new int[needed];
            _generation = 0;
        }
        _generation++;
        _heap.Clear();

        if (!InBounds(grid, from.X, from.Y)) return;

        int start = (from.Y * _width) + from.X;
        _stamp[start] = _generation;
        _cameFrom[start] = -1;
        _cost[start] = 0;
        _heap.Push(start, 0);

        int[] dx = GridPathfinder.NeighbourDx;
        int[] dy = GridPathfinder.NeighbourDy;
        int[] stepCost = GridPathfinder.NeighbourCost;
        while (_heap.Count > 0)
        {
            int current = _heap.Pop();
            int cx = current % _width;
            int cy = current / _width;

            for (int i = 0; i < dx.Length; i++)
            {
                int nx = cx + dx[i];
                int ny = cy + dy[i];
                if (!InBounds(grid, nx, ny) || !grid.IsWalkable(nx, ny)) continue;
                if (i >= 4 && GridPathfinder.CornerOnly(grid, cx, cy, i)) continue;

                int nextCost = _cost[current] + stepCost[i];
                int n = (ny * _width) + nx;
                if (_stamp[n] == _generation && _cost[n] <= nextCost) continue;

                _stamp[n] = _generation;
                _cameFrom[n] = current;
                _cost[n] = nextCost;
                _heap.Push(n, nextCost);
            }
        }
    }

    /// <summary>Whether the last flood reached <paramref name="to"/>. The start tile counts.</summary>
    public bool Reaches(TilePos to)
    {
        return Grid is not null && InBounds(Grid, to.X, to.Y) && _stamp[(to.Y * _width) + to.X] == _generation;
    }

    /// <summary>The cost of the route the last flood found to <paramref name="to"/>, in the
    /// pathfinder's units (<see cref="GridPathfinder.StraightCost"/>); -1 when it did not reach it.</summary>
    public int CostTo(TilePos to) => Reaches(to) ? _cost[(to.Y * _width) + to.X] : -1;

    /// <summary>The nearest of <paramref name="candidates"/> that satisfies <paramref name="isGoal"/>,
    /// with its route, as <c>TryFindNearest(grid, from, p =&gt; candidates.Contains(p) &amp;&amp; isGoal(p), path)</c>
    /// would give it. That search meets the tiles it can reach in order of route cost, then tile
    /// index, and stops at the first the predicate accepts; this asks the predicate of the reachable
    /// candidates in that same order, each once, so a caller that knows where its goals could be
    /// (the tiles bodies lie on) pays for those tiles and not for every tile of the map. The
    /// predicate must be pure, as the search's must; a candidate given twice is asked once.</summary>
    public bool TryNearest(List<TilePos> candidates, Func<TilePos, bool> isGoal, List<TilePos> path)
    {
        if (candidates is null) throw new ArgumentNullException(nameof(candidates));
        if (isGoal is null) throw new ArgumentNullException(nameof(isGoal));
        if (path is null) throw new ArgumentNullException(nameof(path));

        _order.Clear();
        foreach (TilePos candidate in candidates)
        {
            int cost = CostTo(candidate);
            if (cost < 0) continue;
            _order.Add(((long)cost << 32) | (uint)((candidate.Y * _width) + candidate.X));
        }
        _order.Sort();

        for (int i = 0; i < _order.Count; i++)
        {
            long key = _order[i];
            if (i > 0 && key == _order[i - 1]) continue;
            int index = (int)(uint)key;
            var at = new TilePos(index % _width, index / _width);
            if (isGoal(at)) return TryPathTo(at, path);
        }

        path.Clear();
        return false;
    }

    /// <summary>The route the last flood found to <paramref name="to"/>, as
    /// <see cref="IPathfinder.TryFindNearest"/> would give it: the tiles to walk through, without
    /// the start and with the destination, so a tile the mover stands on gets an empty route and
    /// <c>true</c>. <c>false</c>, and an empty route, when the flood did not reach it.</summary>
    public bool TryPathTo(TilePos to, List<TilePos> path)
    {
        if (path is null) throw new ArgumentNullException(nameof(path));

        path.Clear();
        if (!Reaches(to)) return false;

        for (int at = (to.Y * _width) + to.X; at >= 0 && _cameFrom[at] >= 0; at = _cameFrom[at])
            path.Add(new TilePos(at % _width, at / _width));

        path.Reverse();
        return true;
    }

    private static bool InBounds(IPathGrid grid, int x, int y) =>
        (uint)x < (uint)grid.Width && (uint)y < (uint)grid.Height;
}
