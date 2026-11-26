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

/// <summary>The navigation entries and the battle tab's click, moved out of Unity's runner (plan step 2c).</summary>
public sealed class NavigationEntriesTests
{
    private static readonly PlayerId Keeper = PressFixture.Keeper;

    [Fact]
    public void DoorsAndTrapsAreListedNewestFirstIntoAClearedList()
    {
        DoorState[] doors =
        [
            new() { X = 1, Y = 2, DoorId = 2, PlayerId = Keeper.Value, Unknown_0011 = new byte[3] },
            new() { X = 3, Y = 4, DoorId = 3, PlayerId = 4, Unknown_0011 = new byte[3] },
        ];
        var into = new List<ThingNavigationEntry> { new(9, Keeper, 9, new TilePos(9, 9)) };

        ThingNavigation.DoorEntries(doors, into);

        Assert.Equal([new ThingNavigationEntry(0, new PlayerId(4), 3, new TilePos(3, 4)),
            new ThingNavigationEntry(0, Keeper, 2, new TilePos(1, 2))], into);

        TrapState[] traps =
        [
            new() { X = 5, Y = 6, TrapId = 7, PlayerId = Keeper.Value },
            new() { X = 7, Y = 8, TrapId = 1, PlayerId = Keeper.Value },
        ];
        ThingNavigation.TrapEntries(traps, into);

        Assert.Equal([new ThingNavigationEntry(0, Keeper, 1, new TilePos(7, 8)),
            new ThingNavigationEntry(0, Keeper, 7, new TilePos(5, 6))], into);
    }

    [InstallFact]
    public void RoomsAreListedInTheRoomIndexOrderWithTheirFirstTileAndCentre()
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        byte path = db.Terrain.Values.Single(t => t.Name == "Claimed Path").TerrainId;
        byte lair = db.Rooms[RoomTypeId.Lair].TerrainId;
        MapTile[] tiles = [.. Enumerable.Repeat(new MapTile(path, Keeper.Value, 0, 0), 21)];
        tiles[7 + 1] = tiles[7 + 2] = new MapTile(lair, Keeper.Value, 0, 0);
        tiles[7 + 5] = new MapTile(lair, 4, 0, 0);
        var world = WorldState.FromMap(db, new LevelMap(7, 3, tiles), [PlayerIds.Neutral, Keeper.Value, 4]);
        IReadOnlyList<RoomInstance> rooms = RoomIndex.Build(world, db);
        var into = new List<RoomNavigationEntry>();

        RoomNavigation.Entries(world, db, into);

        var lairs = into.Where(e => e.Type == RoomTypeId.Lair).ToList();
        Assert.Equal(2, lairs.Count);
        Assert.Equal(new TilePos(1, 1), lairs[0].Identity);
        Assert.Equal(Keeper, lairs[0].Owner);
        Assert.Equal(new TilePos(5, 1), lairs[1].Identity);
        Assert.Equal(new PlayerId(4), lairs[1].Owner);
        Assert.Equal(rooms.Count(r => r.Definition.TypeId is not null), into.Count);
        RoomInstance first = rooms.First(r => r.Definition.TypeId == RoomTypeId.Lair);
        Assert.Equal(new TilePos(first.CentreFieldX, first.CentreFieldY), lairs[0].Focus);
    }

    [Fact]
    public void TheBattleTabDoesNothingWhileAMenuIsModal()
    {
        GameDatabase db = PressFixture.Database();
        WorldState world = PressFixture.World(db);
        var controls = new ClassicHudControls();
        controls.Apply(ClassicHudAction.ToggleOptions);

        Assert.Equal(default, BattleTabView.Click(BattleTabButton.CallToArms, 0, controls, world, db, Keeper,
            new ThingNavigation(Keeper), []));
    }

    [Fact]
    public void TheBattleTabDoesNothingWhileInputIsSuppressed()
    {
        GameDatabase db = PressFixture.Database();
        WorldState world = PressFixture.World(db);
        var controls = new ClassicHudControls();
        controls.UpdateResult(new HudResultState(HasResult: true));
        Assert.True(controls.InputSuppressed);
        Assert.False(controls.Modal);

        Assert.Equal(default, BattleTabView.Click(BattleTabButton.CallToArms, 0, controls, world, db, Keeper,
            new ThingNavigation(Keeper), []));
        Assert.Equal(default, BattleTabView.Click(BattleTabButton.Battle, 1, controls, world, db, Keeper,
            new ThingNavigation(Keeper), []));
    }

    [Fact]
    public void TheAlarmsRightClickGlidesToTheViewersRingingAlarmTrapAndWalksOn()
    {
        GameDatabase db = PressFixture.Database([], [], [PressFixture.Trap(4, BattleTabView.AlarmShotTypeId)]);
        WorldState world = PressFixture.World(db);
        ThingProjection.Place(world, db,
        [
            new TrapThing(new byte[16]) { X = 1, Y = 2, TrapId = 4, PlayerId = Keeper.Value, NumberOfShots = 1 },
            new TrapThing(new byte[16]) { X = 5, Y = 6, TrapId = 4, PlayerId = Keeper.Value, NumberOfShots = 1 },
        ]);
        // Both ring: the turn has not passed their recharge deadline.
        foreach (TrapState trap in world.Traps)
        {
            trap.RechargeAt = world.CurrentTick.Value + 100;
        }

        var traps = new ThingNavigation(Keeper);
        var scratch = new List<ThingNavigationEntry>();

        // Newest first (descending id): the trap at (5, 6), then the one at (1, 2).
        Assert.Equal(new BattleTabClick(ClassicHudAction.None, null, new TilePos(5, 6)),
            BattleTabView.Click(BattleTabButton.Alarm, 1, new ClassicHudControls(), world, db, Keeper, traps, scratch));
        Assert.Equal(new BattleTabClick(ClassicHudAction.None, null, new TilePos(1, 2)),
            BattleTabView.Click(BattleTabButton.Alarm, 1, new ClassicHudControls(), world, db, Keeper, traps, scratch));
    }

    [Fact]
    public void TheBattleTabsClicksZoomPostOrGlide()
    {
        GameDatabase db = PressFixture.Database();
        WorldState world = PressFixture.World(db);
        var controls = new ClassicHudControls();
        var traps = new ThingNavigation(Keeper);

        Assert.Equal(new BattleTabClick(ClassicHudAction.NavigateFight, null, null),
            BattleTabView.Click(BattleTabButton.Battle, 1, controls, world, db, Keeper, traps, []));
        Assert.Equal(new BattleTabClick(ClassicHudAction.None, new RemoveCallToArmsCommand(Keeper), null),
            BattleTabView.Click(BattleTabButton.CallToArms, 0, controls, world, db, Keeper, traps, []));
        Assert.Equal(default, BattleTabView.Click(BattleTabButton.CallToArms, 1, controls, world, db, Keeper, traps, []));
        world.Player(Keeper).CallToArms = new CallToArmsState(true, 0, 5, 6, 0);
        Assert.Equal(new BattleTabClick(ClassicHudAction.None, null, new TilePos(5, 6)),
            BattleTabView.Click(BattleTabButton.CallToArms, 1, controls, world, db, Keeper, traps, []));
        Assert.Equal(default, BattleTabView.Click(BattleTabButton.Alarm, 1, controls, world, db, Keeper, traps, []));
        Assert.Equal(default, BattleTabView.Click(BattleTabButton.Battle, 0, controls, world, db, Keeper, traps, []));
    }
}
