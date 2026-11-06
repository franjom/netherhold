// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public class EggCollisionTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;

    private sealed class CollisionRandom(int result) : IRandomAlgorithm
    {
        public List<int> Bounds { get; } = [];
        public int NextInt(int exclusiveMax)
        {
            Bounds.Add(exclusiveMax);
            return exclusiveMax == 2 ? result : 0;
        }
        public void HashInto(ref Hash64 hash) => hash.Add(Bounds.Count);
    }

    private static (GameSimulation Sim, ObjectState Egg, ListEventSink Events, CollisionRandom Rng) Start(
        int collisionResult, bool defaultSystems = false)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [0, Keeper]);
        var rng = new CollisionRandom(collisionResult);
        var sim = new GameSimulation(db, world, new SimRandom(rng),
            defaultSystems ? SimulationSystems.Default() : [new HatcherySystem(lays: false)]);
        ObjectState egg = AddEgg(sim);
        var events = new ListEventSink();
        sim.ObserveWith(events);
        rng.Bounds.Clear();
        return (sim, egg, events, rng);
    }

    private static ObjectState AddEgg(GameSimulation sim)
    {
        ObjectState egg = sim.World.SpawnObject(sim.Database, HatcheryRules.EggObjectId, Keeper,
            sim.World.TileIndex(new TilePos(3, 2)));
        egg.ProcessState = EggLaunchRules.LaunchProcess;
        egg.ProcessAgeTicks = 37;
        egg.PosX = (4 << 12) - 32;
        egg.PosY = (2 << 12) + 2048;
        egg.PosZ = 4096;
        egg.PhysicalVelocityX = 8000;
        egg.Heading = 731;
        return egg;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AZeroCollisionRollBreaksBeforeReflectionAndRequestsTheEffectAtTheOldPosition(bool defaultSystems)
    {
        var (sim, egg, events, rng) = Start(0, defaultSystems);
        var position = new FpPosition3(new(egg.PosX), new(egg.PosY), new(egg.PosZ));
        sim.Tick();
        Assert.DoesNotContain(egg, sim.World.Objects);
        Assert.Equal(0, egg.Health);
        Assert.Equal((0, 0, 0), (egg.PhysicalVelocityX, egg.PhysicalVelocityY, egg.PhysicalVelocityZ));
        Assert.Equal(position, new FpPosition3(new(egg.PosX), new(egg.PosY), new(egg.PosZ)));
        ViewEffectRequest effect = Assert.Single(events.Events, e => e.EffectRequest?.EffectId == new EffectId(301)).EffectRequest!.Value;
        Assert.Equal(position, effect.Position);
        Assert.Equal(Keeper, effect.Owner.Value);
        Assert.Equal(new Dk2Angle(731), effect.Heading);
        Assert.Equal(2, rng.Bounds[0]);
    }

    [Fact]
    public void ANonzeroCollisionRollKeepsTheEggAndReflectsWithoutAnEffect()
    {
        var (sim, egg, events, rng) = Start(1);
        sim.Tick();
        Assert.Contains(egg, sim.World.Objects);
        Assert.Equal(-4000, egg.PhysicalVelocityX);
        Assert.DoesNotContain(events.Events, e => e.EffectRequest?.EffectId == new EffectId(301));
        Assert.Equal([2], rng.Bounds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HeldOrCarriedEggsDoNotMoveAgeOrDraw(bool carried)
    {
        var (sim, egg, events, rng) = Start(0, defaultSystems: true);
        if (carried) egg.CarriedByCreatureId = 123;
        else egg.InHand = true;
        sim.Tick();
        Assert.Contains(egg, sim.World.Objects);
        Assert.Equal(37, egg.ProcessAgeTicks);
        Assert.Equal(8000, egg.PhysicalVelocityX);
        Assert.DoesNotContain(2, rng.Bounds);
        Assert.DoesNotContain(events.Events, e => e.EffectRequest?.EffectId == new EffectId(301));
    }

    [Fact]
    public void ClearMovementDoesNotDrawAndAgesTheEggOnceInTheDefaultPipeline()
    {
        var (sim, egg, events, rng) = Start(0, defaultSystems: true);
        egg.PosX = (2 << 12) + 2048;
        egg.TileIndex = sim.World.TileIndex(new TilePos(2, 2));
        sim.Tick();
        Assert.Contains(egg, sim.World.Objects);
        Assert.Equal(38, egg.ProcessAgeTicks);
        Assert.Equal((2 << 12) + 2548, egg.PosX);
        Assert.DoesNotContain(2, rng.Bounds);
        Assert.DoesNotContain(events.Events, e => e.EffectRequest?.EffectId == new EffectId(301));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheDefaultPipelineInterleavesEggAndChickenDrawsInObjectOrder(bool eggNewer)
    {
        var (sim, egg, _, rng) = Start(0, defaultSystems: true);
        sim.World.RemoveObject(egg.Id);
        if (!eggNewer) egg = AddEgg(sim);
        ObjectState chicken = sim.World.SpawnObject(sim.Database, HatcheryRules.ChickenObjectId, Keeper,
            sim.World.TileIndex(new TilePos(1, 1)));
        chicken.ProcessState = (byte)ChickenProcess.Loose;
        sim.World.Tile(2, 1).TerrainId = Dk2TerrainFixture.Rock;
        sim.World.Tile(1, 2).TerrainId = Dk2TerrainFixture.Rock;
        if (eggNewer) egg = AddEgg(sim);
        rng.Bounds.Clear();
        sim.Tick();
        Assert.Equal(eggNewer ? new[] { 2, 4 } : [4, 2], rng.Bounds.Take(2));
        Assert.DoesNotContain(egg, sim.World.Objects);
    }

    [Fact]
    public void AClearDiagonalStepDoesNotCollideWithAnUnusedAxisPoint()
    {
        var (sim, egg, events, rng) = Start(0);
        egg.PosY = (3 << 12) - 32;
        egg.PhysicalVelocityY = 8000;
        sim.World.Tile(4, 3).TerrainId = Dk2TerrainFixture.ClaimedPath;
        sim.Tick();
        Assert.Contains(egg, sim.World.Objects);
        Assert.Equal(((4 << 12) + 468, (3 << 12) + 468), (egg.PosX, egg.PosY));
        Assert.Empty(rng.Bounds);
        Assert.DoesNotContain(events.Events, e => e.EffectRequest?.EffectId == new EffectId(301));
    }

    [Fact]
    public void ANewlyHatchedChickenWaitsUntilTheNextObjectWalk()
    {
        var (sim, egg, _, _) = Start(0, defaultSystems: true);
        egg.ProcessState = 32;
        egg.ProcessCount = 1;
        egg.PhysicalVelocityX = 0;
        CreatureAnimationTiming[] art = new CreatureAnimationTiming[9];
        art[0] = new() { ResourceType = 6, FrameCount = 45, FramesPerSecond = 30 };
        GameDatabase db = sim.Database;
        db = new GameDatabase(db.Terrain.Values, db.Rooms.Values, db.Creatures.Values, db.Variables,
            objects: db.Objects.Values.Select(o => o.ObjectId == HatcheryRules.EggObjectId ? o with { ArtSlots = art } : o),
            keeperSpells: db.KeeperSpells.Values);
        sim = new GameSimulation(db, sim.World, 7, SimulationSystems.Default());
        sim.Tick();
        ObjectState chicken = Assert.Single(sim.World.Objects, o => o.ObjectTypeId == HatcheryRules.ChickenObjectId);
        Assert.Equal(0, chicken.ProcessAgeTicks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EachCoopOrTransitionedEggIsSteppedOnlyOnceInTheDefaultPipeline(bool coop)
    {
        var (sim, egg, _, rng) = Start(1, defaultSystems: true);
        ObjectState thing = egg;
        if (coop)
        {
            sim.World.RemoveObject(egg.Id);
            thing = sim.World.SpawnObject(sim.Database, HatcheryRules.CoopObjectId, Keeper,
                sim.World.TileIndex(new TilePos(2, 2)));
            thing.ProcessState = 29;
        }
        else egg.PhysicalVelocityX = 2000;
        CreatureAnimationTiming[] art = new CreatureAnimationTiming[9];
        art[0] = art[5] = new() { ResourceType = 6, FrameCount = 45, FramesPerSecond = 30 };
        GameDatabase db = sim.Database;
        db = new GameDatabase(db.Terrain.Values, db.Rooms.Values, db.Creatures.Values, db.Variables,
            objects: db.Objects.Values.Select(o => o.ObjectId == thing.ObjectTypeId ? o with { ArtSlots = art } : o),
            keeperSpells: db.KeeperSpells.Values);
        sim = new GameSimulation(db, sim.World, new SimRandom(rng), SimulationSystems.Default());
        sim.Tick();
        Assert.Contains(thing, sim.World.Objects);
        Assert.Equal(coop ? (byte)29 : (byte)32, thing.ProcessState);
        Assert.Equal(coop ? 1 : 0, thing.ProcessAgeTicks);
    }
}
