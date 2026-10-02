// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Combat;

/// <summary>The combat group's DK2 state ids and the moves the combat code makes beside the
/// activities they mirror (ADR-0208). The dispatch still reads the activity; each change of
/// one also makes the move DK2's handler makes, through the gate.</summary>
internal static class CombatStates
{
    private const string SystemName = "combat";

    /// <summary>199, "engaging in battle": the fight, entered from any state.</summary>
    public const int Engaging = 199;

    /// <summary>202, the withdraw's arrival, which ends at once.</summary>
    public const int Withdrawn = 202;

    /// <summary>203, fleeing: backing off, then the run.</summary>
    public const int Fleeing = 203;

    /// <summary>218, frozen, admitted from any state; its handler sets 1 when the word runs out.</summary>
    public const int Frozen = 218;

    /// <summary>251, the alarm raiser's arrival, "responding to a threat".</summary>
    public const int RespondingToThreat = 251;

    /// <summary>252, the raiser standing down; ends at once but for a worker's action.</summary>
    public const int StandingDown = 252;

    /// <summary>254, "reporting danger": a responder standing at the threat for thirty ticks.</summary>
    public const int ReportingDanger = 254;

    /// <summary>255, stunned; the rule refuses it from the dying and frozen states.</summary>
    public const int Stunned = 255;

    /// <summary>257, getting up from a stun; idle when its action ends.</summary>
    public const int GettingUp = 257;

    /// <summary>259, at the body a victor walked to.</summary>
    public const int AtTheBody = 259;

    /// <summary>260, the victory pause.</summary>
    public const int Celebrating = 260;

    /// <summary>A move into <paramref name="state"/>.</summary>
    public static void Enter(SimulationContext context, CreatureState me, int state) =>
        StateShadow.Move(context, SystemName, me, state);

    /// <summary>The route helper <c>0x00471890</c>: state 6, resuming <paramref name="resume"/>.</summary>
    public static void Travel(SimulationContext context, CreatureState me, int resume) =>
        CreatureStateMachine.BeginTravel(context.Hooks, me, StateShadow.TravelToPoint, resume);

    /// <summary>The withdraw and the alarm's first step: "in the combat group, or
    /// <c>SetState(199)</c>" (combat-fear.md §5, combat-alarm.md §2). True when it entered 199,
    /// whose enter hook stops the creature: DK2 routes after this, ours probed the route before.</summary>
    public static bool IntoTheGroup(SimulationContext context, CreatureState me)
    {
        if (NeedRules.ResolvedCategory(me) == CreatureStateCategory.Combat) return false;
        StateShadow.Move(context, SystemName, me, Engaging);
        return true;
    }

    /// <summary>The travel state's handover, if the creature is in one.</summary>
    public static void Arrive(SimulationContext context, CreatureState me) =>
        StateShadow.Arrive(context, SystemName, me);

    /// <summary>A handler returning zero, or one that sets 1: state 1, which no state refuses.</summary>
    public static void Idle(in StateHookContext hooks, CreatureState me)
    {
        if (CreatureStateMachine.HasState(me)) CreatureStateMachine.TrySetState(hooks, me, CreatureStateMachine.Idle);
    }
}
