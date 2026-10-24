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
        HandCursorPlacement placement = HandCursor.Place(HandCursorKind.Dig, 0, new(640, 400), 1440);
        Assert.Equal(new Vector2(628, 247), placement.Origin);
        Assert.Equal(new Vector2(252, 258), placement.Size);
    }

    // DK2 (0x005DC120, 0x005DC580) puts the hotspot on the pointer; ours puts the claw tip there
    // (owner, 2026-10-24), keeping DK2's hotspot in the sprite table.
    [Theory]
    [InlineData(1271.5f, 487f, 974, 0)]
    [InlineData(1280f, 720f, 1440, 0)]
    [InlineData(1280f, 720f, 1440, 28)]
    [InlineData(512f, 384f, 768, 40)]
    public void PointRestingClawTipLandsOnThePointerInEveryFrame(float x, float y, int screenHeight, int frame)
    {
        HandCursorSprite sprite = HandCursor.Sprite(HandCursorKind.Point);
        Assert.Equal(new Vector2(2, 17), sprite.Hotspot);
        Vector2 tip = HandCursor.Anchor(HandCursorKind.Point, frame);
        HandCursorPlacement placement = HandCursor.Place(HandCursorKind.Point, frame, new(x, y), screenHeight);
        Vector2 tipOnScreen = placement.Origin + tip * (placement.Size.Y / sprite.FrameSize.Y);
        Assert.True(Vector2.Distance(new(x, y), tipOnScreen) < 1e-3f);
    }

    /// <summary>One anchor for the whole loop, frame 0's resting claw tip, so the hand stays still
    /// while the claws flex (owner, 2026-10-24).</summary>
    [Fact]
    public void PointAnchorsOnTheRestingClawTipInEveryFrame()
    {
        for (int frame = 0; frame < 41; frame++)
            Assert.Equal(new Vector2(6, 33), HandCursor.Anchor(HandCursorKind.Point, frame));
        Assert.Throws<ArgumentOutOfRangeException>(() => HandCursor.Anchor(HandCursorKind.Point, 41));
    }

    // At 1440 high the claw tip of Point frame 0 moves from (+12, +48) of the pointer to (0, 0).
    [Fact]
    public void PointFrameZeroDrawsUpAndLeftOfDk2sPlacementByTheTipOffset()
    {
        HandCursorPlacement placement = HandCursor.Place(HandCursorKind.Point, 0, new(1280, 720), 1440);
        Assert.Equal(new Vector2(1280 - 18, 720 - 99), placement.Origin);
        Assert.Equal(new Vector2(246, 159), placement.Size);
    }

    [Theory]
    [InlineData(HandCursorKind.SmallPoint, 0f, 2f)]
    [InlineData(HandCursorKind.Pickup, 0f, 4f)]
    [InlineData(HandCursorKind.Dig, 4f, 51f)]
    [InlineData(HandCursorKind.Tag, 7f, 30f)]
    [InlineData(HandCursorKind.Tagging, 7f, 30f)]
    [InlineData(HandCursorKind.HoldThing, 8f, 42f)]
    [InlineData(HandCursorKind.HoldGold, -18f, 7f)]
    public void SingleFrameStatesUseTheirTipOrKeepDk2sHotspot(HandCursorKind kind, float x, float y) =>
        Assert.Equal(new Vector2(x, y), HandCursor.Anchor(kind, 0));

    // The pick, slap and drops swing through DK2's hotspot; pinning a tip would stop the swing.
    [Theory]
    [InlineData(HandCursorKind.DropThing)]
    [InlineData(HandCursorKind.DropGold)]
    [InlineData(HandCursorKind.Slap)]
    public void ActionAnimationsKeepDk2sHotspotInEveryFrame(HandCursorKind kind)
    {
        HandCursorSprite sprite = HandCursor.Sprite(kind);
        for (int frame = 0; frame < sprite.FrameCount; frame++)
            Assert.Equal(sprite.Hotspot, HandCursor.Anchor(kind, frame));
    }

    // DK2's tooltip sits 98 = 82 - 2 + 18 right of the pointer; with the tip anchor the Point hand
    // reaches 76 (82 wide less the resting tip's x 6), so the tooltip stays clear of the hand.
    [Theory]
    [InlineData(HandCursorKind.Point, 76f)]
    [InlineData(HandCursorKind.SmallPoint, 42f)]
    [InlineData(HandCursorKind.Pickup, 83f)]
    [InlineData(HandCursorKind.Dig, 80f)]
    public void ReachIsTheFrameWidthRightOfTheLeastAnchor(HandCursorKind kind, float reach) =>
        Assert.Equal(reach, HandCursor.Reach(kind));

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
