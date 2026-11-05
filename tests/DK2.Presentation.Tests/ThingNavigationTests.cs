// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Presentation.Tests;

public sealed class ThingNavigationTests
{
    private static ThingNavigationEntry Door(int id, byte kind, byte owner = PlayerIds.FirstKeeper) =>
        new(id, new PlayerId(owner), kind, new TilePos(id, 2));

    [Fact]
    public void CyclesOneKindInOrderAndWraps()
    {
        var navigation = new ThingNavigation(PlayerIds.FirstKeeper);
        ThingNavigationEntry[] doors = [Door(1, 2), Door(2, 3), Door(3, 2), Door(4, 2, PlayerIds.FirstKeeper + 1)];
        Assert.Equal(new TilePos(1, 2), navigation.Next(doors, 2));
        Assert.Equal(new TilePos(3, 2), navigation.Next(doors, 2));
        Assert.Equal(new TilePos(1, 2), navigation.Next(doors, 2));   // a rival's door is skipped, then the head
    }

    [Fact]
    public void ALastThingOfAnotherKindStillStartsTheWalk()
    {
        // 0x00416150 tests the last thing's class and owner, not its kind (0x00416190-0x004161E0).
        var navigation = new ThingNavigation(PlayerIds.FirstKeeper);
        ThingNavigationEntry[] doors = [Door(1, 2), Door(2, 3), Door(3, 2)];
        Assert.Equal(new TilePos(2, 2), navigation.Next(doors, 3));
        Assert.Equal(new TilePos(3, 2), navigation.Next(doors, 2));   // after door 2, not the first of kind 2
    }

    [Fact]
    public void ALastThingNowARivalsRestartsFromTheHead()
    {
        // The last thing counts only while its owner (+0x28) is the viewer (0x004161B5-0x004161BE).
        var navigation = new ThingNavigation(PlayerIds.FirstKeeper);
        Assert.Equal(new TilePos(2, 2), navigation.Next([Door(1, 3), Door(2, 2), Door(3, 2)], 2));
        // Door 2 is now a rival's: the walk restarts at the head, not after it.
        Assert.Equal(new TilePos(1, 2), navigation.Next([Door(1, 2), Door(2, 2, PlayerIds.FirstKeeper + 1), Door(3, 2)], 2));
    }

    [Fact]
    public void NoMatchClearsTheLastThing()
    {
        var navigation = new ThingNavigation(PlayerIds.FirstKeeper);
        ThingNavigationEntry[] doors = [Door(1, 2), Door(2, 2)];
        Assert.Equal(new TilePos(1, 2), navigation.Next(doors, 2));
        Assert.Null(navigation.Next(doors, 5));
        Assert.Equal(new TilePos(1, 2), navigation.Next(doors, 2));   // restarts at the head
        Assert.Equal(new TilePos(2, 2), navigation.Next([Door(2, 2)], 2));   // a removed last thing restarts too
    }
}
