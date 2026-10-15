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
    [InstallTheory]
    [InlineData(true, true, 0, 1, false)]
    [InlineData(true, false, 0, 1, false)]
    [InlineData(false, true, 0, 0, false)]
    [InlineData(true, true, 15, 0, false)]
    [InlineData(true, true, 0, 1, true)]
    public void PortalBuildUpPreviewsThreeSecondsBeforeTheRollWithoutDrawingOrSpawning(
        bool lairPresent, bool portalOpen, int held, int expectedRequests, bool blocked)
    {
        GameDatabase original = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        CreatureDef goblin = original.CreatureByName("Goblin")!;
        var variables = new GlobalVariables();
        variables.CreaturePool.Add(new CreaturePoolEntry(goblin.CreatureId, 15, PlayerIds.FirstKeeper));
        GameDatabase db = original.ForLevel(variables);
        var map = new LevelMap(7, 7, Enumerable.Repeat(new MapTile(1, PlayerIds.Unowned, 0, 0), 49).ToArray());
        WorldState world = WorldState.FromMap(db, map, [PlayerIds.Unowned, PlayerIds.Neutral, PlayerIds.FirstKeeper]);
        for (int y = 2; y <= 4; y++)
            for (int x = 2; x <= 4; x++)
                RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Portal), PlayerIds.FirstKeeper);
        if (lairPresent)
            for (int y = 5; y <= 6; y++)
                for (int x = 5; x <= 6; x++)
                    RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Lair), PlayerIds.FirstKeeper);
        for (int i = 0; i < held; i++)
        {
            world.SpawnCreature(db, goblin.CreatureId, PlayerIds.FirstKeeper, new TilePos(1, 1));
        }
        world.Player(PlayerIds.FirstKeeper).PortalsClosed = !portalOpen;
        if (blocked)
            world.Player(PlayerIds.FirstKeeper).EntranceInfo.Add(
                new EntranceInfoState(goblin.CreatureId, PortalChoiceRules.Fresh, PortalChoiceRules.Fresh));
        var sim = new GameSimulation(db, world, 7, [new PortalSystem()]);
        var events = new ListEventSink();
        sim.ObserveWith(events);
        int warningTick = PortalRules.SpawnIntervalTicks(db, 0) - 3 * SimulationTiming.OriginalTicksPerGameSecond;
        sim.Run(warningTick - 1);
        Assert.DoesNotContain(events.Events, e => e.EffectRequest.HasValue);
        var randomBefore = sim.Rng.SaveState();
        EntranceInfoState[] rowsBefore = world.Player(PlayerIds.FirstKeeper).EntranceInfo.ToArray();
        sim.Tick();
        Assert.Equal(randomBefore, sim.Rng.SaveState());
        Assert.Equal(rowsBefore, world.Player(PlayerIds.FirstKeeper).EntranceInfo);
        Assert.Equal(held, world.Creatures.Count);
        ViewEffectRequest[] requests = events.Events.Where(e => e.EffectRequest.HasValue)
            .Select(e => e.EffectRequest!.Value).ToArray();
        Assert.Equal(expectedRequests, requests.Length);
        if (expectedRequests == 1)
        {
            Assert.Equal(ViewEffectRequest.AtTileCentre(new EffectId(94), PlayerIds.FirstKeeper,
                new TilePos(3, 3), new Fp32(0)), requests[0]);
            var particles = new RoomConstructionParticles(db, 7, () => new EffectTime(0));
            foreach (SimEvent emission in events.Events) particles.Emit(emission);
            particles.AdvanceTo(new EffectTime(51));
            Assert.Equal(12, particles.Particles.Count);
            Assert.All(particles.Particles, p => Assert.Contains(p.Definition.EffectElementId, new ushort[] { 234, 235 }));
        }
        sim.Run(3 * SimulationTiming.OriginalTicksPerGameSecond - 1);
        Assert.Equal(held, world.Creatures.Count);
        Assert.Equal(expectedRequests, events.Events.Count(e => e.EffectRequest.HasValue));
        sim.Tick();
        Assert.Equal(held + (portalOpen && expectedRequests == 1 && !blocked ? 1 : 0), world.Creatures.Count);
    }

    [InstallFact]
    public void ReinforcingCompletesWithFourOriginalWallEffectsAndNoneDuringProgress()
    {
        (GameDatabase db, WorldState world) = Load();
        TerrainDef rock = db.TerrainById(2);
        var at = new TilePos(3, 3);
        world.Tile(at).TerrainId = rock.TerrainId;
        world.Tile(at).OwnerPlayerId = PlayerIds.Neutral;
        world.Tile(at).Health = rock.StartingHealth;
        var sim = new GameSimulation(db, world, 1, [new TileWorkSystem()]);
        var events = new ListEventSink();
        sim.ObserveWith(events);
        Assert.Equal((ushort)11, rock.MaxHealthEffectId);
        Assert.Equal("Reinforce Wall", db.Effects[11].Name);
        for (int work = 0; work < 500 && world.Tile(at).TerrainId == rock.TerrainId; work++)
        {
            world.QueueWork(at.X, at.Y, TileWorkKind.Reinforce, PlayerIds.FirstKeeper);
            sim.Tick();
            if (world.Tile(at).TerrainId == rock.TerrainId)
                Assert.DoesNotContain(events.Events, e => e.EffectRequest.HasValue);
        }
        Assert.Equal((byte)9, world.Tile(at).TerrainId);
        ViewEffectRequest[] requests = events.Events.Where(e => e.EffectRequest.HasValue)
            .Select(e => e.EffectRequest!.Value).ToArray();
        Assert.Equal(new[] { new TilePos(2, 3), new TilePos(3, 2), new TilePos(4, 3), new TilePos(3, 4) },
            requests.Select(r => r.Tile));
        Assert.All(requests, r =>
        {
            Assert.Equal(new EffectId(11), r.EffectId);
            Assert.Equal(new PlayerId(PlayerIds.FirstKeeper), r.Owner);
            Assert.Equal(0, r.Height.Raw);
            Assert.Equal(0, r.Heading.Raw);
            Assert.Equal(ViewEffectRequest.AtTileCentre(new EffectId(11), r.Owner, r.Tile, new Fp32(0)), r);
        });
        world.QueueWork(at.X, at.Y, TileWorkKind.Reinforce, PlayerIds.FirstKeeper);
        sim.Tick();
        Assert.Equal(4, events.Events.Count(e => e.EffectRequest.HasValue));
        var particles = new RoomConstructionParticles(db, 7, () => new EffectTime(0));
        foreach (SimEvent emission in events.Events) particles.Emit(emission);
        ConstructionParticle[] births = CollectBirths(particles);
        Assert.Equal(4 * 12 * 4, births.Length);
        Assert.All(births, p => Assert.Contains(p.Definition.EffectElementId, new ushort[] { 144, 145, 146, 147 }));
    }

    /// <summary>A request shows nothing until the model's next 50 ms update; the Unity runner asks
    /// for one at once before its first frame (the Heart's plug lid; heart-intro.md §4).</summary>
    [InstallFact]
    public void UpdateNowEmitsAFreshRequestsFirstWave_AndLeavesTheClockWhereItWas()
    {
        (GameDatabase db, _) = Load();
        long now = 1000;
        var model = new RoomConstructionParticles(db, 7, () => new EffectTime(now));
        model.Request(ViewEffectRequest.AtTileCentre(new EffectId(8), PlayerIds.FirstKeeper,
            new TilePos(3, 3), new Fp32(Fp32.One)));
        Assert.Empty(model.Particles);
        model.UpdateNow();
        Assert.NotEmpty(model.Particles);
        Assert.All(model.Particles, p => Assert.Equal(0, p.VisualUpdates));
        // The next real update is still due 50 ms after the request, not 50 ms after this one.
        Assert.Equal(0.2f, model.AdvanceTo(new EffectTime(now + 10)));
        Assert.All(model.Particles, p => Assert.Equal(0, p.VisualUpdates));
        model.AdvanceTo(new EffectTime(now + 51));
        Assert.All(model.Particles, p => Assert.Equal(1, p.VisualUpdates));
    }

    /// <summary>The stun's dazed effect (101) tied to a creature's head (0x00434E80, ADR-0480): its
    /// stars start at the point, move with it each update, and stay put once the creature is gone.</summary>
    [InstallFact]
    public void ATiedEffect_FollowsItsCreaturesPoint_AndLetsGoWhenTheCreatureIsGone()
    {
        (GameDatabase db, _) = Load();
        var head = new FpPosition3(new Fp32(3 * Fp32.One), new Fp32(3 * Fp32.One), new Fp32(Fp32.One + 900));
        bool alive = true;
        var model = new RoomConstructionParticles(db, 7, () => new EffectTime(0),
            attachment: (creature, point) => creature == 42 && point == 5000 && alive
                ? EffectAttachment.At(head) : EffectAttachment.Gone);
        model.Request(new ViewEffectRequest(new EffectId(101), PlayerIds.FirstKeeper,
            new FpPosition3(new Fp32(3 * Fp32.One), new Fp32(5 * Fp32.One), new Fp32(Fp32.One)),
            default, AttachedCreatureId: 42, AttachPoint: 5000));
        model.AdvanceTo(new EffectTime(51));
        Assert.NotEmpty(model.Particles);
        // Born about the head (radius 38..40 × 23, heights ±640), not at the request's position.
        Assert.All(model.Particles, p => Assert.InRange(p.YRaw, 3 * Fp32.One - 1100, 3 * Fp32.One + 1100));

        int[] before = model.Particles.Select(p => p.XRaw).ToArray();
        head = head with { X = new Fp32(4 * Fp32.One) };
        model.AdvanceTo(new EffectTime(101));
        Assert.All(model.Particles.Take(before.Length).Select((p, i) => p.XRaw - before[i]),
            dx => Assert.InRange(dx, Fp32.One - 400, Fp32.One + 400));

        alive = false;
        before = model.Particles.Select(p => p.XRaw).ToArray();
        head = head with { X = new Fp32(9 * Fp32.One) };
        model.AdvanceTo(new EffectTime(151));
        Assert.All(model.Particles.Take(before.Length).Select((p, i) => p.XRaw - before[i]),
            dx => Assert.InRange(dx, -400, 400));
    }

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
