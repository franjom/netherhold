// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Systems;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

public class ClaimAndBedEffectTests
{
    [InstallFact]
    public void CompletingFloorClaimShowsItsTwoOriginalParticleWaves()
    {
        (GameDatabase db, WorldState world) = Load();
        TilePos at = new(3, 3);
        TerrainDef floor = db.Terrain.Values.Single(t => t.Name == "Dirt Path");
        world.Tile(at).TerrainId = floor.TerrainId;
        world.Tile(at).OwnerPlayerId = PlayerIds.Unowned;
        world.Tile(at).Health = floor.StartingHealth;
        var sim = new GameSimulation(db, world, 1, [new TileWorkSystem()]);
        var model = new RoomConstructionParticles(db, 7, () => new EffectTime(0));
        sim.ObserveWith(model);
        for (int i = 0; i < 500 && world.Tile(at).OwnerPlayerId != PlayerIds.FirstKeeper; i++)
        {
            world.QueueWork(at.X, at.Y, TileWorkKind.Claim, PlayerIds.FirstKeeper);
            sim.Tick();
        }
        Assert.Equal(PlayerIds.FirstKeeper, world.Tile(at).OwnerPlayerId);

        ConstructionParticle[] particles = CollectBirths(model);

        Assert.Equal(32, particles.Length);
        Assert.All(particles, particle => Assert.Contains(particle.Definition.EffectElementId, new ushort[] { 142, 143 }));
    }

    [InstallFact]
    public void CapturingAPortalShowsTheOriginalEffectOnAllNineTiles()
    {
        (GameDatabase db, WorldState world) = Load();
        for (int y = 2; y <= 4; y++)
            for (int x = 2; x <= 4; x++)
                RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Portal), PlayerIds.Neutral);
        var sim = new GameSimulation(db, world, 1, [new TileWorkSystem()]);
        var model = new RoomConstructionParticles(db, 7, () => new EffectTime(0));
        sim.ObserveWith(model);
        for (int i = 0; i < 500 && world.Tile(2, 2).OwnerPlayerId != PlayerIds.FirstKeeper; i++)
        {
            world.QueueWork(2, 2, TileWorkKind.ConvertRoom, PlayerIds.FirstKeeper);
            sim.Tick();
        }
        Assert.Equal(PlayerIds.FirstKeeper, world.Tile(2, 2).OwnerPlayerId);

        ConstructionParticle[] particles = CollectBirths(model);

        Assert.Equal(9 * 32, particles.Length);
        Assert.All(particles, particle => Assert.Contains(particle.Definition.EffectElementId, new ushort[] { 145, 146 }));
    }

    [InstallFact]
    public void CreatingABedShowsTheFourOriginalLairParticleWaves()
    {
        (GameDatabase db, WorldState world) = Load();
        for (int y = 2; y <= 4; y++)
            for (int x = 2; x <= 4; x++)
                RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Lair), PlayerIds.FirstKeeper);
        CreatureDef goblin = db.Creatures.Values.Single(c => c.Name == "Goblin");
        world.SpawnCreature(db, goblin.CreatureId, PlayerIds.FirstKeeper, new TilePos(3, 3));
        var sim = new GameSimulation(db, world, 1, [new CreatureMovementSystem(), new CreatureSleepSystem()]);
        var events = new ListEventSink();
        var model = new RoomConstructionParticles(db, 7, () => new EffectTime(0));
        sim.ObserveWith(events);
        for (int i = 0; i < 120 && !events.Events.Any(e => e.Kind == "bed_claimed"); i++) sim.Tick();
        Assert.Single(events.Events, e => e.Kind == "bed_claimed");
        ViewEffectRequest request = Assert.Single(events.Events, e => e.EffectRequest.HasValue).EffectRequest!.Value;
        Assert.Equal((ushort)17, request.EffectId.Value);
        Assert.Equal(0, request.Height.Raw);
        Assert.Equal(new PlayerId(PlayerIds.FirstKeeper), request.Owner);
        foreach (SimEvent simEvent in events.Events) model.Emit(simEvent);

        ConstructionParticle[] particles = CollectBirths(model);

        Assert.Equal(256, particles.Length);
        Assert.All(particles, particle => Assert.Contains(particle.Definition.EffectElementId, new ushort[] { 162, 163 }));
    }

    [InstallFact]
    public void RescuingACreatureIntoANewBedRequestsTheLairEffectOnce()
    {
        (GameDatabase db, WorldState world) = Load();
        RoomRules.Place(db, ref world.Tile(3, 3), db.Room(RoomTypeId.Lair), PlayerIds.FirstKeeper);
        CreatureDef goblin = db.Creatures.Values.Single(c => c.Name == "Goblin");
        CreatureState creature = world.SpawnCreature(db, goblin.CreatureId, PlayerIds.FirstKeeper, new TilePos(3, 3));
        creature.Health = 0;
        Assert.True(CreatureDeathRules.Collapse(new StateHookContext(world, db), creature));
        var sim = new GameSimulation(db, world, 1, [new RescueBed(creature)]);
        var events = new ListEventSink();
        sim.ObserveWith(events);

        sim.Tick();

        Assert.True(LairRules.HasBed(world, db, creature));
        ViewEffectRequest request = Assert.Single(events.Events, e => e.EffectRequest.HasValue).EffectRequest!.Value;
        Assert.Equal((ushort)17, request.EffectId.Value);
        Assert.Equal(new FpPosition3(new Fp32(14336), new Fp32(14336), new Fp32(0)), request.Position);
    }

    private sealed class RescueBed(CreatureState creature) : ISimulationSystem
    {
        public string Name => "RescueBed";
        public void Execute(SimulationContext context) => Assert.True(CreatureDeathRules.RescueIntoLair(context, creature));
    }

    private static ConstructionParticle[] CollectBirths(RoomConstructionParticles model)
    {
        var seen = new HashSet<ConstructionParticle>();
        foreach (ConstructionParticle particle in model.Particles) seen.Add(particle);
        for (int step = 1; step <= 5; step++)
        {
            model.AdvanceTo(new EffectTime(step * 50 + 1));
            foreach (ConstructionParticle particle in model.Particles) seen.Add(particle);
        }
        return seen.ToArray();
    }

    private static (GameDatabase, WorldState) Load()
    {
        GameDatabase db = GameDatabaseLoader.LoadForCampaignLevel(Dk2InstallGate.Path, "Level8");
        var map = new LevelMap(7, 7,
            Enumerable.Repeat(new MapTile(1, PlayerIds.Unowned, 0, 0), 49).ToArray());
        return (db, WorldState.FromMap(db, map, [PlayerIds.Unowned, PlayerIds.Good, PlayerIds.Neutral, PlayerIds.FirstKeeper]));
    }
}
