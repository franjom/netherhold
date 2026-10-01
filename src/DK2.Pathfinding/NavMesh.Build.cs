// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Pathfinding;

/// <summary>Building the mesh from a grid: maximal uniform rectangles, constraint insertion by
/// flipping and stamping (navigation-mesh-and-search.md §2.1).</summary>
public sealed partial class NavMesh
{
    /// <summary>The original's whole-map rebuild (`0x004CE770` with the whole map as its
    /// rectangle): reset to the canvas, stamp the map as one rectangle of navigation byte 0, then
    /// every maximal rectangle of one byte in discovery order — <b>skipping those whose byte is
    /// 0</b>, which the base rectangle already covers (`0x004CEB56`). It runs no vertex removal:
    /// the pass that reduces a vertex's degree by flipping and then merges it away is gated on the
    /// partial-rebuild flag (`0x004CEC10`), and a whole-map rebuild never reaches it.</summary>
    public static NavMesh Build(INavByteGrid grid)
    {
        if (grid is null) throw new ArgumentNullException(nameof(grid));
        var mesh = new NavMesh();
        mesh.Reset(CanvasMin, CanvasMin, CanvasMax, CanvasMax);
        mesh.InsertRectangle(0, 0, grid.Width, grid.Height, NavBytes.Floor);
        mesh.InsertRectangles(grid);
        mesh.Coarsen();
        return mesh;
    }

    /// <summary>The scanner (`0x004CE570` over `0x004CE4E0`): take the leftmost column of the
    /// topmost uncovered row, extend right while the byte matches and the column is uncovered,
    /// then down while the whole row segment matches. The original keeps coverage as a per-column
    /// skyline word, which is the same partition as our covered array because every rectangle is
    /// laid against that skyline.</summary>
    private void InsertRectangles(INavByteGrid grid)
    {
        int w = grid.Width, h = grid.Height;
        var covered = new bool[w * h];

        for (int y0 = 0; y0 < h; y0++)
        {
            for (int x0 = 0; x0 < w; x0++)
            {
                if (covered[(y0 * w) + x0]) continue;
                byte nav = grid.NavByte(x0, y0);

                int x1 = x0;
                while (x1 + 1 < w && !covered[(y0 * w) + x1 + 1] && grid.NavByte(x1 + 1, y0) == nav) x1++;

                int y1 = y0;
                while (y1 + 1 < h && RowMatches(grid, covered, x0, x1, y1 + 1, nav)) y1++;

                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                        covered[(y * w) + x] = true;

                if (nav == NavBytes.Floor) continue;
                InsertRectangle(x0, y0, x1 + 1, y1 + 1, nav);
                Coarsen();
            }
        }
    }

    private static bool RowMatches(INavByteGrid grid, bool[] covered, int x0, int x1, int y, byte nav)
    {
        for (int x = x0; x <= x1; x++)
            if (covered[(y * grid.Width) + x] || grid.NavByte(x, y) != nav) return false;
        return true;
    }

    /// <summary>`0x004CCCB0`: four corners as points, four sides as constraints, then every
    /// triangle inside gets the rectangle's byte (`0x004CC9B0`). Corner coordinates are in tiles;
    /// the far corner is exclusive on the grid and so a tile corner itself.</summary>
    public void InsertRectangle(int x0, int y0, int x1, int y1, byte nav)
    {
        int a = InsertPoint(x0, y0);
        int b = InsertPoint(x1, y0);
        int c = InsertPoint(x1, y1);
        int d = InsertPoint(x0, y1);
        InsertConstraint(a, b);
        InsertConstraint(b, c);
        InsertConstraint(c, d);
        InsertConstraint(d, a);

        // The triangles with every vertex inside are those covering the rectangle, which with its
        // sides now edges is a region of its own: fill it from a corner's fan, never crossing to
        // a triangle with a vertex outside. A scan of every slot stamps the same set.
        int start = -1;
        foreach (int t in Fan(a))
            if (AllInside(t, x0, y0, x1, y1)) { start = t; break; }
        if (start < 0)
        {
            for (int t = 0; t < _triCount; t++)
                if (IsTriangleLive(t) && AllInside(t, x0, y0, x1, y1)) Stamp(t, nav);
            return;
        }

        if (++_fillGeneration == 0)
        {
            Array.Clear(_filled, 0, _filled.Length);
            _fillGeneration = 1;
        }
        if (_filled.Length < _triCount) Array.Resize(ref _filled, _nav.Length);
        _fill.Clear();
        _fill.Add(start);
        _filled[start] = _fillGeneration;
        while (_fill.Count > 0)
        {
            int t = _fill[_fill.Count - 1];
            _fill.RemoveAt(_fill.Count - 1);
            Stamp(t, nav);
            for (int i = 0; i < 3; i++)
            {
                int n = Neighbour(t, i);
                if (n == NoNeighbour || _filled[n] == _fillGeneration || !AllInside(n, x0, y0, x1, y1)) continue;
                _filled[n] = _fillGeneration;
                _fill.Add(n);
            }
        }
    }

