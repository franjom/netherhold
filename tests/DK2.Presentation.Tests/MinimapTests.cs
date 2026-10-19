// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using System.Numerics;

namespace DK2.Presentation.Tests;

public sealed class MinimapTests
{
    [Fact]
    public void MinimapClickUsesNativeWholeMapRoundingAndHeading()
    {
        var view = new MinimapNavigationView(100, 80, MinimapZoom.WholeMap, default, new(0));
        Assert.True(MinimapNavigation.TryResolve(new(97.6f, 672), view, out TilePos target));
        Assert.Equal(new TilePos(50, 40), target);
        Assert.True(MinimapNavigation.TryResolve(new(113.6f, 672), view, out target));
        Assert.Equal(new TilePos(61, 40), target);
        Assert.True(MinimapNavigation.TryResolve(new(113.6f, 672), view with { Heading = new(512) }, out target));
        Assert.Equal(new TilePos(50, 51), target);
        Assert.True(MinimapNavigation.TryResolve(new(81.6f, 672), view, out target));
        Assert.Equal(new TilePos(39, 40), target);
        Assert.True(MinimapNavigation.TryResolve(new(73.6f, 650), view with { Heading = new(256) }, out target));
        Assert.Equal(new TilePos(50, 18), target);
    }

    [Fact]
    public void MinimapClickUsesZoomScaleAndCapturedFractionalFocus()
    {
        var view = new MinimapNavigationView(100, 80, new(1), new(new(0xA400), new(0x14C00), new(0)), new(0));
        Assert.True(MinimapNavigation.TryResolve(new(97.6f, 672), view, out TilePos target));
        Assert.Equal(new TilePos(10, 20), target);
        Assert.True(MinimapNavigation.TryResolve(new(113.6f, 672), view, out target));
        Assert.Equal(new TilePos(15, 20), target);
        Assert.True(MinimapNavigation.TryResolve(new(81.6f, 672), view, out target));
        Assert.Equal(new TilePos(5, 20), target);
        int[] eastTiles = [20, 15, 12, 11, 10];
        for (int level = 0; level <= 4; level++)
        {
            Assert.True(MinimapNavigation.TryResolve(new(113.6f, 672), view with { Zoom = new(level) }, out target));
            Assert.Equal(new TilePos(eastTiles[level], 20), target);
        }
    }

    [Theory]
    [InlineData(30, 610)]
    [InlineData(160, 610)]
    [InlineData(160, 740)]
    [InlineData(23.99f, 672)]
    [InlineData(166.4f, 672)]
    [InlineData(97.6f, 726.4f)]
    [InlineData(24, 672)]
    public void MinimapClickRejectsEarlierButtonsHitEdgesAndOutOfMapTargets(float x, float y)
    {
        var view = new MinimapNavigationView(100, 80, MinimapZoom.WholeMap, default, new(0));
        Assert.False(MinimapNavigation.TryResolve(new(x, y), view, out _));
    }

    [Fact]
    public void MagnifierUsesTheNativeAnchoredHitRectangle()
    {
        Assert.True(MinimapControlLayout.IsMagnifier(new(153.6f, 729.6f)));
        Assert.True(MinimapControlLayout.IsMagnifier(new(185.59f, 761.59f)));
        Assert.False(MinimapControlLayout.IsMagnifier(new(134.4f, 710.4f)));
        Assert.False(MinimapControlLayout.IsMagnifier(new(153.59f, 729.6f)));
        Assert.False(MinimapControlLayout.IsMagnifier(new(185.6f, 729.6f)));
        Assert.False(MinimapControlLayout.IsMagnifier(new(150, 761.6f)));
    }

    [Fact]
    public void ZoomedTerrainAndMarkersPreserveTheirDistinctFractionalRounding()
    {
        var zoom = new MinimapZoom(1);
        FpPosition3 focus = new(new(0x1400), new(0x1800), new(0));
        FpPosition3 thing = new(new(0x1C00), new(0x1800), new(0));
        Assert.Equal(new MinimapRasterPoint(61, 61), zoom.RasterOrigin(focus));
        Assert.Equal(new MinimapRasterPoint(65, 64), zoom.Project(thing, focus));
        Assert.Equal(new MinimapRasterPoint(64, 64), zoom.Project(focus, focus));
        zoom = new MinimapZoom(4);
        Assert.Equal(new MinimapRasterPoint(44, 40), zoom.RasterOrigin(focus));
        Assert.Equal(new MinimapRasterPoint(72, 64), zoom.Project(thing, focus));
        Assert.Throws<InvalidOperationException>(() => MinimapZoom.WholeMap.RasterOrigin(focus));
    }

