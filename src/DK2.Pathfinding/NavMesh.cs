// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Pathfinding;

/// <summary>DK2's navigation mesh: a constrained, non-Delaunay triangulation of the map's
/// navigation bytes (docs/research/navigation-mesh-and-search.md §2, §2.1; ADR-0157 phase 1).
/// <para>Points are tile corners in whole tiles. A triangle holds three point indices, a neighbour
/// across each edge, the navigation byte of the tiles it covers, and a constraint bit per edge.
/// Edge <c>i</c> runs from vertex <c>i</c> to vertex <c>i+1</c>; every live triangle is wound so
/// that <see cref="Orient"/> is positive. Point location gives the lowest slot holding the point, as
/// a scan would, not where the original's walk ends — a labelled residual (ADR-0157) that yields the
/// same mesh, pinned slot for slot by <c>NavMeshBuildOnRealDataTests</c>. Deliberately absent, because
/// the shipped game never engages them: edge width classes, mover classes and region ids (§2.2).</para>
/// <para>Determinism: integer arithmetic only, arrays indexed by insertion order, free lists
/// reused in a fixed order. Not thread-safe.</para></summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/navigation-mesh-and-search.md")]
public sealed partial class NavMesh
{
    /// <summary>Positions handed to the router are in this many units per tile — the original's
    /// creature position <c>&gt;&gt; 4</c> and its mesh points <c>&lt;&lt; 8</c> meet here.</summary>
    public const int UnitsPerTile = 256;

    public const int NoNeighbour = -1;

    private int[] _px = new int[64];
    private int[] _py = new int[64];
    private bool[] _pointDead = new bool[64];
    private int _pointCount;
    private readonly List<int> _pointFree = [];

    private int[] _v = new int[3 * 64];
    private int[] _n = new int[3 * 64];
    private byte[] _nav = new byte[64];
    private byte[] _constraint = new byte[64];
    private int _triCount;
    private readonly List<int> _triFree = [];

    // An index over the arrays above, never part of the mesh: it only finds what a scan in slot
    // order would find, faster (perf/navmesh-rebuilds). A point's triangle is one that holds it,
    // left right by every write of a triangle's vertices, and checked before use.
    private int[] _pointTri = new int[64];
    private readonly List<int> _fan = [];
    /// <summary>The original's locate cache (`DAT_006E5AC0`): sixteen triangle slots, one per
    /// 64-tile square of the canvas, each the last triangle located there; the reset fills it with
    /// the first live slot. It is state: where a walk starts decides which of a vertex's fan
    /// triangles is reached first, and the per-tile rebuild starts its removals and its flood from
    /// that one (ADR-0486), so a save carries it and the hash folds it in.</summary>
    private readonly int[] _locateCache = new int[16];

    /// <summary>The canvas the reset covers, in tiles. The original's is always −256 … 1280
    /// (`0x004C8F30`), far outside any map, so a map's own border is an ordinary constraint and
    /// the tiles beyond it are triangles like any other, carrying <see cref="NavBytes.Outside"/>.
    /// </summary>
    public const int CanvasMin = -256, CanvasMax = 1280;

    public int MinX { get; private set; }
    public int MinY { get; private set; }
    public int MaxX { get; private set; }
    public int MaxY { get; private set; }

    public int Width => MaxX - MinX;
    public int Height => MaxY - MinY;

    /// <summary>Slots ever allocated, live or dead; the index bound for iteration.</summary>
    public int TriangleSlots => _triCount;
    public int PointSlots => _pointCount;

    public bool IsTriangleLive(int t) => _nav[t] != NavBytes.Dead;
    public bool IsPointLive(int p) => !_pointDead[p];
    public int Vertex(int t, int i) => _v[(3 * t) + i];
    public int Neighbour(int t, int i) => _n[(3 * t) + i];
    public byte NavByte(int t) => _nav[t];
    public bool IsConstraint(int t, int i) => (_constraint[t] & (1 << i)) != 0;
    public int PointX(int p) => _px[p];
    public int PointY(int p) => _py[p];

    public int LiveTriangleCount()
    {
        int count = 0;
        for (int t = 0; t < _triCount; t++) if (IsTriangleLive(t)) count++;
        return count;
    }

    /// <summary>Sign of the cross product <c>(b − a) × (c − a)</c>: the original's orientation
    /// test, done in 64 bits so 1/256-tile coordinates cannot overflow.</summary>
    public static int Orient(long ax, long ay, long bx, long by, long cx, long cy)
    {
        long d = ((bx - ax) * (cy - ay)) - ((by - ay) * (cx - ax));
        return d > 0 ? 1 : d < 0 ? -1 : 0;
    }

