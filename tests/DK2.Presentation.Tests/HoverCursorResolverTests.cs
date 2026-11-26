// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;
using DK2.Simulation;
using DK2.Simulation.Commands;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>World hover to cursor, moved out of Unity's runner (plan step 2d): the cursor, the placement, the hovered
/// creature, the spell refusal that gates the cast hand, the fog gate and the hand light's point.</summary>
public sealed class HoverCursorResolverTests
{
    private static readonly PlayerId Keeper = PressFixture.Keeper;
    private static readonly BottomPanelChoiceKey Fireball = new(BottomPanelChoiceKind.Spell, 3);

    private sealed class Floor(int width, int height) : INavByteGrid
    {
        public int Width => width;
        public int Height => height;
        public byte NavByte(int x, int y) => NavBytes.Floor;
    }

    private sealed record Rig(GameDatabase Database, WorldState World, HudToolState Tools, PlayerInputTranslator Input,
        ClassicHudControls Controls, WorldView View, HandCursorFeedback Cursor, HoverCursorResolver Resolver)
    {
        public HoverFeedback Resolve(WorldHoverHit? hit = null, bool allowed = true, bool overHud = false, long milliseconds = 0) =>
            Resolver.Resolve(World, Database, new Floor(World.Width, World.Height),
                new HoverCursorInput(hit, Allowed: allowed, OverHud: overHud, PrimaryHeld: false, Gold: 0,
                    Milliseconds: milliseconds, PointerWorld: null));
    }

    private static Rig Build(GameDatabase db, WorldState world)
    {
        var tools = new HudToolState();
        var input = new PlayerInputTranslator();
        var controls = new ClassicHudControls();
        var view = new WorldView(db, Keeper);
        var cursor = new HandCursorFeedback();
        return new Rig(db, world, tools, input, controls, view, cursor,
            new HoverCursorResolver(tools, input, controls, PressFixture.Panel(db), view, cursor, Keeper));
    }

    private static Rig Synthetic()
    {
        GameDatabase db = PressFixture.Database(PressFixture.Spell(3, 0));
        return Build(db, PressFixture.World(db));
    }

    /// <summary>The small finger over the GUI in every tool is 0x0040BBE0's, a HYPOTHESIS with its producer partly read.</summary>
    [Fact]
    public void OverTheHudTheHandShowsTheSmallFingerAndNoIconAsHypothesised()
    {
        HoverFeedback feedback = Synthetic().Resolve(overHud: true);

        Assert.Equal(new HoverFeedback(HandCursorKind.SmallPoint, PlacementFeedback.None, CreatureQueryTarget.None, null, false, false),
            feedback);
        Assert.Equal(HandCursor.Reach(HandCursorKind.SmallPoint), feedback.TooltipReach);
    }

    [Fact]
    public void WithInputOffTheHandPointsAndNothingIsPlacedEvenOverTheHud()
    {
        HoverFeedback feedback = Synthetic().Resolve(new WorldHoverHit(WorldHoverKind.Creature, new TilePos(3, 3), 9),
            allowed: false, overHud: true);

        Assert.Equal(new HoverFeedback(HandCursorKind.Point, PlacementFeedback.None, CreatureQueryTarget.None, null, false, false),
            feedback);
    }

    [Fact]
    public void AHoveredCreatureIsStampedWithoutAPickupTintOrIcon()
    {
        // No creature 9 in the world: the hover id is still stamped, but nothing is picked up or tinted.
        HoverFeedback feedback = Synthetic().Resolve(new WorldHoverHit(WorldHoverKind.Creature, new TilePos(3, 3), 9));

        Assert.Equal(new HoverFeedback(HandCursorKind.Point, PlacementFeedback.None, new CreatureQueryTarget(9), null, false, false),
            feedback);
    }

