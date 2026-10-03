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
    /// cannot fight, 2–3 s in the shipped data), up in 257 for the Goblin's eleven ticks (every
    /// Level 1–3 capture), then 250 for fifty turns from getting up, then idle. The state word
    /// <c>+0x7E</c> is stamped by the drop (<c>0x004E8DA0</c>), so a stale deadline from long ago
    /// does not shorten the stand (ADR-0345; before it, three of Level 1 seed 13's six Goblins
    /// idled at once).</summary>
    [Fact]
    public void AnOwnCreatureDroppedOnPlainFloor_IsStunned_GetsUpForElevenTicks_ThenStandsFiftyFromGettingUp_WhateverTheOldDeadline()
    {
        (GameDatabase db, WorldState world) = World();
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(3, 3));
        (GameSimulation sim, ListEventSink sink) = Simulation(db, world);
        int stun = SimulationTiming.TicksFromSeconds((int)db.CreatureById(GoblinId).StunDuration.ToDouble());
        Assert.Equal(11, DropLandingRules.GetUpTicks(db.CreatureById(GoblinId)));

        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, goblin.Id)));
        goblin.StateDeadlineTick = sim.World.CurrentTick.Value - 1000;
        sim.Tick(InputFrame.Of(new DropCommand(Keeper, Floor)));
        long dropped = sim.World.CurrentTick.Value;
        Assert.Equal(255, goblin.Dk2StateId);

        while (goblin.Dk2StateId == 255 && sim.World.CurrentTick.Value < dropped + 200) sim.Tick();
        long up = sim.World.CurrentTick.Value;
        Assert.InRange(up - dropped, stun - 1, stun + 2);
        Assert.Equal(257, goblin.Dk2StateId);
        Assert.Contains(sink.Events, e => e.Kind == "getting_up");

        while (goblin.Dk2StateId == 257 && sim.World.CurrentTick.Value < up + 40) sim.Tick();
        long stood = sim.World.CurrentTick.Value;
        Assert.Equal(11, stood - up);
        Assert.Equal(DropLandingRules.StandingState, goblin.Dk2StateId);
        // The counter has already moved past the tick whose systems stamped the word.
        long stamped = goblin.StateDeadlineTick;
        Assert.Equal(stood - 1, stamped);
        Assert.Contains(sink.Events, e => e.Kind == "standing" && e.Detail.EndsWith($"until={stamped + DropLandingRules.StandTicks}"));

        while (goblin.Dk2StateId == DropLandingRules.StandingState && sim.World.CurrentTick.Value < stood + 80) sim.Tick();
        Assert.Equal(stamped + DropLandingRules.StandTicks + 1, sim.World.CurrentTick.Value);
        Assert.NotEqual(DropLandingRules.StandingState, goblin.Dk2StateId);
    }

    /// <summary>The get-up length is the creature's animation, read off the captures per type;
    /// a type the captures never stunned gets the measured middle, a guess (Q527).</summary>
    [Fact]
    public void GetUpTicks_IsTheCapturedLengthPerType_AndAGuessForAnUnmeasuredType()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        Assert.Equal(11, DropLandingRules.GetUpTicks(db.CreatureById(GoblinId)));
        Assert.Equal(DropLandingRules.GetUpTicksGuess, DropLandingRules.GetUpTicks(db.CreatureById(Dk2TerrainFixture.MistressCreatureId)));
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