    /// <summary>A canvas from the origin, for a mesh assembled by hand.</summary>
    public void Reset(int width, int height) => Reset(0, 0, width, height);

    /// <summary>The original's reset (`0x004C8D10`): four corner points and two triangles over the
    /// given rectangle, both stamped <see cref="NavBytes.Outside"/>, everything else dead, both free
    /// lists empty.</summary>
    public void Reset(int x0, int y0, int x1, int y1)
    {
        if (x1 <= x0) throw new ArgumentOutOfRangeException(nameof(x1));
        if (y1 <= y0) throw new ArgumentOutOfRangeException(nameof(y1));
        MinX = x0;
        MinY = y0;
        MaxX = x1;
        MaxY = y1;
        _pointCount = 0;
        _pointFree.Clear();
        _triCount = 0;
        _triFree.Clear();
        Array.Fill(_locateCache, 0);

        int p0 = NewPoint(x0, y0);
        int p1 = NewPoint(x1, y0);
        int p2 = NewPoint(x1, y1);
        int p3 = NewPoint(x0, y1);

        // The original's slot 0 is (p3, p1, p0) and slot 1 is (p1, p3, p2), the diagonal from
        // (x1, y0) to (x0, y1); ours are the same corners the other way round. No constraint
        // bits: the reset sets +0xD = 7, every edge bit and no constraint, and only the partial
        // rebuild's side marker ever sets one (§2.2).
        int t0 = NewTriangle(p3, p0, p1, NavBytes.Outside);
        int t1 = NewTriangle(p1, p2, p3, NavBytes.Outside);
        Link(t0, EdgeIndexOf(t0, p1, p3), t1);
    }

    /// <summary>The original's locate (`0x004C98B0`): from the cell cache's triangle, refreshed
    /// through <see cref="RefreshLocateCell"/> when it died, step across the one edge the position
    /// lies strictly beyond, or turn the fan (`0x004C9540`, <see cref="FanToward"/>) around the
    /// corner between two such edges, until a triangle holds the position on or inside every edge.
    /// That first triangle reached is returned and cached, so a position on an edge or at a vertex
    /// names whichever of its holders the walk meets first, which depends on where the walk
    /// started (ADR-0486). −1 when the walk leaves the canvas, which the original never guards.</summary>
    public int Locate(int x, int y)
    {
        if (_triCount == 0) return -1;
        int cell = CellOf(x, y);
        int t = _locateCache[cell];
        if (!LiveSlot(t))
        {
            t = RefreshLocateCell(cell);
            _locateCache[cell] = t;
            if (t < 0) return -1;
        }
        for (int guard = 0; guard <= (4 * _triCount) + 16; guard++)
        {
            // The original tests its edges 0, 1, 2, which are ours 2, 1, 0 reversed: beyond its
            // edge i is beyond our edge 2 - i.
            int code = 0;
            for (int i = 0; i < 3; i++)
            {
                int k = 2 - i;
                int a = Vertex(t, k), b = Vertex(t, (k + 1) % 3);
                if (Orient(Sx(a), Sy(a), Sx(b), Sy(b), x, y) < 0) code |= 1 << i;
            }
            switch (code)
            {
                case 0:
                    _locateCache[cell] = t;
                    return t;
                case 1: t = Neighbour(t, 2); break;
                case 2: t = Neighbour(t, 1); break;
                case 4: t = Neighbour(t, 0); break;
                // Beyond two edges: the original turns the fan around the corner they share, its
                // slot 1 for edges 0 and 1, slot 0 for 0 and 2, slot 2 for 1 and 2 or all three;
                // its slots 0, 1, 2 are ours 0, 2, 1.
                case 3: t = FanToward(t, 2, x, y); break;
                case 5: t = FanToward(t, 0, x, y); break;
                default: t = FanToward(t, 1, x, y); break;
            }
            if (t < 0) return -1;
        }
        return -1;
    }

    /// <summary>The cell of the original's locate cache for a position in 1/256-tile units: the
    /// canvas in 64-tile squares, clamped to a 4 by 4 grid.</summary>
    private static int CellOf(int x, int y) => Math.Clamp(x >> 14, 0, 3) + (4 * Math.Clamp(y >> 14, 0, 3));

    private bool LiveSlot(int t) => t >= 0 && t < _triCount && IsTriangleLive(t);

