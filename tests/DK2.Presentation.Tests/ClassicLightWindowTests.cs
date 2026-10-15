// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Presentation.Tests;

public class ClassicLightWindowTests
{
    [Fact]
    public void SourcesWithinEachCellKeepHeadToNextOrderWhenSlotsOverflow()
    {
        var cache = new PreparedCache();
        var tile = new TilePos(10, 10);
        cache.Cells[tile] = new(
            Enumerable.Range(1, 13).Select(id => Source(id, tile)).ToArray(),
            Enumerable.Range(21, 14).Select(id => Source(id, tile)).ToArray(), new(1));
        var table = new ClassicLightTable(tile);
        new ClassicLightWindow().Gather(table, cache, new ClassicLightFrame(1));
        Assert.Equal(Enumerable.Range(1, 12).Concat(Enumerable.Range(21, 12)), Ids(table));
    }

    [Fact]
    public void StaticAndDynamicOverflowUseDifferentEncounterOrders()
    {
        var cache = new PreparedCache();
        Fill(cache, new TilePos(10, 10), width: 7);
        var table = new ClassicLightTable(new TilePos(10, 10));
        new ClassicLightWindow().Gather(table, cache, new ClassicLightFrame(1));

        Assert.Equal(12, table.StaticCount);
        Assert.Equal(24, table.Count);
        Assert.Equal(Enumerable.Range(1, 12), Ids(table).Take(12));
        Assert.Equal(new[] { 101, 108, 115, 122, 129, 136, 143, 102, 109, 116, 123, 130 }, Ids(table).Skip(12));
        Assert.Equal(new TilePos(7, 7), cache.Reads[0]);
        Assert.Equal(new TilePos(13, 13), cache.Reads[^1]);
        Assert.Equal(49, cache.Reads.Count);
    }

    [Fact]
    public void ConsecutiveTargetRotatesPhysicalColumnsBeforeSelectingTies()
    {
        var cache = new PreparedCache();
        Fill(cache, new TilePos(10, 10), width: 8);
        var window = new ClassicLightWindow();
        window.Gather(new ClassicLightTable(new TilePos(10, 10)), cache, new ClassicLightFrame(1));
        var next = new ClassicLightTable(new TilePos(11, 10));
        cache.Reads.Clear();
        window.Gather(next, cache, new ClassicLightFrame(1));

        Assert.Equal(new[] { 8, 2, 3, 4, 5, 6, 7, 16, 10, 11, 12, 13 }, Ids(next).Take(12));
        Assert.Equal(new[] { 108, 116, 124, 132, 140, 148, 156, 102, 110, 118, 126, 134 }, Ids(next).Skip(12));
        Assert.Equal(Enumerable.Range(7, 7).Select(y => new TilePos(14, y)), cache.Reads);
    }

    [Fact]
    public void RingWrapsAfterSevenStepsAndContinuesRotating()
    {
        var cache = new PreparedCache();
        Fill(cache, new TilePos(10, 10), width: 15, sourcePosition: new TilePos(18, 10));
        var window = new ClassicLightWindow();
        ClassicLightTable? table = null;
        for (int x = 10; x <= 18; x++)
        {
            cache.Reads.Clear();
            table = new ClassicLightTable(new TilePos(x, 10));
            window.Gather(table, cache, new ClassicLightFrame(1));
            if (x == 17)
            {
                Assert.Equal(Enumerable.Range(7, 7).Select(y => new TilePos(20, y)), cache.Reads);
                Assert.Equal(new[] { 8, 9, 10, 11, 12, 13, 14, 23, 24, 25, 26, 27 }, Ids(table).Take(12));
            }
        }
        Assert.Equal(Enumerable.Range(7, 7).Select(y => new TilePos(21, y)), cache.Reads);
        Assert.Equal(new[] { 15, 9, 10, 11, 12, 13, 14, 30, 24, 25, 26, 27 }, Ids(table!).Take(12));
    }

    [Theory]
    [InlineData(10, 10)]
    [InlineData(11, 10)]
    [InlineData(9, 10)]
    [InlineData(13, 10)]
    [InlineData(12, 11)]
    public void NonconsecutiveTargetsResetToTheirOwnLeftEdge(int x, int y)
    {
        var cache = new PreparedCache();
        var window = new ClassicLightWindow();
        window.Gather(new ClassicLightTable(new TilePos(10, 10)), cache, new ClassicLightFrame(1));
        window.Gather(new ClassicLightTable(new TilePos(11, 10)), cache, new ClassicLightFrame(1));
        cache.Reads.Clear();
        window.Gather(new ClassicLightTable(new TilePos(x, y)), cache, new ClassicLightFrame(1));
        Assert.Equal(Enumerable.Range(x - 3, 7), cache.Reads.Take(7).Select(p => p.X));
        Assert.All(cache.Reads.Take(7), p => Assert.Equal(y - 3, p.Y));
    }

