// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Simulation;

namespace DK2.AI;

/// <summary>
/// Cardinal gold seams and the original's historical admission cutoff.
/// Room position approximation and border uncertainty: computer-keeper-management.md §12.
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/computer-keeper-management.md")]
public static class GoldScanner
{
    public const int DepositsKept = 4;

    public static List<GoldDeposit> Scan(KeeperView view)
    {
        var seen = new HashSet<int>();
        var retained = new List<(GoldDeposit Deposit, int Distance)>();
        var queue = new Queue<TilePos>();
        int historicalCutoff = 0;

        for (int index = 0; index < view.TileCount; index++)
        {
            if (seen.Contains(index)) continue;
            TilePos start = view.FromIndex(index);
            if (view.Terrain(start).GoldValue == 0) continue;

            long sumX = 0, sumY = 0;
            int count = 0, amount = 0;
            queue.Enqueue(start);
            seen.Add(index);
            while (queue.Count > 0)
            {
                TilePos p = queue.Dequeue();
                sumX += p.X;
                sumY += p.Y;
                count++;
                amount = unchecked(amount + view.Terrain(p).GoldValue);
                foreach ((int dx, int dy) in KeeperDirections.Clockwise)
                {
                    var n = new TilePos(p.X + dx, p.Y + dy);
                    if (!view.InBounds(n) || view.Terrain(n).GoldValue == 0) continue;
                    int ni = view.IndexOf(n);
                    if (!seen.Add(ni)) continue;
                    queue.Enqueue(n);
                }
            }
            var centroid = new TilePos((int)(sumX / count), (int)(sumY / count));
            TilePos centre = CorrectCentroid(view, centroid, (int)sumY);
            int distance = DistanceToOwnRooms(view, centre);
            if (retained.Count == DepositsKept && distance >= historicalCutoff) continue;
            historicalCutoff = Math.Max(historicalCutoff, distance);

            // Admission remembers evicted distances. A later, farther seam can
            // replace the fourth entry even when that entry was nearer.
            if (retained.Count == DepositsKept) retained.RemoveAt(DepositsKept - 1);
            int insertion = retained.FindIndex(d => d.Distance > distance);
            if (insertion < 0) insertion = retained.Count;
            retained.Insert(insertion, (new GoldDeposit(centre, amount), distance));
        }

        return retained.Select(d => d.Deposit).ToList();
    }

    private static TilePos CorrectCentroid(KeeperView view, TilePos centre, int sumY)
    {
        bool Accepts(TilePos p) => view.InBounds(p) && !view.Terrain(p).IsImpenetrable;
        if (Accepts(centre)) return centre;

        var cursor = new TilePos(centre.X + 1, centre.Y);
        int direction = 2, length = 1;
        int limit = 2 * Math.Max(view.Width, view.Height);
        while (length <= limit)
        {
            (int dx, int dy) = KeeperDirections.Clockwise[direction];
            for (int step = 0; step < length; step++)
            {
                if (Accepts(cursor)) return cursor;
                cursor = new TilePos(cursor.X + dx, cursor.Y + dy);
            }
            if ((direction & 1) != 0 && length >= limit) break;
            if ((direction & 1) == 0) length++;
            direction = (direction + 1) & 3;
        }
        return new TilePos(0, unchecked((ushort)sumY));
    }

    private static int DistanceToOwnRooms(KeeperView view, TilePos p)
    {
        int best = int.MaxValue;
        foreach (KeeperRoomView r in view.OwnRooms)
            best = Math.Min(best, KeeperView.DistanceSquared(KeeperView.Centre(r), p));
        if (best == int.MaxValue && view.Heart is TilePos heart)
            best = KeeperView.DistanceSquared(heart, p);
        return best;
    }
}
