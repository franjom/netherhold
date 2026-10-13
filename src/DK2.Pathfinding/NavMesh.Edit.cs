// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Pathfinding;

/// <summary>The primitives DK2's per-tile rebuild edits the mesh with (navigation-partial-rebuild.md,
/// Q591, ADR-0461): fan walks by slot arithmetic, vertex removal with its degree reduction, and the
/// free lists the removals feed. Each is the original's routine transcribed step for step through
/// one translation, since the original stores a triangle clockwise in cross-product terms and ours
/// the other way round: with a corner at vertex slot <c>s</c> in both, the original's edge at
/// <c>s</c> (leaving the corner) is our <c>Prev(s)</c>, its corner after is our corner before, its
/// edges 0, 1, 2 are ours 2, 1, 0, and its flip keeps the start of an edge where ours keeps the end
/// (the table, with the entering-triangle case, is in the research doc). From the same starting
/// triangle each walk then visits the same triangles in the same order and keeps and frees the
/// same slots; the start is where ours can differ, since the original's locate (<c>0x004C98B0</c>)
/// walks from a cell cache and ours returns the lowest slot holding the point (ADR-0157). Region
/// ids and edge width classes are not modelled: the shipped game reads neither.</summary>
public sealed partial class NavMesh
{
    /// <summary>The original's slot tables <c>0x0066DA14</c> (next) and <c>0x0066DA18</c> (previous). A
    /// slot of -1, which the original's three-way compare yields for a neighbour that does not link
    /// back, reads the word before each table (<c>0x0066DA10</c> is [0, 1, 2] and the tables overlap by
    /// one word), so next(-1) is 0 and prev(-1) is 1; nothing consistent reaches it.</summary>
    private static int Next(int k) => k < 0 ? 0 : (k + 1) % 3;

    private static int Prev(int k) => k < 0 ? 1 : (k + 2) % 3;

    /// <summary>The slot of <c>u</c> whose neighbour is <c>t</c>, or -1 (the original's compare of the
    /// three words, which returns -1 when none matches).</summary>
    private int SlotToward(int u, int t)
    {
        if (_n[3 * u] == t) return 0;
        if (_n[(3 * u) + 1] == t) return 1;
        return _n[(3 * u) + 2] == t ? 2 : -1;
    }

    /// <summary>The original's edge at vertex slot <paramref name="s"/>, the one leaving the corner
    /// there: our edge before <paramref name="s"/>, which arrives at it.</summary>
    private static int EdgeLeaving(int s) => Prev(s);

    /// <summary>The original's corner after the one at slot <paramref name="s"/>: ours before it.</summary>
    private int CornerAfter(int t, int s) => Vertex(t, Prev(s));

    /// <summary>The original's corner before the one at slot <paramref name="s"/>: ours after it.</summary>
    private int CornerBefore(int t, int s) => Vertex(t, Next(s));

    /// <summary>Sets edge <c>i</c>'s constraint bit on <c>t</c> alone, as the original's side marker
    /// `0x004CE380` does; the flip tests both triangles' bits, so one side refuses both.</summary>
    public void MarkConstraint(int t, int i) => SetConstraint(t, i, true);

    /// <summary>Clears edge <c>i</c>'s constraint bit on <c>t</c> alone (`0x004CE410`).</summary>
    public void ClearConstraint(int t, int i) => SetConstraint(t, i, false);

    private void FreeTriangle(int t)
    {
        _nav[t] = NavBytes.Dead;
        _triFree.Add(t);
    }

    private void FreePoint(int p)
    {
        _pointDead[p] = true;
        _pointFree.Add(p);
    }

    /// <summary>Writes one vertex slot in place, keeping the other two where they are, as the
    /// original's edits do.</summary>
    private void SetVertex(int t, int slot, int p)
    {
        _v[(3 * t) + slot] = p;
        _pointTri[p] = t;
    }

    /// <summary>The original's <c>0x004C9B50</c>: a triangle holding the tile corner as a vertex, and its
    /// slot, from the triangle under the corner.</summary>
    public bool TryLocateVertex(int x, int y, out int triangle, out int slot)
    {
        triangle = Locate(x * UnitsPerTile, y * UnitsPerTile);
        slot = -1;
        if (triangle < 0) return false;
        for (int k = 0; k < 3; k++)
        {
            int p = Vertex(triangle, k);
            if (_px[p] == x && _py[p] == y) { slot = k; return true; }
        }
        return false;
    }

