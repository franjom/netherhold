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
    private int _locateHint;

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
        _locateHint = 0;

        int p0 = NewPoint(x0, y0);
        int p1 = NewPoint(x1, y0);
        int p2 = NewPoint(x1, y1);
        int p3 = NewPoint(x0, y1);

        int t0 = NewTriangle(p0, p1, p2, NavBytes.Outside);
        int t1 = NewTriangle(p0, p2, p3, NavBytes.Outside);
        Link(t0, EdgeIndexOf(t0, p2, p0), t1);
        // No constraint bits: the original's reset sets +0xD = 7, every edge bit and no
        // constraint, and only the partial rebuild's side marker ever sets one (§2.2).
    }

    /// <summary>The live triangle containing a position in 1/256-tile units, or −1. A point on a
    /// shared edge returns the lower-indexed triangle, which is what makes the scan
    /// deterministic.</summary>
    public int Locate(int x, int y)
    {
        int t = Walk(x, y);
        if (t < 0) return LocateByScan(x, y);
        _locateHint = t;
        return LowestHolding(t, x, y);
    }

    private int LocateByScan(int x, int y)
    {
        for (int t = 0; t < _triCount; t++)
        {
            if (!IsTriangleLive(t)) continue;
            if (Contains(t, x, y)) return t;
        }
        return -1;
    }

    /// <summary>A triangle holding the position, found by stepping from the last one located
    /// across an edge the position lies strictly beyond. The edge tested first turns with each
    /// step, so the walk cannot circle a fixed loop; one that runs long or off the mesh gives up
    /// and the scan answers instead.</summary>
    private int Walk(long x, long y)
    {
        if (_triCount == 0) return -1;
        int t = _locateHint < _triCount && IsTriangleLive(_locateHint) ? _locateHint : 0;
        if (!IsTriangleLive(t)) return -1;
        for (int steps = 0; steps <= _triCount; steps++)
        {
            int exit = -1;
            for (int k = 0; k < 3 && exit < 0; k++)
            {
                int i = (k + steps) % 3;
                int a = Vertex(t, i), b = Vertex(t, (i + 1) % 3);
                if (Orient(Sx(a), Sy(a), Sx(b), Sy(b), x, y) < 0) exit = i;
            }
            if (exit < 0) return t;
            t = Neighbour(t, exit);
            if (t == NoNeighbour) return -1;
        }
        return -1;
    }

    /// <summary>What the scan in slot order returns for a position <paramref name="t"/> holds:
    /// <c>t</c> itself for an interior position, the lower of the two triangles on an edge, the
    /// lowest of a vertex's fan. No other triangle can hold it, the mesh having no T-junctions.</summary>
    private int LowestHolding(int t, long x, long y)
    {
        for (int k = 0; k < 3; k++)
        {
            int p = Vertex(t, k);
            if (Sx(p) != x || Sy(p) != y) continue;
            int lowest = t;
            foreach (int f in Fan(p)) if (f < lowest) lowest = f;
            return lowest;
        }
        for (int i = 0; i < 3; i++)
        {
            int a = Vertex(t, i), b = Vertex(t, (i + 1) % 3);
            if (Orient(Sx(a), Sy(a), Sx(b), Sy(b), x, y) != 0) continue;
            int n = Neighbour(t, i);
            return n == NoNeighbour ? t : Math.Min(t, n);
        }
        return t;
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
    private int FanToward(int t, int k, long qx, long qy)
    {
        for (int guard = 0; guard <= _triCount; guard++)
        {
            int v = Vertex(t, k), next = Vertex(t, (k + 1) % 3), prev = Vertex(t, (k + 2) % 3);
            bool strictlyInsideLeaving = Orient(Sx(v), Sy(v), Sx(next), Sy(next), qx, qy) > 0;
            bool onOrInsideArriving = Orient(Sx(prev), Sy(prev), Sx(v), Sy(v), qx, qy) >= 0;
            if (strictlyInsideLeaving && onOrInsideArriving) return t;

            int n = Neighbour(t, (k + 2) % 3);
            if (n < 0) return -1;
            int j = 0;
            while (j < 3 && Neighbour(n, j) != t) j++;
            t = n;
            k = j;
        }
        return -1;
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
    /// already a vertex, a split of the two triangles sharing an edge into four if it lies on one,
    /// otherwise a split of the containing triangle into three. No in-circle test, no legalisation.
    /// Returns the point's index.</summary>
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

        int onEdge = -1;
        for (int i = 0; i < 3; i++)
        {
            int a = Vertex(t, i), b = Vertex(t, (i + 1) % 3);
            if (Orient(Sx(a), Sy(a), Sx(b), Sy(b), x, y) == 0) { onEdge = i; break; }
        }

        int np = NewPoint(tileX, tileY);
        if (onEdge >= 0) SplitEdge(t, onEdge, np);
        else SplitInterior(t, np);
        return np;
    }

    private void SplitInterior(int t, int p)
    {
        int a = Vertex(t, 0), b = Vertex(t, 1), c = Vertex(t, 2);
        int nab = Neighbour(t, 0), nbc = Neighbour(t, 1), nca = Neighbour(t, 2);
        bool cab = IsConstraint(t, 0), cbc = IsConstraint(t, 1), cca = IsConstraint(t, 2);
        byte nav = _nav[t];

        // t is reused for (a, b, p); the other two are new.
        SetVertices(t, a, b, p);
        int t2 = NewTriangle(b, c, p, nav);
        int t3 = NewTriangle(c, a, p, nav);

        // Outer edges keep their neighbours and constraint bits.
        _constraint[t] = 0; _constraint[t2] = 0; _constraint[t3] = 0;
        Link(t, 0, nab); SetConstraint(t, 0, cab);
        Link(t2, 0, nbc); SetConstraint(t2, 0, cbc);
        Link(t3, 0, nca); SetConstraint(t3, 0, cca);

        // Inner edges: (b,p) t↔t2, (c,p) t2↔t3, (a,p) t3↔t.
        Link(t, 1, t2);
        Link(t2, 1, t3);
        Link(t3, 1, t);
    }

    private void SplitEdge(int t, int edge, int p)
    {
        // Rotate so that the split edge is (a, b) with c opposite.
        int a = Vertex(t, edge), b = Vertex(t, (edge + 1) % 3), c = Vertex(t, (edge + 2) % 3);
        int nOuterBc = Neighbour(t, (edge + 1) % 3), nOuterCa = Neighbour(t, (edge + 2) % 3);
        bool cAb = IsConstraint(t, edge);
        bool cBc = IsConstraint(t, (edge + 1) % 3), cCa = IsConstraint(t, (edge + 2) % 3);
        int u = Neighbour(t, edge);
        byte navT = _nav[t];

        // t becomes (a, p, c); t2 = (p, b, c).
        SetVertices(t, a, p, c);
        int t2 = NewTriangle(p, b, c, navT);
        _constraint[t] = 0; _constraint[t2] = 0;
        Link(t, 2, nOuterCa); SetConstraint(t, 2, cCa);
        Link(t2, 1, nOuterBc); SetConstraint(t2, 1, cBc);
        Link(t, 1, t2);                       // (p, c) ↔ (c, p)
        SetConstraint(t, 0, cAb); SetConstraint(t2, 0, cAb);

        if (u == NoNeighbour)
        {
            _n[(3 * t) + 0] = NoNeighbour;
            _n[(3 * t2) + 0] = NoNeighbour;
            return;
        }

        // u = (b, a, d) across the same edge becomes (b, p, d); u2 = (p, a, d).
        int j = EdgeIndexOf(u, b, a);
        int d = Vertex(u, (j + 2) % 3);
        int nOuterAd = Neighbour(u, (j + 1) % 3), nOuterDb = Neighbour(u, (j + 2) % 3);
        bool cAd = IsConstraint(u, (j + 1) % 3), cDb = IsConstraint(u, (j + 2) % 3);
        byte navU = _nav[u];

        SetVertices(u, b, p, d);
        int u2 = NewTriangle(p, a, d, navU);
        _constraint[u] = 0; _constraint[u2] = 0;
        Link(u, 2, nOuterDb); SetConstraint(u, 2, cDb);
        Link(u2, 1, nOuterAd); SetConstraint(u2, 1, cAd);
        Link(u, 1, u2);                       // (p, d) ↔ (d, p)
        SetConstraint(u, 0, cAb); SetConstraint(u2, 0, cAb);

        // Across the split edge: t.(a,p) ↔ u2.(p,a); t2.(p,b) ↔ u.(b,p).
        Link(t, 0, u2);
        Link(t2, 0, u);
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

    private int NewTriangle(int a, int b, int c, byte nav)
    {
        int t;
        if (_triFree.Count > 0)
        {
            t = _triFree[_triFree.Count - 1];
            _triFree.RemoveAt(_triFree.Count - 1);
        }
        else
        {
            if (_triCount == _nav.Length)
            {
                Array.Resize(ref _v, _v.Length * 2);
                Array.Resize(ref _n, _n.Length * 2);
                Array.Resize(ref _nav, _nav.Length * 2);
                Array.Resize(ref _constraint, _constraint.Length * 2);
            }
            t = _triCount++;
        }
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
