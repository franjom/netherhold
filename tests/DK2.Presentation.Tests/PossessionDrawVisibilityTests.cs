// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Simulation;
namespace DK2.Presentation.Tests;

public sealed class PossessionDrawVisibilityTests
{
    [Fact]
    public void SettledMaskHidesEveryKindOnAnUnlistedTileAndThePossessedBody()
    {
        byte[] mask = [255, 0, 0, 255];
        var visibility = new PossessionDrawVisibility(mask, 2, true, new CreatureId(4));
        Assert.True(visibility.Tile(new(0, 0)));
        Assert.False(visibility.Tile(new(1, 0)));
        Assert.False(visibility.Tile(new(-1, 0)));
        Assert.False(visibility.Tile(new(2, 0)));
        Assert.False(visibility.Creature(new(4), new(0, 0)));
        Assert.True(visibility.Creature(new(5), new(0, 0)));
        Assert.False(visibility.Creature(new(5), new(1, 0)));
        Assert.True(visibility.Object(new(0, 0), 255));
        Assert.False(visibility.Object(new(0, 0), 0));
        Assert.False(visibility.Object(new(1, 0), 255));
        Assert.False(visibility.Floor(-1));
        Assert.False(visibility.Floor(4));
    }

    [Fact]
    public void FlightAndOrdinaryViewDoNotApplyTheTileMaskButRetainBodyAndAlphaRules()
    {
        var visibility = new PossessionDrawVisibility(new byte[4], 2, false, new CreatureId(4));
        Assert.True(visibility.Tile(new(1, 0)));
        Assert.True(visibility.Floor(-1));
        Assert.False(visibility.Creature(new(4), new(1, 0)));
        Assert.True(visibility.Creature(new(5), new(1, 0)));
        Assert.False(visibility.Object(new(1, 0), 0));
        Assert.True(visibility.Object(new(1, 0), 255));
    }

    [Theory]
    [InlineData(true, false, false, 3, 4, true)]
    [InlineData(false, false, false, 3, 3, false)]
    [InlineData(true, true, false, 3, 3, false)]
    [InlineData(true, false, true, 3, 4, false)]
    [InlineData(true, false, true, 3, 3, true)]
    public void EffectsRequireExplorationNonSolidGroundAndTheOwnerForViewerOnlyRequests(
        bool explored, bool solid, bool viewerOnly, byte owner, byte viewer, bool expected)
    {
        Assert.Equal(expected, WorldEffectVisibility.Allows(explored, solid, viewerOnly, owner, viewer));
    }
}
