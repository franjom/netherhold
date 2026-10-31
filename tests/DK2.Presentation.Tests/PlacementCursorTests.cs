// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>The placement cursor (docs/research/placement-cursor.md): the room release's clamp
/// (0x00408194), and per tool the cage, validity, icon and signed price (0x0040B210, 0x0040B3C0).</summary>
public sealed class PlacementCursorTests
{
    [Fact]
    public void ADragEndPastTheRubberBandLimitIsPulledBackOnEachAxis()
    {
        Assert.Equal(new TilePos(13, 2), PlacementCursor.ClampDrag(new TilePos(5, 5), new TilePos(20, 2), 8));
        Assert.Equal(new TilePos(5, -3), PlacementCursor.ClampDrag(new TilePos(5, 5), new TilePos(5, -9), 8));
        Assert.Equal(new TileRectangle(5, 2, 14, 6), PlacementCursor.DragRectangle(new TilePos(5, 5), new TilePos(20, 2), 8));
        // Left past the limit the cursor keeps the pointer's end, the room release the anchor's.
        Assert.Equal(new TileRectangle(8, 5, 17, 6), PlacementCursor.DragRectangle(new TilePos(20, 5), new TilePos(8, 5), 8));
        Assert.Equal(new TilePos(12, 5), PlacementCursor.ClampDrag(new TilePos(20, 5), new TilePos(8, 5), 8));
    }

    [Fact]
    public void ASellDragKeepsTheLowCornerAsTheSellReleaseDoes()
    {
        var input = new PlayerInputTranslator { RubberBandLimit = 8 };
        var snapshot = new PlayerInputSnapshot(false);
        PlayerId keeper = PlayerIds.FirstKeeper;
        input.Translate(new PlayerIntent(keeper, PlayerToolMode.SellRoom, new TilePos(20, 1), PointerButtonTransition.Pressed), snapshot);
        SellRoomCommand command = Assert.IsType<SellRoomCommand>(Assert.Single(input.Translate(new PlayerIntent(keeper,
            PlayerToolMode.SellRoom, new TilePos(1, 1), PointerButtonTransition.Released), snapshot).All));
        Assert.Equal(Enumerable.Range(1, 9).Select(x => new TilePos(x, 1)), command.Tiles.OrderBy(t => t.X));
    }

    [Fact]
    public void ARoomDragBuildsTheClampedRectangle()
    {
        var input = new PlayerInputTranslator { RubberBandLimit = 8 };
        var snapshot = new PlayerInputSnapshot(false);
        PlayerId keeper = PlayerIds.FirstKeeper;
        input.Translate(new PlayerIntent(keeper, PlayerToolMode.BuildRoom, new TilePos(1, 1), PointerButtonTransition.Pressed,
            RoomTypeId.Treasury), snapshot);
        BuildRoomCommand command = Assert.IsType<BuildRoomCommand>(Assert.Single(input.Translate(new PlayerIntent(keeper,
            PlayerToolMode.BuildRoom, new TilePos(20, 1), PointerButtonTransition.Released, RoomTypeId.Treasury), snapshot).All));
        Assert.Equal(new TilePos(9, 1), command.Release);
        Assert.Equal(9, command.Tiles.Count);
    }

