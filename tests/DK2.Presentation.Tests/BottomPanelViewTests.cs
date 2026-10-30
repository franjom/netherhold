// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Data.Media;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

public sealed class BottomPanelViewTests
{
    [InstallFact]
    public void AVisibleChoicePlaysItsPressAndReleaseOnceAndUnknownChoicesStaySilent()
    {
        var db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        var world = WorldState.FromMap(db, GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "Level1")!,
            [PlayerIds.FirstKeeper], fogOfWarEnabled: false);
        var panel = new BottomPanelView(db, PlayerIds.FirstKeeper, (_, name) => name);
        var key = new BottomPanelChoiceKey(BottomPanelChoiceKind.Room, (byte)RoomTypeId.Lair);
        world.SetAvailability(PlayerIds.FirstKeeper, AvailabilityKind.Room, key.Value, 2);
        panel.Capture(world);
        Assert.False(panel.PressChoice(key)); Assert.Empty(panel.Sounds.Pending);
        world.SetAvailability(PlayerIds.FirstKeeper, AvailabilityKind.Room, key.Value, 3);
        panel.Capture(world);
        Assert.True(panel.PressChoice(key)); Assert.False(panel.PressChoice(key));
        Assert.Null(panel.Selection.Choice);
        panel.ReleaseChoice(); panel.ReleaseChoice();
        Assert.Equal(new uint[] { 0x104, 0x136 }, panel.Sounds.Pending.Select(r => r.Id.Value));
        Assert.All(panel.Sounds.Pending, r => Assert.Equal(new SoundCategory("gui_button_icon_large"), r.Category));
    }

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
    public void SpellsAndWorkshopTabsShowOnlyWhenOneOfTheirChoicesIsPresent()
    {
        var db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        var world = WorldState.FromMap(db, GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "Level1")!,
            [PlayerIds.FirstKeeper], fogOfWarEnabled: false);
        PlayerId keeper = PlayerIds.FirstKeeper;
        foreach (KeeperSpellDef spell in db.KeeperSpells.Values) world.SetAvailability(keeper, AvailabilityKind.Spell, spell.KeeperSpellId, 1);
        foreach (DoorDef door in db.Doors.Values) world.SetAvailability(keeper, AvailabilityKind.Door, door.DoorId, 1);
        foreach (TrapDef trap in db.Traps.Values) world.SetAvailability(keeper, AvailabilityKind.Trap, trap.TrapId, 1);
        var panel = new BottomPanelView(db, keeper, (_, name) => name);

        world.SetAvailability(keeper, AvailabilityKind.Spell, 5, 3);
        TrapDef? hidden = db.Traps.Values.FirstOrDefault(t => (t.Flags & 0x2000) != 0);
        if (hidden != null) world.SetAvailability(keeper, AvailabilityKind.Trap, hidden.TrapId, 3);
        panel.Capture(world);
        Assert.True(panel.TabVisible(BottomPanelCategory.Creatures));
        Assert.True(panel.TabVisible(BottomPanelCategory.Rooms));
        Assert.False(panel.TabVisible(BottomPanelCategory.Spells));
        Assert.False(panel.TabVisible(BottomPanelCategory.Workshop));
        Assert.True(panel.TabDimmed(BottomPanelCategory.Workshop));
        Assert.False(panel.TabDimmed(BottomPanelCategory.Rooms));
        bool ownsCreature = world.Creatures.Any(c => c.OwnerPlayerId == keeper && CreatureStateMachine.HasState(c));
        Assert.Equal(!ownsCreature, panel.TabDimmed(BottomPanelCategory.Creatures));

        world.SetAvailability(keeper, AvailabilityKind.Spell, db.KeeperSpells.Values.First(s => s.KeeperSpellId is not 5 and not 14).KeeperSpellId, 2);
        world.SetAvailability(keeper, AvailabilityKind.Door, db.Doors.Values.First().DoorId, 3);
        panel.Capture(world);
        Assert.True(panel.TabVisible(BottomPanelCategory.Spells));
        Assert.True(panel.TabVisible(BottomPanelCategory.Workshop));
    }

    [InstallFact]
    public void StoredSpecialsFollowTheRoomListThenNewestObjectFirstAsAStandInAndOnlyOwnLibraryBoxesCount()
    {
        var db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        var world = WorldState.FromMap(db, GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "Level1")!,
            [PlayerIds.FirstKeeper], fogOfWarEnabled: false);
        PlayerId keeper = PlayerIds.FirstKeeper;
        RoomRules.Place(db, ref world.Tile(1, 1), db.Room(RoomTypeId.Library), keeper);
        RoomRules.Place(db, ref world.Tile(3, 1), db.Room(RoomTypeId.Library), keeper);
        world.Tile(1, 1).RoomListPosition = new RoomListPosition(5);
        world.Tile(3, 1).RoomListPosition = new RoomListPosition(9);   // the newer room heads the list
        ObjectState older = world.SpawnObject(db, 117, keeper, world.TileIndex(new TilePos(1, 1)));
        ObjectState newer = world.SpawnObject(db, 124, keeper, world.TileIndex(new TilePos(1, 1)));
        ObjectState other = world.SpawnObject(db, 129, keeper, world.TileIndex(new TilePos(3, 1)));
        world.SpawnObject(db, 127, keeper, world.TileIndex(new TilePos(5, 5)));   // not in a Library
        world.SpawnObject(db, 140, PlayerIds.Neutral, world.TileIndex(new TilePos(1, 1)));   // not the keeper's

        var panel = new BottomPanelView(db, keeper, (_, name) => name);
        panel.Capture(world);

        Assert.Equal([other.Id, newer.Id, older.Id], panel.StoredSpecials.Select(c => c.ObjectId));
        Assert.All(panel.StoredSpecials, c =>
        {
            Assert.Equal(BottomPanelChoiceKind.Special, c.Kind);
            Assert.Equal(BottomPanelCategory.Spells, c.Category);
            Assert.True(c.Visible && c.Selectable && c.Affordable);
        });
        Assert.Equal(db.ObjectById(124)!.Name, panel.StoredSpecials[1].Name);

        world.RemoveObject(newer.Id);
        panel = new BottomPanelView(db, keeper, (_, name) => name);   // the list is read once a tick
        panel.Capture(world);
        Assert.Equal([other.Id, older.Id], panel.StoredSpecials.Select(c => c.ObjectId));
    }

    [InstallFact]
    public void DoorsAndTrapsNeedAWorkshopRoomsAndSpellsDoNot()
    {
        var db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        var world = WorldState.FromMap(db, GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "Level1")!,
            [PlayerIds.FirstKeeper], fogOfWarEnabled: false);
        PlayerId keeper = PlayerIds.FirstKeeper;
        var panel = new BottomPanelView(db, keeper, (_, name) => name);

        panel.Capture(world);
        Assert.All(panel.Choices, c => Assert.Equal(
            c.Kind is BottomPanelChoiceKind.Door or BottomPanelChoiceKind.Trap, c.NeedsWorkshop));

        RoomRules.Place(db, ref world.Tile(1, 1), db.Room(RoomTypeId.Workshop), keeper);
        panel = new BottomPanelView(db, keeper, (_, name) => name);   // owned rooms are read once a tick
        panel.Capture(world);
        Assert.All(panel.Choices, c => Assert.False(c.NeedsWorkshop));
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
        BottomPanelGrid grid = BottomPanelLayout.Grid(BottomPanelLayout.Span, 6);
        Assert.Equal(new BottomPanelRect(548, 1612, 264, 264), grid.Slot(BottomPanelLayout.Panel, 0));
        Assert.Equal(new BottomPanelRect(820, 1612, 264, 264), grid.Slot(BottomPanelLayout.Panel, 1));
        Assert.Equal(new BottomPanelRect(472, 1460, 128, 128), BottomPanelLayout.Tab(0));
    }
}
