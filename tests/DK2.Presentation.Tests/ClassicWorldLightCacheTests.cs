// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Presentation.Tests;

public class ClassicWorldLightCacheTests
{
    private static readonly TilePos Tile = new(5, 5);
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExternallyRebuiltTablesAreGatheredAgain(bool replaceStatic)
    {
        var cache = new ClassicWorldLightCache(12, 12);
        cache.SetTerrain(Tile, Source(1, Tile));
        cache.UpdateHand(Source(2, Tile));
#pragma warning disable IDE0300 // Preserve the original array construction.
        TilePos[] visible = { Tile };
#pragma warning restore IDE0300
        for (int i = 0; i < 3; i++)
        {
            cache.PrepareFrame(visible);
        }

        Assert.True(cache.TablesUnchanged);
        ClassicLightTable table = cache.VisibleTables[0];
        if (replaceStatic)
        {
#pragma warning disable IDE0300 // Preserve the original array construction or overload.
            table.RebuildStatic(new[] { Source(9, Tile) });
#pragma warning restore IDE0300
        }
        else
        {
#pragma warning disable IDE0300 // Preserve the original array construction or overload.
            table.RebuildDynamic(new[] { Source(9, Tile) });
#pragma warning restore IDE0300
        }

        cache.PrepareFrame(visible);
        Assert.False(cache.TablesUnchanged);
#pragma warning disable IDE0300 // Preserve the original array construction or overload.
        Assert.Equal(new[] { 1, 2 }, Ids(table));
#pragma warning restore IDE0300
    }

    [Fact]
    public void SourceIdentityAndTileInsertionOrderRetainEqualDistanceOverflowWinners()
    {
        var cache = new ClassicWorldLightCache(12, 12);
        PortalLightView[] lights = [.. Enumerable.Range(1, 26).Select(id => new PortalLightView(new PortalLightId(id), Source(id, Tile)))];
        TilePos[] visible = [Tile];
        cache.UpdatePortalLights(lights);
        for (int i = 0; i < 3; i++)
        {
            cache.PrepareFrame(visible);
        }

        Assert.True(cache.TablesUnchanged);
        Assert.Equal(Enumerable.Range(1, 24), Ids(cache.VisibleTables[0]));
        Array.Reverse(lights);
        cache.UpdatePortalLights(lights);
        cache.PrepareFrame(visible);
        Assert.True(cache.TablesUnchanged);
        Assert.Equal(Enumerable.Range(1, 24), Ids(cache.VisibleTables[0]));
        cache.UpdatePortalLights(lights.Where(light => light.Id.Value != 1).ToArray());
        cache.PrepareFrame(visible);
        Assert.False(cache.TablesUnchanged);
        Assert.Equal(Enumerable.Range(2, 24), Ids(cache.VisibleTables[0]));
        cache.UpdatePortalLights(lights);
        cache.PrepareFrame(visible);
        Assert.False(cache.TablesUnchanged);
        Assert.Equal(Enumerable.Range(2, 24), Ids(cache.VisibleTables[0]));
    }

    [Fact]
    public void ObjectInterpolationAndPortalPulseUpdateWithoutASimulationTick()
    {
        var cache = new ClassicWorldLightCache(12, 12);
        var descriptor = new KwdLight(0, 0, 0, 8192, 0x3800, 244, 244, 204, 0);
        ObjectView[] objects = [ new ObjectView(1, 86, new PlayerId(3), Tile, default, 0,
            5 * Fp32.One + 2048, 5 * Fp32.One + 2048, Fp32.One, 0, Fp32.One,
            previousPosX: new Fp32(4 * Fp32.One + 2048), light: descriptor) ];
        var portals = new PortalCentreLights(new EffectTime(0));
        portals.Create(Tile);
        TilePos[] visible = [Tile];
        cache.UpdateObjects(objects, 0);
        cache.UpdatePortalLights(portals.Advance(new EffectTime(0)));
        for (int i = 0; i < 3; i++)
        {
            cache.UpdateObjects(objects, 0);
            cache.UpdatePortalLights(portals.Advance(new EffectTime(0)));
            cache.PrepareFrame(visible);
        }
        Assert.True(cache.TablesUnchanged);
        cache.UpdateObjects(objects, .5f);
        cache.PrepareFrame(visible);
        Assert.False(cache.TablesUnchanged);
        Assert.Equal(5f, cache.VisibleTables[0].Lights[0].Position.X);
        cache.PrepareFrame(visible);
        Assert.True(cache.TablesUnchanged);
        cache.UpdatePortalLights(portals.Advance(new EffectTime(51)));
        cache.PrepareFrame(visible);
        Assert.False(cache.TablesUnchanged);
        Assert.Equal(System.Numerics.Vector3.Zero, cache.VisibleTables[0].Lights[1].Colour);
    }

