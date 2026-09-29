// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>DK2's stored room membership, the creature's <c>RoomId</c> (<c>+0x3B9</c>): written
/// by the claim <c>0x004E5A70</c> that the enter hooks of the ten room-occupying categories call,
/// cleared by the release <c>0x004E5E00</c> their leave hooks call, and by nothing else but the
/// constructor and a load's relink (state-categories.md, its hook table). The dispatcher runs a
/// leave hook when a walk ends in another category, so a creature walking out of its room is
/// still a member until it arrives, and one walking in is not a member until it does.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/state-categories.md")]
public static class RoomMembershipRules
{
    /// <summary>The categories whose enter hooks claim a room slot: 4, 5, 11 to 17 and 31.</summary>
    public static bool IsRoomCategory(CreatureStateCategory category) => category is
        CreatureStateCategory.Research or CreatureStateCategory.Manufacture or CreatureStateCategory.GuardPost
        or CreatureStateCategory.Temple or CreatureStateCategory.Training or CreatureStateCategory.Prison
        or CreatureStateCategory.Torture or CreatureStateCategory.CombatPit or CreatureStateCategory.Casino
        or CreatureStateCategory.Spectating;

    /// <summary>Whether the creature holds a room id: its state is in a room category, or it is a
    /// walk whose saved caller (<c>StateBeforeUtilityState</c>) was.</summary>
    public static bool IsAttached(CreatureState creature)
    {
        if (!CreatureStateMachine.HasState(creature)) return false;
        ref readonly CreatureStateDef current = ref CreatureStateTable.Get(creature.Dk2StateId);
        if (IsRoomCategory(current.Category)) return true;
        return current.IsUtility && creature.StateBeforeUtilityState >= 0
            && IsRoomCategory(CreatureStateTable.Get(creature.StateBeforeUtilityState).Category);
    }
}
