// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>The one way a creature's DK2 state id changes: DK2's <c>SetState</c>
/// (<c>0x00476D30</c>), which consults the transition rule and refuses an illegal move
/// rather than taking it (ADR-0135 step one).
/// <para>Nothing writes <see cref="CreatureState.Dk2StateId"/> yet, so this gate is inert
/// by construction. It exists first on purpose: the thirty activities retire onto state
/// ids one group at a time, and each retirement that lands before the gate is a chance to
/// invent a transition rule the original does not have. AGENTS.md forbids inventing
/// behaviour that can be researched, and this one is researched — all 78,680 ordered pairs
/// resolve (<see cref="CreatureStateTransitions"/>, Q300).</para></summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/creature-state-table.md")]
public static class CreatureStateMachine
{
    /// <summary>No state has been assigned yet — <see cref="CreatureState.Dk2StateId"/>'s
    /// default. Not state 0, which is a real state (<c>idle</c>, the one row with no
    /// handler); defaulting to it would claim a state nothing had chosen.</summary>
    public const int NoState = -1;

    /// <summary>Whether this creature has been given a state id at all.</summary>
    public static bool HasState(CreatureState creature) => creature.Dk2StateId != NoState;

    /// <summary>What the rule says about a move, without taking it.</summary>
    /// <remarks>A creature with no state id yet is not a transition at all: the rule takes
    /// two states and there is only one. <see cref="Begin"/> is that case.</remarks>
    public static StateTransition Classify(CreatureState creature, int targetStateId)
    {
        if (creature is null) throw new ArgumentNullException(nameof(creature));
        if (!HasState(creature)) return StateTransition.Allowed;

        return CreatureStateTransitions.Classify(
            creature.Dk2StateId, targetStateId, ResumeOrSelf(creature));
    }

    /// <summary>Gives a creature its first state id. Separate from <see cref="TrySetState"/>
    /// because there is nothing to judge a first state against, and because an activity
    /// retiring onto a state id has to start somewhere.</summary>
    public static void Begin(CreatureState creature, int stateId)
    {
        if (creature is null) throw new ArgumentNullException(nameof(creature));
        _ = CreatureStateTable.Get(stateId);

        if (HasState(creature))
        {
            throw new InvalidOperationException(
                $"Creature {creature.Id} is already in state {creature.Dk2StateId}; " +
                $"use {nameof(TrySetState)}.");
        }

        Enter(creature, stateId);
    }

    /// <summary>DK2's <c>SetState</c>. Returns whether the move was taken; on a refusal the
    /// creature keeps the state it has, which is the original's behaviour and not an error
    /// (<c>creature-state-table.md</c> §2.2).</summary>
    public static bool TrySetState(CreatureState creature, int targetStateId)
    {
        if (creature is null) throw new ArgumentNullException(nameof(creature));
        _ = CreatureStateTable.Get(targetStateId);

        if (Classify(creature, targetStateId) == StateTransition.Refused) return false;

        Enter(creature, targetStateId);
        return true;
    }

    /// <summary>Leaves a utility state for the state it was holding, and clears the slot.
    /// Returns false when there is nothing to resume to or the rule refuses the move.</summary>
    /// <remarks>Which site performs this in the original — the travel handler's arrival
    /// branch, a category hook, or the caller — is <b>UNKNOWN</b> (Q414), and so is who
    /// writes <see cref="CreatureState.UtilityNewState"/> in the first place. This is the
    /// shape the slot implies, not a site that has been read; nothing calls it yet.</remarks>
    public static bool TryResume(CreatureState creature)
    {
        if (creature is null) throw new ArgumentNullException(nameof(creature));

        int resume = creature.UtilityNewState;
        if (resume == NoState) return false;
        if (!CreatureStateTable.Get(creature.Dk2StateId).IsUtility) return false;
        if (!TrySetState(creature, resume)) return false;

        creature.UtilityNewState = NoState;
        return true;
    }

    /// <summary>The state the differing-tags half of the rule compares against: a creature
    /// mid-travel is judged by where the travel is going. Falls back to the travel state
    /// itself when the resume slot is empty, because the rule has to answer and a creature
    /// with an unset slot is a state this project cannot yet produce (Q414).</summary>
    private static int ResumeOrSelf(CreatureState creature) =>
        creature.UtilityNewState == NoState ? creature.Dk2StateId : creature.UtilityNewState;

    private static void Enter(CreatureState creature, int stateId)
    {
        // DK2 writes the caller's state into the slot as it enters a utility state, so a
        // travel state always knows what it interrupted (creature-state-table.md §2.1).
        creature.StateBeforeUtilityState = CreatureStateTable.Get(stateId).IsUtility
            ? creature.Dk2StateId
            : NoState;

        creature.Dk2StateId = stateId;
    }
}