    [Fact]
    public void MinimapZoomStartsWithTheWholeMapAndClampsBothDirections()
    {
        MinimapZoom zoom = MinimapZoom.WholeMap;
        Assert.True(zoom.IsWholeMap);
        Assert.Equal(zoom, zoom.ZoomOut());
        for (int level = 0; level <= 4; level++)
        {
            zoom = zoom.ZoomIn();
            Assert.Equal(level, zoom.Level);
            Assert.Equal(1 << level, zoom.PixelsPerTile);
        }
        Assert.Equal(zoom, zoom.ZoomIn());
        for (int level = 3; level >= -1; level--)
        {
            zoom = zoom.ZoomOut();
            Assert.Equal(level, zoom.Level);
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => new MinimapZoom(-2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MinimapZoom(5));
    }

    [Fact]
    public void CameraFootprintMovesWithoutATickAndDoesNotAllocate()
    {
        Vector2[] corners = [new(48, 48), new(80, 48), new(80, 80), new(48, 80)];
        var vertices = new Vector2[8];
        MinimapCameraFootprint.TryBuild(corners, 128, 128, 0, vertices);
        Vector2 before = vertices[0];
        for (int i = 0; i < 4; i++) corners[i] += new Vector2(.25f, 0);
        MinimapCameraFootprint.TryBuild(corners, 128, 128, 0, vertices);
        Assert.True(vertices[0].X > before.X);
        for (int i = 0; i < 100; i++) MinimapCameraFootprint.TryBuild(corners, 128, 128, 0, vertices);
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) MinimapCameraFootprint.TryBuild(corners, 128, 128, i * .001f, vertices);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - allocated);
    }

    [Fact]
    public void CameraFootprintUsesContinuousProjectionRotationAndRadialBorder()
    {
        Vector2[] corners = [new(48, 48), new(80, 48), new(80, 80), new(48, 80)];
        var vertices = new Vector2[8];
        Assert.True(MinimapCameraFootprint.TryBuild(corners, 128, 128, 0, vertices));
        Vector2 centre = new(95.2f, 672);
        Vector2 midpoint = (vertices[0] + vertices[1]) * .5f;
        Assert.InRange(midpoint.X, 79.28f, 79.30f);
        Assert.InRange(midpoint.Y, 656.08f, 656.10f);
        Assert.InRange(Vector2.Distance(vertices[0], vertices[1]), 3.999f, 4.001f);
        Assert.True(Vector2.Distance(vertices[0], centre) > Vector2.Distance(vertices[1], centre));
        Assert.True(MinimapCameraFootprint.TryBuild(corners, 128, 128, MathF.PI / 2, vertices));
        midpoint = (vertices[0] + vertices[1]) * .5f;
        Assert.InRange(midpoint.X, 79.28f, 79.30f);
        Assert.InRange(midpoint.Y, 687.90f, 687.92f);
    }

    [Fact]
    public void CameraFootprintClampsBeforeBuildingTheBorderAndRejectsInvalidPoints()
    {
        Vector2[] corners = [new(-1000, -1000), new(1000, -1000), new(1000, 1000), new(-1000, 1000)];
        var vertices = new Vector2[8];
        Assert.True(MinimapCameraFootprint.TryBuild(corners, 128, 128, 0, vertices));
        Assert.Equal(new Vector2(12.8f, 588.8f), (vertices[0] + vertices[1]) * .5f);
        Assert.Equal(new Vector2(177.6f, 755.2f), (vertices[4] + vertices[5]) * .5f);
        corners[0] = new(float.NaN, 1);
        Assert.False(MinimapCameraFootprint.TryBuild(corners, 128, 128, 0, vertices));
    }

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
    public void MarkingTheWholeGridChangedRepublishesEveryCurrentCellOnceForALostRenderTarget()
    {
        var grid = new MinimapClassGrid(width: 3, height: 2);
        grid.ClearChanges();
        grid.Set(new TilePos(2, 1), MinimapColourClass.Tagged);
        grid.MarkAllChanged();
        Assert.Equal(6, grid.Changes.Length);
        Assert.Equal(new MinimapTileChange(new TilePos(2, 1), MinimapColourClass.Tagged), grid.Changes[5]);
        Assert.True(grid.Set(new TilePos(2, 1), new MinimapColourClass(20)));
        Assert.Equal(6, grid.Changes.Length);
        Assert.Equal((ushort)20, grid.Changes[5].ColourClass.Index);
        grid.ClearChanges();
        Assert.Empty(grid.Changes.ToArray());
        Assert.True(grid.Set(new TilePos(0, 0), new MinimapColourClass(39)));
        Assert.Single(grid.Changes.ToArray());
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
