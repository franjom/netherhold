// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation.Tests;

public sealed class BattleNavigationTests
{
    [Fact]
    public void CyclesNativeSlotsAndSkipsEmptyAndOtherKeepersBattles()
    {
        var navigation = new BattleNavigation(new PlayerId(3));
        BattleViewSnapshot[] battles =
        [
            new(new(6), new(60, 6), PlayerMask.None.With(new PlayerId(3)), 2),
            new(new(1), new(10, 1), PlayerMask.None.With(new PlayerId(3)), 0),
            new(new(2), new(20, 2), PlayerMask.None.With(new PlayerId(4)), 2),
            new(new(3), new(30, 3), PlayerMask.None.With(new PlayerId(3)), 1),
        ];
        Assert.Equal(new TilePos(30, 3), navigation.Next(battles));
        Assert.Equal(new TilePos(60, 6), navigation.Next(battles));
        Assert.Equal(new TilePos(30, 3), navigation.Next(battles));
        Assert.Null(navigation.Next([]));
        Assert.Equal(BattleViewSlot.None, navigation.Last);
        Assert.Equal(new TilePos(30, 3), navigation.Next(battles));
    }
}
