// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

public sealed class BottomPanelIndicatorsTests
{
    // 60 s at 4 turns a second is 240 turns; the file stores seconds in 20.12 fixed point.
    private const int SixtySeconds = 60 << 12;

    [Fact]
    public void RechargeIsFullWhenNeverCastOrOnceTheCooldownHasElapsed()
    {
        Assert.Equal(100, BottomPanelProgress.RechargePercent(0, 50, SixtySeconds));
        Assert.Equal(100, BottomPanelProgress.RechargePercent(10, 250, SixtySeconds));
        Assert.Equal(100, BottomPanelProgress.RechargePercent(10, 900, SixtySeconds));
        Assert.Equal(100, BottomPanelProgress.RechargePercent(10, 10, 0));
    }

    [Fact]
    public void RechargeFloorsThePerTurnRateBeforeMultiplyingByElapsedTurns()
    {
        // floor(409600 / 240) = 1706; 1706 x 120 >> 12 = 49, where exact arithmetic gives 50.
        Assert.Equal(49, BottomPanelProgress.RechargePercent(10, 130, SixtySeconds));
        Assert.Equal(0, BottomPanelProgress.RechargePercent(10, 10, SixtySeconds));
        Assert.Equal(0, BottomPanelProgress.RechargePercent(10, 12, SixtySeconds));
        Assert.Equal(99, BottomPanelProgress.RechargePercent(10, 249, SixtySeconds));
    }

    [Fact]
    public void ManufactureDividesTheLowPointsWordByTheCostWord()
    {
        Assert.Equal(25, BottomPanelProgress.ManufacturePercent(50, 200));
        Assert.Equal(0, BottomPanelProgress.ManufacturePercent(1, 200));
        Assert.Equal(25, BottomPanelProgress.ManufacturePercent(0x10000 + 50, 0x10000 + 200));
    }

    [Fact]
    public void ManufactureOfAZeroCostIsZero_ZeroCostIsOurGuess()
    {
        // DK2's IDIV would fault; nothing in the shipped data queues a zero cost.
        Assert.Equal(0, BottomPanelProgress.ManufacturePercent(50, 0));
    }

    [Fact]
    public void ResearchIsFullAtTheCostAndUnsignedBelowIt()
    {
        Assert.Equal(100, BottomPanelProgress.ResearchPercent(300, 300));
        Assert.Equal(100, BottomPanelProgress.ResearchPercent(0, 0));
        Assert.Equal(33, BottomPanelProgress.ResearchPercent(100, 300));
        Assert.Equal(0, BottomPanelProgress.ResearchPercent(2, 300));
        Assert.Equal(100, BottomPanelProgress.ResearchPercent(-1, 300));
    }

    [InstallFact]
    public void RechargeMarksOnlyARecastingSpellAndNeverCastIsFull()
    {
        (GameDatabase db, WorldState world, PlayerId keeper) = Level1();
        var panel = new BottomPanelView(db, keeper, (_, name) => name);
        BottomPanelChoice spell = panel.Choices.First(c => c.Kind == BottomPanelChoiceKind.Spell &&
            ((uint)(db.KeeperSpellById(c.Key.Value)!.CooldownRaw * 4) >> 12) >= 4);
        SetRow(world, keeper, AvailabilityKind.Spell, spell.Key.Value, 3, lastCast: 0);
        panel.Capture(world);
        Assert.Null(spell.Indicators.RechargePercent);

        // The world stays at tick 0 outside the simulation, so a negative stamp stands for "cast earlier".
        long cooldown = (uint)(db.KeeperSpellById(spell.Key.Value)!.CooldownRaw * 4) >> 12;
        SetRow(world, keeper, AvailabilityKind.Spell, spell.Key.Value, 3, lastCast: -cooldown / 2);
        panel.Capture(world);
        Assert.Equal(BottomPanelProgress.RechargePercent(-cooldown / 2, 0, db.KeeperSpellById(spell.Key.Value)!.CooldownRaw),
            spell.Indicators.RechargePercent);

        SetRow(world, keeper, AvailabilityKind.Spell, spell.Key.Value, 3, lastCast: -cooldown);
        panel.Capture(world);
        Assert.Null(spell.Indicators.RechargePercent);
    }

    [InstallFact]
    public void ManufactureMarksOnlyTheHeadOrdersItemAndNotAtZero_DividesBySlotZerosCost_Q618()
    {
        (GameDatabase db, WorldState world, PlayerId keeper) = Level1();
        DoorDef first = db.Doors.Values.First(), second = db.Doors.Values.Skip(1).First();
        SetRow(world, keeper, AvailabilityKind.Door, first.DoorId, 3);
        SetRow(world, keeper, AvailabilityKind.Door, second.DoorId, 3);
        ref PlayerState state = ref world.Player(keeper);
        state.ManufactureQueue.Clear();
        state.ManufactureRingTail = 0;
        ManufactureRules.Enqueue(ref state, new ManufactureOrder(ManufactureRules.DoorKind, first.DoorId, 200));
        ManufactureRules.Enqueue(ref state, new ManufactureOrder(ManufactureRules.DoorKind, second.DoorId, 100));
        state.ManufacturePoints = 50;
        var panel = new BottomPanelView(db, keeper, (_, name) => name);
        panel.Capture(world);
        Assert.Equal((byte?)25, Door(panel, first).Indicators.ManufacturePercent);
        Assert.Null(Door(panel, second).Indicators.ManufacturePercent);

        // The first order paid: the head is the 100-point one, and 0x00506B70 still divides by
        // slot 0's 200, so 50 points show 25, not 50.
        world.Player(keeper).ManufactureQueue.RemoveAt(0);
        panel.Capture(world);
        Assert.Equal((byte?)25, Door(panel, second).Indicators.ManufacturePercent);

        world.Player(keeper).ManufacturePoints = 1;
        panel.Capture(world);
        Assert.Null(Door(panel, second).Indicators.ManufacturePercent);
    }

