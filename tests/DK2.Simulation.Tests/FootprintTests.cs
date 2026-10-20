// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public sealed class FootprintTests
{
    [Fact]
    public void LeavingWaterProducesFortyRequestsIncludingWhileStationary()
    {
        var db = Dk2TerrainFixture.Database();
        var world = WorldState.FromMap(db, Dk2TerrainFixture.Map(),
            [PlayerIds.Unowned, PlayerIds.Neutral, PlayerIds.FirstKeeper]);
        world.Tile(2, 2).TerrainId = Dk2TerrainFixture.Water;
        world.Tile(3, 2).TerrainId = Dk2TerrainFixture.ClaimedPath;
        var creature = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId,
            PlayerIds.FirstKeeper, new TilePos(2, 2));
        var sim = new GameSimulation(db, world, 1, [new FootprintSystem()]);
        var events = new ListEventSink();
        sim.ObserveWith(events);
        sim.Tick();
        Assert.DoesNotContain(events.Events, e => e.EffectRequest?.EffectId.Value == 100);
        creature.PosX = CreatureState.CentreOf(3);
        sim.Run(41);
        Assert.Equal(40, events.Events.Count(e => e.EffectRequest?.EffectId.Value == 100));
    }

    [Fact]
    public void DefaultPipelineIncludesFootprintsBeforeMovement()
    {
        var systems = SimulationSystems.Default();
        Assert.True(Array.FindIndex(systems, s => s is FootprintSystem)
            < Array.FindIndex(systems, s => s is CreatureMovementSystem));
        Assert.Single(systems.OfType<FootprintSystem>());
    }

    [Fact]
    public void CorpseSuppliesBloodOnItsWholeTileAndWaterWashesItOff()
    {
        var (sim, creature, events) = Model();
        var corpse = sim.World.SpawnCreature(sim.Database, Dk2TerrainFixture.GoblinCreatureId,
            PlayerIds.Neutral, creature.Tile);
        CreatureDeathRules.BecomeCorpse(sim.World, sim.Database, corpse);
        corpse.PosX += 1700;
        creature.Heading = 0x200;
        sim.Run(45);
        Assert.Equal(45, events.Events.Count(e => e.EffectRequest?.EffectId.Value == 99));
        Assert.Equal(39, creature.BloodFootprintTicks);
        ViewEffectRequest request = events.Events.First(e => e.EffectRequest.HasValue).EffectRequest!.Value;
        Assert.Equal(new Dk2Angle(0x200), request.Heading);
        Assert.Equal(creature.PosX, request.Position.X.Raw);
        Assert.Equal(0, request.Position.Z.Raw);
        sim.World.Tile(3, 2).TerrainId = Dk2TerrainFixture.Water;
        creature.PosX = CreatureState.CentreOf(3);
        sim.Tick();
        Assert.Equal(0, creature.BloodFootprintTicks);
        Assert.Equal(40, creature.WaterFootprintTicks);
        creature.PosX = CreatureState.CentreOf(4);
        sim.Tick();
        Assert.Equal(100, events.Events.Last(e => e.EffectRequest.HasValue).EffectRequest!.Value.EffectId.Value);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void HeldAndCarriedCorpsesDoNotSupplyBlood(bool held, bool carried)
    {
        var (sim, creature, events) = Model();
        var corpse = sim.World.SpawnCreature(sim.Database, Dk2TerrainFixture.GoblinCreatureId,
            PlayerIds.Neutral, creature.Tile);
        CreatureDeathRules.BecomeCorpse(sim.World, sim.Database, corpse);
        corpse.InHand = held;
        if (carried) corpse.CarriedByCreatureId = creature.Id;
        sim.Tick();
        Assert.DoesNotContain(events.Events, e => e.EffectRequest.HasValue);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RaisedOrFlyingCreatureLosesBothTrails(bool flying)
    {
        var (sim, creature, events) = Model();
        creature.BloodFootprintTicks = 12;
        creature.WaterFootprintTicks = 13;
        if (flying)
        {
            var definition = sim.Database.CreatureById(creature.CreatureTypeId);
            var db = new GameDatabase(sim.Database.Terrain.Values, sim.Database.Rooms.Values,
                [definition with { Flags = definition.Flags | CreatureFlags.CanFly }], sim.Database.Variables);
            sim = new GameSimulation(db, sim.World, 1, [new FootprintSystem()]);
            sim.ObserveWith(events);
        }
        else creature.PosZ = 1;
        sim.Tick();
        Assert.Equal(0, creature.BloodFootprintTicks);
        Assert.Equal(0, creature.WaterFootprintTicks);
        Assert.DoesNotContain(events.Events, e => e.EffectRequest.HasValue);
    }

    [Fact]
    public void SaveRoundTripPreservesCountersAndTheRemainingRequests()
    {
        var (sim, creature, events) = Model();
        creature.BloodFootprintTicks = 11;
        creature.WaterFootprintTicks = 7;
        ulong hash = sim.StateHash();
        creature.WaterFootprintTicks++;
        Assert.NotEqual(hash, sim.StateHash());
        creature.WaterFootprintTicks--;
        var restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), sim.Database, [new FootprintSystem()]);
        var restoredEvents = new ListEventSink();
        restored.ObserveWith(restoredEvents);
        Assert.Equal(hash, restored.StateHash());
        sim.Run(20);
        restored.Run(20);
        Assert.Equal(sim.StateHash(), restored.StateHash());
        Assert.Equal(events.Events.Where(e => e.EffectRequest.HasValue).Select(e => e.EffectRequest),
            restoredEvents.Events.Where(e => e.EffectRequest.HasValue).Select(e => e.EffectRequest));
        Assert.Equal(18, events.Events.Count(e => e.EffectRequest.HasValue));
    }

    private static (GameSimulation Simulation, CreatureState Creature, ListEventSink Events) Model()
    {
        var db = Dk2TerrainFixture.Database();
        var world = WorldState.FromMap(db, Dk2TerrainFixture.Map(),
            [PlayerIds.Unowned, PlayerIds.Neutral, PlayerIds.FirstKeeper]);
        foreach (int x in new[] { 2, 3, 4 }) world.Tile(x, 2).TerrainId = Dk2TerrainFixture.ClaimedPath;
        var creature = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId,
            PlayerIds.FirstKeeper, new TilePos(2, 2));
        var events = new ListEventSink();
        var sim = new GameSimulation(db, world, 1, [new FootprintSystem()]);
        sim.ObserveWith(events);
        return (sim, creature, events);
    }
}