    [Fact]
    public void ReusedFramesMatchFullGathersThroughSourceChangesEvictionAndReentry()
    {
        var cached = new ClassicWorldLightCache(32, 32);
        var full = new ClassicWorldLightCache(32, 32);
        TilePos sentinel = new(31, 31);
        TilePos[][] views = [ [Tile, new TilePos(6, 5), new TilePos(7, 5)],
            [new TilePos(7, 5), Tile, new TilePos(6, 5)],
            [new TilePos(12, 12)], [new TilePos(-1, 0), new TilePos(0, 0)] ];
        for (int frame = 0; frame < 80; frame++)
        {
            foreach (ClassicWorldLightCache cache in new[] { cached, full })
            {
                if (frame == 0 || frame == 25)
                {
                    cache.SetTerrain(Tile, Source(1, Tile));
                }

                if (frame == 15)
                {
                    cache.SetTerrain(Tile, null);
                }

                if (frame == 5 || frame == 30)
                {
                    cache.UpdateHand(Source(2, Tile));
                }

                if (frame == 10)
                {
                    cache.UpdateHand(Source(3, new TilePos(6, 5)));
                }

                if (frame == 35)
                {
                    cache.UpdateHand(null);
                }

                if (frame == 45)
                {
                    cache.Clear();
                }
            }
            // Change an unprepared input twice: final inputs match, but force a full gather.
            full.SetTorch(sentinel, Source(255, sentinel));
            full.SetTorch(sentinel, null);
            TilePos[] visible = views[frame < 20 ? 0 : frame < 25 ? 1 : frame < 40 ? 2 : frame < 50 ? 0 : frame < 60 ? 3 : 0];
            cached.PrepareFrame(visible);
            full.PrepareFrame(visible);
            Assert.False(full.TablesUnchanged);
            Assert.Equal(full.Frame, cached.Frame);
            Assert.Equal(full.VisibleTables.Length, cached.VisibleTables.Length);
            for (int i = 0; i < full.VisibleTables.Length; i++)
            {
                ClassicLightTable expected = full.VisibleTables[i], actual = cached.VisibleTables[i];
                Assert.Equal(expected.Tile, actual.Tile);
                Assert.Equal(expected.StaticFrame, actual.StaticFrame);
                Assert.Equal(expected.StaticCount, actual.StaticCount);
                Assert.Equal(expected.Lights.ToArray(), actual.Lights.ToArray());
            }
            for (int y = 0; y < 20; y++)
            {
                for (int x = 0; x < 20; x++)
            {
                var tile = new TilePos(x, y);
                ClassicLightCacheCell expected = full.GetCell(tile), actual = cached.GetCell(tile);
                Assert.Equal(expected.BuildFrame, actual.BuildFrame);
                Assert.Equal(expected.StaticSources.ToArray(), actual.StaticSources.ToArray());
                Assert.Equal(expected.DynamicSources.ToArray(), actual.DynamicSources.ToArray());
            }
            }
        }
    }

