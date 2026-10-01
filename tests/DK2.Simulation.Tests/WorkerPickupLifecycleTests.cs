// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Simulation.Tests;

public class WorkerPickupLifecycleTests
{
    [Fact]
    public void SavingDuringTravelPreservesTheLeaveHookResult()
    {
        var db = Dk2TerrainFixture.Database();
        var world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)3]);
        var creature = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, 3, new DK2.Core.TilePos(2, 2));
        creature.Dk2StateId = 6;
        creature.StateBeforeUtilityState = 45;
        creature.UtilityNewState = 43;
        creature.Flags = 0xA6;
        var original = new GameSimulation(db, world, 42, []);
        var restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(original), db, []);
        var copy = restored.World.CreatureById(creature.Id)!;
        Assert.Equal(original.StateHash(), restored.StateHash());
        Assert.True(CreatureStateMachine.TrySetState(new StateHookContext(world, db), creature, 1));
        Assert.True(CreatureStateMachine.TrySetState(new StateHookContext(world, db), copy, 1));
        Assert.Equal((byte)0xAF, copy.Flags);
        Assert.Equal(original.StateHash(), restored.StateHash());
    }

    [Theory]
    [InlineData(43, 0xAF)]
    [InlineData(44, 0xAF)]
    [InlineData(46, 0xAF)]
    [InlineData(49, 0xAF)]
    [InlineData(45, 0xA6)]
    [InlineData(47, 0xA6)]
    [InlineData(48, 0xA6)]
    [InlineData(51, 0xA6)]
    [InlineData(52, 0xA6)]
    public void LeavingWorkerCategoryRestoresOnlyTheListedStates(int state, byte expected)
    {
        var creature = new CreatureState { Dk2StateId = state, Flags = 0xA6 };
        Assert.True(CreatureStateMachine.TrySetState(DetachedHooks.Context, creature, 1));
        Assert.Equal(expected, creature.Flags);
    }

    [Theory]
    [InlineData(43, 45, 0xA6)]
    [InlineData(45, 43, 0xAF)]
    [InlineData(43, 44, 0xAF)]
    [InlineData(91, 43, 0xA6)]
    public void SavedCallerChoosesTheHookButResumeChoosesItsBranch(int caller, int resume, byte expected)
    {
        var creature = new CreatureState
        {
            Dk2StateId = 6, StateBeforeUtilityState = caller,
            UtilityNewState = resume, Flags = 0xA6,
        };
        Assert.True(CreatureStateMachine.TrySetState(DetachedHooks.Context, creature, 1));
        Assert.Equal(expected, creature.Flags);
    }

    [Fact]
    public void SameCategoryAndTravelEntryDoNotReleaseTheTask()
    {
        var creature = new CreatureState { Dk2StateId = 43, Flags = 0xA6 };
        Assert.True(CreatureStateMachine.TrySetState(DetachedHooks.Context, creature, 44));
        Assert.Equal((byte)0xA6, creature.Flags);
        CreatureStateMachine.BeginTravel(DetachedHooks.Context, creature, 6, 46);
        Assert.Equal((byte)0xA6, creature.Flags);
        Assert.True(CreatureStateMachine.Arrive(DetachedHooks.Context, creature));
        Assert.Equal((byte)0xA6, creature.Flags);
    }

    [Fact]
    public void TravelToTravelRunsTheWorkerLeaveHookThroughIdle()
    {
        var creature = new CreatureState { Dk2StateId = 43, Flags = 0xA6 };
        CreatureStateMachine.BeginTravel(DetachedHooks.Context, creature, 6, 44);
        CreatureStateMachine.BeginTravel(DetachedHooks.Context, creature, 7, 46);
        Assert.Equal((byte)0xAF, creature.Flags);
        Assert.Equal(1, creature.StateBeforeUtilityState);
    }
}
