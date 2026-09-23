// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Simulation.Tests;

/// <summary>The transition gate, ADR-0135 step one. Nothing in the simulation writes a
/// state id yet, so every test here drives <see cref="CreatureStateMachine"/> directly —
/// which is the point: the gate has to be right before the thirty activities start
/// arriving on it, because each retirement that lands without it is a chance to invent a
/// transition rule DK2 does not have.
/// <para><see cref="CreatureStateTransitions"/> owns the rule and is tested against all
/// 78,680 ordered pairs elsewhere. These tests are about the gate's own behaviour: what it
/// refuses, what it writes on the way in, and what it will not let a caller skip.</para></summary>
public class CreatureStateMachineTests
{
    private const int Idle = 1;                  // group 0, Idle
    private const int LeavingDungeon = 194;      // group 1, Unnamed7
    private const int EnRoute = 6;               // group 9, Movement — a utility state
    private const int Sleeping = 83;             // group 7, Sleeping
    private const int LookingForLair = 79;       // group 7, Lair

    private static CreatureState Fresh() => new() { Id = 1 };

    [Fact]
    public void AFreshCreatureHasNoStateAndTakesItsFirstOneUnjudged()
    {
        CreatureState creature = Fresh();

        Assert.False(CreatureStateMachine.HasState(creature));
        Assert.Equal(CreatureStateMachine.NoState, creature.Dk2StateId);

        CreatureStateMachine.Begin(creature, Idle);

        Assert.True(CreatureStateMachine.HasState(creature));
        Assert.Equal(Idle, creature.Dk2StateId);
    }

    [Fact]
    public void BeginRefusesACreatureThatAlreadyHasAState()
    {
        // Not a style rule. Begin skips the transition check, so a second Begin would be a
        // way to reach any state from any state and the gate would be decoration.
        CreatureState creature = Fresh();
        CreatureStateMachine.Begin(creature, Idle);

        Assert.Throws<InvalidOperationException>(
            () => CreatureStateMachine.Begin(creature, Sleeping));
        Assert.Equal(Idle, creature.Dk2StateId);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(281)]
    [InlineData(int.MaxValue)]
    public void AStateIdOutsideTheTableIsRejectedByBothEntryPoints(int notAState)
    {
        CreatureState fresh = Fresh();
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CreatureStateMachine.Begin(fresh, notAState));

