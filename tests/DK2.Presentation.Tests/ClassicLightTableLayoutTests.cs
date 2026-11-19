// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Presentation.Tests;

public class ClassicLightTableLayoutTests
{
    [Fact]
    public void ATileTakesItsCountThenTwoTexelsASourceAndAWrappedRowFitsVulkansGuarantee()
    {
        Assert.Equal(49, ClassicLightTableLayout.TexelsPerTile);
        Assert.Equal(83, ClassicLightTableLayout.TilesPerTextureRow);
        Assert.True(ClassicLightTableLayout.TilesPerTextureRow * ClassicLightTableLayout.TexelsPerTile <= 4096);
        Assert.True((ClassicLightTableLayout.TilesPerTextureRow + 1) * ClassicLightTableLayout.TexelsPerTile > 4096);
    }

    [Theory]
    [InlineData(82, 82 * 49, 1)]
    [InlineData(83, 83 * 49, 1)]
    [InlineData(84, 83 * 49, 2)]
    [InlineData(85, 83 * 49, 2)]
    [InlineData(166, 83 * 49, 2)]
    [InlineData(167, 83 * 49, 3)]
    public void ALayerWrapsEvery83Tiles(int mapWidth, int width, int height)
    {
        Assert.Equal(width, ClassicLightTableLayout.LayerWidth(mapWidth));
        Assert.Equal(height, ClassicLightTableLayout.LayerHeight(mapWidth));
        Assert.True(ClassicLightTableLayout.LayerWidth(mapWidth) <= ClassicLightTableLayout.MaximumTextureWidth);
    }

    [Theory]
    [InlineData(81, 48, 81 * 49 + 48, 0)]
    [InlineData(82, 0, 82 * 49, 0)]
    [InlineData(82, 48, 82 * 49 + 48, 0)]
    [InlineData(83, 0, 0, 1)]
    [InlineData(83, 1, 1, 1)]
    [InlineData(84, 2, 49 + 2, 1)]
    public void ATexelIsItsTilesColumnOnItsWrappedRowInItsRowsLayer(int tileX, int texel, int x, int row)
    {
        Assert.Equal((x, row, 7), ClassicLightTableLayout.Texel(new TilePos(tileX, 7), texel));
    }

    [Theory]
    [InlineData(82)]
    [InlineData(83)]
    [InlineData(84)]
    [InlineData(85)]
    public void EveryTexelOfAMapHasItsOwnFloatsInsideItsLayer(int mapWidth)
    {
        int width = ClassicLightTableLayout.LayerWidth(mapWidth), height = ClassicLightTableLayout.LayerHeight(mapWidth);
        int layerFloats = width * height * 4;
        var seen = new HashSet<int>();
        for (int y = 0; y < 3; y++)
        for (int x = 0; x < mapWidth; x++)
        for (int texel = 0; texel < ClassicLightTableLayout.TexelsPerTile; texel++)
        {
            int index = ClassicLightTableLayout.FloatIndex(new TilePos(x, y), texel, mapWidth);
            Assert.Equal(0, index % 4);
            Assert.Equal(y, index / layerFloats);
            Assert.True(seen.Add(index));
            (int column, int row, int layer) = ClassicLightTableLayout.Texel(new TilePos(x, y), texel);
            Assert.Equal(((layer * height + row) * width + column) * 4, index);
            // A tile's texels follow one another, as LightTables writes a source's eight floats after its count's.
            if (texel > 0) Assert.Equal(ClassicLightTableLayout.FloatIndex(new TilePos(x, y), texel - 1, mapWidth) + 4, index);
        }
    }

    [Fact]
    public void OutOfRangeTexelsAndWidthsAreRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ClassicLightTableLayout.Texel(new TilePos(0, 0), 49));
        Assert.Throws<ArgumentOutOfRangeException>(() => ClassicLightTableLayout.Texel(new TilePos(0, 0), -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ClassicLightTableLayout.Texel(new TilePos(-1, 0), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ClassicLightTableLayout.LayerWidth(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ClassicLightTableLayout.LayerHeight(-3));
    }
}
