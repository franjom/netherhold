// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.TestSupport;

namespace DK2.Simulation.Tests;

public sealed class AllocationBudgetTests
{
    [Fact]
    public void PresentedCopy_ReusesStorageForAnUnchangedPopulation()
    {
        GameSimulation simulation = TestWorld.Simulation();
        simulation.Run(100);
        WorldState copy = WorldState.CopyOf(simulation.World);
        for (int i = 0; i < 100; i++) copy.CopyFrom(simulation.World);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) copy.CopyFrom(simulation.World);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
        StateGraph.AssertDeepCopy(simulation.World, copy, StateGraph.WorldIgnored, StateGraph.WorldShared);
    }

    [Fact]
    public void EntityIteration_DoesNotBoxAnEnumeratorPerLoop()
    {
        WorldState world = TestWorld.Simulation().World;
        int visited = 0;
        for (int i = 0; i < 100; i++)
            foreach (CreatureState creature in world.Creatures) visited += creature.Id;

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
            foreach (CreatureState creature in world.Creatures) visited += creature.Id;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(visited > 0);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void RepeatedRoomQueries_ReuseTheSweepWithoutAllocating()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(database, Dk2TerrainFixture.Map(), [PlayerIds.FirstKeeper]);
        RoomDef treasury = database.Room(RoomTypeId.Treasury);
        RoomRules.Place(database, ref world.Tile(2, 2), treasury, PlayerIds.FirstKeeper);
        int tileIndex = 2 * world.Width + 2;
        RoomInstance? found = null;
        for (int i = 0; i < 100; i++) found = RoomIndex.RoomAt(world, database, tileIndex);
        Assert.NotNull(found);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) found = RoomIndex.RoomAt(world, database, tileIndex);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(RoomTypeId.Treasury, found!.Definition.TypeId);
        Assert.Equal(0, allocated);
    }
}
