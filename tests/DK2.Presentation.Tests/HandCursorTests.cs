// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.Presentation.Tests;

public sealed class HandCursorTests
{
    [Fact]
    public void HeldThingAndGoldCursorsAndFiniteDropSlapFeedbackUseOriginalSheets()
    {
        var feedback = new HandCursorFeedback();
        Assert.Equal(HandCursorKind.HoldThing, feedback.Resolve(HandCursorKind.Dig, holding: true, gold: false, primaryHeld: false, milliseconds: 0));
        Assert.Equal(HandCursorKind.HoldGold, feedback.Resolve(HandCursorKind.Dig, holding: true, gold: true, primaryHeld: false, milliseconds: 1));
        feedback.Play(HandCursorKind.Slap, 100);
        Assert.Equal(HandCursorKind.Slap, feedback.Resolve(HandCursorKind.Point, false, false, false, 599));
        Assert.Equal(HandCursorKind.Point, feedback.Resolve(HandCursorKind.Point, false, false, false, 600));
        feedback.Play(HandCursorKind.Tag, 700);
        Assert.Equal(HandCursorKind.Tagging, feedback.Resolve(HandCursorKind.Dig, false, false, true, 734));
        Assert.Equal(HandCursorKind.Dig, feedback.Resolve(HandCursorKind.Dig, false, false, false, 735));
        feedback.Play(HandCursorKind.DropGold, 800);
        feedback.Reset();
        Assert.Equal(HandCursorKind.Point, feedback.Resolve(HandCursorKind.Point, false, false, false, 801));
        Assert.Equal("DropGold.png", HandCursor.Sprite(HandCursorKind.DropGold).FileName);
    }

    [Fact]
    public void GuiTooltipUsesQuarterSecondDelayAndStillResetsOnWidgetChange()
    {
        var delay = new HoverTooltipDelay();
        var snapshot = new HoverTextSnapshot("Mana description", "Mana", new WorldHoverHit(WorldHoverKind.Widget, default, 1));
        Assert.False(delay.Visible(snapshot, 0));
        Assert.False(delay.Visible(snapshot, 250));
        Assert.True(delay.Visible(snapshot, 251));
        Assert.False(delay.Visible(snapshot with { Hit = new WorldHoverHit(WorldHoverKind.Widget, default, 2) }, 252));
    }
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

    [Fact]
    public void ExploredFloorTakesThePointingHandAndUnexploredFogTakesTheDigCursor_FogClauseLikely()
    {
        GameDatabase database = new(
            [Terrain(1, "Floor", TerrainFlags.Ownable), Terrain(3, "Gold", TerrainFlags.Solid | TerrainFlags.Taggable)],
            rooms: [], creatures: [], variables: new GlobalVariables());
        var map = new LevelMap(5, 1,
            [new MapTile(1, 2, 0, 0), new MapTile(1, 1, 0, 0), new MapTile(3, 0, 0, 0), new MapTile(1, 2, 0, 0), new MapTile(1, 1, 0, 0)]);
        WorldState world = WorldState.FromMap(database, map, [1, 2], fogOfWarEnabled: true);
        var viewer = new PlayerId(2);
        world.Tile(0, 0).SetExploredBy(viewer);
        world.Tile(2, 0).SetExploredBy(viewer);
        world.Tile(3, 0).SetExploredBy(viewer);
        world.Tile(3, 0).SetTaggedBy(viewer, true);

        Assert.False(world.Tile(0, 0).IsDiscoveredBy(viewer));
        Assert.False(world.Tile(1, 0).IsExploredBy(viewer));
        Assert.False(HandCursor.ShowsDig(world, database, new TilePos(0, 0), viewer));
        Assert.True(HandCursor.ShowsDig(world, database, new TilePos(1, 0), viewer));
        Assert.True(HandCursor.ShowsDig(world, database, new TilePos(2, 0), viewer));
        Assert.True(HandCursor.ShowsDig(world, database, new TilePos(3, 0), viewer));
        // Scanned but unexplored (a Claim Scan tile): the case the layer change moved from Point to Dig.
        world.Tile(4, 0).SetDiscoveredBy(viewer);
        Assert.False(world.Tile(4, 0).IsExploredBy(viewer));
        Assert.True(HandCursor.ShowsDig(world, database, new TilePos(4, 0), viewer));
    }

    private static TerrainDef Terrain(byte id, string name, TerrainFlags flags) => new()
    {
        Name = name, TerrainId = id, Flags = flags, StartingHealth = 1, MaxHealth = 1,
        GoldValue = 0, ManaGain = 0, MaxManaGain = 0, Damage = 0, NameStringId = 0, TooltipStringId = 0,
        MaxHealthTypeTerrainId = id, DestroyedTypeTerrainId = id, Depth = new Fp32(0),
    };
}
