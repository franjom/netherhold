// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>Menu39's message tabs (message-tabs.md): the first eight of the viewer's records,
/// oldest first, each sliding in from the right and flashing until opened; the interface's
/// dismissed and opened bits; the type table's strings and graphics.</summary>
public sealed class MessageTabsViewTests
{
    [Fact]
    public void TheTypeTableGivesEachTypeItsStringAndTabGraphic()
    {
        Assert.Equal(-10345, MessageTypeTable.StringReference(1, 0));   // Speech 345, "Your Dungeon Heart is under attack!"
        Assert.Equal(3, MessageTypeTable.Graphic(1));                    // mt-fight
        Assert.Equal(-11299, MessageTypeTable.StringReference(5, 3));   // Speech 1299, "The King is dying!"
        Assert.Equal(0, MessageTypeTable.StringReference(5, 23));        // off the list: the terminator's
        Assert.Equal(-10042, MessageTypeTable.StringReference(23, 1));   // the list before the table (0x0069FFB0)
        Assert.Equal(1, MessageTypeTable.Graphic(6));                    // graphic 0 reads as mt-info
        Assert.Equal(2, MessageTypeTable.Graphic(13));                   // mt-creature
        Assert.Equal(MessageTypeTable.StringReference(0, 0), MessageTypeTable.StringReference(250, 0));
    }

    [InstallFact]
    public void TheFirstEightRecordsTabOldestFirstSlidingInAndAnOpenedOneStopsFlashing_FlashTurnAssumedTickPlusOne()
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        PlayerId keeper = PlayerIds.FirstKeeper;
        var world = WorldState.FromMap(db, GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "Level1")!,
            [keeper.Value], fogOfWarEnabled: false);
        var posted = new List<PlayerMessage>();
        posted.Add(world.Messages.Post(world, keeper.Value, PlayerMessageKind.HeartAttacked, 0, 0)!.Value);
        for (int v = 1; v <= 4; v++) posted.Add(world.Messages.Post(world, keeper.Value, PlayerMessageKind.LowManaWarning, v, 0)!.Value);
        for (int v = 1; v <= 5; v++) posted.Add(world.Messages.Post(world, keeper.Value, PlayerMessageKind.ManaShortfall, v, 0)!.Value);
        var view = new MessageTabsView(keeper);

        view.Capture(world, 1000);
        Assert.Equal(posted.Take(8).Select(m => m.Id), view.Tabs.Select(t => t.MessageId));
        Assert.Equal(3, view.Tabs[0].Graphic);
        Assert.Equal(88 + 1020, view.Tabs[0].LocalX);           // the slide starts 1020 to the right
        Assert.All(view.Tabs, t => Assert.Equal(2, t.Frame));   // tick 0 is DK2's odd turn 1

        view.Capture(world, 1640);
        Assert.Equal([88, 176, 264, 352, 440, 528, 616, 704], view.Tabs.Select(t => t.LocalX));

        view.OpenTab(1);
        Assert.Equal(posted[1].Id, view.Open?.Id);
        view.Hovered = 2;
        view.Capture(world, 1700);
        Assert.Equal([2, 0, 1], view.Tabs.Take(3).Select(t => t.Frame));

        // Dismissed, the first tab goes; the ninth record still waits, as only eight are examined.
        view.Hovered = -1;
        view.DismissTab(0);
        view.Capture(world, 1800);
        Assert.Equal(posted.Skip(1).Take(7).Select(m => m.Id), view.Tabs.Select(t => t.MessageId));
        view.DismissOpen();
        Assert.Null(view.Open);
    }
}