    [Fact]
    public void OverTheHudADoorChoiceShowsItsIconWithNoGoAndNoCage()
    {
        GameDatabase db = PressFixture.Database([], [PressFixture.Door(2)], []);
        Rig rig = Build(db, PressFixture.World(db));
        rig.Tools.SelectPanelChoice(new BottomPanelSelection(new BottomPanelChoiceKey(BottomPanelChoiceKind.Door, 2),
            PlayerToolMode.Hand, default), rig.Input, null);

        HoverFeedback feedback = rig.Resolve(overHud: true);

        Assert.Equal(HandCursorKind.SmallPoint, feedback.Cursor);
        Assert.Equal(new PlacementFeedback(null, false, 0, PlacementIcon.PlaceDoor, true, true), feedback.Placement);
        Assert.True(rig.Resolver.SpellRefused);
    }

    [Fact]
    public void ACastClickPlaysTheCastHandOnlyWhenTheLastHoverDidNotRefuseIt()
    {
        Rig rig = Synthetic();
        rig.Tools.SelectPanelChoice(new BottomPanelSelection(Fireball, PlayerToolMode.Hand, default), rig.Input, null);
        var cast = new CastSpellAtPlaceCommand(Keeper, 3, CreatureState.CentreOf(3), CreatureState.CentreOf(3));

        // Over the GUI the spell's icon takes No Go and the cursor stays the small finger.
        Assert.Equal(HandCursorKind.SmallPoint, rig.Resolve(overHud: true).Cursor);
        Assert.True(rig.Resolver.SpellRefused);
        rig.Resolver.CastPosted(cast, 100);
        Assert.Equal(HandCursorKind.SpellPoint, rig.Resolve(milliseconds: 150).Cursor);

        // Off the map nothing is refused, so the next cast plays the cast hand for its 12 frames at 30 per second.
        Assert.False(rig.Resolver.SpellRefused);
        rig.Resolver.CastPosted(cast, 200);
        Assert.Equal(HandCursorKind.SpellCast, rig.Resolve(milliseconds: 250).Cursor);
        Assert.Equal(HandCursorKind.SpellPoint, rig.Resolve(milliseconds: 600).Cursor);
        // A command that is not a cast plays nothing.
        rig.Resolver.CastPosted(new SlapCreatureCommand(Keeper, 5), 700);
        Assert.Equal(HandCursorKind.SpellPoint, rig.Resolve(milliseconds: 710).Cursor);
    }

    [Fact]
    public void UnderFogAnUnexploredTileIsTheBareTile()
    {
        GameDatabase db = PressFixture.Database();
        var fogged = WorldState.FromMap(db, new LevelMap(8, 8, Enumerable.Repeat(new MapTile(0, 0, 0, 0), 64).ToArray()),
            [PlayerIds.Good, PlayerIds.Neutral, Keeper.Value], fogOfWarEnabled: true);
        var tile = new TilePos(3, 4);

        Assert.Equal(new WorldHoverHit(WorldHoverKind.Tile, tile), HoverCursorResolver.FogHit(fogged, Keeper, tile));
        fogged.Tile(tile).SetExploredBy(Keeper);
        Assert.Null(HoverCursorResolver.FogHit(fogged, Keeper, tile));
        Assert.Null(HoverCursorResolver.FogHit(PressFixture.World(db), Keeper, new TilePos(5, 5)));
    }

    [Fact]
    public void TheObjectsOnlyKeyCountsOnlyWithOrdinaryInputAndNoModalMenu()
    {
        Assert.True(HoverCursorResolver.ObjectsOnlyKeyCounts(new HudVisibility(true, true), modal: false));
        Assert.False(HoverCursorResolver.ObjectsOnlyKeyCounts(new HudVisibility(true, true), modal: true));
        Assert.False(HoverCursorResolver.ObjectsOnlyKeyCounts(new HudVisibility(true, false), modal: false));
    }

