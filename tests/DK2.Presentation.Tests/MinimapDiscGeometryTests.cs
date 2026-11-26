// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using System.Numerics;
using Xunit;

namespace DK2.Presentation.Tests;

public sealed class MinimapDiscGeometryTests
{
    private static FpPosition3 At(float x, float y) => new(new Fp32((int)(x * 4096)), new Fp32((int)(y * 4096)), default);

    [Fact]
    public void WholeMapFitsTheIntegerTileCornerBeforePlacingDotsAndDoorSpans()
    {
        var geometry = new MinimapDiscGeometry();
        geometry.Build(new MinimapClassGrid(90, 90),
            [new(MinimapMarkerKind.Creature, At(12.75f, 17.5f), 0xFF123456),
             new(MinimapMarkerKind.VerticalDoor, At(12.75f, 17.5f), 0xFF654321)],
            MinimapZoom.WholeMap, default, null, default);
        Assert.Equal(2, geometry.Quads.Count);
        Assert.Equal(new Vector2(32.5f, 37.5f), geometry.Quads[0].Centre);
        Assert.Equal(new Vector2(3, 3), geometry.Quads[0].Size);
        Assert.Equal(MinimapQuadKind.Dot, geometry.Quads[0].Kind);
        Assert.Equal(new Vector2(32.5f, 37.5f), geometry.Quads[1].Centre);
        Assert.Equal(new Vector2(1, 1), geometry.Quads[1].Size);
    }

    [Fact]
    public void ZoomedDoorStartsAtItsTileEdgeAndReportingRadiusScalesInTiles()
    {
        var geometry = new MinimapDiscGeometry();
        geometry.Build(new MinimapClassGrid(90, 90),
            [new(MinimapMarkerKind.VerticalDoor, At(12.75f, 17.5f), 0xFF123456),
             new(MinimapMarkerKind.Ring, At(12.75f, 17.5f), 0xFF654321, 1, ReportingDistanceTiles: 3)],
            new MinimapZoom(2), At(12, 17), null, default);
        Assert.Equal(new Vector2(67.5f, 66), geometry.Quads[0].Centre);
        Assert.Equal(new Vector2(1, 4), geometry.Quads[0].Size);
        Assert.Equal(new Vector2(26, 26), geometry.Quads[1].Size);
        Assert.Equal(12, geometry.Quads[1].Radius);
    }

    [Theory]
    [InlineData(0, MinimapQuadKind.Solid, 1)]
    [InlineData(1, MinimapQuadKind.Dot, 3)]
    [InlineData(2, MinimapQuadKind.Ring, 6)]
    public void RingKindsAndExtentsMatchTheOracle(int radius, MinimapQuadKind kind, int size)
    {
        var geometry = new MinimapDiscGeometry();
        geometry.Build(new MinimapClassGrid(90, 90), [new(MinimapMarkerKind.Ring, At(20, 30), 0xFF123456, radius)],
            MinimapZoom.WholeMap, default, null, default);
        Assert.Equal(kind, geometry.Quads[0].Kind);
        Assert.Equal(new Vector2(size, size), geometry.Quads[0].Size);
    }

    [Fact]
    public void EmptyRectanglesAndFullMapOnlyKindsAreSkippedWithoutReorderingMarkers()
    {
        var geometry = new MinimapDiscGeometry();
        geometry.Build(new MinimapClassGrid(90, 90),
            [new(MinimapMarkerKind.Rectangle, At(20, 30), 1), new(MinimapMarkerKind.FullMapCreature, At(20, 30), 2),
             new(MinimapMarkerKind.Creature, At(20, 30), 3), new(MinimapMarkerKind.Trap, At(20, 30), 4)],
            MinimapZoom.WholeMap, default, null, default);
        Assert.Equal(new uint[] { 3, 4 }, geometry.Quads.Select(q => q.ColourArgb));
    }

    [Fact]
    public void WarmGeometryBuildsReuseStorageAndAnEmptyCaptureClearsOldMarkers()
    {
        var geometry = new MinimapDiscGeometry();
        var grid = new MinimapClassGrid(90, 90);
        MinimapMarker[] markers = [new(MinimapMarkerKind.Creature, At(20, 30), 0xFF123456)];
        for (int i = 0; i < 32; i++)
        {
            geometry.Build(grid, markers, new MinimapZoom(0), At(25, 25), new TilePos(20, 30), new GameTick(i));
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            geometry.Build(grid, markers, new MinimapZoom(0), At(25, 25), new TilePos(20, 30), new GameTick(i));
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(0xFF123456u, geometry.Quads[0].ColourArgb);
        Assert.All(geometry.Quads.Skip(1), q => Assert.Equal(0xFFFFFFFFu, q.ColourArgb));
        geometry.Build(grid, [], MinimapZoom.WholeMap, default, null, default);
        Assert.Empty(geometry.Quads);
    }
}
