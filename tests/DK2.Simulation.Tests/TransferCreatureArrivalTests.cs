// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Tests;

/// <summary>The campaign's transfer creature (<c>0x0050C67D</c> to <c>0x0050C7BF</c>, ADR-0673): the host calls
/// <see cref="TransferCreatureRules.Arrive"/> after the level start, and the creature draws its random quarter-turn
/// facing (World.cpp line 0x99D) once, after its creation and before its level-ups. Paired runs compare the world seed
/// against a creature made at the same tile by the ordinary spawn.</summary>
public class TransferCreatureArrivalTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte GoblinId = 7;

    private static (GameSimulation Sim, WorldState World) Start(uint seed)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [0, PlayerIds.Neutral, Keeper]);
        Dk2TerrainFixture.HeartRoom(world, db, Keeper);
        return (new GameSimulation(db, world, new SimRandom(new Dk2Lcg(seed)), []), world);
    }

    private static uint Seed(GameSimulation sim) => ((Dk2Lcg)sim.Rng.Algorithm).Seed;

    [Fact]
    public void TheArrivalDrawsOneFacingAfterTheCreatureIsMade()
    {
        var (arrived, _) = Start(9);
        var (spawned, spawnedWorld) = Start(9);
        spawnedWorld.TrySpawnCreature(spawned.Database, GoblinId, new PlayerId(Keeper), new TilePos(3, 1));
        var lcg = new Dk2Lcg(Seed(spawned));
        int facing = lcg.NextInt(4);

        CreatureState? creature = TransferCreatureRules.Arrive(arrived, new PlayerId(Keeper), GoblinId, 1);

        Assert.NotNull(creature);
        Assert.Equal(facing << 9, creature.Heading);
        Assert.Equal(lcg.Seed, Seed(arrived));
    }

    [Fact]
    public void ALevelledArrivalDrawsItsFacingBeforeTheLevelUps()
    {
        var (levelOne, _) = Start(9);
        var (levelThree, _) = Start(9);
        CreatureState one = TransferCreatureRules.Arrive(levelOne, new PlayerId(Keeper), GoblinId, 1)!;
        CreatureState three = TransferCreatureRules.Arrive(levelThree, new PlayerId(Keeper), GoblinId, 3)!;
        Assert.Equal(one.Heading, three.Heading);
        Assert.Equal(3, three.Level);
    }

    [Fact]
    public void NoStoredCreatureDrawsNothing()
    {
        var (sim, _) = Start(9);
        uint before = Seed(sim);
        Assert.Null(TransferCreatureRules.Arrive(sim, new PlayerId(Keeper), 0, 4));
        Assert.Equal(before, Seed(sim));
    }
}
