// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Presentation.Tests;

public class ClassicLightInvalidationTests
{
    private static readonly TilePos First = new(10, 10), Next = new(11, 10);

    [Fact]
    public void FreshWindowRefreshesOnlyForStrictlyNewerSourceStamp()
    {
        ClassicLightTable table = Seed(First, frame: 4);
        var cache = new Cache();
        var window = new ClassicLightWindow();
        cache.Cells[First] = Cell(1, First, stamp: 4);
        window.Gather(table, cache, new ClassicLightFrame(5));
#pragma warning disable IDE0300 // Preserve the original array construction or overload.
        Assert.Equal(new[] { 9 }, Ids(table));
#pragma warning restore IDE0300
        Assert.Equal(new ClassicLightFrame(4), table.StaticFrame);
        cache.Cells[First] = Cell(2, First, stamp: 5);
        window.Gather(table, cache, new ClassicLightFrame(6));
#pragma warning disable IDE0300 // Preserve the original array construction or overload.
        Assert.Equal(new[] { 2 }, Ids(table));
#pragma warning restore IDE0300
        Assert.Equal(new ClassicLightFrame(6), table.StaticFrame);
    }

    [Fact]
    public void DynamicRefreshDoesNotRestampStaticAndNewEmptyCellRemovesOldStatic()
    {
        ClassicLightTable table = Seed(First, frame: 4);
        var cache = new Cache();
        var window = new ClassicLightWindow();
        cache.Cells[First] = new(new[] { Source(1, First) }, new[] { Source(2, First) }, new(4));
        window.Gather(table, cache, new ClassicLightFrame(9));
#pragma warning disable IDE0300 // Preserve the original array construction or overload.
        Assert.Equal(new[] { 9, 2 }, Ids(table));
#pragma warning restore IDE0300
        Assert.Equal(new ClassicLightFrame(4), table.StaticFrame);
        cache.Cells[First] = new(default, default, new(5));
        window.Gather(table, cache, new ClassicLightFrame(10));
        Assert.Empty(Ids(table));
        Assert.Equal(new ClassicLightFrame(10), table.StaticFrame);
    }

    [Fact]
    public void RetainedDirtyBitRebuildsNextTableEvenIfItsOwnStampIsNewer()
    {
        var cache = new Cache();
        cache.Cells[new TilePos(8, 7)] = Cell(1, First, stamp: 1);
        var window = new ClassicLightWindow();
        window.Gather(new ClassicLightTable(First), cache, new ClassicLightFrame(2));
        ClassicLightTable next = Seed(Next, frame: 100);
        window.Gather(next, cache, new ClassicLightFrame(101));
#pragma warning disable IDE0300 // Preserve the original array construction or overload.
        Assert.Equal(new[] { 1 }, Ids(next));
#pragma warning restore IDE0300
        Assert.Equal(new ClassicLightFrame(101), next.StaticFrame);
    }

    [Fact]
    public void RetainedCleanBitIsNotComparedAgainAgainstNextTargetsOlderStamp()
    {
        var cache = new Cache();
        cache.Cells[new TilePos(8, 7)] = Cell(1, First, stamp: 5);
        var window = new ClassicLightWindow();
        window.Gather(Seed(First, frame: 10), cache, new ClassicLightFrame(11));
        ClassicLightTable next = Seed(Next, frame: 1);
        window.Gather(next, cache, new ClassicLightFrame(12));
#pragma warning disable IDE0300 // Preserve the original array construction or overload.
        Assert.Equal(new[] { 9 }, Ids(next));
#pragma warning restore IDE0300
        Assert.Equal(new ClassicLightFrame(1), next.StaticFrame);
    }

    [Fact]
    public void EnteringCleanColumnClearsTheLastDirtyBit()
    {
        var cache = new Cache();
        cache.Cells[new TilePos(7, 7)] = Cell(1, First, stamp: 1);
        var window = new ClassicLightWindow();
        window.Gather(new ClassicLightTable(First), cache, new ClassicLightFrame(2));
        ClassicLightTable next = Seed(Next, frame: 100);
        window.Gather(next, cache, new ClassicLightFrame(101));
#pragma warning disable IDE0300 // Preserve the original array construction or overload.
        Assert.Equal(new[] { 9 }, Ids(next));
#pragma warning restore IDE0300
        Assert.Equal(new ClassicLightFrame(100), next.StaticFrame);
    }

