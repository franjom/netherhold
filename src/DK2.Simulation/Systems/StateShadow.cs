// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Simulation.Systems;

/// <summary>The gated moves a system makes beside the activity changes they mirror, while
/// the systems still dispatch on the activity (ADR-0135, ADR-0209). A refusal is reported
/// as <c>state_refused</c> and the creature keeps its state, as the original's would,
/// rather than being forced.</summary>
internal static class StateShadow
{
    /// <summary>DK2's two travel states: 6 walks to a point, 7 walks to a room and checks
    /// the room again on arrival (<c>travel-states.md</c> §5).</summary>
    public const int TravelToPoint = 6;
    public const int TravelToRoom = 7;

    public static bool IsTravelling(CreatureState creature) =>
        CreatureStateMachine.HasState(creature)
        && CreatureStateTable.Get(creature.Dk2StateId).IsUtility;

    public static void Move(SimulationContext context, string system, CreatureState creature, int state) =>
        TryMove(context, system, creature, state);

    /// <summary><see cref="Move"/>, returning whether the rule took it.</summary>
    public static bool TryMove(SimulationContext context, string system, CreatureState creature, int state)
    {
        bool byRule = CreatureStateMachine.Classify(creature, state) == StateTransition.Refused;
        if (CreatureStateMachine.TrySetState(context.Hooks, creature, state, context.Tick.Value)) return true;
        Refused(context, system, creature, state, byRule);
        return false;
    }

    /// <summary>State 82's own shape (<c>0x0046F9F8</c>): set the target, and if that is
    /// refused set 1 and then the target — so a move the rule refuses goes through idle.</summary>
    public static void MoveOrViaIdle(
        SimulationContext context, string system, CreatureState creature, int state)
    {
        if (CreatureStateMachine.TrySetState(context.Hooks, creature, state, context.Tick.Value)) return;
        CreatureStateMachine.TrySetState(context.Hooks, creature, CreatureStateMachine.Idle, context.Tick.Value);
        Move(context, system, creature, state);
    }

    /// <summary>The travel state's handover, if the creature is in one. False when it was
    /// refused.</summary>
    public static bool Arrive(SimulationContext context, string system, CreatureState creature)
    {
        if (!IsTravelling(creature)) return true;
        int to = creature.UtilityNewState;
        bool byRule = CreatureStateMachine.Classify(creature, to) == StateTransition.Refused;
        if (CreatureStateMachine.Arrive(context.Hooks, creature, context.Tick.Value)) return true;
        Refused(context, system, creature, to, byRule);
        return false;
    }

    /// <summary>A handler that returns failure: DK2 drops the creature to state 1.</summary>
    public static void ToIdle(SimulationContext context, string system, CreatureState creature)
    {
        if (!CreatureStateMachine.HasState(creature)) return;
        Move(context, system, creature, CreatureStateMachine.Idle);
    }

    /// <summary><c>state_refused</c> when the rule refused and the creature kept its state;
    /// <c>enter_refused</c> when the target's enter hook did, leaving it in state 1 (a full room).</summary>
    private static void Refused(
        SimulationContext context, string system, CreatureState creature, int state, bool byRule) =>
        context.Emit(system, byRule ? "state_refused" : "enter_refused",
            $"creature={creature.Id} from={creature.Dk2StateId} to={state}");
}
