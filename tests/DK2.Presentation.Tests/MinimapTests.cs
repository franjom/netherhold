// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Presentation.Tests;

public sealed class MinimapTests
{
    private static uint[] SourcePixels() => Enumerable.Range(0, 37)
        .Select(i => 0x12010200u + (uint)i).ToArray();

    [Fact]
    public void PaletteMapsNativeBanksAndForcesOpaquePixels()
    {
        uint[] source = SourcePixels();
        var palette = new MinimapPalette(source);
        Assert.Equal(0x225, palette.Colours.Length);
        Assert.Equal(0xFF010200u, palette.Colours[0]);
        Assert.Equal(0xFFFFFFFFu, palette.Colours[1]);
        Assert.Equal(0xFF010201u, palette.Colours[2]);
        Assert.Equal(0xFF01020Fu, palette.Colours[16]);
        Assert.Equal(0xFFFFFFFFu, palette.Colours[17]);
        Assert.Equal(0xFF010210u, palette.Colours[18]);
        Assert.Equal(0xFF010216u, palette.Colours[24]);
        Assert.Equal(0xFF010217u, palette.Colours[27]);
        Assert.Equal(0xFF01021Du, palette.Colours[33]);
        Assert.Equal(0xFF01021Eu, palette.Colours[36]);
        Assert.Equal(0xFF010224u, palette.Colours[42]);
        Assert.Equal(0xFFFFFFFFu, palette.Colours[548]);
        source[0] = 0;
        Assert.Equal(0xFF010200u, palette.Colours[0]);
    }

    [Fact]
    public void NeutralCycleCopiesCurrentBanksIncludingTheSelfCopyPhase()
    {
        var palette = new MinimapPalette(SourcePixels());
        PlayerId neutral = PlayerIds.Neutral;
        Assert.True(palette.AdvanceNeutralCycle(GameTick.Zero, neutral));
        foreach (int bank in new[] { 17, 26, 35 })
            Assert.Equal(0xFFFFFFFFu, palette.Colours[bank + neutral.Value]);
        Assert.False(palette.AdvanceNeutralCycle(GameTick.Zero, neutral));
        Assert.True(palette.AdvanceNeutralCycle(new GameTick(1), neutral));
        uint colour = palette.Colours[19];
        Assert.Equal(0xFF010210u, colour);
        Assert.False(palette.AdvanceNeutralCycle(new GameTick(2), neutral));
        Assert.Equal(colour, palette.Colours[19]);
        Assert.True(palette.AdvanceNeutralCycle(new GameTick(3), neutral));
        Assert.Equal(0xFF010212u, palette.Colours[19]);
        Assert.Equal(0xFF010219u, palette.Colours[28]);
        Assert.Equal(0xFF010220u, palette.Colours[37]);
        Assert.Equal(0xFF010216u, palette.Colours[24]);
    }

    [Fact]
    public void GridStartsUnknownAndPublishesOneLatestChangePerTile()
    {
        var grid = new MinimapClassGrid(width: 3, height: 2);
        Assert.Equal(6, grid.Changes.Length);
        Assert.All(grid.Cells.ToArray(), c => Assert.Equal(MinimapColourClass.Unknown, c));
        grid.ClearChanges();
        Assert.False(grid.Set(new TilePos(1, 1), MinimapColourClass.Unknown));
        Assert.Empty(grid.Changes.ToArray());
        Assert.True(grid.Set(new TilePos(1, 1), new MinimapColourClass(20)));
        Assert.True(grid.Set(new TilePos(0, 0), MinimapColourClass.Tagged));
        Assert.True(grid.Set(new TilePos(1, 1), MinimapColourClass.Unknown));
        Assert.Equal(2, grid.Changes.Length);
        Assert.Equal(new MinimapTileChange(new TilePos(1, 1), MinimapColourClass.Unknown), grid.Changes[0]);
        Assert.Equal(new MinimapTileChange(new TilePos(0, 0), MinimapColourClass.Tagged), grid.Changes[1]);
        Assert.Equal(MinimapColourClass.Tagged, grid[new TilePos(0, 0)]);
        grid.ClearChanges();
        Assert.Empty(grid.Changes.ToArray());
        Assert.True(grid.Set(new TilePos(1, 1), new MinimapColourClass(39)));
        Assert.Single(grid.Changes.ToArray());
        Assert.Equal((ushort)39, grid[new TilePos(1, 1)].Index);
    }

    [Fact]
    public void InvalidGridAddressesAndPaletteInputsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new MinimapPalette(new uint[36]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MinimapColourClass(549));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MinimapClassGrid(width: 0, height: 2));
        var grid = new MinimapClassGrid(width: 3, height: 2);
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.Set(new TilePos(3, 0), default));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.Set(new TilePos(-1, 1), default));
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = grid[new TilePos(0, 2)]; });
    }
}
