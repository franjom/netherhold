// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>The one way a creature's DK2 state id changes: DK2's <c>SetState</c>
/// (<c>0x00476D30</c>), which consults the transition rule and refuses an illegal move
/// rather than taking it (ADR-0135 step one).
/// <para>The activities retire onto state ids one group at a time (<see cref="IsRetired"/>),
/// and the gate came first on purpose: each retirement that landed before it would have
/// been a chance to invent a transition rule the original does not have. AGENTS.md forbids inventing
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
            creature.Dk2StateId, targetStateId, creature.UtilityNewState);
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

    /// <summary>Starts a walk the way the original's two route helpers do: enter the travel
    /// state, then write the resume slot (<c>travel-states.md</c> §2). The order is theirs
    /// and it matters — <c>SetState</c> saves the caller into
    /// <see cref="CreatureState.StateBeforeUtilityState"/> before the slot is touched, and
    /// the helpers do not test the transition's result.</summary>
    /// <remarks>A caller's whole contribution in DK2 is one call with a target, a resume
    /// state and an option word. Routing and the destination are the mover's; this models
    /// only the state side, because nothing retires onto it yet.</remarks>
    public static void BeginTravel(CreatureState creature, int travelStateId, int resumeStateId)
    {
        if (creature is null) throw new ArgumentNullException(nameof(creature));
        if (!CreatureStateTable.Get(travelStateId).IsUtility)
        {
            throw new ArgumentOutOfRangeException(
                nameof(travelStateId), travelStateId, "Not a travel state.");
        }

        _ = CreatureStateTable.Get(resumeStateId);
        TrySetState(creature, travelStateId);
        creature.UtilityNewState = resumeStateId;
    }

    /// <summary>The arrival branch of a travel handler: hand control to the resume state and
    /// put the slot back to <see cref="Idle"/>.</summary>
    /// <remarks>The reset happens <b>whether or not the transition was allowed</b>, which is
    /// the original's order and not a tidy-up — state 6 sets the slot from a register loaded
    /// before the call and only then branches on the result. Returns what the move returned,
    /// as the handler does; a false there reaches DK2's dispatcher, which idles the
    /// creature.</remarks>
    public static bool Arrive(CreatureState creature)
    {
        if (creature is null) throw new ArgumentNullException(nameof(creature));
        if (!CreatureStateTable.Get(creature.Dk2StateId).IsUtility)
        {
            throw new InvalidOperationException(
                $"Creature {creature.Id} is in state {creature.Dk2StateId}, not a travel state.");
        }

        bool took = TrySetState(creature, creature.UtilityNewState);
        creature.UtilityNewState = Idle;
        return took;
    }

    /// <summary>Takes a creature's state id away again. <b>A migration shim, not DK2</b>, and
    /// it goes when the last activity retires (ADR-0176).</summary>
    /// <remarks>While only some activities are on state ids, a creature that leaves them —
    /// or is pulled out by a system that still sets <see cref="CreatureState.Activity"/>
    /// directly — would otherwise keep a state it is no longer in, and the next gated move
    /// would be judged from the wrong place. So the rule while migrating is: a creature
    /// holds a state id <b>only while it is in a retired activity</b>. Releasing keeps the
    /// saved caller and the resume slot, which DK2 itself never clears.</remarks>
    public static void Release(CreatureState creature)
    {
        if (creature is null) throw new ArgumentNullException(nameof(creature));
        creature.Dk2StateId = NoState;
    }

    /// <summary>The retired activities, the only ones a creature may hold a state id in:
    /// eating (ADR-0176), a job's walk and work (ADR-0177), the lair and sleep (ADR-0178),
    /// collecting wages (ADR-0180), an imp's tile work (ADR-0181), its gold (ADR-0182), objects (ADR-0183) and bodies (ADR-0184). Part of the shim.</summary>
    public static bool IsRetired(CreatureActivity activity) =>
        activity is CreatureActivity.SeekingFood or CreatureActivity.FetchingChicken
            or CreatureActivity.GoingToJob or CreatureActivity.AtWork
            or CreatureActivity.SeekingLair or CreatureActivity.GoingToSleep
            or CreatureActivity.Sleeping or CreatureActivity.SeekingWages
            or CreatureActivity.MovingToJob or CreatureActivity.Working
            or CreatureActivity.FetchingGold or CreatureActivity.DeliveringGold
            or CreatureActivity.FetchingObject or CreatureActivity.CarryingObject
            or CreatureActivity.FetchingBody or CreatureActivity.CarryingBody
            or CreatureActivity.Imprisoned;

    /// <summary>Hands the state id back if the creature has left the retired activities.
    /// Every system that owns a retired activity calls it first, so one system's shim
    /// cannot take the id of an errand another owns.</summary>
    public static void ReleaseIfLeft(CreatureState creature)
    {
        if (creature is null) throw new ArgumentNullException(nameof(creature));
        if (HasState(creature) && !IsRetired(creature.Activity)) Release(creature);
    }

    /// <summary>The category whose update hook DK2's per-tick dispatcher <c>0x00476FC0</c>
    /// runs for this creature, or null when none does (ADR-0185). Outside a travel state it
    /// is the current state's. In one, the dispatcher compares the categories of the saved
    /// caller and the resume slot and runs the hook only when they agree, so a walk between
    /// two states of one category keeps that category's effect and any other walk has none.
    /// A creature with no state id yet has no category.</summary>
    public static CreatureStateCategory? UpdatingCategory(CreatureState creature)
    {
        if (creature is null) throw new ArgumentNullException(nameof(creature));
        if (!HasState(creature)) return null;

        CreatureStateCategory current = CreatureStateTable.Get(creature.Dk2StateId).Category;
        if (current != CreatureStateCategory.Movement) return current;

        CreatureStateCategory resume = CreatureStateTable.Get(creature.UtilityNewState).Category;
        CreatureStateCategory saved = CreatureStateTable.Get(creature.StateBeforeUtilityState).Category;
        return resume == saved ? resume : null;
    }

    /// <summary>State 1, <c>idle</c> — and the value DK2 leaves in the resume slot between
    /// walks. Its constructor writes 0 and every arrival writes 1, so the slot is never
    /// unset while a creature is travelling (<c>travel-states.md</c> §2.4, §9).</summary>
    public const int Idle = 1;

    private static void Enter(CreatureState creature, int stateId)
    {
        // Entering a utility state saves the caller; leaving one does NOT clear the save.
        // DK2's own 2-to-2 move recurses through SetState(1) first, so the slot ends up
        // holding 1 rather than the travel state it came from - reproduced here rather
        // than shortened, because a stale save is load-bearing: it is what the transition
        // rule judges a creature by when a walk drops it to idle (travel-states.md 3, 5).
        if (CreatureStateTable.Get(stateId).IsUtility)
        {
            if (HasState(creature) && CreatureStateTable.Get(creature.Dk2StateId).IsUtility)
            {
                Enter(creature, Idle);
            }

            creature.StateBeforeUtilityState = creature.Dk2StateId;
        }
        else if (EntersCategory(creature, stateId))
        {
            // The target's enter hook: every countdown hook's seeds its byte with variable 19.
            creature.HookCountdown = HookCountdownSeed;
        }

        creature.Dk2StateId = stateId;
    }

    /// <summary>Whether <c>SetState</c> runs the target's enter hook (<c>travel-states.md</c> §3):
    /// judged against the current state's category, or the saved caller's when leaving a walk,
    /// so a walk between two states of one category runs none.</summary>
    private static bool EntersCategory(CreatureState creature, int stateId)
    {
        if (!HasState(creature)) return true;
        CreatureStateDef current = CreatureStateTable.Get(creature.Dk2StateId);
        int from = current.IsUtility ? creature.StateBeforeUtilityState : creature.Dk2StateId;
        if (from < 0) return true;
        return CreatureStateTable.Get(from).Category != CreatureStateTable.Get(stateId).Category;
    }

    /// <summary>What every countdown hook's enter hook seeds and most reload: variable 19,
    /// DK2's four turns a game second (<c>training.md</c> §3).</summary>
    public const int HookCountdownSeed = SimulationTiming.OriginalTicksPerGameSecond;

    /// <summary>One turn of a countdown hook (Casino, Spectating, Training, Temple, Torture,
    /// Prison): nonzero, it counts down and the hook does nothing; at zero it reloads with
    /// <paramref name="reload"/> and the hook acts. So the effect lands every reload + 1
    /// turns, the first on the fifth turn after entry (Q424, ADR-0192). Called once a turn
    /// while the creature's <see cref="UpdatingCategory"/> is the hook's.</summary>
    public static bool HookFires(CreatureState creature, int reload)
    {
        if (creature is null) throw new ArgumentNullException(nameof(creature));
        if (creature.HookCountdown != 0)
        {
            creature.HookCountdown--;
            return false;
        }

        creature.HookCountdown = reload;
        return true;
    }
}
