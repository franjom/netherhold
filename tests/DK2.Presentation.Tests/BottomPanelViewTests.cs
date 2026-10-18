// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

public sealed class BottomPanelViewTests
{
    [InstallFact]
    public void OwnedTimedReaperBlocksAnotherSummonAndShowsWholeSeconds()
    {
        var db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        var world = WorldState.FromMap(db, GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "Level1")!,
            [PlayerIds.FirstKeeper], fogOfWarEnabled: false);
        CreatureDef horny = db.Creatures.Values.First(c => (c.Flags & CreatureFlags.IsHorny) != 0);
        CreatureState creature = world.SpawnCreature(db, horny.CreatureId, PlayerIds.FirstKeeper, new TilePos(10, 10));
        creature.IsOneShotReaper = true; creature.ReaperTicks = 43 * SimulationTiming.OriginalTicksPerGameSecond + 3;
        world.SetAvailability(PlayerIds.FirstKeeper, AvailabilityKind.Spell, 5, 3);
        var panel = new BottomPanelView(db, PlayerIds.FirstKeeper, (_, name) => name);
        panel.Capture(world);
        Assert.False(panel.Reaper!.Selectable);
        Assert.Equal(43, panel.ReaperSecondsRemaining);
        creature.IsOneShotReaper = false;
        panel.Capture(world);
        Assert.Null(panel.ReaperSecondsRemaining);
    }

    [InstallFact]
    public void CampaignTalismanSelectsTheOneShotSpellAndConsumesTheRetainedUse()
    {
        string install = Dk2InstallGate.Path;
        var level = GameDatabaseLoader.LoadCampaignLevel(install, "Level1")!;
        var db = GameDatabaseLoader.Load(install).ForLevel(null,
            level with { Flags = (ushort)(level.Flags | 0x10), TalismanPieces = 4 });
        var world = WorldState.FromMap(db, GameDatabaseLoader.LoadCampaignMap(install, "Level1")!,
            [PlayerIds.FirstKeeper], fogOfWarEnabled: false);
        world.SetAvailability(PlayerIds.FirstKeeper, AvailabilityKind.Spell, 5, 1);
        var panel = new BottomPanelView(db, PlayerIds.FirstKeeper, (_, name) => name);
        panel.Capture(world);
        Assert.Equal(14, panel.Reaper!.Key.Value);
        Assert.Equal(4, panel.TalismanPieces);
        Assert.True(panel.ReaperOneShotReady);
        Assert.True(panel.Select(panel.Reaper.Key));
        world.Player(PlayerIds.FirstKeeper).OneShotReaperUsed = true;
        panel.Capture(world);
        Assert.False(panel.ReaperOneShotReady);
        Assert.False(panel.Reaper.Selectable);
    }

    [InstallFact]
    public void ReaperUsesItsSeparateNativeControlAndNormalGridExcludesBothReaperSpells()
    {
        var db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        var world = WorldState.FromMap(db, GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "Level1")!,
            [PlayerIds.FirstKeeper], fogOfWarEnabled: false);
        var panel = new BottomPanelView(db, PlayerIds.FirstKeeper, (_, name) => name);
        panel.Capture(world);
        Assert.NotNull(panel.Reaper);
        Assert.DoesNotContain(panel.Choices, c => c.Kind == BottomPanelChoiceKind.Spell && (c.Key.Value == 5 || c.Key.Value == 14));
        Assert.False(panel.Reaper.Visible);
        world.SetAvailability(PlayerIds.FirstKeeper, AvailabilityKind.Spell, 5, 3);
        panel.Capture(world);
        Assert.True(panel.Reaper.Selectable);
        Assert.True(panel.Select(panel.Reaper.Key));
        Assert.Equal(panel.Reaper.Key, panel.Selection.Choice);
    }

    [InstallFact]
    public void UnknownAndAbsentRoomsCannotSelectAndHandCancelsTheSelectedRoom()
    {
        var db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        var world = WorldState.FromMap(db, GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "Level1")!,
            [PlayerIds.FirstKeeper], fogOfWarEnabled: false);
        var panel = new BottomPanelView(db, PlayerIds.FirstKeeper, (_, name) => name);
        var key = new BottomPanelChoiceKey(BottomPanelChoiceKind.Room, (byte)RoomTypeId.Lair);
        world.SetAvailability(PlayerIds.FirstKeeper, AvailabilityKind.Room, key.Value, 2);
        panel.Capture(world);
        Assert.True(panel.Choices.Single(c => c.Key == key).Unknown);
        Assert.False(panel.Select(key));
        world.SetAvailability(PlayerIds.FirstKeeper, AvailabilityKind.Room, key.Value, 1);
        panel.Capture(world);
        Assert.False(panel.Choices.Single(c => c.Key == key).Visible);
        Assert.False(panel.Select(key));
        world.SetAvailability(PlayerIds.FirstKeeper, AvailabilityKind.Room, key.Value, 3);
        panel.Capture(world);
        Assert.True(panel.Select(key));
        panel.SelectTool(PlayerToolMode.Hand);
        Assert.Null(panel.Selection.Choice);
        Assert.DoesNotContain(panel.Choices, c => c.Selected);
    }

    [InstallFact]
    public void RoomChoicesUseLocalizedNamesAndRetainUnaffordableSelection()
    {
        var db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        var world = WorldState.FromMap(db, GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "Level1")!,
            [PlayerIds.FirstKeeper], fogOfWarEnabled: false);
        var panel = new BottomPanelView(db, PlayerIds.FirstKeeper, (_, name) => "localized " + name);
        panel.Capture(world);
        Assert.Equal(RoomTypeId.Lair, (RoomTypeId)panel.Choices.First(c => c.Kind == BottomPanelChoiceKind.Room && c.Visible).Key.Value);
        BottomPanelChoice room = panel.Choices.First(c => c.Kind == BottomPanelChoiceKind.Room && c.Visible && c.Selectable);
        Assert.StartsWith("localized ", room.Name);
        Assert.True(panel.Select(room.Key));
        Assert.Equal(PlayerToolMode.BuildRoom, panel.Selection.Tool);
        Assert.Equal((RoomTypeId)room.Key.Value, panel.Selection.Room);
        panel.Capture(world);
        Assert.True(room.Selected);
        Assert.Equal(db.Rooms[panel.Selection.Room].Cost, room.Cost);
    }

    [InstallFact]
    public void CaptureIsReadOnlyAndAllocatesNothingAfterConstruction()
    {
        var db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        var world = WorldState.FromMap(db, GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "Level1")!,
            [PlayerIds.FirstKeeper], fogOfWarEnabled: false);
        var sim = new GameSimulation(db, world, seed: 7);
        var panel = new BottomPanelView(db, PlayerIds.FirstKeeper, (_, name) => name);
        panel.Capture(world);
        ulong hash = sim.StateHash();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) panel.Capture(world);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(hash, sim.StateHash());
    }

    [Fact]
    public void NativeGridUsesColumnMajorSlotsAndBottomAnchoredCoordinates()
    {
        Assert.Equal(new BottomPanelRect(548, 1612, 264, 264), BottomPanelLayout.Cell(0));
        Assert.Equal(new BottomPanelRect(820, 1612, 264, 264), BottomPanelLayout.Cell(1));
        Assert.Equal(new BottomPanelRect(472, 1460, 128, 128), BottomPanelLayout.Tab(0));
    }
}