    [Fact]
    public void TheHandLightTakesADoorsTileCentreAndLeavesATileToTheAdapter()
    {
        WorldState world = PressFixture.World(PressFixture.Database());

        Assert.True(HoverCursorResolver.TryHandLightPoint(world, new WorldHoverHit(WorldHoverKind.Door, new TilePos(3, 4)), out Vector2 door));
        Assert.Equal(new Vector2(3.5f, 4.5f), door);
        Assert.False(HoverCursorResolver.TryHandLightPoint(world, new WorldHoverHit(WorldHoverKind.Tile, new TilePos(3, 4)), out _));
        // A thing that is gone falls through to the adapter's ground point too.
        Assert.False(HoverCursorResolver.TryHandLightPoint(world, new WorldHoverHit(WorldHoverKind.Creature, new TilePos(3, 4), 9), out _));
        // Raised over a door, low over a tile; none on the map's edge.
        Assert.Equal(0x1E00 / (float)Fp32.One, HoverCursorResolver.HandLight(world, Keeper,
            new WorldHoverHit(WorldHoverKind.Door, new TilePos(3, 4)), door, false)!.Value.Position.Y);
        Assert.Equal(0x1C00 / (float)Fp32.One, HoverCursorResolver.HandLight(world, Keeper,
            new WorldHoverHit(WorldHoverKind.Tile, new TilePos(3, 4)), new Vector2(3.2f, 4.7f), false)!.Value.Position.Y);
        Assert.Null(HoverCursorResolver.HandLight(world, Keeper, new WorldHoverHit(WorldHoverKind.Tile, new TilePos(0, 4)),
            new Vector2(.5f, 4.5f), false));
    }

    [InstallFact]
    public void AHeldCreatureShowsTheDropCageInTheWorldAndTheHeldIconsOverTheHud()
    {
        (GameDatabase? db, WorldState? world, CreatureState? goblin) = PressFixture.Goblin();
        goblin.InHand = true;
        world.Player(Keeper).Hand.Add(HandItem.Of(goblin));
        Rig rig = Build(db, world);

        HoverFeedback inWorld = rig.Resolve(new WorldHoverHit(WorldHoverKind.Tile, new TilePos(2, 2)));
        Assert.Equal(HandCursorKind.HoldThing, inWorld.Cursor);
        Assert.Equal(new TileRectangle(2, 2, 3, 3), inWorld.Placement.Cage);
        Assert.Equal(PlacementIcon.Held, inWorld.Placement.Icon);

        HoverFeedback overHud = rig.Resolve(overHud: true);
        Assert.Equal(HandCursorKind.HoldThing, overHud.Cursor);
        Assert.Equal(new PlacementFeedback(null, false, 0, PlacementIcon.Held, false, false), overHud.Placement);

        // With a modal menu the hand's own states are not resolved: the small finger stays.
        rig.Controls.Apply(ClassicHudAction.ToggleOptions);
        Assert.Equal(HandCursorKind.SmallPoint, rig.Resolve(overHud: true).Cursor);
    }

    // The install's Goblin on a claimed 5 x 5 floor (PressFixture.Goblin), with (4, 4) turned to neutral water, where no
    // creature may be dropped, and (1, 3) to a dig-taggable terrain.
    private static readonly TilePos Water = new(4, 4), Dig = new(1, 3), Floor22 = new(2, 2);

    private static (Rig Rig, CreatureState Goblin) Goblin()
    {
        (GameDatabase? db, WorldState? world, CreatureState? goblin) = PressFixture.Goblin();
        world.Tile(Water).TerrainId = db.Terrain.Values.Single(t => t.Name == "Water").TerrainId;
        world.Tile(Water).OwnerPlayerId = PlayerIds.Neutral;
        world.Tile(Dig).TerrainId = db.Terrain.Values.First(t => t.IsTaggable).TerrainId;
        return (Build(db, world), goblin);
    }

    private static void Hold(WorldState world, CreatureState goblin)
    {
        goblin.InHand = true;
        world.Player(Keeper).Hand.Add(HandItem.Of(goblin));
    }

    private static WorldHoverHit TileHit(TilePos tile) => new(WorldHoverKind.Tile, tile);

    [InstallFact]
    public void TheSpellRefusalIsTakenBeforeTheHeldIconGate()
    {
        // The goblin is in the hand but hidden: its pickup is pending with a deferred drop, so nothing is visibly held.
        (Rig? rig, CreatureState? goblin) = Goblin();
        Assert.True(rig.View.LocalPickups.TryQueue(rig.World, rig.Database, Keeper, new CreatureId(goblin.Id), 0));
        Assert.True(rig.View.LocalPickups.TryQueueDrop(new DropCommand(Keeper, Floor22)));
        Hold(rig.World, goblin);
        Assert.False(rig.View.LocalPickups.HasVisibleItems(rig.World, Keeper));
        Assert.True(PlacementCursor.HandDrop(rig.World, rig.Database, Keeper, Water).NoGo);

        HoverFeedback feedback = rig.Resolve(TileHit(Water));

        // The drop cage's No Go counts for the refusal; the gate then clears the held placement.
        Assert.True(rig.Resolver.SpellRefused);
        Assert.Equal(PlacementFeedback.None, feedback.Placement);
    }