    [InstallFact]
    public void ARoomOnTheKeepersClaimedFloorIsBluePricedPerTileAndElsewhereRedWithNoPrice_CountAssumedCanBuild()
    {
        (GameDatabase db, WorldState world, PlayerId keeper) = Level1();
        RoomDef treasury = db.Rooms[RoomTypeId.Treasury];
        TilePos own = Claimed(world, db, keeper, 0), other = Claimed(world, db, keeper, 1);
        int cost = RoomRules.CostPerTile(treasury);

        PlacementFeedback hover = PlacementCursor.Room(world, db, keeper, treasury, own, null, 100000);
        Assert.Equal((true, -cost, PlacementIcon.PlaceRoom, false), (hover.Valid, hover.Price, hover.Icon, hover.NoGo));
        Assert.Null(hover.Cage);

        PlacementFeedback poor = PlacementCursor.Room(world, db, keeper, treasury, own, null, 0);
        Assert.Equal((false, -cost, false, true), (poor.Valid, poor.Price, poor.Affordable, poor.NoGo));

        TilePos rock = new(0, 0);
        PlacementFeedback refused = PlacementCursor.Room(world, db, keeper, treasury, rock, null, 100000);
        Assert.Equal((false, 0, true), (refused.Valid, refused.Price, refused.NoGo));

        PlacementFeedback dragged = PlacementCursor.Room(world, db, keeper, treasury, other, own, 100000);
        Assert.NotNull(dragged.Cage);
        int placeable = 0;
        TileRectangle area = dragged.Cage!.Value;
        for (int y = area.Y0; y < area.Y1; y++)
            for (int x = area.X0; x < area.X1; x++)
                if (RoomRules.CanBuild(db, world, new TilePos(x, y), treasury, keeper) == RoomPlacement.Allowed) placeable++;
        Assert.Equal(-cost * placeable, dragged.Price);
    }

    [InstallFact]
    public void ADoorWithoutAWorkshopIsNoGoWithNoPriceAndWithOneCostsItsGold()
    {
        (GameDatabase db, WorldState world, PlayerId keeper) = Level1();
        DoorDef door = db.Doors.Values.First(d => d.GoldCost > 0);
        TilePos own = Claimed(world, db, keeper, 0);
        PlacementFeedback none = PlacementCursor.Door(world, db, keeper, door, own, hasWorkshop: false, 100000);
        Assert.Equal((false, 0, true, (TileRectangle?)null), (none.Valid, none.Price, none.NoGo, none.Cage));
        PlacementFeedback some = PlacementCursor.Door(world, db, keeper, door, own, hasWorkshop: true, 0);
        Assert.Equal((-door.GoldCost, false, PlacementIcon.PlaceDoor), (some.Price, some.Affordable, some.Icon));
        Assert.True(some.NoGo);
    }

    [Theory]
    [InlineData(HoverCageTool.Room, false, false, true)]
    [InlineData(HoverCageTool.Sell, false, false, true)]
    [InlineData(HoverCageTool.Door, false, false, true)]
    [InlineData(HoverCageTool.Trap, false, false, true)]
    [InlineData(HoverCageTool.Hand, false, false, false)]
    [InlineData(HoverCageTool.Hand, true, false, true)]
    [InlineData(HoverCageTool.Hand, false, true, true)]
    [InlineData(HoverCageTool.SpellOrQuery, false, true, false)]
    [InlineData(HoverCageTool.SpellOrQuery, true, false, true)]
    public void TheHoverCageShowsPerToolAs0x004096B0Decides(HoverCageTool tool, bool digTaggable, bool handHolds, bool shows) =>
        Assert.Equal(shows, PlacementCursor.HoverCageShows(tool, digTaggable, handHolds));

    [Theory]
    [InlineData(HoverCageTool.Hand, true, false, true)]
    [InlineData(HoverCageTool.Room, true, false, true)]
    [InlineData(HoverCageTool.SpellOrQuery, true, false, true)]
    [InlineData(HoverCageTool.Sell, true, false, false)]
    [InlineData(HoverCageTool.Door, false, false, false)]
    [InlineData(HoverCageTool.Room, false, true, true)]
    public void ADigTileLeavesTheResetsBlueExceptWhereTheArmRewritesIt(HoverCageTool tool, bool dig, bool armValid, bool blue) =>
        Assert.Equal(blue, PlacementCursor.HoverCageBlue(tool, dig, armValid));

    [Fact]
    public void TheHoverCageIsHiddenOnAMapBorderTile()
    {
        Assert.Null(PlacementCursor.HoverCage(new TilePos(0, 5), 20, 20));
        Assert.Null(PlacementCursor.HoverCage(new TilePos(5, 19), 20, 20));
        Assert.Equal(new TileRectangle(1, 1, 2, 2), PlacementCursor.HoverCage(new TilePos(1, 1), 20, 20));
    }

