// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Pathfinding;

/// <summary>A gate the route keeps after the funnel (navigation-and-steering.md §2.1): the
/// endpoint on the walker's right (<see cref="A"/>), the one on the left (<see cref="B"/>),
/// where the funnel polyline's line crosses it, and which endpoint the point selector measures
/// from (0 for <see cref="A"/>, 1 for <see cref="B"/>).</summary>
public struct GateRecord
{
    public NavPoint A;
    public NavPoint B;
    public NavPoint Crossing;
    public int Selector;

    public GateRecord(NavPoint a, NavPoint b, int selector)
    {
        A = a;
        B = b;
        Crossing = default;
        Selector = selector;
    }
}

/// <summary>The stage after the funnel, the original's `grtc:` (<c>0x004CF490</c>, Q344). It walks
/// the route's gates again and keeps each one that some sight-line from the last kept gate's
/// endpoints to the next gate's endpoints does not pass through; picks the endpoint nearer the
/// funnel polyline; caps a kept gate at sixteen tiles around the point where the polyline's line
/// crosses it; and finally takes one point across each kept gate. The mover aims at those points,
/// not at the funnel's corners. Coordinates are the router's, 256 to a tile.</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/navigation-and-steering.md")]
public static class GateWalk
{
    /// <summary>The cap the wrapper <c>0x004D0150</c> passes as a literal: sixteen tiles.
    /// The mover's <c>0x2000</c> goes only to the point selector.</summary>
    public const int Cap = 0x1000;

    /// <summary>The mover's fraction across each kept gate, over <see cref="FractionOne"/>.</summary>
    public const int Midpoint = 0x2000;

    public const int FractionOne = 0x4000;

    /// <summary>A kept gate within a tile of the polyline is collapsed straight away, without
    /// looking for a crossing.</summary>
    public const int NearThreshold = NavMesh.UnitsPerTile;

    /// <summary>The original's buffer holds 256 records; it diagnoses the 257th and writes it over
    /// its own count. Kept gates past the cap are dropped here instead, a divergence no shipped
    /// route can reach.</summary>
    public const int MaxRecords = 256;

    private struct Gate
    {
        public NavPoint A;
        public NavPoint B;
    }

    /// <summary>The longer axis distance plus half the shorter, the original's cheap length.</summary>
    public static int Octile(NavPoint p, NavPoint q)
    {
        int dx = Math.Abs(p.X - q.X), dy = Math.Abs(p.Y - q.Y);
        return dx > dy ? dx + (dy >> 1) : dy + (dx >> 1);
    }

