// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Pathfinding;

/// <summary>DK2's route search (docs/research/navigation-mesh-and-search.md §3, §4): greedy
/// best-first over the mesh's triangles, keyed on the nearest vertex's squared distance to the goal
/// in whole tiles, shifted by a 1–4 transition cost, with no accumulated distance anywhere. Run
/// from both ends, each corridor funnelled, the shorter kept. It is not Dijkstra and not A*, and
/// must not be made so: a shortest-path router here would be a fidelity bug (ADR-0157).
/// <para>The heap is the original's 1024-entry binary heap; ties are settled by its sift order,
/// which is what decides between equally near triangles. Scratch arrays are reused between
/// calls with a generation stamp, as the original does. Not thread-safe.</para></summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/navigation-mesh-and-search.md")]
public sealed class MeshRouter
{
    public const int HeapCapacity = 1024;

    private readonly NavMesh _mesh;
    private byte[] _stamp = [];
    private int[] _pred = [];
    private int[] _key = [];
    private readonly int[] _heap = new int[HeapCapacity + 2];
    private int _heapCount;
    private byte _generation;

    private readonly List<int> _corridorA = [];
    private readonly List<int> _corridorB = [];
    private readonly List<int> _route = [];
    private readonly List<NavPoint> _pathA = [];
    private readonly List<NavPoint> _pathB = [];
    private readonly GateRecord[] _records = new GateRecord[GateWalk.MaxRecords + 1];

    public MeshRouter(NavMesh mesh) => _mesh = mesh ?? throw new ArgumentNullException(nameof(mesh));

    /// <summary>The whole request: locate both ends, search from the destination toward the
    /// mover and from the mover toward the destination, funnel both, keep the shorter. Either
    /// search failing fails the request, as the original's driver does. The kept corridor is then
    /// funnelled again in walking order and re-walked by <see cref="GateWalk"/>: the waypoints are
    /// the midpoint of each kept gate, then the destination.</summary>
    public bool TryRoute(NavPoint from, NavPoint to, MoverProfile mover, List<NavPoint> waypoints)
    {
        if (waypoints is null) throw new ArgumentNullException(nameof(waypoints));
        waypoints.Clear();

        int fromTri = _mesh.LocateEndpoint(from.X, from.Y);
        int toTri = _mesh.LocateEndpoint(to.X, to.Y);
        if (fromTri < 0 || toTri < 0) return false;

        // Forward: seeded at the destination, drawn toward the mover, so the predecessor chain
        // already reads mover -> destination.
        if (!TrySearch(toTri, fromTri, from, mover, _corridorA, seededAtDestination: true)) return false;
        if (!TrySearch(fromTri, toTri, to, mover, _corridorB, seededAtDestination: false)) return false;

        _pathA.Clear();
        _pathB.Clear();
        int lengthA = Funnel.Reduce(_mesh, from, to, _corridorA, _pathA);
        int lengthB = Funnel.Reduce(_mesh, to, from, _corridorB, _pathB);

        List<int> route = _corridorA;
        if (lengthA > lengthB)
        {
            // The backward corridor runs destination -> mover; `Bak reverse choice` copies its
            // triangles reversed over the forward one before the funnel runs again.
            _route.Clear();
            for (int i = _corridorB.Count - 1; i >= 0; i--) _route.Add(_corridorB[i]);
            route = _route;
        }

        _pathA.Clear();
        Funnel.Reduce(_mesh, from, to, route, _pathA);
        int count = GateWalk.Run(_mesh, from, to, route, _pathA, _records, GateWalk.Cap);
        GateWalk.Select(_records, count, to, GateWalk.Midpoint, waypoints);
        return true;
    }

    /// <summary>One direction of the search (`0x004CD000`). The corridor comes out ordered from
    /// <paramref name="target"/> back to <paramref name="seed"/> by following predecessors, which
    /// is the walking order when the seed is the destination.</summary>
    public bool TrySearch(int seed, int target, NavPoint goal, MoverProfile mover, List<int> corridor, bool seededAtDestination = true)
    {
        corridor.Clear();
        EnsureScratch();
        NextGeneration();

        int gx = goal.X / NavMesh.UnitsPerTile, gy = goal.Y / NavMesh.UnitsPerTile;

        _key[seed] = 1;
        _pred[seed] = seed;
        _stamp[seed] = _generation;
        _heapCount = 0;
        if (!Push(seed)) return false;

        // Two pops a round (`0x004CD15A`–`0x004CD1C4`): the root and then, if the new root is
        // not the goal, the next one; both are expanded before the root is tested again.
        while (_heapCount > 0)
        {
            if (_heap[1] == target) break;
            int first = Pop();
            int second = -1;
            if (_heapCount > 0)
            {
                if (_heap[1] == target) break;
                second = Pop();
            }

            if (!Expand(first, gx, gy, mover, seededAtDestination)) return false;
            if (second >= 0 && !Expand(second, gx, gy, mover, seededAtDestination)) return false;
        }

        if (_heapCount == 0 || _heap[1] != target) return false;

        for (int t = target; ; t = _pred[t])
        {
            corridor.Add(t);
            if (t == seed) break;
        }
        return true;
    }

