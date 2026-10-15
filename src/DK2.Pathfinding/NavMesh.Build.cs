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
        mesh.InsertRectangles(grid, 0, 0, grid.Width, grid.Height, skipFloor: true);
        mesh.CoarsenRectangle(0, 0, grid.Width, grid.Height);
        return mesh;
    }

    /// <summary>The scanner (`0x004CE570` over `0x004CE4E0`): take the leftmost column of the
    /// topmost uncovered row, extend right while the byte matches and the column is uncovered,
    /// then down while the whole row segment matches. The original keeps coverage as a per-column
    /// skyline word, which is the same partition as our covered array because every rectangle is
    /// laid against that skyline. It runs over the tiles <paramref name="x0"/>..<paramref name="x1"/> by
    /// <paramref name="y0"/>..<paramref name="y1"/> (exclusive far corners): a partial rebuild runs it
    /// over its rectangle with the skyline reset to the rectangle's top, inserting every rectangle;
    /// the whole-map build over the map, skipping byte 0, which its base rectangle stamped.</summary>
    private void InsertRectangles(INavByteGrid grid, int x0, int y0, int x1, int y1, bool skipFloor)
    {
        int w = grid.Width;
        var covered = new bool[w * grid.Height];

        for (int ry = y0; ry < y1; ry++)
        {
            for (int rx = x0; rx < x1; rx++)
            {
                if (covered[(ry * w) + rx]) continue;
                byte nav = grid.NavByte(rx, ry);

                int ex = rx;
                while (ex + 1 < x1 && !covered[(ry * w) + ex + 1] && grid.NavByte(ex + 1, ry) == nav) ex++;

                int ey = ry;
                while (ey + 1 < y1 && RowMatches(grid, covered, rx, ex, ey + 1, nav)) ey++;

                for (int y = ry; y <= ey; y++)
                    for (int x = rx; x <= ex; x++)
                        covered[(y * w) + x] = true;

                if (skipFloor && nav == NavBytes.Floor) continue;
                InsertRectangle(rx, ry, ex + 1, ey + 1, nav);
                CoarsenRectangle(rx, ry, ex + 1, ey + 1);
            }
        }
    }

    private static bool RowMatches(INavByteGrid grid, bool[] covered, int x0, int x1, int y, byte nav)
    {
        for (int x = x0; x <= x1; x++)
            if (covered[(y * grid.Width) + x] || grid.NavByte(x, y) != nav) return false;
        return true;
    }

    /// <summary>`0x004CCCB0`: the corners as points in the original's order (x0,y0), (x0,y1),
    /// (x1,y1), (x1,y0), the sides as constraints in that order round, then every triangle inside
    /// gets the rectangle's byte (`0x004CC9B0`). Corner coordinates are in tiles; the far corner
    /// is exclusive on the grid and so a tile corner itself.</summary>
    public void InsertRectangle(int x0, int y0, int x1, int y1, byte nav)
    {
        int a = InsertPoint(x0, y0);
        int d = InsertPoint(x0, y1);
        int c = InsertPoint(x1, y1);
        int b = InsertPoint(x1, y0);
        InsertConstraint(a, d);
        InsertConstraint(d, c);
        InsertConstraint(c, b);
        InsertConstraint(b, a);

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

    private void Stamp(int t, byte nav) => _nav[t] = nav;

    private bool AllInside(int t, int x0, int y0, int x1, int y1) =>
        Inside(Vertex(t, 0), x0, y0, x1, y1) && Inside(Vertex(t, 1), x0, y0, x1, y1)
        && Inside(Vertex(t, 2), x0, y0, x1, y1);

    private bool Inside(int p, int x0, int y0, int x1, int y1) =>
        _px[p] >= x0 && _px[p] <= x1 && _py[p] >= y0 && _py[p] <= y1;

    /// <summary>The original's constraint insertion (`0x004CBD10`), transcribed in its slots
    /// (NavMesh.Slots.cs). From the target end it turns the fan toward the other end; if the edge
    /// is there it is done; if the far end of the edge leaving the target is on the segment it
    /// recurses on the two halves; otherwise it walks the triangles the segment crosses, carrying
    /// an edge on one side, and where the next apex falls on the far side of that edge it runs the
    /// flip sweep (<see cref="FlipSweep"/>) from the carried edge to the triangle beyond the apex,
    /// then swaps the ends and starts over, until the edge exists. No constraint bit is set: only
    /// the partial rebuild's side marker sets them (§2.2). Both ends must be vertices.</summary>
    public void InsertConstraint(int pa, int pb)
    {
        if (pa == pb) return;
        InsertConstraint(_px[pa], _py[pa], _px[pb], _py[pb]);
    }

    private void InsertConstraint(int ax, int ay, int bx, int by)
    {
        if (ax == bx && ay == by) return;
        int cx = ax, cy = ay, tx = bx, ty = by;
        if (!TryLocateVertex(tx, ty, out int tri, out int ourSlot)) return;
        int s = DkV(ourSlot);
        for (int pass = 0; ; pass++)
        {
            if (pass > 64) throw new InvalidOperationException($"constraint ({ax},{ay})-({bx},{by}) never became an edge");
            if (!TryLocateVertex(cx, cy, out _, out _)) return;

            int cur = tri, k = DkV(s);
            if (!TurnFan(ref cur, ref k, (long)cx * UnitsPerTile, (long)cy * UnitsPerTile)) return;
            int ds = DkV(k);
            int e = Next(ds);
            int tw = cur;
            int across = DkVertex(cur, e);
            if (_px[across] == cx && _py[across] == cy) return;

            int u = DkNeighbour(cur, ds);
            if (u == NoNeighbour) return;
            int bu = DkBack(u, cur);
            int far = DkVertex(u, bu);
            if (Orient(tx, ty, cx, cy, _px[far], _py[far]) == 0)
            {
                InsertConstraint(tx, ty, _px[far], _py[far]);
                InsertConstraint(cx, cy, _px[far], _py[far]);
                return;
            }

            int apexX, apexY;
            int guard = 0;
            do
            {
                if (++guard > (4 * _triCount) + 64) throw new InvalidOperationException($"constraint ({ax},{ay})-({bx},{by}) walks without end");
                int v = DkNeighbour(tw, e);
                if (v == NoNeighbour) return;
                int bv = DkBack(v, tw);
                e = Next(bv);
                int apex = DkVertex(v, Prev(bv));
                apexX = _px[apex];
                apexY = _py[apex];
                if (Orient(tx, ty, cx, cy, apexX, apexY) >= 0)
                {
                    int beyond = DkNeighbour(v, e);
                    if (beyond == NoNeighbour) return;
                    int bb = DkBack(beyond, v);
                    int p1 = DkVertex(beyond, bb);
                    int v2 = DkVertex(u, Next(bu)), v1 = DkVertex(u, bu);
                    if (Orient(_px[v2], _py[v2], _px[v1], _py[v1], _px[p1], _py[p1]) < 0)
                    {
                        FlipSweep(u, bu, beyond);
                        break;
                    }
                    e = Prev(bv);
                    u = beyond;
                    bu = bb;
                }
                tw = v;
            } while (apexX != cx || apexY != cy);

            if (!TryLocateVertex(cx, cy, out tri, out ourSlot)) return;
            s = DkV(ourSlot);
            (cx, cy, tx, ty) = (tx, ty, cx, cy);
        }
    }

    /// <summary>`0x004CBBD0`: from the triangle across edge <paramref name="s"/> of
    /// <paramref name="t"/>, flip the edge after the shared one and go on until the triangle across
    /// it is <paramref name="target"/>; a refused flip steps across instead. A sweep that both
    /// flipped and was refused runs again from the start; one that only flipped, or only stepped,
    /// is done. False where the walk leaves the mesh or comes back to <paramref name="t"/>.</summary>
    private bool FlipSweep(int t, int s, int target)
    {
        int n = DkNeighbour(t, s);
        if (n == NoNeighbour) return false;
        for (int round = 0; ; round++)
        {
            if (round > 64) throw new InvalidOperationException("the constraint flip sweep did not settle");
            int b = DkBack(n, t);
            int e = Next(b);
            int fails = 0, flips = 0, cur = n;
            for (int guard = 0; DkNeighbour(cur, e) != target; guard++)
            {
                if (guard > (4 * _triCount) + 64) throw new InvalidOperationException("the constraint flip sweep walks without end");
                if (TryFlip(cur, DkE(e)))
                {
                    flips++;
                    continue;
                }
                int m = DkNeighbour(cur, e);
                if (m == NoNeighbour || m == t) return false;
                e = Next(DkBack(m, cur));
                fails++;
                cur = m;
            }
            if (fails == 0 || flips == 0) return true;
            n = DkNeighbour(t, s);
            if (n == NoNeighbour) return false;
        }
    }

    /// <summary>The original's flip (`0x004C9FC0`): refuse across a constraint; then, before the
    /// convexity test, move the constraint bits as the flip will need them, so a flip refused as
    /// non-convex leaves them moved (the original's order, kept: the degree reduction tries flips
    /// around a fan and steps on when refused); then refuse a quad whose shared edge's ends are not
    /// strictly on opposite sides of the line through the apexes; otherwise the two triangles trade
    /// their shared edge for the other diagonal, rewritten in place. The original keeps the start of
    /// its edge in <c>t</c>; its edge is ours reversed, so ours keeps the end <c>b</c> of edge
    /// <c>k</c> and the apex <c>c</c> in <c>t</c>, writing <c>u</c>'s apex over <c>a</c>, and <c>u</c>
    /// keeps <c>a</c> and its apex, taking <c>c</c> over <c>b</c> (the translation in NavMesh.Edit.cs).
    /// The new diagonal comes back as <paramref name="c"/>–<paramref name="d"/>.</summary>
    public bool TryFlip(int t, int edge) => TryFlip(t, edge, out _, out _);

    public bool TryFlip(int t, int k, out int c, out int d)
    {
        c = d = -1;
        int u = Neighbour(t, k);
        if (u == NoNeighbour || IsConstraint(t, k)) return false;
        int j = SlotToward(u, t);
        if (IsConstraint(u, j)) return false;

        // The original clears each triangle's next edge (its diagonal to be) and moves the other
        // outer edge's bit onto the shared slot; its next edges are our previous ones.
        bool tPrev = IsConstraint(t, Prev(k)), uPrev = IsConstraint(u, Prev(j));
        SetConstraint(t, Prev(k), false);
        SetConstraint(u, Prev(j), false);
        SetConstraint(t, k, uPrev);
        SetConstraint(u, j, tPrev);

        int a = Vertex(t, k), b = Vertex(t, Next(k));
        c = Vertex(t, Prev(k));
        d = Vertex(u, Prev(j));
        if (Orient(_px[c], _py[c], _px[d], _py[d], _px[a], _py[a]) *
            Orient(_px[c], _py[c], _px[d], _py[d], _px[b], _py[b]) >= 0) return false;

        // t becomes (d, b, c): its slot k holds the segment d-b that was u's previous edge, its
        // previous slot the new diagonal (c, d). u becomes (c, a, d): its slot j holds c-a, which was
        // t's previous edge, and its previous slot the diagonal (d, c).
        int nDb = Neighbour(u, Prev(j)), nCa = Neighbour(t, Prev(k));
        SetVertex(t, k, d);
        SetVertex(u, j, c);
        _n[(3 * t) + k] = nDb;
        if (nDb != NoNeighbour) _n[(3 * nDb) + SlotToward(nDb, u)] = t;
        _n[(3 * t) + Prev(k)] = u;
        _n[(3 * u) + j] = nCa;
        if (nCa != NoNeighbour) _n[(3 * nCa) + SlotToward(nCa, t)] = u;
        _n[(3 * u) + Prev(j)] = t;
        _pointTri[b] = t; _pointTri[c] = t; _pointTri[a] = u; _pointTri[d] = u;
        return true;
    }

    // The flood's stamp per triangle slot, by generation; the stack it walks.
    private int[] _floodStamp = [];
    private int _floodGeneration;
    private readonly List<int> _flood = [];

    /// <summary>The original's coarsening (`0x004CC650`) over one rectangle: a depth-first flood
    /// seeded from the rectangle's four corners (`0x004CC3D0`: the triangle under the corner, its
    /// neighbours in slot order, then the rest of the corner's fan), each triangle once per
    /// generation and never a solid one. A popped triangle's edges 0, 1, 2 are tried against
    /// <see cref="ShouldFlip"/>; on a hit the flip is made (its result ignored, as the original
    /// ignores it), the triangle's live, unstamped, non-solid neighbours are pushed, and the next
    /// edge of the same, by then rewritten, triangle follows. After a hit the flood **gives up** once
    /// the stack holds about 996 entries, keeping the flip just made. Returns the hit count.</summary>
    public int CoarsenRectangle(int x0, int y0, int x1, int y1)
    {
        if (_floodStamp.Length < _nav.Length) Array.Resize(ref _floodStamp, _nav.Length);
        if (++_floodGeneration == int.MaxValue)
        {
            Array.Clear(_floodStamp, 0, _floodStamp.Length);
            _floodGeneration = 1;
        }
        _flood.Clear();
        Seed(x0, y0);
        Seed(x0, y1);
        Seed(x1, y0);
        Seed(x1, y1);

        // The original's edges 0, 1, 2 are our edges 2, 1, 0 (the translation in NavMesh.Edit.cs),
        // and so are the neighbours it pushes.
        int hits = 0;
        while (_flood.Count > 0)
        {
            int t = _flood[_flood.Count - 1];
            _flood.RemoveAt(_flood.Count - 1);
            for (int k = 2; k >= 0; k--)
            {
                if (!ShouldFlip(t, k)) continue;
                hits++;
                TryFlip(t, k);
                if (_flood.Count + 4 > 999) return hits;
                for (int i = 2; i >= 0; i--) PushFlood(Neighbour(t, i));
            }
        }
        return hits;
    }

    private void PushFlood(int t)
    {
        if (t == NoNeighbour || !IsTriangleLive(t) || _floodStamp[t] == _floodGeneration || NavBytes.IsSolid(_nav[t])) return;
        _floodStamp[t] = _floodGeneration;
        _flood.Add(t);
    }

    /// <summary>`0x004CC3D0`: the corner's triangle, its three neighbours, then its fan.</summary>
    private void Seed(int x, int y)
    {
        int t = Locate(x * UnitsPerTile, y * UnitsPerTile);
        if (t < 0) return;
        PushFlood(t);
        for (int i = 2; i >= 0; i--) PushFlood(Neighbour(t, i));
        if (!TryLocateVertex(x, y, out int start, out int slot)) return;
        int prev = start;
        int nb = Neighbour(start, EdgeLeaving(slot));
        while (nb != NoNeighbour)
        {
            int back = SlotToward(nb, prev);
            PushFlood(nb);
            if (nb == start) return;
            prev = nb;
            nb = Neighbour(nb, EdgeLeaving(back));
        }
    }

    /// <summary>`0x004CC190`: an edge gives way to the quad's other diagonal when both triangles
    /// carry one navigation byte, the shared edge's ends lie strictly on opposite sides of the line
    /// through the apexes, and the other diagonal is <b>strictly shorter</b> in squared length. That,
    /// not any in-circle test, is what decides every diagonal inside a room. The original also needs
    /// bit `edge` of `+0xD` on both sides, which every edge carries (STRONGLY INFERRED: set by the
    /// reset, splits and clones, only moved by the flip). Constraint bits are the flip's to refuse.
    /// </summary>
    private bool ShouldFlip(int t, int edge)
    {
        int u = Neighbour(t, edge);
        if (u == NoNeighbour || _nav[u] != _nav[t]) return false;

        int a = Vertex(t, edge), b = Vertex(t, (edge + 1) % 3);
        int c = Vertex(t, (edge + 2) % 3);
        int d = Vertex(u, Prev(SlotToward(u, t)));
        if (Orient(_px[c], _py[c], _px[d], _py[d], _px[a], _py[a]) *
            Orient(_px[c], _py[c], _px[d], _py[d], _px[b], _py[b]) >= 0) return false;

        long nx = _px[d] - _px[c], ny = _py[d] - _py[c];
        long ox = _px[b] - _px[a], oy = _py[b] - _py[a];
        return (nx * nx) + (ny * ny) < (ox * ox) + (oy * oy);
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