    [Fact]
    public void IdenticalInputsReuseOnlyAfterStaticGatherHasSettledAndStillAdvanceFrames()
    {
        var cache = new ClassicWorldLightCache(12, 12);
        cache.SetTerrain(Tile, Source(1, Tile));
#pragma warning disable IDE0300 // Preserve the original array construction.
        TilePos[] visible = { Tile, new(6, 5) };
#pragma warning restore IDE0300
        cache.PrepareFrame(visible);
        Assert.False(cache.TablesUnchanged);
        cache.PrepareFrame(visible);
        Assert.False(cache.TablesUnchanged);
        cache.SetTerrain(Tile, Source(1, Tile));
        for (int i = 0; i < 4; i++)
        {
            cache.PrepareFrame(visible);
            Assert.True(cache.TablesUnchanged);
            Assert.Equal(new ClassicLightFrame((uint)(i + 3)), cache.Frame);
            Assert.Equal(new ClassicLightFrame(1), cache.GetCell(Tile).BuildFrame);
#pragma warning disable IDE0300 // Preserve the original array construction or overload.
            Assert.Equal(new[] { 1 }, Ids(cache.VisibleTables[0]));
#pragma warning restore IDE0300
        }
    }

    [Fact]
    public void SameTickHandValueAndRemovalInvalidateReuse()
    {
        var cache = new ClassicWorldLightCache(12, 12);
#pragma warning disable IDE0300 // Preserve the original array construction.
        TilePos[] visible = { Tile };
#pragma warning restore IDE0300
        cache.UpdateHand(Source(1, Tile));
        for (int i = 0; i < 3; i++)
        {
            cache.PrepareFrame(visible);
        }

        Assert.True(cache.TablesUnchanged);
        cache.UpdateHand(Source(2, Tile));
        cache.PrepareFrame(visible);
        Assert.False(cache.TablesUnchanged);
#pragma warning disable IDE0300 // Preserve the original array construction or overload.
        Assert.Equal(new[] { 2 }, Ids(cache.VisibleTables[0]));
#pragma warning restore IDE0300
        cache.PrepareFrame(visible);
        Assert.True(cache.TablesUnchanged);
        cache.UpdateHand(null);
        cache.PrepareFrame(visible);
        Assert.False(cache.TablesUnchanged);
        Assert.Empty(Ids(cache.VisibleTables[0]));
    }

    [Fact]
    public void ReuseDoesNotBypassVisibleOrderDuplicateValidationOrClear()
    {
        var cache = new ClassicWorldLightCache(12, 12);
        TilePos next = new(6, 5);
#pragma warning disable IDE0300 // Preserve the original array construction.
        TilePos[] visible = { Tile, next };
#pragma warning restore IDE0300
        for (int i = 0; i < 3; i++)
        {
            cache.PrepareFrame(visible);
        }

        Assert.True(cache.TablesUnchanged);
#pragma warning disable IDE0300 // Preserve the original array construction or overload.
        cache.PrepareFrame(new[] { next, Tile });
#pragma warning restore IDE0300
        Assert.False(cache.TablesUnchanged);
        Assert.Equal(next, cache.VisibleTables[0].Tile);
        ClassicLightFrame frame = cache.Frame;
#pragma warning disable IDE0300 // Preserve the original array construction or overload.
        Assert.Throws<ArgumentException>(() => cache.PrepareFrame(new[] { next, next }));
#pragma warning restore IDE0300
        Assert.Equal(frame, cache.Frame);
        cache.Clear();
        Assert.False(cache.TablesUnchanged);
        cache.PrepareFrame(visible);
        Assert.False(cache.TablesUnchanged);
        Assert.Equal(new ClassicLightFrame(1), cache.Frame);
    }