    /// <summary>The original's <c>0x004C9C30</c>: the triangle and vertex slot at
    /// (<paramref name="x"/>, <paramref name="y"/>) whose edge leaving that corner (our edge
    /// <see cref="EdgeLeaving"/> of the slot) runs toward the target, found by turning around the fan
    /// until the edge's far corner is collinear and ahead on both axes.</summary>
    public bool TryEdgeToward(int x, int y, int tx, int ty, out int triangle, out int slot)
    {
        triangle = slot = -1;
        if (!TryLocateVertex(x, y, out int start, out int s)) return false;
        long dx = tx - x, dy = ty - y;
        int cur = start;
        do
        {
            int q = CornerAfter(cur, s);
            long qx = _px[q] - x, qy = _py[q] - y;
            if ((qx * dy) - (qy * dx) == 0 && Math.Sign(qx) == Math.Sign(dx) && Math.Sign(qy) == Math.Sign(dy))
            {
                triangle = cur;
                slot = s;
                return true;
            }
            int nb = Neighbour(cur, EdgeLeaving(s));
            if (nb == NoNeighbour) return false;
            s = SlotToward(nb, cur);
            cur = nb;
        } while (cur != start);
        return false;
    }

    /// <summary>The original's <c>0x004CAF40</c>: flips edges around the vertex at
    /// (<paramref name="triangle"/>, <paramref name="slot"/>) until its degree is 3 or 4, returning that
    /// degree, -1 for a vertex on the hull, or another count below 5 when the walk and a recount
    /// disagree. The triangle and slot come back naming the vertex where the walk left it.</summary>
    public int ReduceVertexDegree(ref int triangle, ref int slot)
    {
        while (true)
        {
            int t0 = triangle, s0 = slot;
            int count = CountFan(t0, s0);
            if (count == 3) return 3;

            int refused = 0, cur = t0, cs = s0;
            int nb = Neighbour(cur, EdgeLeaving(cs));
            bool closed = false;
            while (nb != NoNeighbour)
            {
                if (!TryFlip(cur, EdgeLeaving(cs)))
                {
                    cs = SlotToward(nb, cur);
                    refused++;
                    cur = nb;
                }
                // Compared against the neighbour read before the flip, as the original does.
                if (nb == t0)
                {
                    triangle = cur;
                    slot = cs;
                    closed = true;
                    break;
                }
                nb = Neighbour(cur, EdgeLeaving(cs));
            }
            if (!closed) refused = -1;
            if (refused == 3) return 3;
            if (refused == -1) return -1;

            int recount = CountFan(triangle, slot);
            if (recount == refused && refused == 4) return 4;
            // The original logs "l0 %02ld n %02ld l1 %02ld" here and returns any count below 5.
            if (refused < 5) return refused;
        }
    }

    /// <summary>The fan's size by the original's walk: steps until the start triangle comes round,
    /// or -1 when the walk runs off the hull.</summary>
    private int CountFan(int t0, int s0)
    {
        int count = 0, prev = t0;
        int nb = Neighbour(t0, EdgeLeaving(s0));
        while (nb != NoNeighbour)
        {
            int back = SlotToward(nb, prev);
            count++;
            if (nb == t0) return count;
            prev = nb;
            nb = Neighbour(nb, EdgeLeaving(back));
        }
        return -1;
    }

    /// <summary>The original's <c>0x004CB8C0</c>: merges the three triangles around a vertex of degree
    /// 3 into the given one, frees the other two (the second pushed before the third) and the point.
    /// Returns false when the fan does not close in three or reaches the hull. The kept triangle's
    /// constraint bits are left as they were, as the original leaves them.</summary>
    public bool RemoveDegree3Vertex(int t, int s)
    {
        int p = Vertex(t, s);
        int u = Neighbour(t, EdgeLeaving(s));
        if (u == NoNeighbour) return false;
        int su = SlotToward(u, t);
        int w = Neighbour(u, EdgeLeaving(su));
        if (w == NoNeighbour) return false;
        int sw = SlotToward(w, u);
        if (Neighbour(w, EdgeLeaving(sw)) != t) return false;

        // The outer edges: the original's edge prev(b) of each is our Next(b).
        int outerW = Neighbour(w, Next(sw));
        int outerU = Neighbour(u, Next(su));
        // The corner of u opposite the shared edge, the original's prev(su), is our Prev(su).
        SetVertex(t, s, Vertex(u, Prev(su)));
        // The original's edge s of t (now r-q) takes u's outer edge; its edge prev(s) (now o-r) w's.
        _n[(3 * t) + EdgeLeaving(s)] = outerU;
        _n[(3 * t) + s] = outerW;
        if (outerU != NoNeighbour) _n[(3 * outerU) + SlotToward(outerU, u)] = t;
        if (outerW != NoNeighbour) _n[(3 * outerW) + SlotToward(outerW, w)] = t;
        for (int k = 0; k < 3; k++) _pointTri[Vertex(t, k)] = t;
        FreeTriangle(u);
        FreeTriangle(w);
        FreePoint(p);
        return true;
    }

