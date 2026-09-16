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
    private readonly List<NavPoint> _pathA = [];
    private readonly List<NavPoint> _pathB = [];

    public MeshRouter(NavMesh mesh) => _mesh = mesh ?? throw new ArgumentNullException(nameof(mesh));

    /// <summary>The whole request: locate both ends, search from the destination toward the
    /// mover and from the mover toward the destination, funnel both, keep the shorter. Either
    /// search failing fails the request, as the original's driver does. Waypoints are the turn
    /// corners followed by the destination.</summary>
    public bool TryRoute(NavPoint from, NavPoint to, MoverProfile mover, List<NavPoint> waypoints)
    {
        if (waypoints is null) throw new ArgumentNullException(nameof(waypoints));
        waypoints.Clear();

        int fromTri = _mesh.Locate(from.X, from.Y);
        int toTri = _mesh.Locate(to.X, to.Y);
        if (fromTri < 0 || toTri < 0) return false;

        // Forward: seeded at the destination, drawn toward the mover, so the predecessor chain
        // already reads mover -> destination.
        if (!TrySearch(toTri, fromTri, from, mover, _corridorA)) return false;
        if (!TrySearch(fromTri, toTri, to, mover, _corridorB)) return false;

        _pathA.Clear();
        _pathB.Clear();
        int lengthA = Funnel.Reduce(_mesh, from, to, _corridorA, _pathA);
        int lengthB = Funnel.Reduce(_mesh, to, from, _corridorB, _pathB);

        if (lengthA > lengthB)
        {
            // The backward path runs destination -> mover: its corners reversed, then the
            // destination — `Bak reverse choice`.
            for (int i = _pathB.Count - 2; i >= 0; i--) waypoints.Add(_pathB[i]);
            waypoints.Add(to);
        }
        else
        {
            waypoints.AddRange(_pathA);
        }
        return true;
    }

    /// <summary>One direction of the search (`0x004CD000`). The corridor comes out ordered from
    /// <paramref name="target"/> back to <paramref name="seed"/> by following predecessors, which
    /// is the walking order when the seed is the destination.</summary>
    public bool TrySearch(int seed, int target, NavPoint goal, MoverProfile mover, List<int> corridor)
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

        while (_heapCount > 0)
        {
            int current = _heap[1];
            if (current == target) break;
            Pop();

            byte currentNav = _mesh.NavByte(current);
            for (int slot = 0; slot < 3; slot++)
            {
                int n = _mesh.Neighbour(current, slot);
                if (n == NavMesh.NoNeighbour || _stamp[n] == _generation) continue;

                int cost = TransitionCost(_mesh.NavByte(n), currentNav, mover);
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
        }

        if (_heapCount == 0 || _heap[1] != target) return false;

        for (int t = target; ; t = _pred[t])
        {
            corridor.Add(t);
            if (t == seed) break;
        }
        return true;
    }

    /// <summary>The original's transition cost (`0x004C8BD0`) on two navigation bytes, for the
    /// arms that exist without doors: solid is impassable; water and lava may only be entered by a
    /// mover with the flag; otherwise one plus the category. Entering is judged from the
    /// expansion's side, so the two search directions do not agree — faithfully.</summary>
    public static int TransitionCost(byte next, byte current, MoverProfile mover)
    {
        if (NavBytes.IsSolid(next)) return 0;
        int nc = NavBytes.Category(next), cc = NavBytes.Category(current);
        if (nc == 3 && cc != 3 && !mover.CanWalkOnLava) return 0;
        if (nc == 1 && cc != 1 && !mover.CanWalkOnWater) return 0;
        if (((next ^ current) & 4) != 0) return 0;
        return nc + 1;
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
    /// is strictly smaller than the child's, so it sinks below equal keys.</summary>
    private void Pop()
    {
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
    }
}
