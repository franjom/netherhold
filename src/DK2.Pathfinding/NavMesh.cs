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
/// that <see cref="Orient"/> is positive. Point location is a scan, not the original's walk — a
/// labelled residual (ADR-0157) that yields the same mesh. Deliberately absent, because the shipped
/// game never engages them: edge width classes, mover classes and region ids (§2.2).</para>
/// <para>Determinism: integer arithmetic only, arrays indexed by insertion order, free lists
/// reused in a fixed order. Not thread-safe.</para></summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/navigation-mesh-and-search.md")]
public sealed class NavMesh
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

    public int Width { get; private set; }
    public int Height { get; private set; }

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

    /// <summary>The original's reset (`0x004C8D10`): four corner points and two triangles over the
    /// whole map, everything else dead.</summary>
    public void Reset(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        Width = width;
        Height = height;
        _pointCount = 0;
        _pointFree.Clear();
        _triCount = 0;
        _triFree.Clear();

        int p0 = NewPoint(0, 0);
        int p1 = NewPoint(width, 0);
        int p2 = NewPoint(width, height);
        int p3 = NewPoint(0, height);

        int t0 = NewTriangle(p0, p1, p2, NavBytes.Floor);
        int t1 = NewTriangle(p0, p2, p3, NavBytes.Floor);
        Link(t0, EdgeIndexOf(t0, p2, p0), t1);
        // The map border is a constraint on every side; nothing routes off the map.
        for (int i = 0; i < 3; i++)
        {
            if (Neighbour(t0, i) == NoNeighbour) SetConstraint(t0, i, true);
            if (Neighbour(t1, i) == NoNeighbour) SetConstraint(t1, i, true);
        }
    }

    /// <summary>The live triangle containing a position in 1/256-tile units, or −1. A point on a
    /// shared edge returns the lower-indexed triangle, which is what makes the scan
    /// deterministic.</summary>
    public int Locate(int x, int y)
    {
        for (int t = 0; t < _triCount; t++)
        {
            if (!IsTriangleLive(t)) continue;
            if (Contains(t, x, y)) return t;
        }
        return -1;
    }

    private bool Contains(int t, long x, long y)
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
        if (tileX < 0 || tileY < 0 || tileX > Width || tileY > Height)
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