    [Fact]
    public void RollingRetainsListHeadsAndOnlyReadsEnteringCellsUntilReset()
    {
        var cache = new Cache();
        TilePos retained = new(8, 7);
        cache.Cells[retained] = Cell(1, First, stamp: 1);
        var window = new ClassicLightWindow();
        window.Gather(new ClassicLightTable(First), cache, new ClassicLightFrame(2));
        cache.Cells[retained] = Cell(2, First, stamp: 99);
        cache.Reads.Clear();
        var next = new ClassicLightTable(Next);
        window.Gather(next, cache, new ClassicLightFrame(3));
#pragma warning disable IDE0300 // Preserve the original array construction or overload.
        Assert.Equal(new[] { 1 }, Ids(next));
#pragma warning restore IDE0300
        Assert.Equal(Enumerable.Range(7, 7).Select(y => new TilePos(14, y)), cache.Reads);
        cache.Reads.Clear();
        window.Reset();
        window.Gather(next, cache, new ClassicLightFrame(100));
#pragma warning disable IDE0300 // Preserve the original array construction or overload.
        Assert.Equal(new[] { 2 }, Ids(next));
#pragma warning restore IDE0300
        Assert.Equal(49, cache.Reads.Count);
    }

    [Theory]
    [InlineData(0x80000000u, 0x7FFFFFFFu, true)]
    [InlineData(0u, 0xFFFFFFFFu, false)]
    [InlineData(0xFFFFFFFFu, 0u, true)]
    public void StampComparisonIsUnsignedWithoutWrapCorrection(uint sourceFrame, uint targetFrame, bool refresh)
    {
        var cache = new Cache();
        cache.Cells[First] = Cell(1, First, sourceFrame);
        ClassicLightTable table = Seed(First, targetFrame);
        new ClassicLightWindow().Gather(table, cache, new ClassicLightFrame(17));
#pragma warning disable IDE0300 // Preserve the original array construction or overload.
        Assert.Equal(new[] { refresh ? 1 : 9 }, Ids(table));
#pragma warning restore IDE0300
        Assert.Equal(new ClassicLightFrame(refresh ? 17u : targetFrame), table.StaticFrame);
    }

    [Fact]
    public void ManuallyReplacingStaticSourcesInvalidatesTheGatheredStamp()
    {
        ClassicLightTable table = Seed(First, frame: 10);
#pragma warning disable IDE0300 // Preserve the original array construction or overload.
        table.RebuildStatic(new[] { Source(3, First) });
#pragma warning restore IDE0300
        Assert.Equal(default, table.StaticFrame);
        var cache = new Cache();
        cache.Cells[First] = Cell(1, First, stamp: 2);
        new ClassicLightWindow().Gather(table, cache, new ClassicLightFrame(11));
#pragma warning disable IDE0300 // Preserve the original array construction or overload.
        Assert.Equal(new[] { 1 }, Ids(table));
#pragma warning restore IDE0300
    }

    private static ClassicLightTable Seed(TilePos tile, uint frame)
    {
        var table = new ClassicLightTable(tile);
        var cache = new Cache();
        cache.Cells[tile] = Cell(9, tile, stamp: 1);
        new ClassicLightWindow().Gather(table, cache, new ClassicLightFrame(frame));
        return table;
    }

    private static ClassicLightCacheCell Cell(int id, TilePos source, uint stamp)
    {
        return new(new[] { Source(id, source) }, default, new(stamp));
    }

    private static ClassicPointLight Source(int id, TilePos tile)
    {
        return ClassicPointLight.FromTerrain(tile, new Fp32(0), new TerrainLightColour(id, 0, 0));
    }

    private static int[] Ids(ClassicLightTable table)
    {
        return [.. table.Lights.ToArray().Select(l => (int)MathF.Round(l.Colour.X * 255))];
    }

    private sealed class Cache : IClassicLightCache
    {
        public readonly Dictionary<TilePos, ClassicLightCacheCell> Cells = new();
        public readonly List<TilePos> Reads = new();
        public ClassicLightCacheCell GetCell(TilePos tile)
        {
            Reads.Add(tile);
            return Cells.TryGetValue(tile, out ClassicLightCacheCell cell) ? cell : default;
        }
    }
}