    [Fact]
    public void PreparedCellOrdersTerrainBeforeTorchAndStaticTablePersistsAcrossFrames()
    {
        var cache = new ClassicWorldLightCache(12, 12);
        cache.SetTerrain(Tile, Source(1, Tile));
        cache.SetTorch(Tile, Source(2, Tile));
#pragma warning disable IDE0300 // Preserve the original array construction or overload.
        cache.PrepareFrame(new[] { Tile });
        ClassicLightTable table = cache.VisibleTables[0];
        Assert.Equal(new[] { 1, 2 }, Ids(table));
        Assert.Equal(new ClassicLightFrame(1), table.StaticFrame);
        cache.PrepareFrame(new[] { Tile });
#pragma warning restore IDE0300
        Assert.Same(table, cache.VisibleTables[0]);
        Assert.Equal(new ClassicLightFrame(2), cache.Frame);
        Assert.Equal(new ClassicLightFrame(1), table.StaticFrame);
        cache.SetTerrain(Tile, Source(3, Tile));
#pragma warning disable IDE0300 // Preserve the original array construction or overload.
        cache.PrepareFrame(new[] { Tile });
        Assert.Equal(new[] { 3, 2 }, Ids(table));
#pragma warning restore IDE0300
        Assert.Equal(new ClassicLightFrame(3), table.StaticFrame);
    }

    /// <summary>0x00576010 frees each entry the last frame did not touch after it is drawn; the
    /// port defers that to the next PrepareFrame, so the old cell is still readable in between.
    /// The tile that returns gets a new cell and table, stamped afresh.</summary>
    [Fact]
    public void AnUntouchedEntryIsFreedDeferredToTheNextFrameAndRebuiltWhenItsTileReturns()
    {
        var cache = new ClassicWorldLightCache(16, 16);
        cache.SetTerrain(Tile, Source(1, Tile));
#pragma warning disable IDE0300 // Preserve the original array construction or overload.
        cache.PrepareFrame(new[] { Tile });
        ClassicLightTable first = cache.VisibleTables[0];
        cache.PrepareFrame(new[] { new TilePos(12, 12) });
        Assert.Equal(new ClassicLightFrame(1), cache.GetCell(Tile).BuildFrame);
        cache.PrepareFrame(new[] { Tile });
#pragma warning restore IDE0300
        // Freed, then taken again from the free tables and rebuilt afresh (its static stamp is this frame).
        Assert.Same(first, cache.VisibleTables[0]);
        Assert.Equal(new ClassicLightFrame(3), cache.GetCell(Tile).BuildFrame);
        Assert.Equal(new ClassicLightFrame(3), cache.VisibleTables[0].StaticFrame);
#pragma warning disable IDE0300 // Preserve the original array construction or overload.
        Assert.Equal(new[] { 1 }, Ids(cache.VisibleTables[0]));
#pragma warning restore IDE0300
    }

    [Fact]
    public void ATilePreparedAsABorderKeepsItsTable()
    {
        var cache = new ClassicWorldLightCache(16, 16);
        cache.SetTerrain(Tile, Source(1, Tile));
#pragma warning disable IDE0300 // Preserve the original array construction or overload.
        cache.PrepareFrame(new[] { Tile });
        ClassicLightTable first = cache.VisibleTables[0];
        cache.PrepareFrame(new[] { new TilePos(4, 5), new TilePos(6, 5) });
        cache.PrepareFrame(new[] { Tile });
#pragma warning restore IDE0300
        Assert.Same(first, cache.VisibleTables[0]);
        Assert.Equal(new ClassicLightFrame(1), first.StaticFrame);
    }

    /// <summary>One way a kept entry goes stale (Level 1's Lord's hall walls stayed dark at a wide
    /// view until the sweep was ported): (5,5) is reached by a rolling step whose six kept columns
    /// are clean for (4,5), refreshed while (5,5) was out of view. Its incoming column x 8,
    /// untouched for a frame, was freed and is rebuilt, so the table sees the light added at
    /// (6,5); a cache that kept every entry left it stale.</summary>
    [Fact]
    public void ARolledTableSeesALightAddedWhileItsTileWasOutOfView_TouchedModelledAsPreparedOrVisible()
    {
        var cache = new ClassicWorldLightCache(16, 16);
        TilePos left = new(4, 5), light = new(6, 5);
#pragma warning disable IDE0300 // Preserve the original array construction or overload.
        cache.PrepareFrame(new[] { left, Tile });
        cache.SetTerrain(light, Source(1, light));
        cache.PrepareFrame(new[] { left });
        Assert.Equal(new[] { 1 }, Ids(cache.VisibleTables[0]));
        cache.PrepareFrame(new[] { left, Tile });
        Assert.Equal(new[] { 1 }, Ids(cache.VisibleTables[1]));
#pragma warning restore IDE0300
    }

