// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Tests;

public sealed class CreatureSlotSnapshotTests
{
    // 004B7900 unlinks an allocated slot: it cannot simultaneously be free or owned twice.
    [Theory]
    [InlineData(0)]
    [InlineData(454)]
    [InlineData(500)]
    public void InvalidOrFreeTagOnALiveCreatureIsRejected(ushort tag)
    {
        GameDatabase db = TestWorld.Database();
        WorldState world = TestWorld.World(db);
        CreatureState c = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, PlayerIds.Good, new TilePos(5, 5));
        c.NativeSlotTag = tag;
        byte[] bytes = WorldSnapshot.ToBytes(new GameSimulation(db, world, 1, []));
        Assert.Equal(SaveIncompatibleReason.Corrupt,
            Assert.Throws<SaveIncompatibleException>(() => WorldSnapshot.FromBytes(bytes, db, [])).Reason);
    }

    [Fact]
    public void DuplicateAllocatedTagIsRejected()
    {
        GameDatabase db = TestWorld.Database();
        WorldState world = TestWorld.World(db);
        CreatureState a = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, PlayerIds.Good, new TilePos(5, 5));
        CreatureState b = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, PlayerIds.Good, new TilePos(5, 5));
        b.NativeSlotTag = a.NativeSlotTag;
        byte[] bytes = WorldSnapshot.ToBytes(new GameSimulation(db, world, 1, []));
        Assert.Equal(SaveIncompatibleReason.Corrupt,
            Assert.Throws<SaveIncompatibleException>(() => WorldSnapshot.FromBytes(bytes, db, [])).Reason);
    }
}