    private int[] _filled = [];
    private int _fillGeneration;
    private readonly List<int> _fill = [];

    private void Stamp(int t, byte nav)
    {
        if (_nav[t] == nav) return;
        _nav[t] = nav;
        Touch(t);
    }

    private bool AllInside(int t, int x0, int y0, int x1, int y1) =>
        Inside(Vertex(t, 0), x0, y0, x1, y1) && Inside(Vertex(t, 1), x0, y0, x1, y1)
        && Inside(Vertex(t, 2), x0, y0, x1, y1);

    private bool Inside(int p, int x0, int y0, int x1, int y1) =>
        _px[p] >= x0 && _px[p] <= x1 && _py[p] >= y0 && _py[p] <= y1;

    /// <summary>The original's constraint insertion (`0x004CBD10`): walk from one end toward the
    /// other collecting every edge the segment crosses, recursing where it passes through a
    /// vertex, then flip the crossed edges. The order is Sloan's, not the original's unread one
    /// (ADR-0157 residual): a crossed edge whose quad is not convex waits at the back of the
    /// queue, and a new diagonal that still crosses the segment joins it. Once the segment is an
    /// edge it is marked a constraint on both sides, which the flip will later refuse to cross.
    /// Both ends must already be vertices.</summary>
    public void InsertConstraint(int pa, int pb)
    {
        if (pa == pb) return;
        long ax = _px[pa], ay = _py[pa], bx = _px[pb], by = _py[pb];

        // pa's fan in slot order: the triangles a scan of every slot would reach, in its order.
        int[] fan = [.. Fan(pa)];
        Array.Sort(fan);
        int t0 = -1, exit = -1;
        for (int k = 0; k < fan.Length && t0 < 0; k++)
        {
            int t = fan[k];
            int i = IndexOf(t, pa);

            int v1 = Vertex(t, (i + 1) % 3), v2 = Vertex(t, (i + 2) % 3);
            if (v1 == pb) { MarkConstraint(t, i); return; }
            if (v2 == pb) { MarkConstraint(t, (i + 2) % 3); return; }

            int o1 = Orient(ax, ay, bx, by, _px[v1], _py[v1]);
            int o2 = Orient(ax, ay, bx, by, _px[v2], _py[v2]);
            if (o1 == 0 && Ahead(pa, pb, v1)) { InsertConstraint(pa, v1); InsertConstraint(v1, pb); return; }
            if (o2 == 0 && Ahead(pa, pb, v2)) { InsertConstraint(pa, v2); InsertConstraint(v2, pb); return; }

            // The segment leaves t through the edge opposite pa when v1 is to its right and v2 to its left.
            if (o1 < 0 && o2 > 0) { t0 = t; exit = (i + 1) % 3; }
        }
        if (t0 < 0) throw new InvalidOperationException($"constraint {pa}->{pb} found no exit edge");

        _crossed.Clear();
        int cur = t0, edge = exit;
        for (int steps = 0; ; steps++)
        {
            if (steps > _triCount) throw new InvalidOperationException($"constraint {pa}->{pb} never reaches its end");
            if (IsConstraint(cur, edge)) throw new InvalidOperationException($"constraint {pa}->{pb} crosses an existing constraint");
            int v1 = Vertex(cur, edge), v2 = Vertex(cur, (edge + 1) % 3);
            _crossed.Add((v1, v2));

            int u = Neighbour(cur, edge);
            if (u == NoNeighbour) throw new InvalidOperationException($"constraint {pa}->{pb} runs off the mesh");
            int j = EdgeIndexOf(u, v2, v1);
            int d = Vertex(u, (j + 2) % 3);
            if (d == pb) break;

            int od = Orient(ax, ay, bx, by, _px[d], _py[d]);
            if (od == 0) { InsertConstraint(pa, d); InsertConstraint(d, pb); return; }
            // u reads (v2, v1, d) from j, so it exits by (v1, d) when d is left of the segment, else (d, v2).
            cur = u;
            edge = od > 0 ? (j + 1) % 3 : (j + 2) % 3;
        }

        for (int head = 0, guard = 0; head < _crossed.Count; guard++)
        {
            if (guard > (8 * _triCount) + 64)
                throw new InvalidOperationException($"constraint {pa}->{pb} from ({ax},{ay}) to ({bx},{by}) did not converge");
            (int v1, int v2) = _crossed[head++];
            int t = TriangleWithEdge(v1, v2, out int e);
            if (!TryFlip(t, e, out int c, out int d)) { _crossed.Add((v1, v2)); continue; }
            if ((c == pa && d == pb) || (c == pb && d == pa)) { MarkConstraint(t, EdgeBetween(t, c, d)); return; }
            if (Crosses(ax, ay, bx, by, c, d)) _crossed.Add((c, d));
        }
        throw new InvalidOperationException($"constraint {pa}->{pb} left no edge to mark");
    }

