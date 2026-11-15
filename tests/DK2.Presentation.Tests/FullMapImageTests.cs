// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Presentation.Tests;

/// <summary>The full map's image from the minimap's class grid, as Unity's DK2FullMap shader colours it.</summary>
public sealed class FullMapImageTests
{
    private static MinimapPalette Palette() =>
        new(Enumerable.Range(0, 37).Select(i => 0x00102030u + (uint)i).ToArray());

    private static byte[] Pixel(FullMapImage image, int x, int y) => image.Pixels.Slice((y * image.Width + x) * 4, 4).ToArray();

    [Fact]
    public void EachTileIsItsClassColourOpaqueAndTheUnknownClassIsBlack()
    {
        var grid = new MinimapClassGrid(3, 2);
        MinimapPalette palette = Palette();
        grid.Set(new TilePos(1, 0), new MinimapColourClass(4));
        grid.Set(new TilePos(2, 1), new MinimapColourClass(0));
        var image = new FullMapImage(3, 2);
        Assert.True(image.Update(grid, palette));
        Assert.Equal(new byte[] { 0, 0, 0, 0xFF }, Pixel(image, 0, 0));        // unknown (class 1): black, not its palette white
        uint rock = palette.Colours[4];
        Assert.Equal(new[] { (byte)(rock >> 16), (byte)(rock >> 8), (byte)rock, (byte)0xFF }, Pixel(image, 1, 0));
        uint floor = palette.Colours[0];
        Assert.Equal(new[] { (byte)(floor >> 16), (byte)(floor >> 8), (byte)floor, (byte)0xFF }, Pixel(image, 2, 1)); // rows top-down
        Assert.False(image.Update(grid, palette));                              // nothing changed: no upload
        grid.Set(new TilePos(0, 1), new MinimapColourClass(9));
        Assert.True(image.Update(grid, palette));
    }

    [Fact]
    public void TheNeutralCycleRecoloursItsTiles()
    {
        var grid = new MinimapClassGrid(1, 1);
        MinimapPalette palette = Palette();
        PlayerId neutral = PlayerIds.Neutral;
        grid.Set(new TilePos(0, 0), new MinimapColourClass((ushort)(26 + neutral.Value)));
        var image = new FullMapImage(1, 1);
        palette.AdvanceNeutralCycle(new GameTick(1), neutral);
        image.Update(grid, palette);
        uint before = palette.Colours[26 + neutral.Value];
        Assert.True(palette.AdvanceNeutralCycle(new GameTick(3), neutral));
        Assert.NotEqual(before, palette.Colours[26 + neutral.Value]);
        Assert.True(image.Update(grid, palette));                               // the grid did not change; the palette did
        uint colour = palette.Colours[26 + neutral.Value];
        Assert.Equal(new[] { (byte)(colour >> 16), (byte)(colour >> 8), (byte)colour, (byte)0xFF }, Pixel(image, 0, 0));
    }

    [Fact]
    public void AGridOfAnotherSizeIsRefused()
    {
        var image = new FullMapImage(2, 2);
        Assert.Throws<ArgumentException>(() => image.Update(new MinimapClassGrid(2, 3), Palette()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FullMapImage(0, 1));
    }
}