    /// <summary>The re-walk over <paramref name="corridor"/>, whose funnel in walking order is
    /// <paramref name="funnel"/> (corners, then the destination). Fills <paramref name="records"/>,
    /// which must hold <see cref="MaxRecords"/> + 1, and returns the count; the last record is
    /// always the degenerate destination gate.</summary>
    public static int Run(NavMesh mesh, NavPoint from, NavPoint to, List<int> corridor, List<NavPoint> funnel, GateRecord[] records, int cap)
    {
        if (records.Length <= MaxRecords) throw new ArgumentException("needs MaxRecords + 1 slots", nameof(records));
        int gates = corridor.Count - 1;
        int count = 0;
        if (gates <= 0)
        {
            records[count++] = new GateRecord(to, to, 0);
            return count;
        }

        Gate cur = Shared(mesh, corridor[0], corridor[1]);
        NavPoint keptA = from, keptB = from, previousF = from;
        int j = 0;
        for (int i = 1; i <= gates; i++)
        {
            Gate next = i < gates ? Shared(mesh, corridor[i], corridor[i + 1]) : new Gate { A = to, B = to };
            bool transparent = State(keptA, cur, next.A) == 0 && State(keptA, cur, next.B) == 0
                && State(keptB, cur, next.A) == 0 && State(keptB, cur, next.B) == 0;
            if (transparent)
            {
                cur = next;
                continue;
            }

            if (count == MaxRecords) break;
            ref GateRecord rec = ref records[count];
            rec = new GateRecord(cur.A, cur.B, -1);

            int a0 = Octile(cur.A, funnel[j]), b0 = Octile(cur.B, funnel[j]);
            bool moreFunnel = j < funnel.Count - 1;
            int a1 = moreFunnel ? Octile(cur.A, funnel[j + 1]) : a0;
            int b1 = moreFunnel ? Octile(cur.B, funnel[j + 1]) : b0;
            int proximity;
            if (Math.Min(a0, b0) < Math.Min(a1, b1))
            {
                rec.Selector = a0 >= b0 ? 1 : 0;
                proximity = Math.Min(a0, b0);
            }
            else
            {
                rec.Selector = a1 >= b1 ? 1 : 0;
                proximity = Math.Min(a1, b1);
                if (moreFunnel) previousF = funnel[j++];
            }

            if (proximity < NearThreshold)
            {
                Collapse(ref rec, cap);
            }
            else
            {
                int saved = rec.Selector;
                rec.Selector = 2;
                bool found = Intersect(ref rec, previousF, funnel[j]);
                if (!found)
                {
                    // Past the last funnel point the original reads whatever the buffer held before;
                    // a degenerate segment stands in for that stale point and never crosses.
                    NavPoint after = j < funnel.Count - 1 ? funnel[j + 1] : funnel[j];
                    found = Intersect(ref rec, funnel[j], after);
                    if (found && j < funnel.Count - 1) previousF = funnel[j++];
                    if (!found) rec.Selector = saved;
                }

                if (rec.Selector == 2) AdjustAroundCrossing(ref rec, cap);
                else Collapse(ref rec, cap);
                rec.Selector = saved;
            }

            keptA = rec.A;
            keptB = rec.B;
            count++;
            cur = next;
        }

        records[count++] = new GateRecord(to, to, 0);
        return count;
    }

    /// <summary>The point selector (<c>0x004D0240</c>): one point <paramref name="fraction"/> of the
    /// way across each kept gate from its selected endpoint, then the destination. The fraction is
    /// clamped below <see cref="FractionOne"/>.</summary>
    public static void Select(GateRecord[] records, int count, NavPoint to, int fraction, List<NavPoint> waypoints)
    {
        if (fraction > FractionOne - 1) fraction = FractionOne - 1;
        else if (fraction < 0) fraction = 0;
        for (int k = 0; k < count - 1; k++)
        {
            ref readonly GateRecord r = ref records[k];
            int dx = (r.B.X - r.A.X) * fraction >> 14, dy = (r.B.Y - r.A.Y) * fraction >> 14;
            waypoints.Add(r.Selector == 0
                ? new NavPoint(r.A.X + dx, r.A.Y + dy)
                : new NavPoint(r.B.X - dx, r.B.Y - dy));
        }

        waypoints.Add(to);
    }

    /// <summary>Where the line through <paramref name="p"/> and <paramref name="q"/> (not the
    /// segment) meets the gate (<c>0x004CFFD0</c>): the gate parameter in Q14 must lie within
    /// ±1.0 and the point inside the gate's bounding box. The crossing is written before the box
    /// test, so a miss can still leave one in the record.</summary>
    public static bool Intersect(ref GateRecord r, NavPoint p, NavPoint q)
    {
        int gx = (r.B.X - r.A.X) << 6, gy = (r.B.Y - r.A.Y) << 6;
        int ax = (r.A.X - p.X) << 6, ay = (r.A.Y - p.Y) << 6;
        int sx = (p.X - q.X) << 6, sy = (p.Y - q.Y) << 6;
        int num = MulQ14(sy, ax) - MulQ14(sx, ay);
        int den = MulQ14(gy, sx) - MulQ14(gx, sy);
        if (den == 0) return false;
        int t = DivQ14(num, den);
        if (t < -FractionOne || t > FractionOne) return false;
        r.Crossing = new NavPoint(r.A.X + (MulQ14(t, gx) >> 6), r.A.Y + (MulQ14(t, gy) >> 6));
        return InBox(r.Crossing, r.A, r.B);
    }

