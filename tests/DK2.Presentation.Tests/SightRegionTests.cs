// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Presentation.Tests;

public sealed class SightRegionTests
{
    [Theory]
    [InlineData(10, 10, 63)]
    [InlineData(13, 10, 63)]
    [InlineData(14, 10, 191)]
    [InlineData(15, 10, 0)]
    public void NativeFogMeshUsesTheBiasedCacheScalarPackedToEightBitOpacity(int x, int y, byte expected)
    {
        var region = new SightRegion(new GameTick(1), new TilePos(10, 10), 64, 7, 30, 30);
        Assert.Equal(expected, region.FogOpacity(new TilePos(x, y), new GameTick(25)));
    }

    [Fact]
    public void SimulationTickZeroIsAValidStartBecauseTheRegionItselfIsOptional()
    {
        var region = new SightRegion(GameTick.Zero, new TilePos(10, 10), 64, 5, 30, 30);
        Assert.False(region.IsVisible(new TilePos(10, 10), GameTick.Zero));
        Assert.True(region.IsVisible(new TilePos(10, 10), new GameTick(1)));
        Assert.True(region.IsVisible(new TilePos(10, 10), new GameTick(24)));
        Assert.False(region.IsVisible(new TilePos(10, 10), new GameTick(64)));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(25, true)]
    [InlineData(64, true)]
    [InlineData(65, false)]
    [InlineData(66, false)]
    public void NativeRegionOpensAndClosesOnTheBridgeTurn(long turn, bool visible)
    {
        var region = new SightRegion(new GameTick(1), new TilePos(10, 10), 64, 5, 30, 30);
        Assert.Equal(visible, region.IsVisible(new TilePos(10, 10), new GameTick(turn)));
    }

    [Fact]
    public void NativeRadialPredicateRemainsFiveTilesEvenWithUpgradedBounds()
    {
        var region = new SightRegion(new GameTick(1), new TilePos(10, 10), 64, 7, 30, 30);
        Assert.True(region.IsVisible(new TilePos(14, 10), new GameTick(25)));
        Assert.False(region.IsVisible(new TilePos(15, 10), new GameTick(25)));
        Assert.False(region.IsVisible(new TilePos(14, 14), new GameTick(25)));
    }

    [Fact]
    public void ClippedBoundsChangeTheRadialCentreAndKeepTheirUpperEdgeExclusive()
    {
        var region = new SightRegion(new GameTick(1), new TilePos(0, 0), 64, 5, 10, 10);
        Assert.True(region.IsVisible(new TilePos(2, 2), new GameTick(25)));
        Assert.False(region.IsVisible(new TilePos(5, 2), new GameTick(25)));
    }
}
