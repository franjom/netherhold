// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Combat;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>State 1's walk back to a fight withdrawn from (0x004773D0, ADR-0225).</summary>
public class FightReturnTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;

    [Fact]
    public void AHealedCreatureWalksBackToWhereItWithdrew_WithinFiveHundredTurns()
    {
        (GameDatabase db, WorldState world, CreatureState goblin) = Withdrawn();
        (GameSimulation sim, ListEventSink sink) = Simulation(db, world);

        sim.Tick();

        Assert.Contains(sink.Events, e => e.Kind == "returns_to_fight" && e.Detail.Contains("to=(9,9)"));
        Assert.Equal(CreatureStateMachine.Idle, goblin.UtilityNewState);
        Assert.Equal(new TilePos(9, 9), new TilePos(goblin.Body.PathTargetPos.X >> 16, goblin.Body.PathTargetPos.Y >> 16));
        Assert.Equal(0, goblin.OldFightTileX);
        for (int t = 0; t < 200 && goblin.Tile != new TilePos(9, 9); t++) sim.Tick();
        Assert.Equal(new TilePos(9, 9), goblin.Tile);
        // The walk resumes 1: arrived, the creature is back in state 1, not left in the walk.
        for (int t = 0; t < 40 && goblin.Dk2StateId != CreatureStateMachine.Idle; t++) sim.Tick();
        Assert.Equal(CreatureStateMachine.Idle, goblin.Dk2StateId);
    }

    /// <summary>The test is strict: <c>(MaxHealth × 3) &gt;&gt; 2 &lt; Health</c>. At exactly three
    /// quarters the creature stays, and keeps its stamp.</summary>
    [Fact]
    public void AtThreeQuartersHealthTheCreatureStays_AndKeepsTheStamp()
    {
        (GameDatabase db, WorldState world, CreatureState goblin) = Withdrawn();
        int max = CreatureHealthRules.MaxHealth(db, db.CreatureById(goblin.CreatureTypeId), goblin);
        goblin.Health = (max * 3) >> 2;
        (GameSimulation sim, ListEventSink sink) = Simulation(db, world);

        sim.Tick();

        Assert.DoesNotContain(sink.Events, e => e.Kind == "returns_to_fight");
        Assert.Equal(9, goblin.OldFightTileX);
    }

    [Fact]
    public void AStampFiveHundredTurnsOld_IsForgotten()
    {
        (GameDatabase db, WorldState world, CreatureState goblin) = Withdrawn();
        goblin.OldFightTileX = 0;
        (GameSimulation sim, ListEventSink sink) = Simulation(db, world);
        sim.Run(FightReturnRules.ReturnWindowTicks);
        sink.Clear();
        goblin.OldFightTileX = goblin.OldFightTileY = 9;
        goblin.OldFightTick = 0;
        Assert.True(CreatureStateMachine.TrySetState(new StateHookContext(world, db), goblin, CreatureStateMachine.Idle));   // state 1's arm, not 3's

        sim.Tick();

        Assert.DoesNotContain(sink.Events, e => e.Kind == "returns_to_fight");
        Assert.Equal(0, goblin.OldFightTileX);
    }

    private static (GameDatabase, WorldState, CreatureState) Withdrawn()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);
        for (int y = 1; y < world.Height - 1; y++)
            for (int x = 1; x < world.Width - 1; x++)
                world.Tile(x, y).TerrainId = Dk2TerrainFixture.DirtPath;
        CreatureState goblin = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(3, 5));
        goblin.OldFightTileX = 9;
        goblin.OldFightTileY = 9;
        goblin.OldFightTick = 1;
        return (db, world, goblin);
    }

    private static (GameSimulation, ListEventSink) Simulation(GameDatabase db, WorldState world)
    {
        var sim = new GameSimulation(db, world, 7, [new CreatureJobSystem(), new CreatureMovementSystem()]);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        return (sim, sink);
    }
}
