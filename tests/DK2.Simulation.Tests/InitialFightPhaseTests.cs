// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Tests;

public sealed class InitialFightPhaseTests
{
    private static CreatureState Spawn(GameDatabase db, WorldState world) =>
        world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, PlayerIds.Good, new TilePos(5, 5));

    // 00513A4B/00513A5C construct seven players then448 pool records;
    // 004B5A10 prepends tags8..455; 0048755D initializes tag modulo10.
    [Fact]
    public void FreshPoolPhasesFollowDescendingTagsRatherThanCreationIds()
    {
        GameDatabase db = TestWorld.Database();
        WorldState world = TestWorld.World(db);
        byte[] actual = Enumerable.Range(0, 12).Select(_ => Spawn(db, world).FightProcessCounter).ToArray();
        Assert.Equal(new byte[] { 5, 4, 3, 2, 1, 0, 9, 8, 7, 6, 5, 4 }, actual);
    }

    // 004B7A20 returns a freed tag to the head; 004B78D0 takes it next.
    [Theory]
    [InlineData(0, 5)]
    [InlineData(1, 4)]
    public void RemovedCreatureSlotIsReusedWithItsOriginalPhase(int removed, byte expected)
    {
        GameDatabase db = TestWorld.Database();
        WorldState world = TestWorld.World(db);
        CreatureState[] creatures = [Spawn(db, world), Spawn(db, world)];
        world.RemoveCreature(creatures[removed].Id);
        CreatureState next = Spawn(db, world);
        Assert.Equal(3, next.Id);
        Assert.Equal(expected, next.FightProcessCounter);
    }

    // 004B7A4C-004B7A74 prepends, including multiple returns before creation.
    [Fact]
    public void MultipleReturnedSlotsAreReusedLastInFirstOut()
    {
        GameDatabase db = TestWorld.Database();
        WorldState world = TestWorld.World(db);
        CreatureState first = Spawn(db, world), second = Spawn(db, world);
        world.RemoveCreature(first.Id);
        world.RemoveCreature(second.Id);
        Assert.Equal(4, Spawn(db, world).FightProcessCounter);
        Assert.Equal(5, Spawn(db, world).FightProcessCounter);
    }

    // 004B7A20 returns the creature slot after00487C70; a native body has its own pool.
    [Fact]
    public void CorpseReleasesCreatureSlotAndLaterRemovalDoesNotReturnItTwice()
    {
        GameDatabase db = TestWorld.Database();
        WorldState world = TestWorld.World(db);
        CreatureState body = Spawn(db, world);
        body.Health = 0;
        CreatureDeathRules.BecomeCorpse(world, db, body);
        Assert.Contains(body, world.Creatures);
        Assert.Equal(5, Spawn(db, world).FightProcessCounter);
        world.RemoveCreature(body.Id);
        Assert.Equal(4, Spawn(db, world).FightProcessCounter);
    }

    // 004B7900/004B7A20 make future phase depend on free-list order, not just living ids.
    [Fact]
    public void DifferentFreeListOrderChangesTheStateHash()
    {
        GameDatabase db = TestWorld.Database();
        WorldState a = TestWorld.World(db), b = TestWorld.World(db);
        int a1 = Spawn(db, a).Id, a2 = Spawn(db, a).Id;
        int b1 = Spawn(db, b).Id, b2 = Spawn(db, b).Id;
        a.RemoveCreature(a1); a.RemoveCreature(a2);
        b.RemoveCreature(b2); b.RemoveCreature(b1);
        Assert.NotEqual(new GameSimulation(db, a, 1, []).StateHash(), new GameSimulation(db, b, 1, []).StateHash());
    }

    // Persistent pool state is required to repeat0048755D after a save, including returned slots.
    [Fact]
    public void SnapshotKeepsReturnedSlotOrderAndSubsequentConstructorPhases()
    {
        GameDatabase db = TestWorld.Database();
        WorldState world = TestWorld.World(db);
        CreatureState first = Spawn(db, world), second = Spawn(db, world);
        world.RemoveCreature(first.Id);
        world.RemoveCreature(second.Id);
        var sim = new GameSimulation(db, world, 1, []);
        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db, []);
        Assert.Equal(sim.StateHash(), restored.StateHash());
        foreach (byte phase in new byte[] { 4, 5, 3 })
        {
            Assert.Equal(phase, Spawn(db, world).FightProcessCounter);
            Assert.Equal(phase, Spawn(db, restored.World).FightProcessCounter);
            Assert.Equal(sim.StateHash(), restored.StateHash());
        }
    }
}
