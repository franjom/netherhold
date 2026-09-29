// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation.Commands;

/// <summary>
/// Marks tiles for digging.
///
/// <para>CONFIRMED: a keeper paints a rectangle of rock with dig markers, imps
/// dig what is marked, and the terrain table's per-type <c>Taggable</c> flag
/// says what may be marked at all.</para>
///
/// <para>Ours, not DK2's: the shape of this command — an explicit list of tiles
/// with an explicit tag/untag intent. The original's interface is a drag-toggle
/// whose exact rule is UNKNOWN (does a drag starting on a tagged tile untag
/// everything it touches?) and belongs to the UI layer.</para>
/// </summary>
public sealed record TagTilesCommand(byte PlayerId, IReadOnlyList<TilePos> Tiles, bool Tagged)
    : PlayerCommand(PlayerId)
{
    public static TagTilesCommand Tag(byte playerId, params TilePos[] tiles) =>
        new(playerId, tiles, true);

    public static TagTilesCommand Untag(byte playerId, params TilePos[] tiles) =>
        new(playerId, tiles, false);

    /// <summary>Every tile in a rectangle, in row-major order.</summary>
    public static TagTilesCommand Rect(
        byte playerId, int x0, int y0, int x1, int y1, bool tagged = true)
    {
        var tiles = new List<TilePos>();
        if (!tagged)
        {
            for (int y = Math.Min(y0, y1); y <= Math.Max(y0, y1); y++)
            {
                for (int x = Math.Min(x0, x1); x <= Math.Max(x0, x1); x++)
                {
                    tiles.Add(new TilePos(x, y));
                }
            }

            return new TagTilesCommand(playerId, tiles, tagged);
        }

        // Marking files the tiles in DK2's order (0x00512a20, run under an emulator): square
        // shells around the corner the drag began at, shell k from (k,0) up to (k,k) and back to
        // (0,k) when k is odd, the other way round when k is even, skipping what lies outside.
        int sx = x1 >= x0 ? 1 : -1, sy = y1 >= y0 ? 1 : -1;
        int w = Math.Abs(x1 - x0), h = Math.Abs(y1 - y0);
        tiles.Add(new TilePos(x0, y0));
        for (int k = 1; k <= Math.Max(w, h); k++)
        {
            for (int i = 0; i <= 2 * k; i++)
            {
                (int dx, int dy) = i <= k ? (k, i) : (2 * k - i, k);
                if (k % 2 == 0)
                {
                    (dx, dy) = (dy, dx);
                }

                if (dx <= w && dy <= h)
                {
                    tiles.Add(new TilePos(x0 + sx * dx, y0 + sy * dy));
                }
            }
        }
        return new TagTilesCommand(playerId, tiles, tagged);
    }

    public override void HashInto(ref Hash64 hash)
    {
        base.HashInto(ref hash);
        hash.Add(Tagged);
        hash.Add(Tiles.Count);
        foreach (TilePos t in Tiles)
        {
            hash.Add(t.X);
            hash.Add(t.Y);
        }
    }
}