    [Fact]
    public void RemovingLastSourcePublishesANewerEmptyCell()
    {
        var cache = new ClassicWorldLightCache(12, 12);
        cache.SetTorch(Tile, Source(1, Tile));
#pragma warning disable IDE0300 // Preserve the original array construction or overload.
        cache.PrepareFrame(new[] { Tile });
        cache.SetTorch(Tile, null);
        cache.PrepareFrame(new[] { Tile });
#pragma warning restore IDE0300
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
#pragma warning disable IDE0300 // Preserve the original array construction or overload.
        cache.PrepareFrame(new[] { Tile, new TilePos(6, 5) });
        Assert.Equal(0, cache.VisibleTables[0].Count);
        Assert.Equal(new[] { 1 }, Ids(cache.VisibleTables[1]));
#pragma warning restore IDE0300
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
#pragma warning disable IDE0300 // Preserve the original array construction or overload.
        cache.PrepareFrame(new[] { new TilePos(-1, 0), new TilePos(0, 0) });
        Assert.Equal(new TilePos(-1, 0), cache.VisibleTables[0].Tile);
        Assert.Equal(new[] { 1 }, Ids(cache.VisibleTables[0]));
#pragma warning restore IDE0300
        Assert.Equal(default, cache.GetCell(new TilePos(-1, 0)));
    }

    [Fact]
    public void ClearRemovesInputsTablesAndFrameBeforeResync()
    {
        var cache = new ClassicWorldLightCache(12, 12);
        cache.SetTerrain(Tile, Source(1, Tile));
#pragma warning disable IDE0300 // Preserve the original array construction or overload.
        cache.PrepareFrame(new[] { Tile });
#pragma warning restore IDE0300
        cache.Clear();
        Assert.Equal(default, cache.Frame);
        Assert.Equal(0, cache.VisibleTables.Length);
        Assert.False(cache.IsVisible(Tile));
#pragma warning disable IDE0300 // Preserve the original array construction or overload.
        cache.PrepareFrame(new[] { Tile });
#pragma warning restore IDE0300
        Assert.Equal(0, cache.VisibleTables[0].Count);
    }