    /// <summary>`0x004C97B0`: a live triangle for a cell whose cached one died: the entries of the
    /// cell's left, right, upper and lower neighbours in that order, then every entry in order,
    /// then the first live slot, else −1.</summary>
    private int RefreshLocateCell(int cell)
    {
        int cx = cell & 3, cy = cell >> 2;
        if (cx > 0 && LiveSlot(_locateCache[cell - 1])) return _locateCache[cell - 1];
        if (cx < 3 && LiveSlot(_locateCache[cell + 1])) return _locateCache[cell + 1];
        if (cy > 0 && LiveSlot(_locateCache[cell - 4])) return _locateCache[cell - 4];
        if (cy < 3 && LiveSlot(_locateCache[cell + 4])) return _locateCache[cell + 4];
        foreach (int c in _locateCache) if (LiveSlot(c)) return c;
        for (int t = 0; t < _triCount; t++) if (IsTriangleLive(t)) return t;
        return -1;
    }

    /// <summary>The far point the endpoint rule leans toward: 257 tiles past the origin on both axes.</summary>
    public const int FarX = 0x10100, FarY = 0x10100;

    /// <summary>The triangle a route endpoint belongs to (`0x004C9DF0`): the containing triangle,
    /// unless the point is a vertex — then the fan triangle around it whose wedge holds the far
    /// point — or lies on an edge with the far point beyond it — then the neighbour across. So a
    /// target on a corner shared with a solid tile resolves toward the south-east, as DK2's does
    /// (Q354). −1 when the fan or the edge runs off the map.</summary>
    public int LocateEndpoint(int x, int y)
    {
        int t = Locate(x, y);
        if (t < 0) return -1;
        for (int k = 0; k < 3; k++)
        {
            int p = Vertex(t, k);
            if (Sx(p) == x && Sy(p) == y) return FanToward(t, k, FarX, FarY);
        }
        for (int i = 0; i < 3; i++)
        {
            int a = Vertex(t, i), b = Vertex(t, (i + 1) % 3);
            if (Orient(Sx(a), Sy(a), Sx(b), Sy(b), x, y) != 0) continue;
            if (Orient(Sx(a), Sy(a), Sx(b), Sy(b), FarX, FarY) < 0) return Neighbour(t, i);
        }
        return t;
    }

    /// <summary>The original's fan walk (`0x004C9540`) around vertex slot <c>k</c> of <c>t</c>, written
    /// for our winding, which is the original's reversed: stop where the far point is strictly
    /// inside the edge leaving the vertex and inside or on the edge arriving at it; otherwise cross
    /// the arriving edge and keep the same vertex. −1 when the fan reaches the hull.</summary>
    private int FanToward(int t, int k, long qx, long qy) => TurnFan(ref t, ref k, qx, qy) ? t : -1;

    /// <summary>The fan walk with both results: the triangle and the vertex's slot in it. False
    /// at the hull, where the original's loop would read a −1 slot.</summary>
    private bool TurnFan(ref int t, ref int k, long qx, long qy)
    {
        for (int guard = 0; guard <= _triCount; guard++)
        {
            int v = Vertex(t, k), next = Vertex(t, (k + 1) % 3), prev = Vertex(t, (k + 2) % 3);
            bool strictlyInsideLeaving = Orient(Sx(v), Sy(v), Sx(next), Sy(next), qx, qy) > 0;
            bool onOrInsideArriving = Orient(Sx(prev), Sy(prev), Sx(v), Sy(v), qx, qy) >= 0;
            if (strictlyInsideLeaving && onOrInsideArriving) return true;

            int n = Neighbour(t, (k + 2) % 3);
            if (n < 0) return false;
            int j = 0;
            while (j < 3 && Neighbour(n, j) != t) j++;
            t = n;
            k = j;
        }
        return false;
    }

    /// <summary>Whether a position in 1/256-tile units lies in or on triangle <c>t</c>. A point
    /// on a shared edge is in both triangles; <see cref="Locate"/> picks the lower index.</summary>
    public bool Contains(int t, long x, long y)
    {
        for (int i = 0; i < 3; i++)
        {
            int a = Vertex(t, i), b = Vertex(t, (i + 1) % 3);
            if (Orient(Sx(a), Sy(a), Sx(b), Sy(b), x, y) < 0) return false;
        }
        return true;
    }

    private long Sx(int p) => (long)_px[p] * UnitsPerTile;
    private long Sy(int p) => (long)_py[p] * UnitsPerTile;

