// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>Hand state 1: a pick-up command only prepares the creature (state 75, the lift sound), and the
/// creature enters the hand two turns later or at the keeper's next hand command (ADR-0731).</summary>
public class HandLiftTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte GoblinId = Dk2TerrainFixture.GoblinCreatureId;
    private static readonly TilePos Floor = new(2, 2);

    [Fact]
    public void APickUpPreparesTheCreatureAndTheHandTakesItAtTheStartOfTheSecondTickAfter()
    {
        (GameDatabase db, WorldState world) = World();
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(3, 3));
        (GameSimulation sim, ListEventSink sink) = Simulation(db, world);
        long commandTick = sim.CurrentTick.Value;

        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, goblin.Id)));
        AssertPrepared(world, goblin, commandTick);
        Assert.Contains(sink.Events, e => e.Kind == "lift_prepared" && e.Tick.Value == commandTick);
        Assert.DoesNotContain(sink.Events, e => e.Kind == "picked_up");

        // The pending lift is state the hash covers and a snapshot carries.
        ulong pending = sim.StateHash();
        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db);
        PlayerState loaded = restored.World.Player(Keeper);
        Assert.Equal((goblin.Id, commandTick), (loaded.PendingLiftCreatureId, loaded.PendingLiftTick));
        Assert.Equal(pending, restored.StateHash());
        world.Player(Keeper).PendingLiftCreatureId = 0;
        Assert.NotEqual(pending, sim.StateHash());
        world.Player(Keeper).PendingLiftCreatureId = goblin.Id;

        sim.Tick();
        AssertPrepared(world, goblin, commandTick);

        sim.Tick();
        Assert.True(goblin.InHand);
        Assert.Equal(commandTick + 1, goblin.PickupTick);
        Assert.Equal([HandItem.Of(goblin)], world.Player(Keeper).Hand);
        Assert.Equal((0, 0L), (world.Player(Keeper).PendingLiftCreatureId, world.Player(Keeper).PendingLiftTick));
        Assert.Contains(sink.Events, e => e.Kind == "picked_up" && e.Tick.Value == commandTick + 2);
    }

    [Fact]
    public void ASecondHandCommandOnTheNextTurnTakesThePreparedCreatureBeforeTheCreaturesUpdate()
    {
        (GameDatabase db, WorldState world) = World();
        CreatureState first = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(3, 3));
        CreatureState second = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(3, 2));
        var sink = new ListEventSink();
        bool heldWhenCreaturesRan = false;
        var sim = new GameSimulation(db, world, seed: 42, [new HandSystem(), new Observer(() => heldWhenCreaturesRan = first.InHand)]);
        sim.ObserveWith(sink);
        long firstTick = sim.CurrentTick.Value;

        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, first.Id)));
        Assert.False(heldWhenCreaturesRan);
        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, second.Id)));

        Assert.True(heldWhenCreaturesRan);
        Assert.True(first.InHand);
        Assert.Equal(firstTick + 1, first.PickupTick);
        Assert.Equal([HandItem.Of(first)], world.Player(Keeper).Hand);
        Assert.False(second.InHand);
        Assert.Equal(second.Id, world.Player(Keeper).PendingLiftCreatureId);
        Assert.Equal(firstTick + 1, world.Player(Keeper).PendingLiftTick);

        string[] order = [.. sink.Events.Where(e => e.Kind is "picked_up" or "lift_prepared").Select(e => $"{e.Kind}")];
        Assert.Equal(["lift_prepared", "picked_up", "lift_prepared"], order);
    }

    private static void AssertPrepared(WorldState world, CreatureState goblin, long commandTick)
    {
        Assert.Equal(75, goblin.Dk2StateId);
        Assert.False(goblin.InHand);
        Assert.Empty(world.Player(Keeper).Hand);
        Assert.Equal((goblin.Id, commandTick), (world.Player(Keeper).PendingLiftCreatureId, world.Player(Keeper).PendingLiftTick));
    }

    // DK2's drop action enters its state only with something in the hand (0x00513661): with only a
    // lift pending it does nothing, and the creature is taken a turn later as if no drop was sent.
    [Fact]
    public void ADropWithOnlyAPendingLiftDoesNothingAndTheLiftStillCompletes()
    {
        (GameDatabase db, WorldState world) = World();
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(3, 3));
        (GameSimulation sim, ListEventSink sink) = Simulation(db, world);

        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, goblin.Id)));
        sim.Tick(InputFrame.Of(new DropCommand(Keeper, Floor)));
        Assert.False(goblin.InHand);
        Assert.False(goblin.LandingPending);
        Assert.Equal(goblin.Id, world.Player(Keeper).PendingLiftCreatureId);
        Assert.DoesNotContain(sink.Events, e => e.Kind is "picked_up" or "dropped_falling");

        sim.Tick();
        Assert.True(goblin.InHand);
        Assert.Equal(new TilePos(3, 3), goblin.Tile);
    }

    // The computer keeper's routines call the insert straight after the preparation (0x004BFBD0 then
    // 0x004BC500), so its lift and drop work within one tick and a full hand refuses at once.
    [Fact]
    public void TheComputerKeepersTakePutsTheCreatureInTheHandInTheCommandsTick()
    {
        (GameDatabase db, WorldState world) = World();
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(3, 3));
        CreatureState other = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(4, 3));
        (GameSimulation sim, ListEventSink sink) = Simulation(db, world);

        sim.Tick(InputFrame.Of(new TakeCreatureCommand(Keeper, goblin.Id)));
        Assert.True(goblin.InHand);
        Assert.Equal(0, world.Player(Keeper).PendingLiftCreatureId);
        Assert.Equal(sim.CurrentTick.Value - 1, goblin.PickupTick);

        List<HandItem> hand = world.Player(Keeper).Hand;
        while (hand.Count < HandRules.Capacity(db)) hand.Add(HandItem.Of(goblin));
        sim.Tick(InputFrame.Of(new TakeCreatureCommand(Keeper, other.Id)));
        Assert.False(other.InHand);
        Assert.Equal(HandRules.PreparationState, other.Dk2StateId);
        Assert.Equal(0, world.Player(Keeper).PendingLiftCreatureId);
        Assert.Contains(sink.Events, e => e.Kind == "pickup_refused" && e.Detail.Contains("hand_full"));
    }

    private static (GameDatabase, WorldState) World()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);
        return (db, world);
    }

    private static (GameSimulation, ListEventSink) Simulation(GameDatabase db, WorldState world)
    {
        var sim = new GameSimulation(db, world, seed: 42, [new HandSystem()]);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        return (sim, sink);
    }

    /// <summary>Stands in for the creature update that follows the hand in a tick.</summary>
    private sealed class Observer(Action body) : ISimulationSystem
    {
        public string Name => "hand lift observer";
        public void Execute(SimulationContext context) => body();
    }
}
