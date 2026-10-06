// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.AI;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.AI.Tests;

/// <summary>Tasks 2 and 3 over an accepted site (<c>0x004FD030</c>, <c>0x004FD2B0</c>; Q566, Q555): the
/// joining path before the rectangle's tags, and the build cursor that waits on each tile in turn.</summary>
public class KeeperRoomTaskTests
{
    private static readonly GameDatabase Db = KeeperWorld.Database();
    private const byte Keeper = KeeperWorld.Keeper;

    /// <summary>A 3x3 north of the heart's patch, walked from its south-west corner: east, then north.</summary>
    private static readonly TilePos Start = new(14, 11);

    private static KeeperAI AtTask(KeeperTask task, byte speed = 1)
    {
        var keeper = new KeeperAI(Keeper, KeeperWorld.Parameters(speed: speed), new SimRandom(7));
        KeeperAIState s = keeper.State;
        s.RectangleOrigin = new TilePos(14, 9);
        s.RectangleWidth = s.RectangleHeight = 3;
        s.RectangleStart = Start;
        s.RectangleStepX = 1;
        s.RectangleStepY = -1;
        s.JoinPathStart = new TilePos(15, 11);
        s.JoinPathEnd = KeeperWorld.HeartAt;
        s.CurrentRoomToBuild = (byte)RoomTypeId.Lair;
        s.DigRoomAtCurrent = Start;
        s.BeginTask(task);
        return keeper;
    }

    private static WorldState World()
    {
        WorldState world = KeeperWorld.World(Db);
        world.Tile(KeeperWorld.HeartAt).GoldHeld = 5000;
        return world;
    }

    [Fact]
    public void TheSiteSearchStopsAtTheNearEdgesMiddleAndWalksTheSquareFromItsUnflippedCorner()
    {
        var view = new KeeperView(Db, World(), Keeper);

        SiteChoice choice = SiteFinder.Search(view, KeeperWorld.HeartAt, startDirection: 0, roomSize: 9,
            tilesBetweenRooms: 0, opennessPercent: 20, BuildArea.WholeMap(30, 30))!.Value;

        // North: the walk leaves the heart at (15,14); the square is laid from one tile west of it,
        // east along x and north along y, so its corner is the south-west one.
        Assert.Equal(new TilePos(15, 14), choice.JoinPoint);
        Assert.Equal(new TilePos(14, 14), choice.Start);
        Assert.Equal((1, -1), (choice.StepX, choice.StepY));
        Assert.Equal(new RoomSite(new TilePos(14, 12), 3, 3), choice.Site);
    }

    [Fact]
    public void AWestwardSiteIsWalkedFromItsEasternCornerWestward()
    {
        var view = new KeeperView(Db, World(), Keeper);

        SiteChoice choice = SiteFinder.Search(view, KeeperWorld.HeartAt, startDirection: 1, roomSize: 9,
            tilesBetweenRooms: 0, opennessPercent: 20, BuildArea.WholeMap(30, 30))!.Value;

        // West (-1,0,-1,1): the walk stops at (14,15); the corner is one row north of it.
        Assert.Equal(new TilePos(14, 15), choice.JoinPoint);
        Assert.Equal(new TilePos(14, 14), choice.Start);
        Assert.Equal((-1, 1), (choice.StepX, choice.StepY));
        Assert.Equal(new RoomSite(new TilePos(12, 14), 3, 3), choice.Site);
    }

    [Fact]
    public void AFoundSiteHandsTaskTwoItsJoiningPath()
    {
        var keeper = new KeeperAI(Keeper, KeeperWorld.Parameters(), new SimRandom(7));
        keeper.State.SearchOrigin = KeeperWorld.HeartAt;
        keeper.State.SearchDirection = 2;
        keeper.State.CurrentRoomToBuild = (byte)RoomTypeId.Lair;
        keeper.State.BeginTask(KeeperTask.FindSite);

        keeper.Think(Db, World());

        Assert.Equal(KeeperTask.DigRectangle, keeper.State.Task);
        Assert.True(keeper.State.JoinPathPending);
        Assert.Equal(KeeperWorld.HeartAt, keeper.State.JoinPathEnd);
        // South (0,1,-1,1): the walk stops at (15,16), the corner one column east of it.
        Assert.Equal(new TilePos(15, 16), keeper.State.JoinPathStart);
        Assert.Equal(new TilePos(16, 16), keeper.State.RectangleStart);
        Assert.Equal((-1, 1), (keeper.State.RectangleStepX, keeper.State.RectangleStepY));
    }