    [InstallFact]
    public void AnEmptyHandHasNoDropCage()
    {
        (GameDatabase db, WorldState world, PlayerId keeper) = Level1();
        Assert.Equal(PlacementFeedback.None, PlacementCursor.HandDrop(world, db, keeper, Claimed(world, db, keeper, 0)));
    }

    [InstallFact]
    public void APlaceSpellShowsItsIconWithNoCageOrPriceAndNoGoWhereTheSimulationRefusesTheCast_FlagTwoNotMirrored()
    {
        (GameDatabase db, WorldState world, PlayerId keeper) = Level1();
        byte spell = CallToArmsRules.SpellId;
        world.Player(keeper).Availabilities.Add(new AvailabilityState(AvailabilityKind.Spell, spell, 3));
        world.Player(keeper).Mana = 1_000_000;
        var simulation = new GameSimulation(db, world, 1);
        simulation.Navigation.Refresh();
        TilePos own = Claimed(world, db, keeper, 0);

        PlacementFeedback ok = PlacementCursor.Spell(world, db, simulation.Navigation, keeper, spell, own,
            new WorldHoverHit(WorldHoverKind.Tile, own));
        Assert.Equal((true, false, 0, PlacementIcon.Spell, spell, (TileRectangle?)null),
            (ok.Valid, ok.NoGo, ok.Price, ok.Icon, ok.SpellId, ok.Cage));

        // Rock is solid to location selector 4, as the cast's own permission finds.
        TilePos rock = new(0, 0);
        Assert.Equal(KeeperSpellRefusal.Location, KeeperSpellRules.CanCastAtPlace(world, db, simulation.Navigation,
            world.CurrentTick, new CastSpellAtPlaceCommand(keeper, spell, CreatureState.CentreOf(0), CreatureState.CentreOf(0))));
        PlacementFeedback refused = PlacementCursor.Spell(world, db, simulation.Navigation, keeper, spell, rock,
            new WorldHoverHit(WorldHoverKind.Tile, rock));
        Assert.Equal((false, true, PlacementIcon.Spell), (refused.Valid, refused.NoGo, refused.Icon));

        Assert.True(db.KeeperSpellById(spell)!.ManaCost > 0);
        world.Player(keeper).Mana = 0;
        Assert.True(PlacementCursor.Spell(world, db, simulation.Navigation, keeper, spell, own,
            new WorldHoverHit(WorldHoverKind.Tile, own)).NoGo);
        Assert.True(PlacementCursor.Spell(world, db, simulation.Navigation, keeper, spell, null, default).NoGo);
    }

    [InstallFact]
    public void HealIsNoGoOffACreatureAndOnAnUninjuredOneAsTheCastRefuses_OursAtLeastMaxWhereDk2TestsEquality()
    {
        (GameDatabase db, WorldState world, PlayerId keeper) = Level1();
        world.Player(keeper).Availabilities.Add(new AvailabilityState(AvailabilityKind.Spell, KeeperSpellRules.Heal, 3));
        world.Player(keeper).Mana = 1_000_000;
        CreatureState creature = world.SpawnCreature(db, db.CreatureByName("Imp")!.CreatureId, keeper, Claimed(world, db, keeper, 0));
        var simulation = new GameSimulation(db, world, 1);
        simulation.Navigation.Refresh();
        var onCreature = new WorldHoverHit(WorldHoverKind.Creature, creature.Tile, creature.Id);
        int max = CreatureHealthRules.MaxHealth(db, db.CreatureById(creature.CreatureTypeId), creature);

        creature.Health = max;
        Assert.True(PlacementCursor.Spell(world, db, simulation.Navigation, keeper, KeeperSpellRules.Heal, creature.Tile, onCreature).NoGo);
        creature.Health = max - 1;
        PlacementFeedback injured = PlacementCursor.Spell(world, db, simulation.Navigation, keeper, KeeperSpellRules.Heal,
            creature.Tile, onCreature);
        Assert.Equal((true, false, KeeperSpellRules.Heal), (injured.Valid, injured.NoGo, injured.SpellId));
        // A thing spell over a bare tile: our click sends no command there.
        Assert.True(PlacementCursor.Spell(world, db, simulation.Navigation, keeper, KeeperSpellRules.Heal, creature.Tile,
            new WorldHoverHit(WorldHoverKind.Tile, creature.Tile)).NoGo);
    }

