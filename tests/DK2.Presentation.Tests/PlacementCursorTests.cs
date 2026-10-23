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

    private static (GameDatabase, WorldState, PlayerId) Level1()
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        PlayerId keeper = PlayerIds.FirstKeeper;
        var world = WorldState.FromMap(db, GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "Level1")!,
            [keeper.Value], fogOfWarEnabled: false);
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