    [Fact]
    public void AnAcceptedSiteDigsItsJoiningPathBeforeAnyRectangleTag()
    {
        KeeperAI keeper = AtTask(KeeperTask.DigRectangle);
        keeper.State.JoinPathPending = true;

        IReadOnlyList<PlayerCommand> commands = keeper.Think(Db, World());

        // Task 7 from the search's origin to where its walk stopped, with task 2 to follow.
        Assert.Equal(KeeperTask.DigPath, keeper.State.Task);
        Assert.Equal(KeeperTask.DigRectangle, keeper.State.NextTask);
        Assert.Equal(new TilePos(15, 11), keeper.State.DigRoomAt);
        Assert.False(keeper.State.JoinPathPending);
        Assert.DoesNotContain(commands.OfType<TagTilesCommand>(), c => c.Tiles.Contains(new TilePos(14, 9)));
    }

    [Fact]
    public void TheRectangleIsTaggedInWalkingOrderAndTaskThreeStartsAtItsCorner()
    {
        KeeperAI keeper = AtTask(KeeperTask.DigRectangle);

        IReadOnlyList<PlayerCommand> commands = keeper.Think(Db, World());

        TagTilesCommand tags = Assert.Single(commands.OfType<TagTilesCommand>());
        Assert.Equal(
            [new(14, 11), new(15, 11), new(16, 11), new(14, 10), new(15, 10), new(16, 10), new(14, 9), new(15, 9), new(16, 9)],
            tags.Tiles);
        Assert.Equal(KeeperTask.PlaceRoom, keeper.State.Task);
        Assert.Equal(Start, keeper.State.DigRoomAtCurrent);
    }

    [Fact]
    public void TaskThreeWaitsOnItsCursorTileWhileALaterTileIsReady()
    {
        WorldState world = World();
        KeeperWorld.Set(world, Db, new TilePos(15, 11), KeeperWorld.ClaimedPath, Keeper);
        KeeperAI keeper = AtTask(KeeperTask.PlaceRoom, speed: 5);

        IReadOnlyList<PlayerCommand> commands = keeper.Think(Db, world);

        // The corner is still rock: nothing is built, though (15,11) could take the room.
        Assert.Empty(commands.OfType<BuildRoomCommand>());
        Assert.Equal(Start, keeper.State.DigRoomAtCurrent);

        KeeperWorld.Set(world, Db, Start, KeeperWorld.ClaimedPath, Keeper);
        commands = keeper.Think(Db, world);

        BuildRoomCommand build = Assert.Single(commands.OfType<BuildRoomCommand>());
        Assert.Equal([Start], build.Tiles);
    }

    [Fact]
    public void ARockCursorIsTaggedAndHoldsTheWholeAllowance()
    {
        KeeperAI keeper = AtTask(KeeperTask.PlaceRoom, speed: 5);

        IReadOnlyList<PlayerCommand> commands = keeper.Think(Db, World());

        TagTilesCommand tag = Assert.Single(commands.OfType<TagTilesCommand>());
        Assert.Equal([Start], tag.Tiles);
        Assert.Equal(Start, keeper.State.DigRoomAtCurrent);
        Assert.Equal(KeeperTask.PlaceRoom, keeper.State.Task);
    }

    [Fact]
    public void ABuiltTileAdvancesTheCursorAndTheLastEndsTheTask()
    {
        WorldState world = World();
        KeeperAI keeper = AtTask(KeeperTask.PlaceRoom, speed: 5);
        foreach (TilePos t in new RoomSite(new TilePos(14, 9), 3, 3).Tiles())
            KeeperWorld.Set(world, Db, t, KeeperWorld.LairFloor, Keeper);

        keeper.Think(Db, world);

        // Five allowance units over nine built tiles: the cursor stops on the sixth.
        Assert.Equal(KeeperTask.PlaceRoom, keeper.State.Task);
        Assert.Equal(new TilePos(16, 10), keeper.State.DigRoomAtCurrent);
        keeper.Think(Db, world);
        Assert.Equal(KeeperTask.Done, keeper.State.Task);
        Assert.Equal(Start, keeper.State.DigRoomAtCurrent);
    }
}
