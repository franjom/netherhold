// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;

namespace DK2.Presentation.Tests;

public sealed class HandCursorTests
{
    [Theory]
    [InlineData(true, true, false, HandCursorKind.Dig)]
    [InlineData(true, true, true, HandCursorKind.Dig)]
    [InlineData(false, true, false, HandCursorKind.Point)]
    [InlineData(false, false, true, HandCursorKind.Pickup)]
    [InlineData(true, false, false, HandCursorKind.Point)]
    public void ContextUsesTheTileDigFlagAndPickupPermission(bool tile, bool dig, bool pickup, HandCursorKind expected) =>
        Assert.Equal(expected, HandCursor.ForHover(tile, dig, pickup));

    [Fact]
    public void DigSpriteUsesNativeSheetDimensionsAndHotspot()
    {
        HandCursorSprite sprite = HandCursor.Sprite(HandCursorKind.Dig);
        Assert.Equal("PickAxeHold.png", sprite.FileName);
        Assert.Equal(new Vector2(84, 86), sprite.FrameSize);
        Assert.Equal(new Vector2(4, 51), sprite.Hotspot);
        Assert.Equal(1, sprite.FrameCount);
        Assert.Equal(1, sprite.FramesPerSecond);
        HandCursorPlacement placement = HandCursor.Place(HandCursorKind.Dig, new(640, 400), 1440);
        Assert.Equal(new Vector2(628, 247), placement.Origin);
        Assert.Equal(new Vector2(252, 258), placement.Size);
    }

    [Fact]
    public void CompletingAnAnimationDiscardsOvershootAndChangingStateRestarts()
    {
        var animation = new HandCursorAnimation();
        Assert.Equal(0, animation.Advance(HandCursorKind.Point, 0));
        Assert.Equal(40, animation.Advance(HandCursorKind.Point, 1350));
        Assert.Equal(0, animation.Advance(HandCursorKind.Point, 2000));
        Assert.Equal(3, animation.Advance(HandCursorKind.Point, 2100));
        Assert.Equal(0, animation.Advance(HandCursorKind.Dig, 2200));
        Assert.Equal(0, animation.Advance(HandCursorKind.Point, 2300));
    }

    [Fact]
    public void ClickingUsesTheHoveredEntityRatherThanAnotherCreatureOnTheCursorTile()
    {
        var snapshot = new PlayerInputSnapshot(false,
            [new(PlayerInputTargetKind.Creature, 1, new TilePos(5, 5)),
             new(PlayerInputTargetKind.Creature, 2, new TilePos(6, 5))]);
        var hover = new WorldHoverHit(WorldHoverKind.Creature, new TilePos(5, 5), 2);
        var translator = new PlayerInputTranslator();
        var frame = translator.Translate(new PlayerIntent(PlayerIds.FirstKeeper, PlayerToolMode.Hand,
            hover.Tile, PointerButtonTransition.Pressed), snapshot.ForHover(hover));
        Assert.Equal(2, Assert.IsType<PickUpCreatureCommand>(Assert.Single(frame.All)).CreatureId);
        Assert.Empty(snapshot.ForHover(new WorldHoverHit(WorldHoverKind.Tile, hover.Tile)).Targets);
        Assert.Empty(snapshot.ForHover(new WorldHoverHit(WorldHoverKind.Object, hover.Tile, 99)).Targets);
    }
}