    [Fact]
    public void InvalidSourcesAndDuplicateTargetsAreRejectedBeforeAdvancingFrame()
    {
        var cache = new ClassicWorldLightCache(12, 12);
        Assert.Throws<ArgumentOutOfRangeException>(() => cache.SetTerrain(new TilePos(-1, 0), null));
        Assert.Throws<ArgumentOutOfRangeException>(() => cache.SetTorch(new TilePos(12, 0), null));
#pragma warning disable IDE0300 // Preserve the original array construction or overload.
        Assert.Throws<ArgumentException>(() => cache.PrepareFrame(new[] { Tile, Tile }));
#pragma warning restore IDE0300
        Assert.Equal(default, cache.Frame);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ClassicWorldLightCache(0, 2));
    }

    [Fact]
    public void WarmFramePreparationAndGatherAllocateNothing()
    {
        var cache = new ClassicWorldLightCache(12, 12);
        cache.SetTerrain(Tile, Source(1, Tile));
        TilePos[] visible = [Tile, new(6, 5), new(7, 5)];
        for (int i = 0; i < 100; i++)
        {
            cache.PrepareFrame(visible);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            cache.PrepareFrame(visible);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void AWarmFrameWithEntrancesAndAMovingHandAllocatesNothing()
    {
        var cache = new ClassicWorldLightCache(12, 12);
        var portals = new PortalCentreLights(new EffectTime(0));
        portals.Create(new TilePos(4, 4));
        TilePos[] visible = [Tile, new(6, 5), new(7, 5)];
        for (int frame = 0; frame < 400; frame++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            cache.UpdatePortalLights(portals.Advance(new EffectTime(frame * 33)));
            cache.UpdateHand(ClassicPointLight.FromHand(5 + (frame % 20) * 0.1f, 5.5f, false, true, 12, 12));
            cache.PrepareFrame(visible);
            if (frame >= 200)
            {
                Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            }
        }
    }

    [Fact]
    public void AScrollingViewReusesFreedTablesAndAllocatesNothing()
    {
        var cache = new ClassicWorldLightCache(40, 40);
        for (int y = 0; y < 40; y += 2)
        {
            for (int x = 0; x < 40; x += 3)
            {
                cache.SetTerrain(new TilePos(x, y), Source(1 + (x + y) % 200, new TilePos(x, y)));
            }
        }
        // A 12x12 view sweeping 16 tiles east and back, each frame's tiles prepared once.
        var views = new TilePos[32][];
        for (int step = 0; step < views.Length; step++)
        {
            int left = step < 16 ? step : 31 - step;
            views[step] = new TilePos[144];
            for (int i = 0; i < 144; i++)
            {
                views[step][i] = new TilePos(4 + left + i % 12, 10 + i / 12);
            }
        }
        for (int frame = 0; frame < views.Length * 2; frame++)
        {
            cache.PrepareFrame(views[frame % views.Length]);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int frame = 0; frame < views.Length * 3; frame++)
        {
            cache.PrepareFrame(views[frame % views.Length]);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void AClearKeepsItsTablesForTheNextFrames()
    {
        var cache = new ClassicWorldLightCache(12, 12);
        cache.SetTerrain(Tile, Source(1, Tile));
#pragma warning disable IDE0300 // Preserve the original array construction.
        TilePos[] visible = { Tile, new(6, 5), new(7, 5) };
#pragma warning restore IDE0300
        for (int i = 0; i < 10; i++)
        {
            cache.PrepareFrame(visible);
        }

        cache.Clear();
        cache.SetTerrain(Tile, Source(1, Tile));
        long before = GC.GetAllocatedBytesForCurrentThread();
        cache.PrepareFrame(visible);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
#pragma warning disable IDE0300 // Preserve the original array construction or overload.
        Assert.Equal(new[] { 1 }, Ids(cache.VisibleTables[0]));
#pragma warning restore IDE0300
    }

    [Fact]
    public void AFreedTableIsTakenAgainEmptyForItsNewTile()
    {
        TilePos far = new(30, 30);
        var cache = new ClassicWorldLightCache(40, 40);
        cache.SetTerrain(Tile, Source(7, Tile));
        cache.SetTerrain(far, Source(9, far));
        cache.SetTerrain(new TilePos(31, 30), Source(11, new TilePos(31, 30)));
#pragma warning disable IDE0300 // Preserve the original array construction or overload.
        cache.PrepareFrame(new[] { Tile });
        Assert.True(cache.TryGetVisibleTable(Tile, out ClassicLightTable first));
        Assert.Equal(new[] { 7 }, Ids(first));
        // Tile's cells are freed at the frame after the one that left them, then its table serves the far tile.
        cache.PrepareFrame(new[] { new TilePos(20, 20) });
        cache.PrepareFrame(new[] { far });
#pragma warning restore IDE0300
        Assert.True(cache.TryGetVisibleTable(far, out ClassicLightTable table));
        Assert.Same(first, table);
        Assert.Equal(far, table.Tile);
        Assert.Equal(new[] { 9, 11 }, Ids(table).OrderBy(id => id));
        Assert.Equal(table.Count, table.StaticCount);
        Assert.False(cache.TryGetVisibleTable(Tile, out _));
    }

    private static ClassicPointLight Source(int id, TilePos tile)
        => ClassicPointLight.FromTerrain(tile, new Fp32(0), new TerrainLightColour(id, 0, 0));
    private static int[] Ids(ClassicLightTable table)
        => [.. table.Lights.ToArray().Select(l => (int)MathF.Round(l.Colour.X * 255))];
}
