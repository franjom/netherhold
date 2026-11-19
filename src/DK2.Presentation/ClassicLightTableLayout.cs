// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using DK2.Core;

namespace DK2.Presentation;

/// <summary>Where a tile's light table sits in a float texture array a shader reads with <c>texelFetch</c> (Godot's
/// <c>WorldScene</c>): a layer per row of tiles, <see cref="TexelsPerTile"/> texels a tile (the count and fill weight, then
/// two a source), every <see cref="TilesPerTextureRow"/> tiles wrapped onto another texel row so a layer stays within the
/// <see cref="MaximumTextureWidth"/> texels Vulkan guarantees. Engine plumbing, kept here so its index maths has tests.</summary>
public static class ClassicLightTableLayout
{
    public const int TexelsPerTile = 1 + 2 * ClassicLightTable.MaximumSources;
    public const int MaximumTextureWidth = 4096;
    public const int TilesPerTextureRow = MaximumTextureWidth / TexelsPerTile;

    /// <summary>A layer's width in texels for a map <paramref name="mapWidth"/> tiles wide.</summary>
    public static int LayerWidth(int mapWidth) => Math.Min(Positive(mapWidth), TilesPerTextureRow) * TexelsPerTile;

    /// <summary>A layer's height in texel rows for a map <paramref name="mapWidth"/> tiles wide.</summary>
    public static int LayerHeight(int mapWidth) => (Positive(mapWidth) + TilesPerTextureRow - 1) / TilesPerTextureRow;

    /// <summary>The texel <paramref name="texel"/> of <paramref name="tile"/>'s table: its column, its row within the layer,
    /// and the layer (the tile's row).</summary>
    public static (int X, int Row, int Layer) Texel(TilePos tile, int texel)
    {
        if ((uint)texel >= TexelsPerTile) throw new ArgumentOutOfRangeException(nameof(texel));
        if (tile.X < 0 || tile.Y < 0) throw new ArgumentOutOfRangeException(nameof(tile));
        return ((tile.X % TilesPerTextureRow * TexelsPerTile) + texel, tile.X / TilesPerTextureRow, tile.Y);
    }

    /// <summary>The first float of that texel in an RGBA float array holding the layers one after another.</summary>
    public static int FloatIndex(TilePos tile, int texel, int mapWidth)
    {
        (int x, int row, int layer) = Texel(tile, texel);
        int width = LayerWidth(mapWidth);
        return (((layer * LayerHeight(mapWidth)) + row) * width + x) * 4;
    }

    private static int Positive(int width) => width > 0 ? width : throw new ArgumentOutOfRangeException(nameof(width));
}
