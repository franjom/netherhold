// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Simulation.Tests;

/// <summary>DK2's room id (<c>+0x3B9</c>) as the state machine implies it: claimed by the ten
/// room categories' enter hooks, released when a walk ends elsewhere (state-categories.md).</summary>
public class RoomMembershipRulesTests
{
    [Theory]
    [InlineData(CreatureStateCategory.Research, true)]
    [InlineData(CreatureStateCategory.Manufacture, true)]
    [InlineData(CreatureStateCategory.GuardPost, true)]
    [InlineData(CreatureStateCategory.Temple, true)]
    [InlineData(CreatureStateCategory.Training, true)]
    [InlineData(CreatureStateCategory.Prison, true)]
    [InlineData(CreatureStateCategory.Torture, true)]
    [InlineData(CreatureStateCategory.CombatPit, true)]
    [InlineData(CreatureStateCategory.Casino, true)]
    [InlineData(CreatureStateCategory.Spectating, true)]
    [InlineData(CreatureStateCategory.Lair, false)]
    [InlineData(CreatureStateCategory.Sleeping, false)]
    [InlineData(CreatureStateCategory.Eating, false)]
    [InlineData(CreatureStateCategory.Combat, false)]
    [InlineData(CreatureStateCategory.Idle, false)]
    public void TheTenRoomCategoriesHoldARoomId(CreatureStateCategory category, bool attached)
    {
        var creature = new CreatureState { Dk2StateId = StateIn(category) };
        Assert.Equal(attached, RoomMembershipRules.IsRoomCategory(category));
        Assert.Equal(attached, RoomMembershipRules.IsAttached(creature));
    }

    [Fact]
    public void AWalkKeepsTheCallersMembershipAndAWalkInHasNoneYet()
    {
        int prison = StateIn(CreatureStateCategory.Prison);
        var leaving = new CreatureState { Dk2StateId = 6, StateBeforeUtilityState = prison, UtilityNewState = 1 };
        var arriving = new CreatureState { Dk2StateId = 6, StateBeforeUtilityState = 1, UtilityNewState = prison };
        var corpse = new CreatureState { Dk2StateId = CreatureStateMachine.NoState };

        // The leave hook runs when the walk ends in another category; the enter hook when it ends in the room.
        Assert.True(RoomMembershipRules.IsAttached(leaving));
        Assert.False(RoomMembershipRules.IsAttached(arriving));
        Assert.False(RoomMembershipRules.IsAttached(corpse));
    }

    private static int StateIn(CreatureStateCategory category)
    {
        for (int id = 0; id < CreatureStateTable.Count; id++)
            if (CreatureStateTable.Get(id).Category == category) return id;
        throw new InvalidOperationException($"no state in {category}");
    }
}
