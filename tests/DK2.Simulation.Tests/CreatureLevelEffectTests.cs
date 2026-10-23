// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Simulation.Tests;

public class CreatureLevelEffectTests
{
    [Theory]
    [InlineData(false, 1, 1)]
    [InlineData(true, 1, 1)]
    [InlineData(false, 10, 0)]
    [InlineData(false, 1, 0, true)]
    public void OnlySuccessfulPromotionsRequestTheNativeEffect(bool earned, int level, int expected, bool dead = false)
    {
        var db = Dk2TerrainFixture.Database();
        var world = WorldState.FromMap(db, Dk2TerrainFixture.Map(7, 7), [PlayerIds.Neutral, PlayerIds.FirstKeeper]);
        var imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, PlayerIds.FirstKeeper, new TilePos(3, 3));
        imp.Level = level;
        imp.PosX += 137; imp.PosY -= 219; imp.PosZ = 321; imp.Heading = 627;
        if (dead) imp.Health = 0;
        var sim = new GameSimulation(db, world, 1, [new Promote(imp, earned)]);
        var sink = new ListEventSink(); sim.ObserveWith(sink);
        sim.Tick();
        var requests = sink.Events.Where(e => e.EffectRequest.HasValue).Select(e => e.EffectRequest!.Value).ToArray();
        Assert.Equal(expected, requests.Length);
        if (expected != 0)
            Assert.Equal(new ViewEffectRequest(new EffectId(339), imp.OwnerPlayerId,
                new FpPosition3(new Fp32(imp.PosX), new Fp32(imp.PosY), new Fp32(imp.PosZ)),
                new Dk2Angle((ushort)imp.Heading)), requests[0]);
    }

    private sealed class Promote(CreatureState imp, bool earned) : ISimulationSystem
    {
        public string Name => nameof(Promote);
        public void Execute(SimulationContext context)
        {
            var definition = context.Database.CreatureById(imp.CreatureTypeId);
            if (earned) CreatureLevelRules.Grant(context.Hooks, definition, imp,
                CreatureLevelRules.ExperienceForNextLevel(context.Database, definition, imp.Level));
            else CreatureLevelRules.LevelUp(context.Hooks, definition, imp);
        }
    }
}