        CreatureState placed = Fresh();
        CreatureStateMachine.Begin(placed, Idle);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CreatureStateMachine.TrySetState(placed, notAState));
    }

    [Fact]
    public void ARefusedMoveLeavesTheCreatureWhereItWasAndIsNotAnError()
    {
        // DK2's SetState returns a boolean and the caller carries on; a refusal is an
        // answer, not a fault (creature-state-table.md 2.2).
        CreatureState creature = Fresh();
        CreatureStateMachine.Begin(creature, Sleeping);

        // Same group tag, different category: the equal-tags half refuses outright.
        Assert.Equal(CreatureStateCategory.Sleeping, CreatureStateTable.Get(Sleeping).Category);
        Assert.Equal(CreatureStateCategory.Lair, CreatureStateTable.Get(LookingForLair).Category);
        Assert.Equal(
            CreatureStateTable.Get(Sleeping).Group,
            CreatureStateTable.Get(LookingForLair).Group);

        Assert.Equal(StateTransition.Refused,
            CreatureStateMachine.Classify(creature, LookingForLair));
        Assert.False(CreatureStateMachine.TrySetState(creature, LookingForLair));
        Assert.Equal(Sleeping, creature.Dk2StateId);
    }

    [Fact]
    public void EnteringATravelStateRecordsWhatItInterrupted()
    {
        // DK2 writes the caller's state as it enters a utility state, which is the half of
        // the resume slot that IS read (creature-state-table.md 2.1). Who writes the other
        // half, UtilityNewState, is Q414 and this gate does not guess.
        CreatureState creature = Fresh();
        CreatureStateMachine.Begin(creature, LeavingDungeon);
        Assert.Equal(CreatureStateMachine.NoState, creature.StateBeforeUtilityState);

        Assert.True(CreatureStateTable.Get(EnRoute).IsUtility);
        Assert.True(CreatureStateMachine.TrySetState(creature, EnRoute));

        Assert.Equal(EnRoute, creature.Dk2StateId);
        Assert.Equal(LeavingDungeon, creature.StateBeforeUtilityState);
        Assert.Equal(CreatureStateMachine.NoState, creature.UtilityNewState);
    }

    [Fact]
    public void LeavingATravelStateClearsTheSlotItWroteOnTheWayIn()
    {
        CreatureState creature = Fresh();
        CreatureStateMachine.Begin(creature, LeavingDungeon);
        CreatureStateMachine.TrySetState(creature, EnRoute);
        Assert.Equal(LeavingDungeon, creature.StateBeforeUtilityState);

        Assert.True(CreatureStateMachine.TrySetState(creature, Idle));
        Assert.Equal(CreatureStateMachine.NoState, creature.StateBeforeUtilityState);
    }

    [Fact]
    public void ACreatureInTransitIsJudgedByWhereItIsHeading()
    {
        // The substitution is the rule's, not ours, and it is why the resume slot had to
        // exist before the gate could: with the slot set, a move out of a travel state is
        // tested against the errand rather than against the walk.
        CreatureState creature = Fresh();
        CreatureStateMachine.Begin(creature, Idle);
        CreatureStateMachine.TrySetState(creature, EnRoute);

        creature.UtilityNewState = Sleeping;
        Assert.Equal(Sleeping,
            CreatureStateTransitions.ResolveComparisonState(creature.Dk2StateId, Sleeping));

        // With Sleeping substituted in, the sibling Lair state is refused exactly as it is
        // from Sleeping itself - the walk has stopped shielding it.
        Assert.Equal(
            CreatureStateTransitions.Classify(Sleeping, LookingForLair, CreatureStateMachine.NoState),
            CreatureStateMachine.Classify(creature, LookingForLair));
    }

    [Fact]
    public void ResumeNeedsASlotAndATravelStateToLeave()
    {
        CreatureState creature = Fresh();
        CreatureStateMachine.Begin(creature, Idle);

        // Not travelling.
        creature.UtilityNewState = Sleeping;
        Assert.False(CreatureStateMachine.TryResume(creature));
        Assert.Equal(Idle, creature.Dk2StateId);

        // Travelling with an empty slot.
        CreatureStateMachine.TrySetState(creature, EnRoute);
        creature.UtilityNewState = CreatureStateMachine.NoState;
        Assert.False(CreatureStateMachine.TryResume(creature));
        Assert.Equal(EnRoute, creature.Dk2StateId);

        // Both, and it lands on the errand and forgets it.
        creature.UtilityNewState = Idle;
        Assert.True(CreatureStateMachine.TryResume(creature));
        Assert.Equal(Idle, creature.Dk2StateId);
        Assert.Equal(CreatureStateMachine.NoState, creature.UtilityNewState);
    }

    [Fact]
    public void ATravelStateWithNoResumeTargetIsOursAndDoesNotThrow()
    {
        // Classify throws on an unset slot - there is no state -1 to look up - and DK2
        // presumably never asks, because the slot is written before the travel state is
        // entered. Who writes it is Q414. Until that is read this project CAN reach the
        // unset case, so the gate judges the travel state by itself instead. Ours, not
        // DK2's, pinned so it cannot be mistaken for the original when Q414 lands.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CreatureStateTransitions.Classify(EnRoute, Idle, CreatureStateMachine.NoState));

        CreatureState creature = Fresh();
        CreatureStateMachine.Begin(creature, EnRoute);
        Assert.Equal(CreatureStateMachine.NoState, creature.UtilityNewState);

        StateTransition answered = CreatureStateMachine.Classify(creature, Idle);
        Assert.Equal(CreatureStateTransitions.Classify(EnRoute, Idle, EnRoute), answered);
    }

    [Fact]
    public void TheGateAgreesWithTheRuleOnEveryPairItCanBeAsked()
    {
        // The gate must not be a second opinion. Every ordered pair of the 281 states is
        // driven through both, which is the only way to be sure the wrapper adds no rule
        // of its own - 78,680 pairs, and a single disagreement fails.
        int checked_ = 0;
        for (int from = 0; from < CreatureStateTable.Count; from++)
        {
            for (int to = 0; to < CreatureStateTable.Count; to++)
            {
                if (from == to) continue;

                CreatureState creature = Fresh();
                CreatureStateMachine.Begin(creature, from);

                // The rule's third argument is the resume slot. An unset slot is a state
                // this project cannot yet produce, and the rule throws on it, so the gate
                // substitutes the travel state itself; feed the rule the same thing or
                // this compares two different questions.
                StateTransition expected = CreatureStateTransitions.Classify(
                    from, to, CreatureStateTable.Get(from).IsUtility
                        ? from
                        : CreatureStateMachine.NoState);
                Assert.Equal(expected, CreatureStateMachine.Classify(creature, to));

                bool took = CreatureStateMachine.TrySetState(creature, to);
                Assert.Equal(expected == StateTransition.Allowed, took);
                Assert.Equal(took ? to : from, creature.Dk2StateId);
                checked_++;
            }
        }

        Assert.Equal(CreatureStateTable.Count * (CreatureStateTable.Count - 1), checked_);
    }
}
