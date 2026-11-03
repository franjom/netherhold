// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The neutral player's ambient tile, <c>0x004BB130</c> (ADR-0646).</summary>
public class NeutralAmbientEffectTests
{
    private static (GameSimulation Sim, ListEventSink Sink) Start(uint seed, bool neutral = true, bool lava = false)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        byte[] players = neutral ? [PlayerIds.Neutral, PlayerIds.FirstKeeper] : [PlayerIds.FirstKeeper];
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), players);
        if (lava)
            for (int y = 1; y < world.Height - 1; y++)
                for (int x = 1; x < world.Width - 1; x++)
                    world.Tile(x, y).TerrainId = Dk2TerrainFixture.Lava;
        var sim = new GameSimulation(db, world, new SimRandom(new Dk2Lcg(seed)), [new NeutralAmbientEffectSystem()]);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        return (sim, sink);
    }

    private static uint SeedOf(GameSimulation sim) => ((Dk2Lcg)sim.Rng.Algorithm).Seed;

    private static List<ViewEffectRequest> Requests(ListEventSink sink) =>
        [.. sink.Events.Where(e => e.EffectRequest.HasValue).Select(e => e.EffectRequest!.Value)];

    [Fact]
    public void AnActiveNeutralDrawsAnInteriorTileEveryTurn()
    {
        var (sim, sink) = Start(1);
        for (int turn = 0; turn < 3; turn++)
        {
            var expected = new Dk2Lcg(SeedOf(sim));
            sim.Tick();
            expected.NextInt(sim.World.Width - 2);
            expected.NextInt(sim.World.Height - 2);
            Assert.Equal(expected.Seed, SeedOf(sim));
        }
        Assert.Empty(Requests(sink));
    }

    [Fact]
    public void WithoutAnActiveNeutralNothingIsDrawn()
    {
        var (sim, _) = Start(1, neutral: false);
        uint before = SeedOf(sim);
        sim.Tick();
        Assert.Equal(before, SeedOf(sim));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(999)]
    public void ALavaTileDrawsTheEffectRollThenTheHeading(int wanted)
    {
        // The first seed whose roll is the wanted one: 3 is the last rare effect, 4 the first common.
        uint seed = 1;
        while (true)
        {
            var probe = new Dk2Lcg(seed);
            probe.NextInt(14);
            probe.NextInt(10);
            if (probe.NextInt(1000) == wanted) break;
            seed++;
        }
        var (sim, sink) = Start(seed, lava: true);
        var expected = new Dk2Lcg(seed);
        sim.Tick();

        var tile = new TilePos(expected.NextInt(sim.World.Width - 2) + 1, expected.NextInt(sim.World.Height - 2) + 1);
        int roll = expected.NextInt(1000);
        Assert.Equal(wanted, roll);
        bool rare = roll < 4;
        var heading = new Dk2Angle((ushort)expected.NextInt(0x800));
        Assert.Equal(expected.Seed, SeedOf(sim));
        ViewEffectRequest request = Assert.Single(Requests(sink));
        Assert.Equal(rare
                ? ViewEffectRequest.AtTileCentre(new EffectId((ushort)(316 + roll)), new PlayerId(PlayerIds.Neutral), tile, new Fp32(0), heading)
                : ViewEffectRequest.AtTileCentre(new EffectId(302), new PlayerId(PlayerIds.Neutral), tile, new Fp32(0x1000), heading),
            request);
    }

    [Fact]
    public void TheNeutralFrameFollowsTheKeepersVerdictAndPrecedesTheTremor()
    {
        List<ISimulationSystem> systems = [.. SimulationSystems.Default()];
        int ambient = systems.FindIndex(s => s is NeutralAmbientEffectSystem);
        Assert.Equal(systems.FindIndex(s => s is DungeonHeartSystem) + 1, ambient);
        Assert.Equal(ambient + 1, systems.FindIndex(s => s is TremorSystem));
    }
}