    /// <summary>A gate at least <paramref name="cap"/> long is cut to one tile under the cap,
    /// measured from the endpoint the selector keeps (<c>0x004CFC50</c>).</summary>
    public static void Collapse(ref GateRecord r, int cap)
    {
        if (Octile(r.A, r.B) < cap) return;
        bool keepB = r.Selector == 1;
        NavPoint kept = keepB ? r.B : r.A, moved = keepB ? r.A : r.B;
        int dx = (moved.X - kept.X) << 6, dy = (moved.Y - kept.Y) << 6;
        int den = IntegerMath.SquareRoot(MulQ14(dx, dx) + MulQ14(dy, dy)) << 13;
        if (den == 0) den = 1;
        int tiles = (cap >> 8) - 1;
        var cut = new NavPoint(kept.X + (DivQ14(dx, den) * tiles), kept.Y + (DivQ14(dy, den) * tiles));
        if (keepB) r.A = cut;
        else r.B = cut;
    }

    /// <summary>With a crossing known (<c>0x004CFDA0</c>): nothing when both endpoints are within
    /// half the cap of it; a collapse toward an endpoint that is; otherwise each endpoint moves to
    /// half the cap from the crossing along the gate, if that point lies in the gate's box. The
    /// second test sees the first move.</summary>
    public static void AdjustAroundCrossing(ref GateRecord r, int cap)
    {
        int dA = Octile(r.A, r.Crossing), dB = Octile(r.B, r.Crossing);
        int half = cap >> 1;
        if (dA < half && dB < half) return;
        int nearHalf = (cap + 2) >> 1;
        if (dA < nearHalf)
        {
            r.Selector = 0;
            Collapse(ref r, cap);
            return;
        }

        if (dB < nearHalf)
        {
            r.Selector = 1;
            Collapse(ref r, cap);
            return;
        }

        int dx = (r.B.X - r.A.X) << 6, dy = (r.B.Y - r.A.Y) << 6;
        int den = IntegerMath.SquareRoot(MulQ14(dx, dx) + MulQ14(dy, dy)) << 7;
        int ux = den == 0 ? 0 : DivQ14(dx, den) >> 6, uy = den == 0 ? 0 : DivQ14(dy, den) >> 6;
        int tiles = cap >> 9;
        var minus = new NavPoint(r.Crossing.X - (ux * tiles), r.Crossing.Y - (uy * tiles));
        var plus = new NavPoint(r.Crossing.X + (ux * tiles), r.Crossing.Y + (uy * tiles));
        if (InBox(minus, r.A, r.B)) r.A = minus;
        if (InBox(plus, r.A, r.B)) r.B = plus;
    }

    /// <summary>−1 when the sight-line from <paramref name="o"/> to <paramref name="n"/> leaves the
    /// gate's right endpoint on its left, 1 when it leaves the left endpoint on its right, 0 when it
    /// passes between them.</summary>
    private static int State(NavPoint o, in Gate g, NavPoint n)
    {
        if (NavMesh.Orient(o.X, o.Y, g.A.X, g.A.Y, n.X, n.Y) < 0) return -1;
        return NavMesh.Orient(o.X, o.Y, g.B.X, g.B.Y, n.X, n.Y) > 0 ? 1 : 0;
    }

    /// <summary>The original's edge function (<c>0x004CCD50</c>) names the walker's right-hand
    /// endpoint first: its triangles are wound the other way from ours.</summary>
    private static Gate Shared(NavMesh mesh, int t, int next)
    {
        Funnel.Gate(mesh, t, next, out NavPoint left, out NavPoint right);
        return new Gate { A = right, B = left };
    }

    private static bool InBox(NavPoint p, NavPoint a, NavPoint b) =>
        p.X >= Math.Min(a.X, b.X) && p.X <= Math.Max(a.X, b.X) &&
        p.Y >= Math.Min(a.Y, b.Y) && p.Y <= Math.Max(a.Y, b.Y);

    private static int MulQ14(int a, int b) => (int)(((long)a * b) >> 14);

    private static int DivQ14(int a, int den) => (int)(((long)a << 14) / den);
}
