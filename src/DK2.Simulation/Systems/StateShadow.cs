// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Simulation.Systems;

/// <summary>The gated moves a system makes beside the activity changes they mirror, while
/// activities retire onto DK2's state ids one at a time (ADR-0135 step two). Every chain
/// built so far is legal under DK2's rule, so a refusal means the mapping is wrong: it is
/// reported as <c>state_refused</c> and the creature keeps its state, as the original's
/// would, rather than being forced.</summary>
internal static class StateShadow
{
    /// <summary>DK2's two travel states: 6 walks to a point, 7 walks to a room and checks
    /// the room again on arrival (<c>travel-states.md</c> §5).</summary>
    public const int TravelToPoint = 6;
    public const int TravelToRoom = 7;

    public static bool IsTravelling(CreatureState creature) =>
        CreatureStateMachine.HasState(creature)
        && CreatureStateTable.Get(creature.Dk2StateId).IsUtility;

    /// <summary>Idle is not on state ids yet, so a creature setting out from it has none;
    /// it takes state 1 first, which is where DK2's callers run.</summary>
    public static void FromIdle(CreatureState creature)
    {
        if (!CreatureStateMachine.HasState(creature))
        {
            CreatureStateMachine.Begin(creature, CreatureStateMachine.Idle);
        }
    }

    public static void Move(SimulationContext context, string system, CreatureState creature, int state) =>
        TryMove(context, system, creature, state);

    /// <summary><see cref="Move"/>, returning whether the rule took it.</summary>
    public static bool TryMove(SimulationContext context, string system, CreatureState creature, int state)
    {
        if (CreatureStateMachine.TrySetState(creature, state)) return true;
        Refused(context, system, creature, state);
        return false;
    }

    /// <summary>State 82's own shape (<c>0x0046F9F8</c>): set the target, and if that is
    /// refused set 1 and then the target — so a move the rule refuses goes through idle.</summary>
    public static void MoveOrViaIdle(
        SimulationContext context, string system, CreatureState creature, int state)
    {
        if (CreatureStateMachine.TrySetState(creature, state)) return;
        CreatureStateMachine.TrySetState(creature, CreatureStateMachine.Idle);
        Move(context, system, creature, state);
    }

    /// <summary>The travel state's handover, if the creature is in one.</summary>
    public static void Arrive(SimulationContext context, string system, CreatureState creature)
    {
        if (!IsTravelling(creature)) return;
        int to = creature.UtilityNewState;
        if (!CreatureStateMachine.Arrive(creature)) Refused(context, system, creature, to);
    }

    /// <summary>A handler that returns failure: DK2 drops the creature to state 1. Idle is
    /// not on state ids yet, so the id is then released.</summary>
    public static void ToIdle(SimulationContext context, string system, CreatureState creature)
    {
        if (!CreatureStateMachine.HasState(creature)) return;
        Move(context, system, creature, CreatureStateMachine.Idle);
        CreatureStateMachine.Release(creature);
    }

    private static void Refused(SimulationContext context, string system, CreatureState creature, int state) =>
        context.Emit(system, "state_refused",
            $"creature={creature.Id} from={creature.Dk2StateId} to={state}");
}