    private readonly List<(int, int)> _crossed = [];

    private bool Crosses(long ax, long ay, long bx, long by, int c, int d)
    {
        long cx = _px[c], cy = _py[c], dx = _px[d], dy = _py[d];
        return Orient(ax, ay, bx, by, cx, cy) * Orient(ax, ay, bx, by, dx, dy) < 0
            && Orient(cx, cy, dx, dy, ax, ay) * Orient(cx, cy, dx, dy, bx, by) < 0;
    }

    /// <summary>The lowest slot holding the edge <c>p</c>–<c>q</c>, as a scan of every slot finds
    /// it: the two triangles on it are both in p's fan.</summary>
    private int TriangleWithEdge(int p, int q, out int edge)
    {
        int best = -1;
        edge = -1;
        foreach (int t in Fan(p))
        {
            if (best >= 0 && t > best) continue;
            int e = EdgeBetween(t, p, q);
            if (e >= 0) { best = t; edge = e; }
        }
        if (best < 0) throw new InvalidOperationException($"no live triangle has the edge {p}-{q}");
        return best;
    }

    /// <summary>The edge of <c>t</c> joining <c>p</c> and <c>q</c> in either order, or −1.</summary>
    private int EdgeBetween(int t, int p, int q)
    {
        for (int i = 0; i < 3; i++)
        {
            int a = Vertex(t, i), b = Vertex(t, (i + 1) % 3);
            if ((a == p && b == q) || (a == q && b == p)) return i;
        }
        return -1;
    }

    /// <summary>The original's flip (`0x004C9FC0`): refuse across a constraint or when the quad is
    /// not convex; otherwise the two triangles trade their shared edge for the other diagonal,
    /// which comes back as <paramref name="c"/>–<paramref name="d"/>.</summary>
    public bool TryFlip(int t, int edge) => TryFlip(t, edge, out _, out _);

    public bool TryFlip(int t, int edge, out int c, out int d) => TryFlip(t, edge, true, out c, out d);

