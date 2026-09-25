// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Simulation.Tests;

/// <summary>Puts a creature straight into a state for a test's setup. Not a move: the rule
/// is not asked. It writes what entering the state would: the id, the saved caller for a
/// travel state, and otherwise the hook countdown an entered category seeds.</summary>
internal static class StatePlacement
{
    public static void Place(CreatureState creature, int stateId)
    {
        ref readonly CreatureStateDef state = ref CreatureStateTable.Get(stateId);
        if (state.IsUtility) creature.StateBeforeUtilityState = creature.Dk2StateId;
        else creature.HookCountdown = CreatureStateMachine.HookCountdownSeed;
        creature.Dk2StateId = stateId;
    }
}
