// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Pathfinding;

/// <summary>The original's `Route2path` (`0x004CEF40`, navigation-and-steering.md §2.1): reduce a
/// corridor of triangles to the corners a straight-line path must turn at. Left and right bounds
/// are kept from the current apex; when one crosses the other, that bound's gate endpoint becomes
/// a waypoint and the apex. The destination is the final, degenerate gate. Nothing here rounds to
/// a tile, which is why a route across open floor is one straight segment.</summary>
/// <remarks>What the mover aims at is not these corners: <see cref="GateWalk"/> re-walks the
/// route's gates against this polyline and the mover takes the midpoint of each kept gate.</remarks>
[Dk2Behavior(Confidence.Confirmed, "docs/research/navigation-and-steering.md")]
public static class Funnel
{
    /// <summary>Appends the turn points strictly between <paramref name="from"/> and
    /// <paramref name="to"/>, then <paramref name="to"/> itself, and returns the accumulated
    /// Euclidean length through the original's integer square root.</summary>
    public static int Reduce(NavMesh mesh, NavPoint from, NavPoint to, List<int> corridor, List<NavPoint> waypoints)
    {
        int gates = corridor.Count - 1;
        var left = new NavPoint[gates + 2];
        var right = new NavPoint[gates + 2];
        left[0] = right[0] = from;
        for (int k = 0; k < gates; k++)
        {
            Gate(mesh, corridor[k], corridor[k + 1], out left[k + 1], out right[k + 1]);
        }
        left[gates + 1] = right[gates + 1] = to;

        int startCount = waypoints.Count;
        NavPoint apex = from, fl = from, fr = from;
        int apexIndex = 0, leftIndex = 0, rightIndex = 0;

        for (int i = 1; i < left.Length; i++)
        {
            NavPoint pl = left[i], pr = right[i];

            // Tighten the right bound if the new right point is inside the funnel.
            if (Orient(apex, fr, pr) >= 0)
            {
                if (apex == fr || Orient(apex, fl, pr) <= 0)
                {
                    fr = pr; rightIndex = i;
                }
                else
                {
                    // Right crossed left: the left bound is a corner.
                    waypoints.Add(fl);
                    apex = fl; apexIndex = leftIndex;
                    fl = fr = apex; leftIndex = rightIndex = apexIndex;
                    i = apexIndex;
                    continue;
                }
            }

            if (Orient(apex, fl, pl) <= 0)
            {
                if (apex == fl || Orient(apex, fr, pl) >= 0)
                {
                    fl = pl; leftIndex = i;
                }
                else
                {
                    waypoints.Add(fr);
                    apex = fr; apexIndex = rightIndex;
                    fl = fr = apex; leftIndex = rightIndex = apexIndex;
                    i = apexIndex;
                    continue;
                }
            }
        }

        waypoints.Add(to);

        int length = 0;
        NavPoint prev = from;
        for (int k = startCount; k < waypoints.Count; k++)
        {
            length += IntegerMath.Length(waypoints[k].X - prev.X, waypoints[k].Y - prev.Y);
            prev = waypoints[k];
        }
        return length;
    }

    /// <summary>The shared edge between two adjacent corridor triangles, as the gate the walk
    /// passes through: with <paramref name="t"/> on the left of its directed edge, crossing out of
    /// it puts the edge's end on the walker's left.</summary>
    public static void Gate(NavMesh mesh, int t, int next, out NavPoint left, out NavPoint right)
    {
        for (int i = 0; i < 3; i++)
        {
            if (mesh.Neighbour(t, i) != next) continue;
            int a = mesh.Vertex(t, i), b = mesh.Vertex(t, (i + 1) % 3);
            right = new NavPoint(mesh.PointX(a) * NavMesh.UnitsPerTile, mesh.PointY(a) * NavMesh.UnitsPerTile);
            left = new NavPoint(mesh.PointX(b) * NavMesh.UnitsPerTile, mesh.PointY(b) * NavMesh.UnitsPerTile);
            return;
        }
        throw new InvalidOperationException($"triangles {t} and {next} are not neighbours");
    }

    private static int Orient(NavPoint a, NavPoint b, NavPoint c) =>
        NavMesh.Orient(a.X, a.Y, b.X, b.Y, c.X, c.Y);
}
