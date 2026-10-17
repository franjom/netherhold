// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Presentation.Tests;

public sealed class MinimapMarkerTests
{
    private static readonly FpPosition3 Position = new(new(0x1800), new(0x2800), new(0));
    private static MinimapPalette Palette()
    {
        var pixels = new uint[37];
        pixels[18] = 0xABCDEF;
        pixels[19] = 0x123456;
        return new MinimapPalette(pixels);
    }

    [Theory]
    [InlineData(MinimapMarkerKind.Creature)]
    [InlineData(MinimapMarkerKind.Trap)]
    [InlineData(MinimapMarkerKind.VerticalDoor)]
    [InlineData(MinimapMarkerKind.HorizontalDoor)]
    public void OwnMarkersBypassExplorationButOthersRequireIt(MinimapMarkerKind kind)
    {
        var own = new MinimapMarkerFacts(kind, Position, PlayerIds.FirstKeeper, 0);
        Assert.True(MinimapMarkerRules.TryCreate(own, PlayerIds.FirstKeeper, new(1), Palette(), out _));
        var enemy = own with { Owner = new PlayerId(4) };
        Assert.False(MinimapMarkerRules.TryCreate(enemy, PlayerIds.FirstKeeper, new(1), Palette(), out _));
        Assert.True(MinimapMarkerRules.TryCreate(enemy with { Flags = MinimapMarkerFlags.Explored },
            PlayerIds.FirstKeeper, new(1), Palette(), out _));
    }

    [Theory]
    [InlineData(MinimapMarkerFlags.InViewerHand)]
    [InlineData(MinimapMarkerFlags.PickupPending)]
    public void HeldAndPendingCreaturesAreAbsentEvenOnExploredTiles(MinimapMarkerFlags flag)
    {
        var facts = new MinimapMarkerFacts(MinimapMarkerKind.Creature, Position,
            PlayerIds.FirstKeeper, flag | MinimapMarkerFlags.Explored);
        Assert.False(MinimapMarkerRules.TryCreate(facts, PlayerIds.FirstKeeper, new(1), Palette(), out _));
    }

    [Fact]
    public void HiddenEnemyTrapsStayHiddenButOwnersSeeTheirOwn()
    {
        var facts = new MinimapMarkerFacts(MinimapMarkerKind.Trap, Position, new PlayerId(4),
            MinimapMarkerFlags.Explored | MinimapMarkerFlags.Hidden);
        Assert.False(MinimapMarkerRules.TryCreate(facts, PlayerIds.FirstKeeper, new(1), Palette(), out _));
        Assert.True(MinimapMarkerRules.TryCreate(facts, new PlayerId(4), new(1), Palette(), out _));
    }

    [Theory]
    [InlineData(MinimapMarkerKind.Creature, 0, 0xFF000000u)]
    [InlineData(MinimapMarkerKind.Creature, 1, 0xFFABCDEFu)]
    [InlineData(MinimapMarkerKind.Trap, 0, 0xFF000000u)]
    [InlineData(MinimapMarkerKind.Trap, 1, 0xFFABCDEFu)]
    [InlineData(MinimapMarkerKind.VerticalDoor, 0, 0xFFABCDEFu)]
    [InlineData(MinimapMarkerKind.VerticalDoor, 1, 0xFF000000u)]
    public void MarkerKindsUseTheirNativeBlinkPhase(MinimapMarkerKind kind, long tick, uint expected)
    {
        var facts = new MinimapMarkerFacts(kind, Position, PlayerIds.FirstKeeper, 0);
        Assert.True(MinimapMarkerRules.TryCreate(facts, PlayerIds.FirstKeeper, new(tick), Palette(), out var marker));
        Assert.Equal(expected, marker.ColourArgb);
        Assert.Equal(Position, marker.Position);
    }

    [Theory]
    [InlineData(100, 100, 20, 20, 90, 90)]
    [InlineData(50, 100, 42, 20, 45, 90)]
    [InlineData(100, 51, 20, 41, 90, 45)]
    public void WholeMapBoundsUseTheSameIntegerFitAsNativeTilesAndMarkers(
        int width, int height, int left, int top, int spanX, int spanY)
    {
        Assert.Equal(new MinimapMapBounds(left, top, spanX, spanY), MinimapMapBounds.Fit(width, height));
    }
}
