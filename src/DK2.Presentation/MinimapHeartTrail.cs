// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Presentation;

[Dk2Behavior(Confidence.Confirmed, "docs/research/minimap-rendering.md")]
public static class MinimapHeartTrail
{
    public const int MaximumDots = 32;
    public static int Build(TilePos heart, FpPosition3 focus, MinimapZoom zoom, GameTick tick,
        Span<MinimapRasterPoint> output)
    {
        if (output.Length < MaximumDots) throw new ArgumentException("Trail buffer is too short.", nameof(output));
        if (zoom.IsWholeMap) return 0;
        int step = zoom.PixelsPerTile;
        int x = Offset(heart.X, focus.X.Raw), y = Offset(heart.Y, focus.Y.Raw);
        int length = IntegerMath.SquareRoot(unchecked((uint)(x * x + y * y)));
        if (length <= 8) return 0;
        int nx = unchecked(x * 256) / length, ny = unchecked(y * 256) / length;
        int count = (length - 5) >> 2;
        // Nothing beyond distance 128 can enter the 128x128 square; skip that invisible prefix.
        int skip = Math.Max(0, (length - 128) / 4);
        int distance = length + (int)(tick.Value & 3) - skip * 4;
        int ax = unchecked(nx * distance), ay = unchecked(ny * distance);
        int written = 0;
        for (int i = skip; i < count; i++)
        {
            int px = 64 + ax / 256, py = 64 + ay / 256;
            if (px > 0 && px < 128 && py > 0 && py < 128) output[written++] = new(px, py);
            ax = unchecked(ax - nx * 4); ay = unchecked(ay - ny * 4);
        }
        return written;

        int Offset(int tile, int rawFocus) => unchecked(
            (tile - (rawFocus >> 12) + 64 / step) * step - ((rawFocus % 4096) >> (12 - zoom.Level)) - 64);
    }
}
