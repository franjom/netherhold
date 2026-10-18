// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Presentation.Tests;

public sealed class RoomNavigationTests
{
    [Fact]
    public void CyclesWrapsAndResetsAcrossTypesOrRemovedRooms()
    {
        RoomNavigationEntry Room(RoomTypeId type, int x) => new(new(x, 2), PlayerIds.FirstKeeper, type, new(x, 2));
        RoomNavigationEntry a = Room(RoomTypeId.Lair, 1), b = Room(RoomTypeId.Lair, 3), c = Room(RoomTypeId.Treasury, 5);
        var navigation = new RoomNavigation(PlayerIds.FirstKeeper);
        RoomNavigationEntry[] rooms = [a, b, c];
        Assert.Equal(new TilePos(1, 2), navigation.Next(rooms, RoomTypeId.Lair));
        Assert.Equal(new TilePos(3, 2), navigation.Next(rooms, RoomTypeId.Lair));
        Assert.Equal(new TilePos(1, 2), navigation.Next(rooms, RoomTypeId.Lair));
        Assert.Equal(new TilePos(5, 2), navigation.Next(rooms, RoomTypeId.Treasury));
        Assert.Equal(new TilePos(1, 2), navigation.Next(rooms, RoomTypeId.Lair));
        Assert.Equal(new TilePos(3, 2), navigation.Next([b, c], RoomTypeId.Lair));
        Assert.Null(navigation.Next([c], RoomTypeId.Lair));
    }
}
