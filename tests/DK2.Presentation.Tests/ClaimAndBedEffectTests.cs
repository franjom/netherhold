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
    public void PortalSmokeHoldsItsLastTextureInsteadOfReplayingWhileAlive()
    {
        (GameDatabase db, _) = Load();
        var model = new RoomConstructionParticles(db, 7, () => new EffectTime(0));
        model.Request(ViewEffectRequest.AtTileCentre(new EffectId(8), PlayerIds.FirstKeeper,
            new TilePos(3, 3), new Fp32(Fp32.One)));
        model.AdvanceTo(new EffectTime(51));
        ConstructionParticle smoke = model.Particles.First(p => p.Definition.EffectElementId == 146);
        Assert.True((smoke.Definition.ArtResource.Flags & 0x80) != 0);
        Assert.Equal(0, smoke.InitialPhase);
        for (int update = 1; update <= smoke.FrameDuration + 1; update++)
            model.AdvanceTo(new EffectTime((update + 1) * 50 + 1));
        Assert.Contains(smoke, model.Particles);
        Assert.True(smoke.Health > 0);
        Assert.Equal(Fp32.One, smoke.PhaseRaw);
        Assert.InRange(smoke.RemainingSpriteLifetimeFraction, 0.0001f, 0.001f);
    }

    [InstallFact]
    public void StaticManaSparklesDoNotAdvanceTexturePhase()
    {
        (GameDatabase db, _) = Load();
        var model = new RoomConstructionParticles(db, 7, () => new EffectTime(0));
        model.Request(ViewEffectRequest.AtTileCentre(new EffectId(59), PlayerIds.FirstKeeper,
            new TilePos(3, 3), new Fp32(Fp32.One)));
        model.AdvanceTo(new EffectTime(51));
        ConstructionParticle sparkle = model.Particles.First();
        Assert.True((sparkle.Definition.ArtResource.Flags & 4) == 0);
        model.AdvanceTo(new EffectTime(101));
        Assert.Equal(0, sparkle.PhaseRaw);
        Assert.Equal(1f, sparkle.RemainingSpriteLifetimeFraction);
    }

    [InstallFact]
    public void UnflaggedAnimatedMistStillWrapsItsTextureCycle()
    {
        (GameDatabase db, _) = Load();
        var model = new RoomConstructionParticles(db, 7, () => new EffectTime(0));
        model.Request(ViewEffectRequest.AtTileCentre(new EffectId(117), PlayerIds.FirstKeeper,
            new TilePos(3, 3), new Fp32(Fp32.One)));
        model.AdvanceTo(new EffectTime(51));
        ConstructionParticle smoke = model.Particles.First(p => p.Health > p.FrameDuration);
        Assert.True((smoke.Definition.ArtResource.Flags & 0x80) == 0);
        for (int update = 1; update <= smoke.FrameDuration; update++)
            model.AdvanceTo(new EffectTime((update + 1) * 50 + 1));
        Assert.Contains(smoke, model.Particles);
        Assert.Equal(smoke.InitialPhase, smoke.PhaseRaw);
    }

    [InstallFact]
    public void EdgeCaptureParticlesStayOnTheSelectedBoundaryAndAreQuieter()
    {
        const ushort effectId = CaptureEffectLayout.RoomEffectId;
        (GameDatabase db, _) = Load();
        var native = new RoomConstructionParticles(db, 7, () => new EffectTime(0));
        int boundaryCalls = 0;
        var edge = new RoomConstructionParticles(db, 7, () => new EffectTime(0),
            captureEffectStyle: CaptureEffectStyle.Edges,
            captureEdges: _ => ++boundaryCalls == 1 ? CaptureEdges.West : CaptureEdges.East);
        var request = ViewEffectRequest.AtTileCentre(new EffectId(effectId), PlayerIds.FirstKeeper,
            new TilePos(3, 3), new Fp32(Fp32.One));
        var emission = new SimEvent(new GameTick(0), "test", "effect_request", "") { EffectRequest = request };
        native.Emit(emission);
        edge.Emit(emission);
        native.AdvanceTo(new EffectTime(51));
        edge.AdvanceTo(new EffectTime(51));
        Assert.NotEmpty(edge.Particles);
        Assert.Equal(native.Particles[0].InitialScale * 4 / 5, edge.Particles[0].InitialScale);
        Assert.Equal(native.Particles[0].Red * 4 / 5, edge.Particles[0].Red);
        Assert.All(edge.Particles, p => Assert.Equal(request.Position.X.Raw - 1984, p.XRaw));
        edge.AdvanceTo(new EffectTime(101));
        Assert.Equal(1, boundaryCalls);
        Assert.All(edge.Particles, p => Assert.Equal(request.Position.X.Raw - 1984, p.XRaw));
    }

    [InstallFact]
    public void FloorCaptureRetainsItsNativeCentreOriginAndIntensity()
    {
        (GameDatabase db, _) = Load();
        var native = new RoomConstructionParticles(db, 7, () => new EffectTime(0));
        var profile = new RoomConstructionParticles(db, 7, () => new EffectTime(0),
            captureEffectStyle: CaptureEffectStyle.Edges,
            captureEdges: _ => throw new InvalidOperationException("Floor capture must not query edges."));
        var request = ViewEffectRequest.AtTileCentre(new EffectId(CaptureEffectLayout.LandEffectId),
            PlayerIds.FirstKeeper, new TilePos(3, 3), new Fp32(Fp32.One));
        var emission = new SimEvent(new GameTick(0), "test", "effect_request", "") { EffectRequest = request };
        native.Emit(emission);
        profile.Emit(emission);
        native.AdvanceTo(new EffectTime(51));
        profile.AdvanceTo(new EffectTime(51));
        Assert.Equal(native.Particles.Select(p => (p.XRaw, p.YRaw, p.HeightRaw, p.ScaleRaw, p.Red, p.Green, p.Blue)),
            profile.Particles.Select(p => (p.XRaw, p.YRaw, p.HeightRaw, p.ScaleRaw, p.Red, p.Green, p.Blue)));
    }

    [InstallFact]
    public void RoomCaptureEdgesExcludeInternalSeamsAndInteriorTiles()
    {
        (GameDatabase db, WorldState world) = Load();
        for (int y = 2; y <= 4; y++)
            for (int x = 2; x <= 4; x++)
                RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Portal), PlayerIds.FirstKeeper);
        ViewEffectRequest Request(TilePos tile) => ViewEffectRequest.AtTileCentre(
            new EffectId(CaptureEffectLayout.RoomEffectId), PlayerIds.FirstKeeper, tile, new Fp32(0));
        Assert.Equal(CaptureEdges.None, CaptureEffectLayout.Boundary(world, db, Request(new(3, 3))));
        Assert.Equal(CaptureEdges.West | CaptureEdges.North,
            CaptureEffectLayout.Boundary(world, db, Request(new(2, 2))));
        var model = new RoomConstructionParticles(db, 7, () => new EffectTime(0),
            captureEffectStyle: CaptureEffectStyle.Edges,
            captureEdges: r => CaptureEffectLayout.Boundary(world, db, r));
        model.Emit(new SimEvent(new GameTick(0), "test", "effect_request", "")
            { EffectRequest = Request(new(3, 3)) });
        model.AdvanceTo(new EffectTime(51));
        Assert.Empty(model.Particles);
    }

    [InstallFact]
    public void DiggingRockEmitsOriginalDebrisOnlyWhenTheTileFalls()
    {
        (GameDatabase db, WorldState world) = Load();
        TilePos at = new(3, 3);
        TerrainDef rock = db.Terrain.Values.Single(t => t.Name == "Rock");
        Assert.Equal((ushort)6, rock.DestroyedEffectId);
        world.Tile(at).TerrainId = rock.TerrainId;
        world.Tile(at).OwnerPlayerId = PlayerIds.Neutral;
        world.Tile(at).Health = rock.StartingHealth;
        var sim = new GameSimulation(db, world, 1, [new TileWorkSystem()]);
        var events = new ListEventSink();
        var model = new RoomConstructionParticles(db, 7, () => new EffectTime(0));
        sim.ObserveWith(events);

        Assert.DoesNotContain(events.Events, e => e.EffectRequest.HasValue);
        for (int i = 0; i < 500 && world.Tile(at).TerrainId == rock.TerrainId; i++)
        {
            world.QueueWork(at.X, at.Y, TileWorkKind.Dig, PlayerIds.FirstKeeper);
            sim.Tick();
        }
        Assert.Equal(rock.DestroyedTypeTerrainId, world.Tile(at).TerrainId);
        ViewEffectRequest request = Assert.Single(events.Events, e => e.EffectRequest.HasValue).EffectRequest!.Value;
        Assert.Equal((ushort)6, request.EffectId.Value);
        Assert.Equal(new PlayerId(PlayerIds.FirstKeeper), request.Owner);
        Assert.Equal(at, request.Tile);
        Assert.Equal(Fp32.One, request.Height.Raw);
        Assert.Equal(0, request.Heading.Raw);

        foreach (SimEvent simEvent in events.Events) model.Emit(simEvent);

        ConstructionParticle[] particles = CollectBirths(model);
        Assert.Equal(72, particles.Length);
        Assert.Equal(48, particles.Count(p => p.Definition.ArtResource.Type == 5));
        Assert.All(particles, particle => Assert.Contains(particle.Definition.EffectElementId,
            new ushort[] { 542, 7, 8, 9, 10, 144 }));
    }

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
        var seen = new HashSet<ConstructionParticle>(particles);
        for (int update = 6; update <= 70; update++)
        {
            model.AdvanceTo(new EffectTime(update * 50 + 1));
            foreach (ConstructionParticle particle in model.Particles) seen.Add(particle);
        }
        Assert.Equal(9 * 32, seen.Count);
        Assert.Empty(model.Particles);
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
