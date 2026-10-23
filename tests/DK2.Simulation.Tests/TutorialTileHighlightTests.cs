// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Tests;

public class TutorialTileHighlightTests
{
    [Fact]
    public void AnIndefiniteRectangleIsInclusiveAndOnlyTheScriptClearsIt()
    {
        var (db, world) = Setup();
        Run(db, world, 1, true, 0);
        Assert.True(world.TileHighlights.Contains(new TilePos(2, 3)));
        Assert.True(world.TileHighlights.Contains(new TilePos(3, 4)));
        Assert.False(world.TileHighlights.Contains(new TilePos(4, 4)));
        Assert.Equal(-1, world.TileHighlights.Slots[0].RemainingTicks);
        new GameSimulation(db, world, 1, []).Run(20);
        Assert.True(world.TileHighlights.Contains(new TilePos(2, 3)));
        Run(db, world, 1, false, 0);
        Assert.False(world.TileHighlights.HasActive);
    }

    [Theory]
    [InlineData(2, 2)]
    [InlineData(1, 1)]
    [InlineData(-2, 20)]
    public void FiniteDurationsCountGameTicksAndNegativeDurationsRemain(int durationTicks, int ticks)
    {
        var (db, world) = Setup();
        Run(db, world, 1, true, durationTicks);
        var sim = new GameSimulation(db, world, 1, []);
        sim.Run(ticks - 1);
        Assert.True(world.TileHighlights.HasActive);
        sim.Tick();
        Assert.Equal(durationTicks < 0, world.TileHighlights.HasActive);
    }

    [Fact]
    public void ExpirationPrecedesTheTurnsTriggerActions()
    {
        var (db, world) = Setup();
        var sim = new GameSimulation(db, world, 1, [], new TriggerTree([Record(1, true, 1) with { RepeatCount = 1 }], 0xFFFF));
        sim.Tick();
        Assert.Equal(1, world.TileHighlights.Slots[0].RemainingTicks);
        sim.Tick();
        Assert.False(world.TileHighlights.HasActive);
    }

    [Fact]
    public void FourSlotsChooseTheFirstFreeOrMatchingEntryAndClearOnlyOneMatch()
    {
        var (db, world) = Setup();
        for (byte id = 1; id <= 5; id++) Run(db, world, id, true, 0);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, world.TileHighlights.Slots.ToArray().Select(s => s.ActionPoint.Value));
        Run(db, world, 2, false, 0);
        Run(db, world, 4, true, 0);
        Assert.Equal(new byte[] { 1, 4, 3, 4 }, world.TileHighlights.Slots.ToArray().Select(s => s.ActionPoint.Value));
        Run(db, world, 4, false, 0);
        Assert.Equal(new byte[] { 1, 0, 3, 4 }, world.TileHighlights.Slots.ToArray().Select(s => s.ActionPoint.Value));
    }

    [Fact]
    public void AnUnknownActionPointRetainsTheSlotsPreviousRectangle()
    {
        var (db, world) = Setup();
        Run(db, world, 1, true, 0); Run(db, world, 1, false, 0); Run(db, world, 99, true, 0);
        Assert.Equal(new ActionPointId(99), world.TileHighlights.Slots[0].ActionPoint);
        Assert.True(world.TileHighlights.Contains(new TilePos(2, 3)));
    }

    [Fact]
    public void RectanglesAndCountdownsAreHashedAndSurviveSaveRestore()
    {
        var (db, world) = Setup();
        var sim = new GameSimulation(db, world, 1, []);
        ulong before = sim.StateHash();
        Run(db, world, 1, true, 3); Run(db, world, 2, true, 0);
        Assert.NotEqual(before, sim.StateHash());
        sim.Tick();
        var restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db);
        Assert.Equal(sim.StateHash(), restored.StateHash());
        Assert.Equal(world.TileHighlights.Slots.ToArray(), restored.World.TileHighlights.Slots.ToArray());
        sim.Run(2); restored.Run(2);
        Assert.Equal(sim.StateHash(), restored.StateHash());
        Assert.Equal(ActionPointId.None, restored.World.TileHighlights.Slots[0].ActionPoint);
        Assert.Equal(new ActionPointId(2), restored.World.TileHighlights.Slots[1].ActionPoint);
    }

    [Fact]
    public void Action16IsPerformedAsTheWorldActionItIs()
    {
        var db = Dk2TerrainFixture.Database();
        var world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [PlayerIds.Neutral, PlayerIds.FirstKeeper]);
        var record = new ActionTriggerRecord(new byte[16])
        {
            Body = new byte[8], Type = 16, TriggerId = 0xFFFF,
            Parameters = new Dictionary<string, long> { ["ActionPointId"] = 1, ["Available"] = 1, ["Value"] = 0 },
        };
        var tree = new TriggerTree([record], 0xFFFF);
        var result = new TriggerExecutor(tree, world.Triggers, world, db).Run(1);
        Assert.Empty(result.Partial);
    }

    private static (GameDatabase, WorldState) Setup()
    {
        var db = Dk2TerrainFixture.Database();
        var world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [PlayerIds.Neutral, PlayerIds.FirstKeeper]);
        ThingProjection.Place(world, db, Enumerable.Range(1, 5).Select(id => new ActionPointThing(new byte[24])
        { ActionPointId = (byte)id, StartX = 2, StartY = 3, EndX = 3, EndY = 4, Name = "test" }));
        return (db, world);
    }

    private static ActionTriggerRecord Record(byte id, bool enabled, int durationTicks) => new(new byte[16])
    {
        Body = new byte[8], Type = 16, TriggerId = 0xFFFF,
        Parameters = new Dictionary<string, long> { ["ActionPointId"] = id, ["Available"] = enabled ? 1 : 0, ["Value"] = durationTicks },
    };

    private static void Run(GameDatabase db, WorldState world, byte id, bool enabled, int durationTicks) =>
        new TriggerExecutor(new TriggerTree([Record(id, enabled, durationTicks)], 0xFFFF), world.Triggers, world, db).Run(1);
}