    [InstallFact]
    public void AnOwnPickableCreatureIsStampedTintedSoundedAndTakesThePickupHand()
    {
        (Rig? rig, CreatureState? goblin) = Goblin();
        var hit = new WorldHoverHit(WorldHoverKind.Creature, Floor22, goblin.Id);
        SoundRequest? expected = CreatureHoverSound.For(rig.World, rig.Database, hit, Keeper);
        Assert.NotNull(expected);

        HoverFeedback feedback = rig.Resolve(hit);

        Assert.Equal(new HoverFeedback(HandCursorKind.Pickup, PlacementFeedback.None, new CreatureQueryTarget(goblin.Id), expected,
            true, false), feedback);
        Assert.Equal(HandCursor.Reach(HandCursorKind.Pickup), feedback.TooltipReach);
        // Off the Hand only the stamp and the sound stay: no pickup hand, no tint.
        rig.Tools.StartWith(DungeonToolMode.SellRoom, RoomTypeId.Treasury);
        HoverFeedback selling = rig.Resolve(hit);
        Assert.Equal((HandCursorKind.Point, new CreatureQueryTarget(goblin.Id), expected, false),
            (selling.Cursor, selling.HoveredCreature, selling.HoverSound, selling.Tinted));
    }

    [InstallFact]
    public void AnOwnGoldPileTakesThePickupHandAndASpecialOnlyThePointingOne()
    {
        (Rig? rig, CreatureState _) = Goblin();
        ObjectState gold = rig.World.SpawnObject(rig.Database, 1, Keeper, rig.World.TileIndex(new TilePos(1, 1)));
        byte specialType = rig.Database.Objects.Values.First(o => o.IsSpecial).ObjectId;
        ObjectState special = rig.World.SpawnObject(rig.Database, specialType, Keeper, rig.World.TileIndex(new TilePos(3, 1)));

        HoverFeedback onGold = rig.Resolve(new WorldHoverHit(WorldHoverKind.Object, new TilePos(1, 1), gold.Id));
        Assert.Equal((HandCursorKind.Pickup, true, CreatureQueryTarget.None), (onGold.Cursor, onGold.Tinted, onGold.HoveredCreature));
        Assert.Equal(HandCursorKind.Point,
            rig.Resolve(new WorldHoverHit(WorldHoverKind.Object, new TilePos(3, 1), special.Id)).Cursor);
    }

    [InstallFact]
    public void AnOwnStorePileTakesThePickupHandAndAForeignOneDoesNot()
    {
        (Rig? rig, CreatureState _) = Goblin();
        var store = new TilePos(3, 2);
        ref TileState tile = ref rig.World.Tile(store);
        tile.TerrainId = rig.Database.Room(RoomTypeId.Treasury).TerrainId;
        tile.GoldHeld = 500;
        int pile = Assert.Single(new WorldView(rig.Database).Decorations(rig.World), o => WorldView.IsStorePile(o.Id)).Id;
        var hit = new WorldHoverHit(WorldHoverKind.Object, store, pile);

        Assert.Equal(HandCursorKind.Pickup, rig.Resolve(hit).Cursor);
        rig.World.Tile(store).OwnerPlayerId = PlayerIds.Neutral;
        Assert.Equal(HandCursorKind.Point, rig.Resolve(hit).Cursor);
    }

    [InstallFact]
    public void InRoomModeADiggableTileKeepsTheBlueCageAndDrawsNoIcon()
    {
        (Rig? rig, CreatureState _) = Goblin();
        rig.Tools.StartWith(DungeonToolMode.BuildRoom, RoomTypeId.Treasury);

        PlacementFeedback onDig = rig.Resolve(TileHit(Dig)).Placement;
        Assert.Equal(new PlacementFeedback(new TileRectangle(1, 3, 2, 4), true, 0, PlacementIcon.None, false, false), onDig);
        // Off a dig tile the room arm runs and prices the tile.
        Assert.Equal(PlacementIcon.PlaceRoom, rig.Resolve(TileHit(new TilePos(3, 3))).Placement.Icon);
    }