    /// <summary>The original's point insertion (`0x004CAA50`): nothing if the tile corner is
    /// already a vertex of the triangle located under it; a split of the two triangles sharing an
    /// edge into four if it lies on one, the edges tried in the original's order 0, 1, 2 (ours 2,
    /// 1, 0); otherwise a split of the containing triangle into three. No in-circle test, no
    /// legalisation. Returns the point's index.</summary>
    public int InsertPoint(int tileX, int tileY)
    {
        if (tileX < MinX || tileY < MinY || tileX > MaxX || tileY > MaxY)
            throw new ArgumentOutOfRangeException(nameof(tileX), "tile corner outside the map");

        long x = (long)tileX * UnitsPerTile, y = (long)tileY * UnitsPerTile;
        int t = Locate((int)x, (int)y);
        if (t < 0) throw new InvalidOperationException("no live triangle contains the point");

        for (int i = 0; i < 3; i++)
        {
            int p = Vertex(t, i);
            if (_px[p] == tileX && _py[p] == tileY) return p;
        }

        for (int e = 0; e < 3; e++)
        {
            int a = DkVertex(t, e), b = DkVertex(t, Next(e));
            if (Orient(Sx(a), Sy(a), Sx(b), Sy(b), x, y) != 0) continue;
            int np = NewPoint(tileX, tileY);
            SplitEdge(t, e, np);
            return np;
        }

        int ip = NewPoint(tileX, tileY);
        SplitInterior(t, ip);
        return ip;
    }

    /// <summary>`0x004CA500`, in the original's slots: <c>t</c> keeps its edge 0 and takes the
    /// point at slot 2; the first new slot <c>a</c> is <c>t</c>'s copy with the point at slot 0,
    /// keeping edge 1; the second, <c>b</c>, has it at slot 1 and keeps edge 2. Inner edges carry no
    /// constraint bit.</summary>
    private void SplitInterior(int t, int p)
    {
        int a = NewTriangleCopy(t);
        int b = NewTriangleCopy(t);
        DkSetVertex(t, 2, p);
        DkSetVertex(a, 0, p);
        DkSetVertex(b, 1, p);
        DkSetNeighbour(t, 1, a);
        DkSetNeighbour(t, 2, b);
        DkSetConstraint(t, 1, false);
        DkSetConstraint(t, 2, false);
        DkSetNeighbour(a, 0, t);
        DkSetNeighbour(a, 2, b);
        DkSetConstraint(a, 0, false);
        DkSetConstraint(a, 2, false);
        DkSetNeighbour(b, 0, t);
        DkSetNeighbour(b, 1, a);
        DkSetConstraint(b, 0, false);
        DkSetConstraint(b, 1, false);
        int na = DkNeighbour(a, 1);
        if (na != NoNeighbour) DkSetNeighbour(na, DkBack(na, t), a);
        int nb = DkNeighbour(b, 2);
        if (nb != NoNeighbour) DkSetNeighbour(nb, DkBack(nb, t), b);
        IndexVertices(a);
        IndexVertices(b);
    }

    /// <summary>The original's split of an edge (`0x004CAA50` with `0x004CA820` twice): each
    /// triangle on the edge keeps the half starting at its own start of the edge and a copy takes
    /// the other half; then the halves are linked across. <paramref name="e"/> is the original's
    /// edge index of <paramref name="t"/>.</summary>
    private void SplitEdge(int t, int e, int p)
    {
        int u = DkNeighbour(t, e);
        if (u == NoNeighbour)
        {
            int only = SplitEdgeHalf(t, e, p);
            DkSetNeighbour(t, e, NoNeighbour);
            DkSetNeighbour(only, e, NoNeighbour);
            return;
        }
        int bu = DkBack(u, t);
        int nt = SplitEdgeHalf(t, e, p);
        int nu = SplitEdgeHalf(u, bu, p);
        DkSetNeighbour(t, e, nu);
        DkSetNeighbour(u, bu, nt);
    }

    /// <summary>`0x004CA820`: a copy of <paramref name="t"/> takes the point at slot <paramref name="e"/>
    /// and <paramref name="t"/> takes it at next(e), so <paramref name="t"/> keeps the half of edge
    /// <paramref name="e"/> from its own start; the two are linked along the new inner edge, which
    /// carries no constraint bit, and the copy's outer neighbour is pointed back at it. Returns the
    /// copy.</summary>
    private int SplitEdgeHalf(int t, int e, int p)
    {
        int n = NewTriangleCopy(t);
        DkSetVertex(t, Next(e), p);
        DkSetVertex(n, e, p);
        DkSetNeighbour(t, Next(e), n);
        DkSetConstraint(t, Next(e), false);
        DkSetNeighbour(n, Prev(e), t);
        DkSetConstraint(n, Prev(e), false);
        int outer = DkNeighbour(n, Next(e));
        if (outer != NoNeighbour) DkSetNeighbour(outer, DkBack(outer, t), n);
        IndexVertices(n);
        return n;
    }

    // ------------------------------------------------------------------ storage

