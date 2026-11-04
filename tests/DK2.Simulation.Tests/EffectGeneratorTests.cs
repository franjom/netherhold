// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The effect generators' update, <c>0x004B1B60</c>, and the level-start generator (ADR-0648).</summary>
public class EffectGeneratorTests
{
    private static (GameSimulation Sim, ListEventSink Sink) Start(uint seed, bool open = true)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [PlayerIds.Neutral, PlayerIds.FirstKeeper]);
        if (open)
            for (int y = 1; y < world.Height - 1; y++)
                for (int x = 1; x < world.Width - 1; x++)
                    world.Tile(x, y).TerrainId = Dk2TerrainFixture.ClaimedPath;
        var sim = new GameSimulation(db, world, new SimRandom(new Dk2Lcg(seed)), [new EffectGeneratorSystem()]);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        return (sim, sink);
    }

    private static uint SeedOf(GameSimulation sim) => ((Dk2Lcg)sim.Rng.Algorithm).Seed;

    private static List<ViewEffectRequest> Requests(ListEventSink sink) =>
        [.. sink.Events.Where(e => e.EffectRequest.HasValue).Select(e => e.EffectRequest!.Value)];

    /// <summary>The first seed whose slot draw is (or is not) 0, the level-start generator's one effect.</summary>
    private static uint SeedForSlot(bool filled)
    {
        for (uint seed = 1; ; seed++)
            if ((new Dk2Lcg(seed).NextInt(4) == 0) == filled) return seed;
    }

    [Fact]
    public void ALevelStartAddsOneGeneratorOverTheMapsInterior()
    {
        var (sim, _) = Start(1);
        EffectGeneratorState generator = Assert.Single(sim.World.EffectGenerators);
        Assert.Equal((0x1800, 0x1800, 0x2000), (generator.PosX, generator.PosY, generator.PosZ));
        Assert.Equal((sim.World.Width - 2, sim.World.Height - 2), (generator.Unknown_0010, generator.Unknown_0012));
        Assert.Equal(new ushort[] { 90, 0, 0, 0 }, generator.EffectIds);
        Assert.Equal(1, generator.Frequency);
        Assert.Equal(0u, generator.NextDueTurn);
    }

    [Fact]
    public void ARestoreDoesNotAddASecondGenerator()
    {
        var (sim, _) = Start(1);
        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), sim.Database, [new EffectGeneratorSystem()]);
        Assert.Single(restored.World.EffectGenerators);
    }

    [Fact]
    public void AFilledSlotOnOpenFloorDrawsThePointAndHeadingAndIsDueAgainASecondOn()
    {
        uint seed = SeedForSlot(filled: true);
        var (sim, sink) = Start(seed);
        var expected = new Dk2Lcg(seed);
        sim.Tick();

        Assert.Equal(0, expected.NextInt(4));
        int x = 0x1800 + expected.NextInt((sim.World.Width - 2) << 12);
        int y = 0x1800 + expected.NextInt((sim.World.Height - 2) << 12);
        var heading = new Dk2Angle((ushort)expected.NextInt(0x800));
        Assert.Equal(expected.Seed, SeedOf(sim));
        Assert.Equal(new ViewEffectRequest(new EffectId(90), new PlayerId(PlayerIds.Neutral),
            new FpPosition3(new Fp32(x), new Fp32(y), new Fp32(0x2000)), heading), Assert.Single(Requests(sink)));
        Assert.Equal(1u + SimulationTiming.OriginalTicksPerGameSecond, Assert.Single(sim.World.EffectGenerators).NextDueTurn);

        // Due again after turn 5: turns 2 to 5 draw nothing.
        uint before = SeedOf(sim);
        sim.Run(SimulationTiming.OriginalTicksPerGameSecond);
        Assert.Equal(before, SeedOf(sim));
    }

    [Fact]
    public void AnEmptySlotDrawsOnlyTheSlotAndStaysDue()
    {
        uint seed = SeedForSlot(filled: false);
        var (sim, sink) = Start(seed);
        var expected = new Dk2Lcg(seed);
        sim.Tick();
        expected.NextInt(4);
        Assert.Equal(expected.Seed, SeedOf(sim));
        Assert.Empty(Requests(sink));
        Assert.Equal(0u, Assert.Single(sim.World.EffectGenerators).NextDueTurn);
    }

    [Fact]
    public void ASolidPointDrawsNoHeadingAndStaysDue()
    {
        // The fixture's interior is rock but for the keeper's 3x3; the first seed whose point lands on rock.
        for (uint seed = 1; ; seed++)
        {
            var probe = new Dk2Lcg(seed);
            if (probe.NextInt(4) != 0) continue;
            int x = (0x1800 + probe.NextInt(14 << 12)) >> 12, y = (0x1800 + probe.NextInt(10 << 12)) >> 12;
            if (x is >= 1 and <= 3 && y is >= 1 and <= 3) continue;
            if (x == 5 && y == 5 || x == 6 && y == 5) continue; // gold and gems are not rock

            var (sim, sink) = Start(seed, open: false);
            sim.Tick();
            Assert.Equal(probe.Seed, SeedOf(sim));
            Assert.Empty(Requests(sink));
            Assert.Equal(0u, Assert.Single(sim.World.EffectGenerators).NextDueTurn);
            return;
        }
    }

    [Fact]
    public void AnAuthoredGeneratorStartsAtItsTilesCentreAtHeightZero()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [PlayerIds.Neutral, PlayerIds.FirstKeeper]);
        ThingProjection.Place(world, db, [new EffectGeneratorThing(new byte[36]) { X = 5, Y = 7, EffectIds = [1, 2, 3, 4], Unknown_001E = new byte[6] }]);
        EffectGeneratorState generator = Assert.Single(world.EffectGenerators);
        Assert.Equal((CreatureState.CentreOf(5), CreatureState.CentreOf(7), 0), (generator.PosX, generator.PosY, generator.PosZ));
    }

    /// <summary>The level-start generator is linked last, so it updates first. On a seed whose first
    /// draw picks its filled slot it asks for effect 90 before the authored one, every slot of which
    /// is filled, asks for its own.</summary>
    [Fact]
    public void TheNewestGeneratorUpdatesFirst()
    {
        uint seed = SeedForSlot(filled: true);
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [PlayerIds.Neutral, PlayerIds.FirstKeeper]);
        for (int y = 1; y < world.Height - 1; y++)
            for (int x = 1; x < world.Width - 1; x++)
                world.Tile(x, y).TerrainId = Dk2TerrainFixture.ClaimedPath;
        // A zero-size rectangle: its point is its own corner, the centre of (5, 7).
        ThingProjection.Place(world, db, [new EffectGeneratorThing(new byte[36])
        {
            X = 5, Y = 7, EffectIds = [1, 2, 3, 4], Frequency = 1, Unknown_001E = new byte[6],
        }]);
        var sim = new GameSimulation(db, world, new SimRandom(new Dk2Lcg(seed)), [new EffectGeneratorSystem()]);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        sim.Tick();
        List<ViewEffectRequest> requests = Requests(sink);
        Assert.Equal(2, requests.Count);
        Assert.Equal(90, requests[0].EffectId.Value);
        Assert.NotEqual(90, requests[1].EffectId.Value);
    }

    [Fact]
    public void ADueTurnSurvivesARestore()
    {
        uint seed = SeedForSlot(filled: true);
        var (sim, _) = Start(seed);
        sim.Tick();
        Assert.NotEqual(0u, sim.World.EffectGenerators[0].NextDueTurn);
        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), sim.Database, [new EffectGeneratorSystem()]);
        Assert.Equal(sim.World.EffectGenerators[0].NextDueTurn, Assert.Single(restored.World.EffectGenerators).NextDueTurn);
        Assert.Equal(sim.StateHash(), restored.StateHash());
    }

    [Fact]
    public void GeneratorsRunAfterTheActionPointsAsTheThingPassesLastList()
    {
        List<ISimulationSystem> systems = [.. SimulationSystems.Default()];
        Assert.Equal(systems.FindIndex(s => s is ActionPointTriggerSystem) + 1, systems.FindIndex(s => s is EffectGeneratorSystem));
    }
}
