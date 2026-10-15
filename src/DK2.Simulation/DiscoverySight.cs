// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Simulation.Combat;
using DK2.Simulation.Steering;

namespace DK2.Simulation;

/// <summary>The native 256-slot, 15 by 15 discovery grid; see fog-reveal-sources.md section3.1.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/fog-reveal-sources.md")]
public static class DiscoverySight
{
    private readonly record struct Entry(int X, int Y, int First, int Last)
    {
        public int DistanceSquared => X * X + Y * Y;
    }

    private static readonly Entry[] Entries = BuildEntries();

    public static void Fill(ISightBlocker blockers, TilePos source, int width, int height, Span<bool> visible)
    {
        if (visible.Length < 225) throw new ArgumentException("A discovery grid has 225 cells.", nameof(visible));
        visible.Clear();
        if (source.X < 0 || source.Y < 0 || source.X >= width || source.Y >= height) return;
        Span<bool> open = stackalloc bool[256];
        open.Fill(true);
        visible[112] = true;
        for (int i = 1; i < 78; i++)
        {
            Entry entry = Entries[i];
            int x = source.X + entry.X, y = source.Y + entry.Y;
            bool inMap = x >= 0 && y >= 0 && x < width && y < height;
            bool seen = false;
            for (int slot = entry.First; ; slot = (slot + 1) & 255)
            {
                seen |= open[slot];
                if (slot == entry.Last) break;
            }
            if (inMap && seen) visible[(entry.Y + 7) * 15 + entry.X + 7] = true;
            if (inMap && !blockers.Blocks(x, y)) continue;
            for (int slot = entry.First; ; slot = (slot + 1) & 255)
            {
                open[slot] = false;
                if (slot == entry.Last) break;
            }
        }
    }

    private static Entry[] BuildEntries()
    {
        var entries = new Entry[225];
        int index = 0;
        for (int y = 7; y >= -7; y--)
            for (int x = -7; x <= 7; x++)
            {
                int centre = Bearing.Of(2 * x, -2 * y), first = 0, last = 0;
                int min = int.MaxValue, max = int.MinValue;
                for (int cy = -1; cy <= 1; cy += 2)
                    for (int cx = -1; cx <= 1; cx += 2)
                    {
                        int angle = Bearing.Of(2 * x + cx, -2 * y + cy);
                        int difference = ((angle - centre + 1024) & 2047) - 1024;
                        if (difference < min) { min = difference; first = angle >> 3; }
                        if (difference > max) { max = difference; last = angle >> 3; }
                    }
                entries[index++] = new Entry(x, y, first, last);
            }
        // Native compare-exchange order is significant among equal-distance cells.
        for (int end = 224; end > 0; end--)
            for (int previous = end - 1; previous >= 0; previous--)
                if (entries[end].DistanceSquared < entries[previous].DistanceSquared)
                    (entries[end], entries[previous]) = (entries[previous], entries[end]);
        return entries;
    }
}
