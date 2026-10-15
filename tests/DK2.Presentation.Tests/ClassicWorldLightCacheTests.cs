// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Presentation.Tests;

public class ClassicWorldLightCacheTests
{
    private static readonly TilePos Tile = new(5, 5);

    [Fact]
    public void PreparedCellOrdersTerrainBeforeTorchAndStaticTablePersistsAcrossFrames()
    {
        var cache = new ClassicWorldLightCache(12, 12);
        cache.SetTerrain(Tile, Source(1, Tile));
        cache.SetTorch(Tile, Source(2, Tile));
        cache.PrepareFrame(new[] { Tile });
        ClassicLightTable table = cache.VisibleTables[0];
        Assert.Equal(new[] { 1, 2 }, Ids(table));
        Assert.Equal(new ClassicLightFrame(1), table.StaticFrame);
        cache.PrepareFrame(new[] { Tile });
        Assert.Same(table, cache.VisibleTables[0]);
        Assert.Equal(new ClassicLightFrame(2), cache.Frame);
        Assert.Equal(new ClassicLightFrame(1), table.StaticFrame);
        cache.SetTerrain(Tile, Source(3, Tile));
        cache.PrepareFrame(new[] { Tile });
        Assert.Equal(new[] { 3, 2 }, Ids(table));
        Assert.Equal(new ClassicLightFrame(3), table.StaticFrame);
    }

    [Fact]
    public void RemovingLastSourcePublishesANewerEmptyCell()
    {
        var cache = new ClassicWorldLightCache(12, 12);
        cache.SetTorch(Tile, Source(1, Tile));
        cache.PrepareFrame(new[] { Tile });
        cache.SetTorch(Tile, null);
        cache.PrepareFrame(new[] { Tile });
        Assert.Empty(cache.GetCell(Tile).StaticSources.ToArray());
        Assert.Equal(new ClassicLightFrame(2), cache.GetCell(Tile).BuildFrame);
        Assert.Equal(0, cache.VisibleTables[0].Count);
    }

    [Fact]
    public void PreparationIncludesAdjoiningCellsButDoesNotPublishUnpreparedAuthoredSources()
    {
        var cache = new ClassicWorldLightCache(12, 12);
        cache.SetTerrain(new TilePos(8, 5), Source(1, new TilePos(8, 5)));
        cache.SetTerrain(new TilePos(11, 11), Source(2, new TilePos(11, 11)));
        cache.PrepareFrame(new[] { Tile, new TilePos(6, 5) });
        Assert.Equal(0, cache.VisibleTables[0].Count);
        Assert.Equal(new[] { 1 }, Ids(cache.VisibleTables[1]));
        Assert.Single(cache.GetCell(new TilePos(8, 5)).StaticSources.ToArray());
        Assert.Equal(default, cache.GetCell(new TilePos(11, 11)).BuildFrame);
        Assert.Empty(cache.GetCell(new TilePos(11, 11)).StaticSources.ToArray());
        Assert.True(cache.IsVisible(Tile));
        Assert.False(cache.IsVisible(new TilePos(8, 5)));
    }

    [Fact]
    public void OffMapTargetsRemainInEnumerationAndMapBorderSourcesCanLightThem()
    {
        var cache = new ClassicWorldLightCache(3, 3);
        cache.SetTerrain(new TilePos(0, 0), Source(1, new TilePos(0, 0)));
        cache.PrepareFrame(new[] { new TilePos(-1, 0), new TilePos(0, 0) });
        Assert.Equal(new TilePos(-1, 0), cache.VisibleTables[0].Tile);
        Assert.Equal(new[] { 1 }, Ids(cache.VisibleTables[0]));
        Assert.Equal(default, cache.GetCell(new TilePos(-1, 0)));
    }

    [Fact]
    public void ClearRemovesInputsTablesAndFrameBeforeResync()
    {
        var cache = new ClassicWorldLightCache(12, 12);
        cache.SetTerrain(Tile, Source(1, Tile));
        cache.PrepareFrame(new[] { Tile });
        cache.Clear();
        Assert.Equal(default, cache.Frame);
        Assert.Equal(0, cache.VisibleTables.Length);
        Assert.False(cache.IsVisible(Tile));
        cache.PrepareFrame(new[] { Tile });
        Assert.Equal(0, cache.VisibleTables[0].Count);
    }

    [Fact]
    public void InvalidSourcesAndDuplicateTargetsAreRejectedBeforeAdvancingFrame()
    {
        var cache = new ClassicWorldLightCache(12, 12);
        Assert.Throws<ArgumentOutOfRangeException>(() => cache.SetTerrain(new TilePos(-1, 0), null));
        Assert.Throws<ArgumentOutOfRangeException>(() => cache.SetTorch(new TilePos(12, 0), null));
        Assert.Throws<ArgumentException>(() => cache.PrepareFrame(new[] { Tile, Tile }));
        Assert.Equal(default, cache.Frame);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ClassicWorldLightCache(0, 2));
    }

    [Fact]
    public void WarmFramePreparationAndGatherAllocateNothing()
    {
        var cache = new ClassicWorldLightCache(12, 12);
        cache.SetTerrain(Tile, Source(1, Tile));
        TilePos[] visible = { Tile, new(6, 5), new(7, 5) };
        for (int i = 0; i < 100; i++) cache.PrepareFrame(visible);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) cache.PrepareFrame(visible);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static ClassicPointLight Source(int id, TilePos tile)
        => ClassicPointLight.FromTerrain(tile, new Fp32(0), new TerrainLightColour(id, 0, 0));
    private static int[] Ids(ClassicLightTable table)
        => table.Lights.ToArray().Select(l => (int)MathF.Round(l.Colour.X * 255)).ToArray();
}