    [InstallFact]
    public void ResearchMarksTheTargetSlotAndTellsTheUnknownIconFromTheGoldFrame()
    {
        (GameDatabase db, WorldState world, PlayerId keeper) = Level1();
        var lair = new BottomPanelChoiceKey(BottomPanelChoiceKind.Room, (byte)RoomTypeId.Lair);
        SetRow(world, keeper, AvailabilityKind.Room, lair.Value, 2);
        db.Variables.Research.Clear();
        db.Variables.Research.Add(new ResearchEntry(ResearchTreeRules.KindRoom, lair.Value, 400));
        ref PlayerState state = ref world.Player(keeper);
        state.ResearchTarget = 1; state.ResearchPoints = 100;
        var panel = new BottomPanelView(db, keeper, (_, name) => name);
        panel.Capture(world);
        BottomPanelChoice room = panel.Choice(lair)!;
        Assert.Equal((byte?)25, room.Indicators.ResearchPercent);
        Assert.True(room.Indicators.ResearchOnUnknown);
        Assert.DoesNotContain(panel.Choices, c => c != room && c.Indicators.ResearchPercent.HasValue);

        SetRow(world, keeper, AvailabilityKind.Room, lair.Value, 3);
        panel.Capture(world);
        Assert.False(room.Indicators.ResearchOnUnknown);

        world.Player(keeper).ResearchPoints = 0;
        panel.Capture(world);
        Assert.Null(room.Indicators.ResearchPercent);
        world.Player(keeper).ResearchPoints = 100; world.Player(keeper).ResearchTarget = 0;
        panel.Capture(world);
        Assert.Null(room.Indicators.ResearchPercent);
    }

    [InstallFact]
    public void RechargeAtZeroPercentStillMarksTheSlot()
    {
        (GameDatabase db, WorldState world, PlayerId keeper) = Level1();
        var panel = new BottomPanelView(db, keeper, (_, name) => name);
        // One turn into a cooldown over 100 turns floors to 0, which DK2 still treats as recharging.
        BottomPanelChoice spell = panel.Choices.First(c => c.Kind == BottomPanelChoiceKind.Spell &&
            ((uint)(db.KeeperSpellById(c.Key.Value)!.CooldownRaw * 4) >> 12) > 100);
        SetRow(world, keeper, AvailabilityKind.Spell, spell.Key.Value, 3, lastCast: -1);
        panel.Capture(world);
        Assert.Equal((byte?)0, spell.Indicators.RechargePercent);
    }

    [InstallFact]
    public void ResearchOnAnAvailabilityFourRoomIsStateTwo()
    {
        (GameDatabase db, WorldState world, PlayerId keeper) = Level1();
        var lair = new BottomPanelChoiceKey(BottomPanelChoiceKind.Room, (byte)RoomTypeId.Lair);
        SetRow(world, keeper, AvailabilityKind.Room, lair.Value, 4);
        RoomRules.Place(db, ref world.Tile(1, 1), db.Room(RoomTypeId.Lair), keeper);   // availability 4 shows only an owned room
        db.Variables.Research.Clear();
        db.Variables.Research.Add(new ResearchEntry(ResearchTreeRules.KindRoom, lair.Value, 400));
        world.Player(keeper).ResearchTarget = 1; world.Player(keeper).ResearchPoints = 200;
        var panel = new BottomPanelView(db, keeper, (_, name) => name);
        panel.Capture(world);
        BottomPanelChoice room = panel.Choice(lair)!;
        Assert.True(room.Visible);
        Assert.False(room.Unknown);
        Assert.Equal((byte?)50, room.Indicators.ResearchPercent);
        Assert.True(room.Indicators.ResearchOnUnknown);
    }

    private static (GameDatabase, WorldState, PlayerId) Level1()
    {
        var db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        var world = WorldState.FromMap(db, GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "Level1")!,
            [PlayerIds.FirstKeeper], fogOfWarEnabled: false);
        return (db, world, PlayerIds.FirstKeeper);
    }

    private static BottomPanelChoice Door(BottomPanelView panel, DoorDef door) =>
        panel.Choice(new BottomPanelChoiceKey(BottomPanelChoiceKind.Door, door.DoorId))!;

    private static void SetRow(WorldState world, PlayerId keeper, AvailabilityKind kind, int id, int value, long lastCast = 0)
    {
        List<AvailabilityState> rows = world.Player(keeper).Availabilities;
        rows.RemoveAll(r => r.Kind == kind && r.TypeId == id);
        rows.Add(new AvailabilityState(kind, id, value, LastCastTick: lastCast));
    }
}
