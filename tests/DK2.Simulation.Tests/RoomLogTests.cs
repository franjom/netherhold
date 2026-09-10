// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Simulation.Commands;

namespace DK2.Simulation.Tests;

/// <summary>
/// Reading DK2's room list back as input frames. A room object knows its type,
/// its slab count and its bounding slabs but not its tile set, so most of what
/// matters here is the line between what the log determines and what it merely
/// suggests.
/// </summary>
public class RoomLogTests
{
    private static RoomLog Read(string body, long lag = 1, long origin = 1) =>
        RoomLog.Read(new StringReader(RoomLog.Header + "\n" + body), "test", lag, origin);

    /// <summary>wall,tick,player,event,room,type,x,y,size,x0,y0,x1,y1</summary>
    private static string Row(
        long tick, string ev, int room, int size, int x0, int y0, int x1, int y1,
        byte player = 3, byte type = 4) =>
        $"0.0,{tick},{player},{ev},{room},{type},{(x0 + x1) / 2},{(y0 + y1) / 2}," +
        $"{size},{x0},{y0},{x1},{y1}\n";

    private static T Only<T>(InputFrame frame) where T : PlayerCommand =>
        Assert.IsType<T>(Assert.Single(frame.All));

    // ---- what the log determines -----------------------------------------

    [Fact]
    public void ARectangularPlacementReplaysAsItsExactTiles()
    {
        RoomLog log = Read(Row(41, "build", room: 7, size: 6, 10, 20, 12, 21));

        BuildRoomCommand build = Only<BuildRoomCommand>(log.At(40));
        Assert.Equal(4, build.RoomId);
        Assert.Equal(3, build.PlayerId);
        Assert.Equal(
            [
                new TilePos(10, 20), new TilePos(11, 20), new TilePos(12, 20),
                new TilePos(10, 21), new TilePos(11, 21), new TilePos(12, 21),
            ],
            build.Tiles);
    }

    /// <summary>Growing a room builds the difference, not the whole room again.</summary>
    [Fact]
    public void GrowthBuildsOnlyTheTilesThatAreNew()
    {
        RoomLog log = Read(
            Row(41, "build", room: 7, size: 2, 10, 20, 11, 20)
            + Row(61, "grow", room: 7, size: 4, 10, 20, 11, 21));

        BuildRoomCommand grow = Only<BuildRoomCommand>(log.At(60));
        Assert.Equal([new TilePos(10, 21), new TilePos(11, 21)], grow.Tiles);
    }

    [Fact]
    public void ARoomThatVanishesSellsTheTilesItLastHeld()
    {
        RoomLog log = Read(
            Row(41, "build", room: 7, size: 2, 10, 20, 11, 20)
            + Row(61, "gone", room: 7, size: 0, 0, 0, 0, 0));

        SellRoomCommand sell = Only<SellRoomCommand>(log.At(60));
        Assert.Equal([new TilePos(10, 20), new TilePos(11, 20)], sell.Tiles);
    }

    // ---- what it only suggests -------------------------------------------

    /// <summary>
    /// An L-shaped room has the bounds of the rectangle that would contain it
    /// and fewer slabs than that rectangle holds. Nothing in the log says which
    /// slabs are missing, so the placement is counted and dropped — filling the
    /// bounds would put tiles on the map that DK2 never had there, and every
    /// divergence after that would be unattributable.
    /// </summary>
    [Fact]
    public void ARoomWhoseBoundsHoldMoreSlabsThanItHasIsDroppedNotGuessedAt()
    {
        RoomLog log = Read(Row(41, "build", room: 7, size: 8, 10, 20, 12, 22));

        Assert.Empty(log.Turns);
        Assert.Equal(1, log.Tally.Unreplayable);
        Assert.Equal(0, log.Tally.Commands);
    }

    /// <summary>Growth off a room we could not place is not recoverable either.</summary>
    [Fact]
    public void GrowthFromAnUnknownShapeIsDropped()
    {
        RoomLog log = Read(
            Row(41, "build", room: 7, size: 8, 10, 20, 12, 22)
            + Row(61, "grow", room: 7, size: 9, 10, 20, 12, 22));

        Assert.Empty(log.Turns);
        Assert.Equal(2, log.Tally.Unreplayable);
    }

    [Fact]
    public void ShrinkingIsNeverReplayed()
    {
        RoomLog log = Read(
            Row(41, "build", room: 7, size: 4, 10, 20, 11, 21)
            + Row(61, "shrink", room: 7, size: 2, 10, 20, 11, 20));

        Assert.Single(log.Turns);
        Assert.Equal(1, log.Tally.Unreplayable);
    }

    // ---- bookkeeping -----------------------------------------------------

    [Fact]
    public void AStampIsOneTurnLaterThanTheCommand()
    {
        RoomLog log = Read(Row(41, "build", room: 7, size: 1, 10, 20, 10, 20));

        Assert.Equal([40L], log.Turns);
    }

    [Fact]
    public void CommandsBeforeTheRunBeginsAreDroppedAndCounted()
    {
        RoomLog log = Read(Row(1, "build", room: 7, size: 1, 10, 20, 10, 20));

        Assert.Empty(log.Turns);
        Assert.Equal(1, log.Tally.Before);
    }

    [Fact]
    public void TwoPlayersPlacingOnOneTurnBothArrive()
    {
        RoomLog log = Read(
            Row(41, "build", room: 7, size: 1, 10, 20, 10, 20, player: 3)
            + Row(41, "build", room: 8, size: 1, 30, 40, 30, 40, player: 4));

        Assert.Equal(2, log.At(40).All.Count);
    }

    [Fact]
    public void AnEmptyLogYieldsNothingRatherThanFailing()
    {
        RoomLog log = Read(string.Empty);

        Assert.Empty(log.Turns);
        Assert.Equal(0, log.Tally.Rows);
    }

    [Theory]
    [InlineData("0.0,41,3,build,7,4,10,20,1,10,20,10\n")]
    [InlineData("0.0,41,3,demolish,7,4,10,20,1,10,20,10,20\n")]
    [InlineData("0.0,x,3,build,7,4,10,20,1,10,20,10,20\n")]
    public void AMalformedRowIsRefusedRatherThanGuessedAt(string body) =>
        Assert.Throws<InvalidDataException>(() => Read(body));

    [Fact]
    public void AFileWithoutTheProbesHeaderIsRefused() =>
        Assert.Throws<InvalidDataException>(
            () => RoomLog.Read(new StringReader("tick,room\n"), "test"));
}