    [InstallFact]
    public void CreateImpShowsItsSpellIconAndNoGoOnlyWhenTheManaIsShort_TileTestNotMirrored()
    {
        (GameDatabase db, WorldState world, PlayerId keeper) = Level1();
        int cost = ImpSupplyRules.CreateImpCost(world, db, keeper);
        world.Player(keeper).Mana = cost;
        Assert.Equal((true, false, PlacementIcon.Spell, (byte)4), Shape(PlacementCursor.CreateImp(world, db, keeper, 4)));
        world.Player(keeper).Mana = cost - 1;
        Assert.True(PlacementCursor.CreateImp(world, db, keeper, 4).NoGo);

        static (bool, bool, PlacementIcon, byte) Shape(PlacementFeedback f) => (f.Valid, f.NoGo, f.Icon, f.SpellId);
    }

    [Fact]
    public void QueryShowsOnlyTheQueryIcon()
    {
        PlacementFeedback query = PlacementCursor.Query();
        Assert.Equal((PlacementIcon.Query, false, 0, (TileRectangle?)null), (query.Icon, query.NoGo, query.Price, query.Cage));
    }

    [Fact]
    public void HeldIconsStepThirtyTwoPixelsFourToARowWithTheOverflowIconAfterTheEighth() =>
        Assert.Equal(new[] { (0, 0), (32, 0), (96, 0), (0, 32), (96, 32), (128, 32) },
            new[] { 0, 1, 3, 4, 7, 8 }.Select(HeldItemStack.Offset).ToArray());

    [InstallFact]
    public void TheHeldIconsListTheHandFromTheTopAndOverflowPastEight()
    {
        (GameDatabase db, WorldState world, PlayerId keeper) = Level1();
        TilePos at = Claimed(world, db, keeper, 0);
        var stack = new HeldItemStack();
        stack.Refresh(world, keeper);
        Assert.Empty(stack.Icons);
        for (int i = 0; i < 8; i++) Hold(world, world.SpawnCreature(db, ImpType, keeper, at));
        Hold(world, world.SpawnCreature(db, GoblinType, keeper, at));
        stack.Refresh(world, keeper);
        Assert.Equal(HeldItemStack.MaxIcons, stack.Icons.Count);
        Assert.True(stack.More);
        Assert.Equal(new HeldIcon(HeldIconKind.Creature, GoblinType), stack.Icons[0]);
        Assert.Equal(new HeldIcon(HeldIconKind.Creature, ImpType), stack.Icons[7]);
    }

    [InstallFact]
    public void EightHeldThingsFillTheGridWithoutTheOverflowIcon_AnObjectShowsItsOwnIcon()
    {
        (GameDatabase db, WorldState world, PlayerId keeper) = Level1();
        TilePos at = Claimed(world, db, keeper, 0);
        for (int i = 0; i < 7; i++) Hold(world, world.SpawnCreature(db, ImpType, keeper, at));
        HoldObject(world, keeper, world.SpawnObject(db, HatcheryRules.ChickenObjectId, keeper, world.TileIndex(at)));
        var stack = new HeldItemStack();
        stack.Refresh(world, keeper);
        Assert.Equal(HeldItemStack.MaxIcons, stack.Icons.Count);
        Assert.False(stack.More);
        Assert.Equal(new HeldIcon(HeldIconKind.Object, HatcheryRules.ChickenObjectId), stack.Icons[0]);
    }