    [Fact]
    public void CallerSuppliesPreparedBorderAndBuildStampsWhileDynamicAlwaysRefreshes()
    {
        var cache = new PreparedCache();
        var tile = new TilePos(0, 0);
        cache.Cells[new TilePos(-3, -3)] = new(new[] { Source(1, tile) }, new[] { Source(2, tile) }, new(1));
        var table = new ClassicLightTable(tile);
        var window = new ClassicLightWindow();
        window.Gather(table, cache, new ClassicLightFrame(1));
        Assert.Equal(new[] { 1, 2 }, Ids(table));
        Assert.Contains(new TilePos(-3, -3), cache.Reads);
        Assert.Equal(49, cache.Reads.Count);

        cache.Cells.Clear();
        cache.Cells[new TilePos(3, 3)] = new(new[] { Source(3, tile) }, new[] { Source(4, tile) }, new(1));
        window.Gather(table, cache, new ClassicLightFrame(2));
        Assert.Equal(new[] { 1, 4 }, Ids(table));
        cache.Cells[new TilePos(3, 3)] = cache.Cells[new TilePos(3, 3)] with { BuildFrame = new(3) };
        window.Gather(table, cache, new ClassicLightFrame(3));
        Assert.Equal(new[] { 3, 4 }, Ids(table));
        cache.Cells.Clear();
        window.Gather(table, cache, new ClassicLightFrame(4));
        Assert.Equal(new[] { 3 }, Ids(table));
        cache.Cells[new TilePos(-3, -3)] = new(default, default, new(5));
        window.Gather(table, cache, new ClassicLightFrame(5));
        Assert.Empty(Ids(table));
    }

    [Fact]
    public void ResetStartsANewVisibleEnumerationWithoutRollingAcrossItsBoundary()
    {
        var cache = new PreparedCache();
        var window = new ClassicLightWindow();
        window.Gather(new ClassicLightTable(new TilePos(10, 10)), cache, new ClassicLightFrame(1));
        cache.Reads.Clear();
        window.Reset();
        window.Gather(new ClassicLightTable(new TilePos(11, 10)), cache, new ClassicLightFrame(1));
        Assert.Equal(Enumerable.Range(8, 7), cache.Reads.Take(7).Select(p => p.X));
    }

    [Fact]
    public void InvalidArgumentsDoNotAdvanceWindowOrChangeTable()
    {
        var cache = new PreparedCache();
        var window = new ClassicLightWindow();
        var table = new ClassicLightTable(new TilePos(10, 10));
        table.RebuildStatic(new[] { Source(1, table.Tile) });
        Assert.Throws<ArgumentNullException>(() => window.Gather(null!, cache, new ClassicLightFrame(1)));
        Assert.Throws<ArgumentNullException>(() => window.Gather(table, null!, new ClassicLightFrame(1)));
        Assert.Empty(cache.Reads);
        Assert.Equal(new[] { 1 }, Ids(table));
        window.Gather(table, cache, new ClassicLightFrame(1));
        Assert.Equal(new TilePos(7, 7), cache.Reads[0]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WarmGatherDoesNotAllocate(bool refreshStatic)
    {
        var cache = new PreparedCache { TrackReads = false };
        Fill(cache, new TilePos(10, 10), width: 7);
        var window = new ClassicLightWindow();
        var table = new ClassicLightTable(new TilePos(10, 10));
        ClassicLightCacheCell cell = cache.Cells[table.Tile];
        for (uint i = 1; i <= 100; i++)
        {
            if (refreshStatic) cache.Cells[table.Tile] = cell with { BuildFrame = new(i) };
            window.Gather(table, cache, new ClassicLightFrame(i));
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (uint i = 101; i <= 200; i++)
        {
            if (refreshStatic) cache.Cells[table.Tile] = cell with { BuildFrame = new(i) };
            window.Gather(table, cache, new ClassicLightFrame(i));
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(new ClassicLightFrame(refreshStatic ? 200u : 1u), table.StaticFrame);
    }

    private static void Fill(PreparedCache cache, TilePos target, int width, TilePos? sourcePosition = null)
    {
        for (int y = 0; y < 7; y++)
        for (int x = 0; x < width; x++)
        {
            int id = y * width + x + 1;
            cache.Cells[new TilePos(target.X - 3 + x, target.Y - 3 + y)] =
                new(new[] { Source(id, sourcePosition ?? target) }, new[] { Source(100 + id, sourcePosition ?? target) }, new(1));
        }
    }

    private static ClassicPointLight Source(int identity, TilePos tile)
        => ClassicPointLight.FromTerrain(tile, new Fp32(0), new TerrainLightColour(identity, 0, 0));

    private static int[] Ids(ClassicLightTable table)
        => table.Lights.ToArray().Select(l => (int)MathF.Round(l.Colour.X * 255)).ToArray();

    private sealed class PreparedCache : IClassicLightCache
    {
        public readonly Dictionary<TilePos, ClassicLightCacheCell> Cells = new();
        public readonly List<TilePos> Reads = new();
        public bool TrackReads = true;

        public ClassicLightCacheCell GetCell(TilePos tile)
        {
            if (TrackReads) Reads.Add(tile);
            return Cells.TryGetValue(tile, out ClassicLightCacheCell cell) ? cell : default;
        }
    }
}
