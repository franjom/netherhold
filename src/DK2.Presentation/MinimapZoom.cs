// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Presentation;

public readonly record struct MinimapRasterPoint(int X, int Y);

public static class MinimapControlLayout
{
    public static bool IsMagnifier(System.Numerics.Vector2 point) =>
        point.X >= 134.4f && point.X < 185.6f && point.Y >= 710.4f && point.Y < 761.6f;
}

public readonly record struct MinimapZoom
{
    public int Level { get; }
    public bool IsWholeMap => Level == -1;
    public int PixelsPerTile => IsWholeMap ? 0 : 1 << Level;
    public static MinimapZoom WholeMap => new(-1);
    public MinimapZoom(int level)
    {
        if (level < -1 || level > 4) throw new ArgumentOutOfRangeException(nameof(level));
        Level = level;
    }
    public MinimapZoom ZoomIn() => new(Math.Min(Level + 1, 4));
    public MinimapZoom ZoomOut() => new(Math.Max(Level - 1, -1));
    public MinimapRasterPoint RasterOrigin(FpPosition3 focus)
    {
        if (IsWholeMap) throw new InvalidOperationException("The whole map uses the fitted map bounds.");
        return new(unchecked(0x400000 - (focus.X.Raw << (Level + 4))) >> 16,
            unchecked(0x400000 - (focus.Y.Raw << (Level + 4))) >> 16);
    }
    public MinimapRasterPoint Project(FpPosition3 position, FpPosition3 focus)
    {
        if (IsWholeMap) throw new InvalidOperationException("The whole map uses the fitted map bounds.");
        return new(64 + (position.X.Raw >> (12 - Level)) - (focus.X.Raw >> (12 - Level)),
            64 + (position.Y.Raw >> (12 - Level)) - (focus.Y.Raw >> (12 - Level)));
    }
}