    [InstallFact]
    public void AnImpOverItsHeartCentreShowsTheBinWithoutNoGo_ThePlatformAroundItShowsTheImp()
    {
        (GameDatabase db, WorldState world, PlayerId keeper) = Level1();
        TilePos centre = HeartCentre(world, db, keeper);
        Hold(world, world.SpawnCreature(db, ImpType, keeper, Claimed(world, db, keeper, 0)));

        PlacementFeedback bin = PlacementCursor.HandDrop(world, db, keeper, centre);
        Assert.Equal((PlacementIcon.Held, true, true, false), (bin.Icon, bin.Valid, bin.Dispose, bin.NoGo));
        PlacementFeedback platform = PlacementCursor.HandDrop(world, db, keeper, new TilePos(centre.X + 1, centre.Y));
        Assert.Equal((PlacementIcon.Held, true, false, false), (platform.Icon, platform.Valid, platform.Dispose, platform.NoGo));
    }

    [InstallFact]
    public void AnImpOverARuinedHeartsCentreShowsNoBin_TheClickDoesNotDisposeAndWhetherDk2KeepsCell0x40IsUnread()
    {
        (GameDatabase db, WorldState world, PlayerId keeper) = Level1();
        TilePos centre = HeartCentre(world, db, keeper);
        Hold(world, world.SpawnCreature(db, ImpType, keeper, Claimed(world, db, keeper, 0)));
        world.Player(keeper).HeartRuined = true;

        Assert.False(PlacementCursor.HandDrop(world, db, keeper, centre).Dispose);
    }

    [InstallFact]
    public void AnEvilNonWorkerOverItsOwnPortalCentreShowsTheBin_OverTheHeartCentreNoGo()
    {
        (GameDatabase db, WorldState world, PlayerId keeper) = Level1();
        TilePos portal = PaintedPortalCentre(world, db, keeper);
        CreatureState goblin = world.SpawnCreature(db, GoblinType, keeper, Claimed(world, db, keeper, 0));
        Assert.Equal(CreatureFlags.IsEvil, db.CreatureById(GoblinType).Flags & (CreatureFlags.IsWorker | CreatureFlags.IsEvil));
        Hold(world, goblin);

        PlacementFeedback bin = PlacementCursor.HandDrop(world, db, keeper, portal);
        Assert.Equal((true, true, false), (bin.Valid, bin.Dispose, bin.NoGo));
        PlacementFeedback heart = PlacementCursor.HandDrop(world, db, keeper, HeartCentre(world, db, keeper));
        Assert.Equal((false, false, true), (heart.Valid, heart.Dispose, heart.NoGo));
    }

    /// <summary>The Portal disposal arm excludes type 1, so a converted Dwarf can use it.</summary>
    [InstallFact]
    public void ANonEvilWorkerGetsThePortalBinButCannotUseTheHeart()
    {
        (GameDatabase db, WorldState world, PlayerId keeper) = Level1();
        TilePos portal = PaintedPortalCentre(world, db, keeper);
        CreatureDef dwarf = db.Creatures.Values.First(c =>
            (c.Flags & (CreatureFlags.IsWorker | CreatureFlags.IsEvil)) == CreatureFlags.IsWorker);
        Hold(world, world.SpawnCreature(db, dwarf.CreatureId, keeper, Claimed(world, db, keeper, 0)));

        PlacementFeedback overPortal = PlacementCursor.HandDrop(world, db, keeper, portal);
        Assert.Equal((true, true, false), (overPortal.Valid, overPortal.Dispose, overPortal.NoGo));
        PlacementFeedback overHeart = PlacementCursor.HandDrop(world, db, keeper, HeartCentre(world, db, keeper));
        Assert.Equal((false, false, true), (overHeart.Valid, overHeart.Dispose, overHeart.NoGo));
    }

