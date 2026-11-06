// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public sealed class LairResumeGateTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(89)]
    [InlineData(56)]
    public void SavedMakingAHomeResumeRefusesAcquisitionOutsideTravel(int state)
    {
        var (sim, creature, attempt) = Start(state, LairRules.MakingAHomeState);
        uint seed = ((Dk2Lcg)sim.Rng.Algorithm).Seed;

        sim.Tick();

        Assert.False(attempt.Succeeded);
        Assert.Equal(state, creature.Dk2StateId);
        Assert.Equal(LairRules.MakingAHomeState, creature.UtilityNewState);
        Assert.False(creature.HasRoute);
        Assert.Equal(seed, ((Dk2Lcg)sim.Rng.Algorithm).Seed);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(53)]
    public void OtherSavedResumesStillAllowTheBedlessChooserToAcquire(int resume)
    {
        var (sim, creature, attempt) = Start(CreatureJobSystem.IdleChooserState, resume);

        sim.Tick();

        Assert.True(attempt.Succeeded);
        Assert.True(creature.HasRoute);
        Assert.Equal(7, creature.Dk2StateId);
        Assert.Equal(LairRules.MakingAHomeState, creature.UtilityNewState);
    }

    private static (GameSimulation, CreatureState, Attempt) Start(int state, int resume)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [PlayerIds.Neutral, (byte)3]);
        for (int y = 1; y <= 3; y++)
            for (int x = 1; x <= 8; x++)
            {
                world.Tile(x, y).TerrainId = Dk2TerrainFixture.ClaimedPath;
                world.Tile(x, y).OwnerPlayerId = 3;
                if (x >= 6) RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Lair), 3);
            }
        CreatureState creature = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, 3, new TilePos(2, 2));
        StatePlacement.Place(creature, state);
        creature.UtilityNewState = resume;
        var attempt = new Attempt(creature);
        return (new GameSimulation(db, world, new SimRandom(new Dk2Lcg(42)), [attempt]), creature, attempt);
    }

    private sealed class Attempt(CreatureState creature) : ISimulationSystem
    {
        public string Name => "lair_resume_gate_test";
        public bool Succeeded { get; private set; }
        public void Execute(SimulationContext context)
        {
            context.Navigation.Refresh();
            Succeeded = new LairSeeker(new GridPathfinder()).TryGoForALair(context, Name,
                creature, context.Database.CreatureById(creature.CreatureTypeId));
        }
    }
}
