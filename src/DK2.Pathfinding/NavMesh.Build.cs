// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Pathfinding;

/// <summary>Building the mesh from a grid: maximal uniform rectangles, constraint insertion by
/// flipping, stamping and coarsening (navigation-mesh-and-search.md §2.1).</summary>
public sealed partial class NavMesh
{
    /// <summary>The original's whole-map build: reset, then every maximal rectangle of one
    /// navigation byte in row-major discovery order, each inserted as four corners and four
    /// constraint sides and stamped, then coarsened.</summary>
    public static NavMesh Build(INavByteGrid grid)
    {
        if (grid is null) throw new ArgumentNullException(nameof(grid));
        var mesh = new NavMesh();
        mesh.Reset(grid.Width, grid.Height);
        mesh.InsertRectangles(grid);
        mesh.Coarsen();
        return mesh;
    }

    /// <summary>The scanner (`0x004CE570`): from the first uncovered tile in row-major order,
    /// extend right while the byte matches, then down while the whole row segment matches. The
    /// original compares bytes only; stopping at already-covered tiles as well is ours, and keeps
    /// the rectangles disjoint (ADR-0157 residual).</summary>
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

                InsertRectangle(x0, y0, x1 + 1, y1 + 1, nav);
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

        for (int t = 0; t < _triCount; t++)
        {
            if (!IsTriangleLive(t)) continue;
            if (Inside(Vertex(t, 0), x0, y0, x1, y1) && Inside(Vertex(t, 1), x0, y0, x1, y1)
                && Inside(Vertex(t, 2), x0, y0, x1, y1))
                _nav[t] = nav;
        }
    }

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

        int t0 = -1, exit = -1;
        for (int t = 0; t < _triCount && t0 < 0; t++)
        {
            if (!IsTriangleLive(t)) continue;
            int i = IndexOf(t, pa);
            if (i < 0) continue;

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

    private int TriangleWithEdge(int p, int q, out int edge)
    {
        for (int t = 0; t < _triCount; t++)
        {
            if (!IsTriangleLive(t)) continue;
            edge = EdgeBetween(t, p, q);
            if (edge >= 0) return t;
        }
        throw new InvalidOperationException($"no live triangle has the edge {p}-{q}");
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

    public bool TryFlip(int t, int edge, out int c, out int d)
    {
        c = d = -1;
        int u = Neighbour(t, edge);
        if (u == NoNeighbour || IsConstraint(t, edge)) return false;

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

    /// <summary>Same-byte coarsening (`0x004C97B0` / `0x004CB8C0`): a vertex whose every incident
    /// triangle carries one byte separates nothing, so it is removed and its fan re-triangulated.
    /// One pass in point order; removability never changes with other removals.</summary>
    public void Coarsen()
    {
        for (int p = 0; p < _pointCount; p++)
        {
            if (_pointDead[p]) continue;
            if (IsResetCorner(p)) continue;
            List<int>? fan = FanAround(p);
            if (fan is null || fan.Count < 2) continue;

            byte nav = _nav[fan[0]];
            bool uniform = true;
            foreach (int t in fan) if (_nav[t] != nav) { uniform = false; break; }
            if (!uniform) continue;

            RemovePoint(p, fan, nav);
        }
    }

    private bool IsResetCorner(int p) =>
        (_px[p] == 0 || _px[p] == Width) && (_py[p] == 0 || _py[p] == Height);

    /// <summary>The triangles around a point in counter-clockwise order, or null when the fan
    /// does not close and cannot be walked both ways (a point on the map border).</summary>
    private List<int>? FanAround(int p)
    {
        int start = -1;
        for (int t = 0; t < _triCount && start < 0; t++)
            if (IsTriangleLive(t) && IndexOf(t, p) >= 0) start = t;
        if (start < 0) return null;

        // In a positively wound (p, v1, v2) the counter-clockwise next triangle is across
        // (v2, p), edge i+2, and the clockwise previous one across (p, v1), edge i. Walk
        // clockwise to the fan's first triangle if the fan is open at the map border.
        int first = start;
        for (int guard = 0; guard < _triCount; guard++)
        {
            int i = IndexOf(first, p);
            int prev = Neighbour(first, i);
            if (prev == NoNeighbour || prev == start) break;
            first = prev;
        }

        var fan = new List<int>();
        int cur = first;
        for (int guard = 0; guard < _triCount; guard++)
        {
            fan.Add(cur);
            int i = IndexOf(cur, p);
            int next = Neighbour(cur, (i + 2) % 3);
            if (next == NoNeighbour || next == first) break;
            cur = next;
        }
        return fan;
    }

    /// <summary>Delete the fan and ear-clip the boundary polygon, carrying each boundary edge's
    /// neighbour and constraint bit onto the new triangle that owns it.</summary>
    private void RemovePoint(int p, List<int> fan, byte nav)
    {
        int m = fan.Count;
        var poly = new List<int>(m + 1);
        var outer = new List<int>(m + 1);
        var cons = new List<bool>(m + 1);
        foreach (int t in fan)
        {
            int i = IndexOf(t, p);
            poly.Add(Vertex(t, (i + 1) % 3));
            outer.Add(Neighbour(t, (i + 1) % 3));
            cons.Add(IsConstraint(t, (i + 1) % 3));
        }
        bool open = Neighbour(fan[m - 1], (IndexOf(fan[m - 1], p) + 2) % 3) == NoNeighbour;
        if (open)
        {
            int last = fan[m - 1];
            int i = IndexOf(last, p);
            poly.Add(Vertex(last, (i + 2) % 3));
        }

        foreach (int t in fan) { _nav[t] = NavBytes.Dead; _triFree.Add(t); }
        _pointDead[p] = true;
        _pointFree.Add(p);

        // Boundary edges of the polygon: (poly[k], poly[k+1]) with outer[k]; the closing edge of an
        // open fan is the former border pair (poly[m], poly[0]) which had no triangle either side.
        var verts = new List<int>(poly);
        var edgeOuter = new Dictionary<(int, int), (int, bool)>();
        for (int k = 0; k < poly.Count - (open ? 1 : 0); k++)
        {
            int a = poly[k], b = poly[(k + 1) % poly.Count];
            edgeOuter[(a, b)] = (outer[k], cons[k]);
        }
        if (open) edgeOuter[(poly[poly.Count - 1], poly[0])] = (NoNeighbour, true);

        var made = new List<int>();
        int guardTotal = verts.Count * verts.Count + 8;
        while (verts.Count > 3 && guardTotal-- > 0)
        {
            bool clipped = false;
            for (int k = 0; k < verts.Count; k++)
            {
                int a = verts[(k + verts.Count - 1) % verts.Count], b = verts[k], c = verts[(k + 1) % verts.Count];
                if (Orient(_px[a], _py[a], _px[b], _py[b], _px[c], _py[c]) <= 0) continue;
                if (AnyInside(verts, a, b, c)) continue;
                made.Add(NewTriangle(a, b, c, nav));
                verts.RemoveAt(k);
                clipped = true;
                break;
            }
            if (!clipped) throw new InvalidOperationException("ear clipping stalled");
        }
        made.Add(NewTriangle(verts[0], verts[1], verts[2], nav));

        // Link every new triangle: boundary edges to the recorded outer neighbours, inner edges
        // to each other by reversed-edge lookup.
        var inner = new Dictionary<(int, int), int>();
        foreach (int t in made)
            for (int i = 0; i < 3; i++)
                inner[(Vertex(t, i), Vertex(t, (i + 1) % 3))] = t;

        foreach (int t in made)
        {
            for (int i = 0; i < 3; i++)
            {
                int a = Vertex(t, i), b = Vertex(t, (i + 1) % 3);
                if (edgeOuter.TryGetValue((a, b), out (int neighbour, bool constraint) o))
                {
                    Link(t, i, o.neighbour);
                    SetConstraint(t, i, o.constraint || o.neighbour == NoNeighbour);
                }
                else if (inner.TryGetValue((b, a), out int u))
                {
                    _n[(3 * t) + i] = u;
                    SetConstraint(t, i, false);
                }
                else throw new InvalidOperationException("ear clipping left an unmatched edge");
            }
        }
    }

    /// <summary>Whether any other polygon vertex lies in the closed ear triangle. Closed, not
    /// open: on a tile grid many vertices are collinear, and an ear whose diagonal passes through
    /// one would leave an edge nothing matches.</summary>
    private bool AnyInside(List<int> verts, int a, int b, int c)
    {
        foreach (int q in verts)
        {
            if (q == a || q == b || q == c) continue;
            if (Orient(_px[a], _py[a], _px[b], _py[b], _px[q], _py[q]) >= 0 &&
                Orient(_px[b], _py[b], _px[c], _py[c], _px[q], _py[q]) >= 0 &&
                Orient(_px[c], _py[c], _px[a], _py[a], _px[q], _py[q]) >= 0) return true;
        }
        return false;
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