    [InstallFact]
    public void AnotherPlayersCreatureOrAnObjectOnTopShowsNoBin()
    {
        (GameDatabase db, WorldState world, PlayerId keeper) = Level1(PlayerIds.Good);
        TilePos portal = PaintedPortalCentre(world, db, keeper), centre = HeartCentre(world, db, keeper);
        TilePos at = Claimed(world, db, keeper, 0);
        HoldBy(world, keeper, world.SpawnCreature(db, GoblinType, new PlayerId(PlayerIds.Good), at));
        Assert.False(PlacementCursor.HandDrop(world, db, keeper, portal).Dispose);
        HoldBy(world, keeper, world.SpawnCreature(db, ImpType, new PlayerId(PlayerIds.Good), at));
        Assert.False(PlacementCursor.HandDrop(world, db, keeper, centre).Dispose);

        HoldObject(world, keeper, world.SpawnObject(db, HatcheryRules.ChickenObjectId, keeper, world.TileIndex(at)));
        foreach (TilePos tile in new[] { portal, centre, at })
        {
            Assert.False(PlacementCursor.HandDrop(world, db, keeper, tile).Dispose);
            Assert.False(HeldItemStack.Disposes(world, db, keeper, world.Player(keeper).Hand[^1], tile));
        }
    }

    private const byte ImpType = 1, GoblinType = 7;

    private static void Hold(WorldState world, CreatureState creature) =>
        HoldBy(world, new PlayerId(creature.OwnerPlayerId), creature);

    private static void HoldBy(WorldState world, PlayerId player, CreatureState creature)
    {
        creature.InHand = true;
        world.Player(player).Hand.Add(HandItem.Of(creature));
    }

    private static void HoldObject(WorldState world, PlayerId player, ObjectState thing)
    {
        thing.InHand = true;
        world.Player(player).Hand.Add(HandItem.Of(thing));
    }

    private static TilePos HeartCentre(WorldState world, GameDatabase db, PlayerId keeper)
    {
        for (int y = 0; y < world.Height; y++)
            for (int x = 0; x < world.Width; x++)
                if (DisposalRules.IsHeartCentreTile(world, db, new TilePos(x, y), keeper)) return new TilePos(x, y);
        throw new InvalidOperationException("No Heart.");
    }

    /// <summary>A 3×3 Portal the keeper owns, painted over claimed floor in the map corner away from the Heart.</summary>
    private static TilePos PaintedPortalCentre(WorldState world, GameDatabase db, PlayerId keeper)
    {
        byte claimed = world.Tile(Claimed(world, db, keeper, 0)).TerrainId;
        int x0 = HeartCentre(world, db, keeper).X > world.Width / 2 ? 2 : world.Width - 5;
        for (int y = 2; y < 5; y++)
            for (int x = x0; x < x0 + 3; x++)
            {
                ref TileState tile = ref world.Tile(x, y);
                tile.TerrainId = claimed;
                tile.OwnerPlayerId = keeper.Value;
                RoomRules.Place(db, ref tile, db.Room(RoomTypeId.Portal), keeper);
            }
        return new TilePos(x0 + 1, 3);
    }

    private static (GameDatabase, WorldState, PlayerId) Level1(params byte[] others)
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        PlayerId keeper = PlayerIds.FirstKeeper;
        var world = WorldState.FromMap(db, GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "Level1")!,
            [keeper.Value, .. others], fogOfWarEnabled: false);
        return (db, world, keeper);
    }

    /// <summary>The keeper's <paramref name="skip"/>-th tile a Treasury may take, row by row.</summary>
    private static TilePos Claimed(WorldState world, GameDatabase db, PlayerId keeper, int skip)
    {
        RoomDef treasury = db.Rooms[RoomTypeId.Treasury];
        for (int y = 0; y < world.Height; y++)
            for (int x = 0; x < world.Width; x++)
                if (RoomRules.CanBuild(db, world, new TilePos(x, y), treasury, keeper) == RoomPlacement.Allowed && skip-- == 0)
                    return new TilePos(x, y);
        throw new InvalidOperationException("No buildable tile.");
    }
}
