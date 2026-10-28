// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;
using DK2.Tools;

namespace DK2.Tools.Tests;

/// <summary>The replay's <c>--blueprints retry</c> (ADR-0570): DK2's tile rule (<c>0x00450EB0</c>)
/// refuses a door or trap on a tile the keeper does not own, so a recorded placement on a tile ours
/// has not claimed yet waits for the claim.</summary>
public sealed class BlueprintRetryTests
{
    private static readonly PlayerId Keeper = new(3);

    [Fact]
    public void APlacementOnATileOursDoesNotOwnWaitsForTheClaim_TheRestOfItsTurnGoesOn()
    {
        var owned = new HashSet<TilePos>();
        var events = new StringWriter();
        var retry = new BlueprintRetry(c => owned.Contains(((PlaceDoorCommand)c).Tile), events);
        var door = new PlaceDoorCommand(Keeper, new TilePos(24, 39), 2);
        var lever = new LockDoorCommand(Keeper, new TilePos(10, 10));

        Assert.Equal([lever], retry.Filter(2006, InputFrame.Of(door, lever)).All);
        Assert.Empty(retry.Filter(2007, InputFrame.Empty).All);
        owned.Add(door.Tile);
        Assert.Equal([door], retry.Filter(2093, InputFrame.Empty).All);
        Assert.Empty(retry.Filter(2094, InputFrame.Empty).All);

        Assert.Equal((1, 1, 0), (retry.Held, retry.SentLater, retry.Pending));
        string log = events.ToString();
        Assert.Contains("2006\treplay\tblueprint_held\tplayer=3 kind=door type=2 at=(24,39)", log);
        Assert.Contains("2093\treplay\tblueprint_sent_later\tplayer=3 kind=door type=2 at=(24,39) recorded=2006", log);
    }

    [Fact]
    public void HeldPlacementsReleasedTogetherFollowTheTurnsOwnCommands_InTheOrderTheyWereHeld()
    {
        var owned = new HashSet<TilePos>();
        var events = new StringWriter();
        var retry = new BlueprintRetry(c => owned.Contains(((PlaceTrapCommand)c).Tile), events);
        var first = new PlaceTrapCommand(Keeper, new TilePos(26, 35), 5);
        var second = new PlaceTrapCommand(Keeper, new TilePos(22, 35), 5);
        var third = new PlaceTrapCommand(Keeper, new TilePos(29, 32), 5);
        var lever = new LockDoorCommand(Keeper, new TilePos(10, 10));

        retry.Filter(2312, InputFrame.Of(first));
        retry.Filter(2315, InputFrame.Of(second, third));
        owned.UnionWith([first.Tile, second.Tile]);

        Assert.Equal([lever, first, second], retry.Filter(2423, InputFrame.Of(lever)).All);
        Assert.Equal((3, 2, 1), (retry.Held, retry.SentLater, retry.Pending));
        Assert.Contains("2315\treplay\tblueprint_held\tplayer=3 kind=trap type=5 at=(22,35)", events.ToString());
    }

    [Fact]
    public void APlacementNeverClaimedIsCountedAsNever()
    {
        bool owns = false;
        var retry = new BlueprintRetry(_ => owns);
        var trap = new PlaceTrapCommand(Keeper, new TilePos(26, 35), 5);

        Assert.Empty(retry.Filter(3000, InputFrame.Of(trap)).All);
        Assert.Equal((1, 0, 1), (retry.Held, retry.SentLater, retry.Pending));
        Assert.Empty(retry.Filter(3001, InputFrame.Empty).All);
        Assert.Equal(1, retry.Pending);
    }

    [Fact]
    public void AFrameWithNothingHeldOrSentIsPassedUnchanged()
    {
        var retry = new BlueprintRetry(_ => true);
        InputFrame frame = InputFrame.Of(new PlaceTrapCommand(Keeper, new TilePos(1, 1), 5),
            new LockDoorCommand(Keeper, new TilePos(2, 2)));

        Assert.Same(frame.Commands, retry.Filter(100, frame).Commands);
        Assert.Equal((0, 0, 0), (retry.Held, retry.SentLater, retry.Pending));
    }
}