    private int NewPoint(int x, int y)
    {
        int p;
        if (_pointFree.Count > 0)
        {
            p = _pointFree[_pointFree.Count - 1];
            _pointFree.RemoveAt(_pointFree.Count - 1);
        }
        else
        {
            if (_pointCount == _px.Length)
            {
                Array.Resize(ref _px, _px.Length * 2);
                Array.Resize(ref _py, _py.Length * 2);
                Array.Resize(ref _pointDead, _pointDead.Length * 2);
                Array.Resize(ref _pointTri, _pointTri.Length * 2);
            }
            p = _pointCount++;
        }
        _px[p] = x; _py[p] = y; _pointDead[p] = false;
        return p;
    }

    /// <summary>A triangle slot: the free list's last entry (the original's list head), else the
    /// next slot after the high-water mark.</summary>
    private int AllocTriangle()
    {
        if (_triFree.Count > 0)
        {
            int t = _triFree[_triFree.Count - 1];
            _triFree.RemoveAt(_triFree.Count - 1);
            return t;
        }
        if (_triCount == _nav.Length)
        {
            Array.Resize(ref _v, _v.Length * 2);
            Array.Resize(ref _n, _n.Length * 2);
            Array.Resize(ref _nav, _nav.Length * 2);
            Array.Resize(ref _constraint, _constraint.Length * 2);
        }
        return _triCount++;
    }

    private int NewTriangle(int a, int b, int c, byte nav)
    {
        int t = AllocTriangle();
        _nav[t] = nav;
        _constraint[t] = 0;
        for (int i = 0; i < 3; i++) _n[(3 * t) + i] = NoNeighbour;
        SetVertices(t, a, b, c);
        return t;
    }

    /// <summary>Stores the vertices wound positively, swapping the last two if needed.</summary>
    private void SetVertices(int t, int a, int b, int c)
    {
        if (Orient(_px[a], _py[a], _px[b], _py[b], _px[c], _py[c]) < 0) (b, c) = (c, b);
        _v[(3 * t) + 0] = a; _v[(3 * t) + 1] = b; _v[(3 * t) + 2] = c;
        _pointTri[a] = t; _pointTri[b] = t; _pointTri[c] = t;
    }

    /// <summary>Every triangle holding point <c>p</c>, by walking its fan both ways from the
    /// triangle the index names, in no particular order. Falls back to the scan if the index
    /// names a triangle that does not hold the point, which no write leaves it doing.</summary>
    private List<int> Fan(int p)
    {
        _fan.Clear();
        int start = _pointTri[p];
        if (start >= _triCount || !IsTriangleLive(start) || IndexOf(start, p) < 0)
        {
            for (int t = 0; t < _triCount; t++)
                if (IsTriangleLive(t) && IndexOf(t, p) >= 0) _fan.Add(t);
            return _fan;
        }

        // Turn across the edge arriving at p until the fan closes or reaches the hull...
        int cur = start;
        for (int guard = 0; guard <= _triCount; guard++)
        {
            _fan.Add(cur);
            int next = Neighbour(cur, (IndexOf(cur, p) + 2) % 3);
            if (next == start) return _fan;
            if (next == NoNeighbour) break;
            cur = next;
        }

        // ...and, having reached the hull, the other way from the start across the edge leaving p.
        cur = start;
        for (int guard = 0; guard <= _triCount; guard++)
        {
            int next = Neighbour(cur, IndexOf(cur, p));
            if (next == NoNeighbour || next == start) break;
            _fan.Add(next);
            cur = next;
        }
        return _fan;
    }

    /// <summary>Points edge <c>i</c> of <c>t</c> at <c>u</c>, and the matching edge of <c>u</c>
    /// back at <c>t</c>.</summary>
    private void Link(int t, int i, int u)
    {
        _n[(3 * t) + i] = u;
        if (u == NoNeighbour) return;
        int from = Vertex(t, i), to = Vertex(t, (i + 1) % 3);
        int j = EdgeIndexOf(u, to, from);
        _n[(3 * u) + j] = t;
    }

    private void SetConstraint(int t, int i, bool on)
    {
        if (on) _constraint[t] |= (byte)(1 << i);
        else _constraint[t] &= (byte)~(1 << i);
    }

    /// <summary>The edge of <c>t</c> running from <c>from</c> to <c>to</c>.</summary>
    public int EdgeIndexOf(int t, int from, int to)
    {
        for (int i = 0; i < 3; i++)
            if (Vertex(t, i) == from && Vertex(t, (i + 1) % 3) == to) return i;
        throw new InvalidOperationException($"triangle {t} has no edge {from}->{to}");
    }
}
