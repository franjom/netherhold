// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Tests;

public sealed class CreatureIdentityTests
{
    private static readonly PlayerId OtherKeeper = new(4);
    [Fact]
    public void InitialAndFutureCreaturesUseOneSharedRandomAndOwnerLocalCollisionRetries()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        var world = WorldState.FromMap(database, Dk2TerrainFixture.Map(), [PlayerIds.FirstKeeper, OtherKeeper.Value]);
        CreatureState a = world.SpawnCreature(database, Dk2TerrainFixture.GoblinCreatureId, PlayerIds.FirstKeeper, new(2, 2));
        CreatureState b = world.SpawnCreature(database, Dk2TerrainFixture.GoblinCreatureId, PlayerIds.FirstKeeper, new(2, 2));
        CreatureState other = world.SpawnCreature(database, Dk2TerrainFixture.GoblinCreatureId, OtherKeeper, new(2, 2));
        var draws = new ZeroDraws();
        var simulation = new GameSimulation(database, world, new SimRandom(draws), []);
        Assert.Equal((ushort)1, a.NameIndex);
        Assert.Equal((ushort)2, b.NameIndex);
        Assert.Equal((ushort)1, other.NameIndex);
        Assert.Equal((byte)1, a.BloodGroupIndex);
        Assert.Equal((byte)2, b.BloodGroupIndex);
        Assert.Equal((byte)1, other.BloodGroupIndex);
        CreatureState future = world.SpawnCreature(database, Dk2TerrainFixture.GoblinCreatureId, PlayerIds.FirstKeeper, new(2, 2));
        Assert.Equal((ushort)3, future.NameIndex);
        Assert.Equal((byte)3, future.BloodGroupIndex);
        Assert.Equal(new[] { 500, 6, 500, 6, 500, 6, 500, 6 }, draws.Bounds);
    }

    [Fact]
    public void ExhaustedBloodPoolReturnsTheOriginalDrawWithoutAnotherRandomCall()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        var world = WorldState.FromMap(database, Dk2TerrainFixture.Map(), [PlayerIds.FirstKeeper]);
        var draws = new ZeroDraws();
        _ = new GameSimulation(database, world, new SimRandom(draws), []);
        var creatures = new List<CreatureState>();
        for (int i = 0; i < 8; i++)
        {
            creatures.Add(world.SpawnCreature(database, Dk2TerrainFixture.GoblinCreatureId, PlayerIds.FirstKeeper, new(2, 2)));
        }

        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 0, 0 }, creatures.Select(c => c.BloodGroupIndex));
        Assert.Equal(16, draws.Bounds.Count);
    }

    [Fact]
    public void SnapshotRestorePreservesIdentityAndDoesNotConsumeAnyCreationDraws()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        var world = WorldState.FromMap(database, Dk2TerrainFixture.Map(), [PlayerIds.FirstKeeper]);
        CreatureState creature = world.SpawnCreature(database, Dk2TerrainFixture.GoblinCreatureId, PlayerIds.FirstKeeper, new(2, 2));
        var simulation = new GameSimulation(database, world, seed: 42, []);
        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(simulation), database, []);
        Assert.Equal(simulation.StateHash(), restored.StateHash());
        CreatureState back = restored.World.CreatureById(creature.Id)!;
        Assert.Equal(creature.NameIndex, back.NameIndex);
        Assert.Equal(creature.BloodGroupIndex, back.BloodGroupIndex);
        ulong before = simulation.StateHash();
        creature.NameIndex++;
        Assert.NotEqual(before, simulation.StateHash());
        creature.NameIndex--;
        creature.BloodGroupIndex++;
        Assert.NotEqual(before, simulation.StateHash());
        creature.BloodGroupIndex--;
        Assert.Equal(simulation.Rng.NextInt(500), restored.Rng.NextInt(500));
    }

    [Fact]
    public void RetainedCorpsesDoNotReserveCreatureNamesOrBloodGroups()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        var world = WorldState.FromMap(database, Dk2TerrainFixture.Map(), [PlayerIds.FirstKeeper]);
        CreatureState corpse = world.SpawnCreature(database, Dk2TerrainFixture.GoblinCreatureId, PlayerIds.FirstKeeper, new(2, 2));
        var draws = new ZeroDraws();
        _ = new GameSimulation(database, world, new SimRandom(draws), []);
        corpse.Dk2StateId = CreatureStateMachine.NoState;
        StatePlacement.As(corpse, CreatureActivity.Dead);
        CreatureState replacement = world.SpawnCreature(database, Dk2TerrainFixture.GoblinCreatureId, PlayerIds.FirstKeeper, new(2, 2));
        Assert.Equal(corpse.NameIndex, replacement.NameIndex);
        Assert.Equal(corpse.BloodGroupIndex, replacement.BloodGroupIndex);
        Assert.Equal(4, draws.Bounds.Count);
    }

    private sealed class ZeroDraws : IRandomAlgorithm
    {
        public List<int> Bounds { get; } = [];
        public int NextInt(int exclusiveMax) { Bounds.Add(exclusiveMax); return 0; }
        public void HashInto(ref Hash64 hash) => hash.Add(Bounds.Count);
    }
}