    /// <summary>The original's cost function (`0x004C8BD0`, navigation-mesh-and-search.md §4) on
    /// the byte of the tile walked into and the tile left. Zero is impassable; otherwise one plus
    /// the entered tile's category, which the search shifts its key by. A door tile carries its
    /// owner in the high nibble, bit 2 when locked and bit 1 from the tile's tag bit, and the
    /// mover's option bits decide which doors stop it (<see cref="DoorPassable"/>).</summary>
    public static int TransitionCost(byte to, byte from, MoverProfile mover)
    {
        if (NavBytes.IsSolid(to)) return 0;
        int cost = NavBytes.Category(to) + 1;
        if (NavBytes.IsDoor(to))
        {
            bool foreign = NavBytes.Owner(to) != mover.Owner;
            if (foreign && (to & NavBytes.DoorTagged) != 0) return 0;
            return DoorPassable(to, foreign, mover) ? cost : 0;
        }

        if (NavBytes.IsDoor(from))
            return DoorPassable(from, NavBytes.Owner(from) != mover.Owner, mover) ? cost : 0;

        int tc = NavBytes.Category(to), fc = NavBytes.Category(from);
        if (tc == 3 && fc != 3 && !mover.CanWalkOnLava) return 0;
        if (tc == 1 && fc != 1 && !mover.CanWalkOnWater) return 0;
        if (!mover.EntersPortalHole && ((to ^ from) & NavBytes.PortalHole) != 0) return 0;
        return cost;
    }

    /// <summary>The door arms of `0x004C8BD0`: with the respect-doors option (bit 7 of the state's
    /// word) every foreign door and every locked own door stops the mover; without it only a
    /// locked own door does, and the pass-own-locked option (bit 1) lifts even that.</summary>
    private static bool DoorPassable(byte door, bool foreign, MoverProfile mover)
    {
        bool locked = (door & NavBytes.DoorLocked) != 0;
        if (mover.RespectDoors) return !foreign && !locked;
        if (mover.PassOwnLockedDoors) return true;
        return foreign || !locked;
    }

    private void EnsureScratch()
    {
        int slots = _mesh.TriangleSlots;
        if (_stamp.Length >= slots) return;
        int size = Math.Max(slots, 64);
        Array.Resize(ref _stamp, size);
        Array.Resize(ref _pred, size);
        Array.Resize(ref _key, size);
    }

    private void NextGeneration()
    {
        _generation++;
        if (_generation != 0) return;
        Array.Clear(_stamp, 0, _stamp.Length);
        _generation = 1;
    }

    /// <summary>Push each unvisited, passable neighbour keyed on its nearest vertex's squared
    /// distance to the goal, shifted by the transition cost; false only on heap overflow.</summary>
    private bool Expand(int current, int gx, int gy, MoverProfile mover, bool seededAtDestination)
    {
        byte currentNav = _mesh.NavByte(current);
        for (int slot = 0; slot < 3; slot++)
        {
            int n = _mesh.Neighbour(current, slot);
            if (n == NavMesh.NoNeighbour || _stamp[n] == _generation) continue;

            // The tile walked into is judged: seeded at the destination the walk runs n -> current
            // (`0x004CD000` pushes current second), from the mover it runs current -> n (`0x004CD490`).
            byte nNav = _mesh.NavByte(n);
            int cost = seededAtDestination ? TransitionCost(currentNav, nNav, mover) : TransitionCost(nNav, currentNav, mover);
            if (cost == 0) continue;

            int best = int.MaxValue;
            for (int i = 0; i < 3; i++)
            {
                int p = _mesh.Vertex(n, i);
                int dx = gx - _mesh.PointX(p), dy = gy - _mesh.PointY(p);
                int d2 = (dx * dx) + (dy * dy);
                if (d2 < best) best = d2;
            }

            _key[n] = best << ((2 * cost) - 2);
            _stamp[n] = _generation;
            _pred[n] = current;
            if (!Push(n)) return false;
        }
        return true;
    }

    /// <summary>The original's push (`0x004CCF00`): append, then sift up while the key is not
    /// greater than the parent's — so a newcomer rises past parents with an equal key.</summary>
    private bool Push(int t)
    {
        if (_heapCount >= HeapCapacity) return false;
        int i = ++_heapCount;
        while (i > 1)
        {
            int parent = i >> 1;
            if (_key[t] > _key[_heap[parent]]) break;
            _heap[i] = _heap[parent];
            i = parent;
        }
        _heap[i] = t;
        return true;
    }

    /// <summary>The original's pop (`0x004CCF70`): move the last entry to the root and sift it
    /// down toward the smaller child — the left one on a tie — stopping only when the moving key
    /// is strictly smaller than the child's, so it sinks below equal keys. Returns the root.</summary>
    private int Pop()
    {
        int root = _heap[1];
        int t = _heap[_heapCount--];
        int i = 1;
        while (true)
        {
            int l = i << 1, r = l + 1, child;
            if (l > _heapCount) break;
            child = (r <= _heapCount && _key[_heap[r]] < _key[_heap[l]]) ? r : l;
            if (_key[t] < _key[_heap[child]]) break;
            _heap[i] = _heap[child];
            i = child;
        }
        _heap[i] = t;
        return root;
    }
}
