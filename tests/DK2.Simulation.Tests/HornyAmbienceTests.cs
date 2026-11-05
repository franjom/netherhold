// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Systems;
using Xunit;

namespace DK2.Simulation.Tests;

/// <summary><c>Creature_Update</c>'s early <c>random(20)</c> for a Horny creature once its keeper's finale has
/// started (Creature.cpp 1555, <c>0x00488F13</c>; ADR-0675).</summary>
public sealed class HornyAmbienceTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const int Turns = 5;

    /// <summary>One draw a turn for a Horny creature of a keeper whose finale bit is set, in any state
    /// but 233; none without the bit, in 233, or for a creature that is not Horny.</summary>
    [Theory]
    [InlineData(true, true, CreatureStateMachine.Idle, Turns)]
    [InlineData(false, true, CreatureStateMachine.Idle, 0)]
    [InlineData(true, false, CreatureStateMachine.Idle, 0)]
    [InlineData(true, true, 233, 0)]
    public void AHornyCreatureDrawsOnceATurnAfterTheFinaleStarts(bool started, bool horny, int state, int draws)
    {
        GameSimulation with = Run(started, horny, state);
        GameSimulation without = Run(started: false, horny: false, state);
        for (int t = 0; t < Turns; t++) { with.Tick(); without.Tick(); }

        var expected = new Dk2Lcg(((Dk2Lcg)without.Rng.Algorithm).Seed);
        for (int i = 0; i < draws; i++) expected.NextInt(20);
        Assert.Equal(expected.Seed, ((Dk2Lcg)with.Rng.Algorithm).Seed);
    }

    private static GameSimulation Run(bool started, bool horny, int state)
    {
        CreatureDef reaper = Dk2TerrainFixture.Goblin() with
        {
            CreatureId = 100, Flags = horny ? CreatureFlags.IsHorny : Dk2TerrainFixture.Goblin().Flags,
        };
        GameDatabase db = Dk2TerrainFixture.Database([reaper]);
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);
        CreatureState c = world.SpawnCreature(db, 100, Keeper, new TilePos(4, 4));
        Dk2TerrainFixture.SetKnownIdentities(world);
        c.Dk2StateId = state;
        if (started) world.Player(Keeper).Finale.Flags = FinaleRules.SequenceStarted;
        return new GameSimulation(db, world, new SimRandom(new Dk2Lcg(42)), [new CreatureStatusSystem()]);
    }
}