    [InstallFact]
    public void TheQueryToolKeepsItsIconAndTheHoverCursorWhileSomethingIsHeld()
    {
        (Rig? rig, CreatureState? goblin) = Goblin();
        Hold(rig.World, goblin);
        rig.Tools.SelectCorner(ClassicHudAction.SelectQuery, rig.Input, PressFixture.Panel(rig.Database));

        HoverFeedback feedback = rig.Resolve(TileHit(new TilePos(3, 3)));

        // Neither the held hand (the cursor gate) nor the held icons (HeldIcons' Query gate) replace Query's.
        Assert.Equal(HandCursorKind.Point, feedback.Cursor);
        Assert.Equal(PlacementCursor.Query(), feedback.Placement);
    }

    [InstallFact]
    public void ADoorChoiceKeepsItsIconOverTheHeldOnes()
    {
        (Rig? rig, CreatureState? goblin) = Goblin();
        Hold(rig.World, goblin);
        byte door = rig.Database.Doors.Keys.First();
        rig.Tools.SelectPanelChoice(new BottomPanelSelection(new BottomPanelChoiceKey(BottomPanelChoiceKind.Door, door),
            PlayerToolMode.Hand, default), rig.Input, null);

        HoverFeedback feedback = rig.Resolve(TileHit(new TilePos(3, 3)));

        Assert.Equal(PlacementIcon.PlaceDoor, feedback.Placement.Icon);
        Assert.Equal(new TileRectangle(3, 3, 4, 4), feedback.Placement.Cage);
    }

    [InstallFact]
    public void TheFullMapHidesTheCageButNotTheHeldIconsWhileOptionsHidesBoth()
    {
        (Rig? rig, CreatureState? goblin) = Goblin();
        Hold(rig.World, goblin);
        Assert.Equal(new TileRectangle(4, 4, 5, 5), rig.Resolve(TileHit(Water)).Placement.Cage);

        // HeldIcons, with the drop's No Go and no cage.
        rig.Controls.Apply(ClassicHudAction.ToggleFullMap);
        Assert.Equal(new PlacementFeedback(null, false, 0, PlacementIcon.Held, true, true), rig.Resolve(TileHit(Water)).Placement);

        // Only the final held gate, which keeps the empty placement's No Go and affordability.
        rig.Controls.Apply(ClassicHudAction.ToggleFullMap);
        rig.Controls.Apply(ClassicHudAction.ToggleOptions);
        Assert.Equal(new PlacementFeedback(null, false, 0, PlacementIcon.Held, false, false), rig.Resolve(TileHit(Water)).Placement);
    }

    [InstallFact]
    public void TheHoverCageShowsOnAnInnerDigTileOnlyWithoutPossessionOrAModalMenu()
    {
        (Rig? rig, CreatureState? goblin) = Goblin();
        Assert.Equal(new TileRectangle(1, 3, 2, 4), rig.Resolve(TileHit(Dig)).Placement.Cage);
        Assert.True(rig.Resolve(TileHit(Dig)).Placement.Valid);
        // Not over a plain floor with an empty hand.
        Assert.Null(rig.Resolve(TileHit(new TilePos(3, 3))).Placement.Cage);

        rig.World.Player(Keeper).PossessedCreatureId = goblin.Id;
        Assert.Null(rig.Resolve(TileHit(Dig)).Placement.Cage);
        rig.World.Player(Keeper).PossessedCreatureId = 0;
        rig.Controls.Apply(ClassicHudAction.ToggleFullMap);
        Assert.Null(rig.Resolve(TileHit(Dig)).Placement.Cage);
        rig.Controls.Apply(ClassicHudAction.ToggleFullMap);

        // None on the map's border, though the tile is dig-taggable.
        rig.World.Tile(new TilePos(0, 3)).TerrainId = rig.World.Tile(Dig).TerrainId;
        Assert.Null(rig.Resolve(TileHit(new TilePos(0, 3))).Placement.Cage);
    }
}