    /// <summary>The original's <c>0x004CB170</c>: re-joins the four triangles around a vertex of degree
    /// 4 into two across one diagonal and frees the other two and the point. Two orientation tests on
    /// the rim choose the diagonal between the first and third rim corners when they pass; otherwise
    /// the other diagonal is taken without a test. Returns false when the fan does not close in four.
    /// Constraint bits are left as they were.</summary>
    public bool RemoveDegree4Vertex(int t, int s)
    {
        int p = Vertex(t, s);
        int u = Neighbour(t, EdgeLeaving(s));
        if (u == NoNeighbour) return false;
        int su = SlotToward(u, t);
        int w = Neighbour(u, EdgeLeaving(su));
        if (w == NoNeighbour) return false;
        int sw = SlotToward(w, u);
        int x = Neighbour(w, EdgeLeaving(sw));
        if (x == NoNeighbour) return false;
        int sx = SlotToward(x, w);
        if (Neighbour(x, EdgeLeaving(sx)) != t) return false;

        // The rim in the original's walk order: q after p in t, r and m the far corners of u and w,
        // o before p in t.
        int q = CornerAfter(t, s), o = CornerBefore(t, s);
        int r = Vertex(u, Prev(su)), m = Vertex(w, Prev(sw));

        // cross(r - m, q - m) < 0 and cross(o - m, q - m) > 0 choose the diagonal (q, m).
        bool diagonalQm = Orient(_px[m], _py[m], _px[r], _py[r], _px[q], _py[q]) < 0
            && Orient(_px[m], _py[m], _px[o], _py[o], _px[q], _py[q]) > 0;
        if (diagonalQm)
        {
            // Keep u = (q, m, r) and x = (m, q, o); free w then t.
            int outerW = Neighbour(w, Next(sw)), outerT = Neighbour(t, Next(s));
            SetVertex(u, su, m);
            SetVertex(x, sx, q);
            _n[(3 * u) + Prev(su)] = outerW;
            if (outerW != NoNeighbour) _n[(3 * outerW) + SlotToward(outerW, w)] = u;
            _n[(3 * u) + su] = x;
            _n[(3 * x) + Prev(sx)] = outerT;
            if (outerT != NoNeighbour) _n[(3 * outerT) + SlotToward(outerT, t)] = x;
            _n[(3 * x) + sx] = u;
            for (int k = 0; k < 3; k++) { _pointTri[Vertex(u, k)] = u; _pointTri[Vertex(x, k)] = x; }
            FreeTriangle(w);
            FreeTriangle(t);
        }
        else
        {
            // Keep t = (r, q, o) and w = (r, o, m) across the diagonal (r, o); free u then x.
            int outerU = Neighbour(u, Next(su)), outerX = Neighbour(x, Next(sx));
            SetVertex(t, s, r);
            SetVertex(w, sw, o);
            _n[(3 * t) + EdgeLeaving(s)] = outerU;
            if (outerU != NoNeighbour) _n[(3 * outerU) + SlotToward(outerU, u)] = t;
            _n[(3 * t) + s] = w;
            _n[(3 * w) + Prev(sw)] = outerX;
            if (outerX != NoNeighbour) _n[(3 * outerX) + SlotToward(outerX, x)] = w;
            _n[(3 * w) + sw] = t;
            for (int k = 0; k < 3; k++) { _pointTri[Vertex(t, k)] = t; _pointTri[Vertex(w, k)] = w; }
            FreeTriangle(u);
            FreeTriangle(x);
        }
        FreePoint(p);
        return true;
    }

    /// <summary>Removes the vertex as the original's callers do: reduce its degree, then the degree-4
    /// removal for 4 and the degree-3 removal for anything else. Returns whether a removal happened.</summary>
    public bool RemoveVertex(int triangle, int slot)
    {
        int degree = ReduceVertexDegree(ref triangle, ref slot);
        return degree == 4 ? RemoveDegree4Vertex(triangle, slot) : RemoveDegree3Vertex(triangle, slot);
    }
}
