// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using DK2.Core;

namespace DK2.Presentation;

/// <summary>The native temporary Sight mask, independent of persistent tile discovery.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/keeper-spell-sight.md")]
public readonly record struct SightRegion
{
    public SightRegion(GameTick start, TilePos position, int durationTicks, int radiusTiles, int width, int height)
    {
        if (durationTicks <= 16) throw new ArgumentOutOfRangeException(nameof(durationTicks));
        Start = start;
        DurationTicks = durationTicks;
        int radius = Math.Max(0, Math.Min(7, radiusTiles));
        Lower = new TilePos(Math.Max(0, Math.Min(width - 1, position.X - radius)),
            Math.Max(0, Math.Min(height - 1, position.Y - radius)));
        Upper = new TilePos(Math.Max(0, Math.Min(width - 1, position.X + radius)),
            Math.Max(0, Math.Min(height - 1, position.Y + radius)));
    }

    public GameTick Start { get; }
    public int DurationTicks { get; }
    public TilePos Lower { get; }
    public TilePos Upper { get; }

    public bool IsVisible(TilePos position, GameTick now) => Alpha(position, now) != byte.MaxValue;

    public byte FogOpacity(TilePos position, GameTick now)
    {
        byte alpha = Alpha(position, now);
        // The cache scalar is min(alpha + 64, 255) / 256, packed as floor(255 * scalar).
        return alpha == byte.MaxValue ? (byte)0 : (byte)(Math.Min(alpha + 64, 255) - 1);
    }

    public byte Alpha(TilePos position, GameTick now)
    {
        long elapsed = now.Value - Start.Value;
        // Nullable SightRegion represents absence; simulation tick zero is a valid start.
        if (elapsed < 0 || elapsed > DurationTicks
            || position.X < Lower.X || position.X >= Upper.X || position.Y < Lower.Y || position.Y >= Upper.Y)
            return byte.MaxValue;
        int plateau = (DurationTicks - 16) / 2;
        int phase = (int)Math.Min(plateau, Math.Min(elapsed, DurationTicks - elapsed));
        int reach = phase * 1280 / plateau;
        if (reach == 0) return byte.MaxValue;
        int dx = (position.X - (Lower.X + Upper.X) / 2) * 256;
        int dy = (position.Y - (Lower.Y + Upper.Y) / 2) * 256;
        int distance = IntegerMath.SquareRoot(unchecked((uint)(dx * dx + dy * dy)));
        return (byte)Math.Min(255, Math.Max(0, distance - reach + 512) / 2);
    }
}