    private bool TryFlip(int t, int edge, bool respectConstraints, out int c, out int d)
    {
        c = d = -1;
        int u = Neighbour(t, edge);
        if (u == NoNeighbour || (respectConstraints && IsConstraint(t, edge))) return false;

        int a = Vertex(t, edge), b = Vertex(t, (edge + 1) % 3);
        c = Vertex(t, (edge + 2) % 3);
        int j = EdgeIndexOf(u, b, a);
        d = Vertex(u, (j + 2) % 3);

        // Convex iff c and d lie on opposite sides of (a, b) and a and b on opposite sides of (c, d).
        if (Orient(_px[c], _py[c], _px[d], _py[d], _px[a], _py[a]) *
            Orient(_px[c], _py[c], _px[d], _py[d], _px[b], _py[b]) >= 0) return false;

        int nBc = Neighbour(t, (edge + 1) % 3), nCa = Neighbour(t, (edge + 2) % 3);
        int nAd = Neighbour(u, (j + 1) % 3), nDb = Neighbour(u, (j + 2) % 3);
        bool cBc = IsConstraint(t, (edge + 1) % 3), cCa = IsConstraint(t, (edge + 2) % 3);
        bool cAd = IsConstraint(u, (j + 1) % 3), cDb = IsConstraint(u, (j + 2) % 3);
        byte navT = _nav[t], navU = _nav[u];

        // t becomes (a, d, c); u becomes (d, b, c). Shared edge is now (d, c) / (c, d).
        SetVertices(t, a, d, c);
        SetVertices(u, d, b, c);
        _constraint[t] = 0; _constraint[u] = 0;
        _nav[t] = navT; _nav[u] = navU;
        Link(t, EdgeIndexOf(t, a, d), nAd); SetConstraint(t, EdgeIndexOf(t, a, d), cAd);
        Link(t, EdgeIndexOf(t, c, a), nCa); SetConstraint(t, EdgeIndexOf(t, c, a), cCa);
        Link(u, EdgeIndexOf(u, d, b), nDb); SetConstraint(u, EdgeIndexOf(u, d, b), cDb);
        Link(u, EdgeIndexOf(u, b, c), nBc); SetConstraint(u, EdgeIndexOf(u, b, c), cBc);
        Link(t, EdgeIndexOf(t, d, c), u);
        return true;
    }

    /// <summary>The coarsening pass (`0x004CC650`): walk the mesh flipping every edge the
    /// criterion accepts until none is left. The original walks it as a flood fill from the
    /// rebuilt rectangle's corners, stamped by a generation counter, and never enters a solid
    /// triangle (`0x004CC75E` and its two copies test bit 3 of a neighbour's byte); ours sweeps
    /// the slots instead, which reaches the same triangles for a whole-map rebuild.
    /// <para>The sweep is in slot order, a pass at a time until a pass flips nothing, and that is
    /// the order kept here; only which slots are looked at is narrowed. A triangle can offer a flip
    /// only if it or a neighbour was written since the last Coarsen, which left none to offer. Within
    /// a pass, a flip at <c>t</c> changes what <c>t</c>, the triangle across and their neighbours
    /// offer: the sweep would still reach those above <c>t</c> in this pass, and all of them in the
    /// next.</para></summary>
    public void Coarsen()
    {
        _pass.Clear();
        foreach (int t in _touched)
        {
            AddToNext(t);
            for (int i = 0; i < 3; i++) AddToNext(Neighbour(t, i));
        }
        foreach (int t in _touched) _touchedMark[t] = false;
        _touched.Clear();

        while (_next.Count > 0)
        {
            foreach (int t in _next) { _inNext[t] = false; _pass.Push(t); }
            _next.Clear();

            bool flipped = false;
            while (_pass.TryPop(out int t))
            {
                if (!IsTriangleLive(t) || NavBytes.IsSolid(_nav[t])) continue;
                for (int i = 0; i < 3; i++)
                {
                    if (!ShouldFlip(t, i)) continue;
                    int u = Neighbour(t, i);
                    if (!TryFlip(t, i, false, out _, out _)) continue;
                    flipped = true;
                    Affected(t, t);
                    Affected(u, t);
                    for (int k = 0; k < 3; k++)
                    {
                        Affected(Neighbour(t, k), t);
                        Affected(Neighbour(u, k), t);
                    }
                    break;
                }
            }
            if (!flipped) break;
        }

        foreach (int t in _next) _inNext[t] = false;
        _next.Clear();
        foreach (int t in _touched) _touchedMark[t] = false;
        _touched.Clear();
    }

    private readonly SlotHeap _pass = new();
    private readonly List<int> _next = [];
    private bool[] _inNext = [];

