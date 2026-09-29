// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>The one way a creature's DK2 state id changes: DK2's <c>SetState</c>
/// (<c>0x00476D30</c>), which consults the transition rule and refuses an illegal move
/// rather than taking it (ADR-0135 step one).
/// <para>Every creature holds a state from its constructor on, and the activities that
/// once stood in for them all make their moves through here (ADR-0209). The gate came
/// first on purpose: all 78,680 ordered pairs resolve (<see cref="CreatureStateTransitions"/>,
/// Q300), so no retirement had to invent a transition rule.</para></summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/creature-state-table.md")]
public static class CreatureStateMachine
{
    /// <summary>A corpse's: DK2 destroys the creature and makes a <c>CDeadBody</c> thing, and
    /// ours keeps the creature with no state (ADR-0206). Not state 0, which is a real state
    /// (<c>idle</c>, the one row with no handler).</summary>
    public const int NoState = -1;

    /// <summary>Whether this creature holds a state: every creature but a corpse.</summary>
    public static bool HasState(CreatureState creature) => creature.Dk2StateId != NoState;

    /// <summary>What the rule says about a move, without taking it. A corpse's move is not
    /// judged: it has no state to judge it from.</summary>
    public static StateTransition Classify(CreatureState creature, int targetStateId)
    {
        if (creature is null) throw new ArgumentNullException(nameof(creature));
        if (!HasState(creature)) return StateTransition.Allowed;

        return CreatureStateTransitions.Classify(
            creature.Dk2StateId, targetStateId, creature.UtilityNewState);
    }

    /// <summary>DK2's <c>SetState</c>. Returns whether the move was taken; on a refusal the
    /// creature keeps the state it has, which is the original's behaviour and not an error
    /// (<c>creature-state-table.md</c> §2.2).</summary>
    public static bool TrySetState(CreatureState creature, int targetStateId, long currentTick = -1)
    {
        if (creature is null) throw new ArgumentNullException(nameof(creature));
        _ = CreatureStateTable.Get(targetStateId);

        if (Classify(creature, targetStateId) == StateTransition.Refused) return false;

        Enter(creature, targetStateId);
        creature.StateEnteredTick = currentTick;
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
    public static bool Arrive(CreatureState creature, long currentTick = -1)
    {
        if (creature is null) throw new ArgumentNullException(nameof(creature));
        if (!CreatureStateTable.Get(creature.Dk2StateId).IsUtility)
        {
            throw new InvalidOperationException(
                $"Creature {creature.Id} is in state {creature.Dk2StateId}, not a travel state.");
        }

        bool took = TrySetState(creature, creature.UtilityNewState, currentTick);
        creature.UtilityNewState = Idle;
        return took;
    }

    /// <summary>Takes a dying creature's state away as it becomes a corpse, the one creature
    /// with none: DK2 destroys it and makes a <c>CDeadBody</c> (ADR-0206). Keeps the saved
    /// caller and the resume slot, which DK2 never clears.</summary>
    public static void Release(CreatureState creature)
    {
        if (creature is null) throw new ArgumentNullException(nameof(creature));
        creature.Dk2StateId = NoState;
    }

    /// <summary>The category whose update hook DK2's per-tick dispatcher <c>0x00476FC0</c>
    /// runs for this creature, or null when none does (ADR-0185). Outside a travel state it
    /// is the current state's. In one, the dispatcher compares the categories of the saved
    /// caller and the resume slot and runs the hook only when they agree, so a walk between
    /// two states of one category keeps that category's effect and any other walk has none.
    /// A corpse has no category.</summary>
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
            if (HasState(creature))
            {
                int caller = CreatureStateTable.Get(creature.Dk2StateId).IsUtility
                    ? creature.StateBeforeUtilityState : creature.Dk2StateId;
                if (caller >= 0 && CreatureStateTable.Get(caller).Category == CreatureStateCategory.WorkerJob)
                    WorkerPickupRules.OnTaskReleased(creature);
                // The Call to Arms category's leave hook (0x00455E90) takes the run speed off.
                if (caller >= 0 && CreatureStateTable.Get(caller).Category == CreatureStateCategory.CallToArms)
                    creature.Running = false;
            }
            // The target's enter hook: every countdown hook's seeds its byte with variable 19,
            // and the Call to Arms category's (0x00455E70) sets the run speed (0x004887D0).
            creature.HookCountdown = HookCountdownSeed;
            if (CreatureStateTable.Get(stateId).Category == CreatureStateCategory.CallToArms)
                creature.Running = true;
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
