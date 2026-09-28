// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Simulation;

namespace DK2.AI;

/// <summary>
/// Provisional gold scan. Its full sort, tile-count amount and uncorrected centroid
/// differ from DK2; see docs/research/computer-keeper-management.md §12.
/// </summary>
public static class GoldScanner
{
    public const int DepositsKept = 4;

    public static List<GoldDeposit> Scan(KeeperView view)
    {
        var seen = new HashSet<int>();
        var deposits = new List<GoldDeposit>();
        var queue = new Queue<TilePos>();

        for (int index = 0; index < view.World.TileCount; index++)
        {
            if (seen.Contains(index)) continue;
            TilePos start = view.FromIndex(index);
            if (!view.IsGoldRock(start)) continue;

            long sumX = 0, sumY = 0;
            int count = 0;
            queue.Enqueue(start);
            seen.Add(index);
            while (queue.Count > 0)
            {
                TilePos p = queue.Dequeue();
                sumX += p.X;
                sumY += p.Y;
                count++;
                foreach ((int dx, int dy) in KeeperDirections.Clockwise)
                {
                    var n = new TilePos(p.X + dx, p.Y + dy);
                    if (!view.IsGoldRock(n)) continue;
                    int ni = view.IndexOf(n);
                    if (!seen.Add(ni)) continue;
                    queue.Enqueue(n);
                }
            }
            deposits.Add(new GoldDeposit(new TilePos((int)(sumX / count), (int)(sumY / count)), count));
        }

        return deposits
            .OrderBy(d => DistanceToOwnRooms(view, d.Centre))
            .ThenBy(d => d.Centre.Y)
            .ThenBy(d => d.Centre.X)
            .Take(DepositsKept)
            .ToList();
    }

    private static int DistanceToOwnRooms(KeeperView view, TilePos p)
    {
        int best = int.MaxValue;
        foreach (RoomInstance r in view.OwnRooms)
            best = Math.Min(best, KeeperView.DistanceSquared(KeeperView.Centre(r), p));
        if (best == int.MaxValue && view.Heart is TilePos heart)
            best = KeeperView.DistanceSquared(heart, p);
        return best;
    }
}