    private void AddToNext(int t)
    {
        if (t == NoNeighbour) return;
        if (_inNext.Length < _triCount) Array.Resize(ref _inNext, _nav.Length);
        if (_inNext[t]) return;
        _inNext[t] = true;
        _next.Add(t);
    }

    /// <summary>A triangle a flip at <paramref name="at"/> changed: looked at again in the next
    /// pass, and in this one too when the sweep has not reached it yet.</summary>
    private void Affected(int t, int at)
    {
        if (t == NoNeighbour) return;
        AddToNext(t);
        if (t > at) _pass.Push(t);
    }

    /// <summary>Slots in ascending order, each at most once: the order a sweep reaches them.</summary>
    private sealed class SlotHeap
    {
        private int[] _items = new int[64];
        private bool[] _held = new bool[64];
        private int _count;

        public void Clear()
        {
            for (int i = 0; i < _count; i++) _held[_items[i]] = false;
            _count = 0;
        }

        public void Push(int slot)
        {
            if (slot >= _held.Length) Array.Resize(ref _held, Math.Max(slot + 1, _held.Length * 2));
            if (_held[slot]) return;
            _held[slot] = true;
            if (_count == _items.Length) Array.Resize(ref _items, _items.Length * 2);
            int i = _count++;
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (_items[parent] <= slot) break;
                _items[i] = _items[parent];
                i = parent;
            }
            _items[i] = slot;
        }

        public bool TryPop(out int slot)
        {
            if (_count == 0) { slot = -1; return false; }
            slot = _items[0];
            _held[slot] = false;
            int last = _items[--_count];
            int i = 0;
            while (true)
            {
                int child = (2 * i) + 1;
                if (child >= _count) break;
                if (child + 1 < _count && _items[child + 1] < _items[child]) child++;
                if (_items[child] >= last) break;
                _items[i] = _items[child];
                i = child;
            }
            if (_count > 0) _items[i] = last;
            return true;
        }
    }

    /// <summary>`0x004CC190`: an edge gives way to the quad's other diagonal when both triangles
    /// carry one navigation byte, the quad is convex (two orientation tests), and the other
    /// diagonal is <b>strictly shorter</b> in squared length. That, not any in-circle test, is
    /// what decides every diagonal inside a room. The original also needs bit `edge` of `+0xD` on
    /// both sides, which a whole-map rebuild always has, and its flip refuses a constraint, which
    /// a whole-map rebuild never sets (§2.2: the side marker runs on the partial path only).
    /// </summary>
    private bool ShouldFlip(int t, int edge)
    {
        int u = Neighbour(t, edge);
        if (u == NoNeighbour || _nav[u] != _nav[t]) return false;

        int a = Vertex(t, edge), b = Vertex(t, (edge + 1) % 3);
        int c = Vertex(t, (edge + 2) % 3);
        int d = Vertex(u, (EdgeIndexOf(u, b, a) + 2) % 3);
        if (Orient(_px[c], _py[c], _px[d], _py[d], _px[a], _py[a]) *
            Orient(_px[c], _py[c], _px[d], _py[d], _px[b], _py[b]) >= 0) return false;

        long nx = _px[d] - _px[c], ny = _py[d] - _py[c];
        long ox = _px[b] - _px[a], oy = _py[b] - _py[a];
        return (nx * nx) + (ny * ny) < (ox * ox) + (oy * oy);
    }

    private void MarkConstraint(int t, int i)
    {
        SetConstraint(t, i, true);
        int u = Neighbour(t, i);
        if (u != NoNeighbour) SetConstraint(u, EdgeIndexOf(u, Vertex(t, (i + 1) % 3), Vertex(t, i)), true);
    }

    private bool Ahead(int pa, int pb, int v)
    {
        long dx = _px[pb] - _px[pa], dy = _py[pb] - _py[pa];
        long vx = _px[v] - _px[pa], vy = _py[v] - _py[pa];
        return (dx * vx) + (dy * vy) > 0 && (vx * vx) + (vy * vy) < (dx * dx) + (dy * dy);
    }

    private int IndexOf(int t, int p)
    {
        for (int i = 0; i < 3; i++) if (Vertex(t, i) == p) return i;
        return -1;
    }
}
