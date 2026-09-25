// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>A creature put down by the hand: 76's stun, 257's room drop and 250 (hand-drop-landing.md, ADR-0226).</summary>
public class DropLandingTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte GoblinId = Dk2TerrainFixture.GoblinCreatureId;
    private static readonly TilePos Floor = new(2, 2);

    /// <summary>Off a room: stunned for the definition's <c>StunDuration</c> (Q201: drops flat,
    /// cannot fight, 2–3 s in the shipped data), up in 257, then 250 until 50 turns past the state
    /// word <c>+0x7E</c>, then idle.</summary>
    [Fact]
    public void AnOwnCreatureDroppedOnPlainFloor_IsStunned_ThenStandsIn250_ThenIdles()
    {
        (GameDatabase db, WorldState world) = World();
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(3, 3));
        (GameSimulation sim, ListEventSink sink) = Simulation(db, world);
        int stun = SimulationTiming.TicksFromSeconds((int)db.CreatureById(GoblinId).StunDuration.ToDouble());

        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, goblin.Id)));
        goblin.StateDeadlineTick = sim.World.CurrentTick.Value;
        sim.Tick(InputFrame.Of(new DropCommand(Keeper, Floor)));
        long dropped = sim.World.CurrentTick.Value;
        Assert.Equal(255, goblin.Dk2StateId);

        while (goblin.Dk2StateId == 255 && sim.World.CurrentTick.Value < dropped + 200) sim.Tick();
        Assert.InRange(sim.World.CurrentTick.Value - dropped, stun - 1, stun + 2);
        Assert.Equal(DropLandingRules.StandingState, goblin.Dk2StateId);
        Assert.Contains(sink.Events, e => e.Kind == "standing");

        long deadline = goblin.StateDeadlineTick + DropLandingRules.StandTicks;
        while (goblin.Dk2StateId == DropLandingRules.StandingState && sim.World.CurrentTick.Value < deadline + 20) sim.Tick();
        Assert.InRange(sim.World.CurrentTick.Value, deadline, deadline + 2);
        Assert.NotEqual(DropLandingRules.StandingState, goblin.Dk2StateId);
    }

    [Fact]
    public void AWorkerIsNotStunned()
    {
        (GameDatabase db, WorldState world) = World();
        CreatureState imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(3, 3));
        (GameSimulation sim, _) = Simulation(db, world);

        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, imp.Id)));
        sim.Tick(InputFrame.Of(new DropCommand(Keeper, Floor)));

        Assert.NotEqual(255, imp.Dk2StateId);
    }

    /// <summary>The owner's exception (Q201): a prisoner put down outside a cell goes free, not
    /// stunned. It is not the dropping keeper's own creature.</summary>
    [Fact]
    public void AFreedPrisonerIsNotStunned()
    {
        (GameDatabase db, WorldState world) = World();
        CreatureState captive = world.SpawnCreature(db, GoblinId, Keeper + 1, new TilePos(3, 3));
        captive.PrisonOwnerPlayerId = Keeper;
        (GameSimulation sim, ListEventSink sink) = Simulation(db, world);

        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, captive.Id)));
        sim.Tick(InputFrame.Of(new DropCommand(Keeper, Floor)));

        Assert.Contains(sink.Events, e => e.Kind == "prisoner_freed");
        Assert.NotEqual(255, captive.Dk2StateId);
    }

    private static (GameDatabase, WorldState) World()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper, Keeper + 1]);
        return (db, world);
    }

    private static (GameSimulation, ListEventSink) Simulation(GameDatabase db, WorldState world)
    {
        var sim = new GameSimulation(db, world, 7,
            [new HandSystem(), new CreatureStatusSystem(), new CreatureMovementSystem(), new CreatureJobSystem()]);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        return (sim, sink);
    }
}
