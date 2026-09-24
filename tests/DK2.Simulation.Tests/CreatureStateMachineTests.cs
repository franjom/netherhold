// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Simulation.Tests;

/// <summary>The transition gate, ADR-0135 step one. Every test here drives
/// <see cref="CreatureStateMachine"/> directly, apart from the systems that retire onto it:
/// the gate has to be right on its own, because each retirement that leans on a wrong gate
/// is a chance to invent a transition rule DK2 does not have.
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
    private const int EnRouteToRoom = 7;         // group 9, Movement - the room helper's
    private const int HatcheryWalkCaller = 91;   // group 7, Eating - the trace's first state
    private const int Eating = 92;               // group 7, Eating - its resume

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
        // SetState saves the caller as it enters a utility state (travel-states.md 3), and
        // the route helper writes the resume slot only after that call returns - so the
        // save is the caller's state, never the resume.
        CreatureState creature = Fresh();
        CreatureStateMachine.Begin(creature, LeavingDungeon);
        Assert.Equal(0, creature.StateBeforeUtilityState);

        Assert.True(CreatureStateTable.Get(EnRoute).IsUtility);
        Assert.True(CreatureStateMachine.TrySetState(creature, EnRoute));

        Assert.Equal(EnRoute, creature.Dk2StateId);
        Assert.Equal(LeavingDungeon, creature.StateBeforeUtilityState);
    }

    [Fact]
    public void LeavingATravelStateDoesNotClearTheSaveAndTravelToTravelOverwritesItWithIdle()
    {
        // Two things the original does that look like oversights and are not. Leaving a
        // utility state never clears +0x40, so the save goes stale and the rule keeps
        // judging by it. And a travel-to-travel move recurses through SetState(1) first,
        // so the save ends up 1 rather than the walk it came from (travel-states.md 3).
        CreatureState creature = Fresh();
        CreatureStateMachine.Begin(creature, LeavingDungeon);
        CreatureStateMachine.BeginTravel(creature, EnRoute, Idle);
        Assert.Equal(LeavingDungeon, creature.StateBeforeUtilityState);

        Assert.True(CreatureStateMachine.Arrive(creature));
        Assert.Equal(Idle, creature.Dk2StateId);
        Assert.Equal(LeavingDungeon, creature.StateBeforeUtilityState);

        // Travel to travel: the save is 1, not the walk it came from.
        CreatureStateMachine.BeginTravel(creature, EnRoute, Sleeping);
        Assert.Equal(Idle, creature.StateBeforeUtilityState);
        Assert.True(CreatureStateMachine.TrySetState(creature, EnRouteToRoom));
        Assert.Equal(CreatureStateMachine.Idle, creature.StateBeforeUtilityState);
    }

    [Fact]
    public void AWalkIsOneCallInAndOneCallOut()
    {
        // The protocol, as the two route helpers and the arrival branch have it: enter the
        // travel state, then write the slot; on arrival hand over and reset the slot to 1.
        // A caller's whole contribution is the target, the resume state and the options.
        CreatureState creature = Fresh();
        CreatureStateMachine.Begin(creature, HatcheryWalkCaller);

        CreatureStateMachine.BeginTravel(creature, EnRouteToRoom, Eating);
        Assert.Equal(EnRouteToRoom, creature.Dk2StateId);
        Assert.Equal(HatcheryWalkCaller, creature.StateBeforeUtilityState);
        Assert.Equal(Eating, creature.UtilityNewState);

        Assert.True(CreatureStateMachine.Arrive(creature));
        Assert.Equal(Eating, creature.Dk2StateId);
        Assert.Equal(CreatureStateMachine.Idle, creature.UtilityNewState);
    }

    [Fact]
    public void TheHandoverCanNeverBeRefusedByTheRuleAlone()
    {
        // Worth pinning because it explains a branch that otherwise looks live. Leaving a
        // travel state is judged by the resume state, and the target IS the resume state,
        // so the comparison is a state against itself and the rule always says yes. Every
        // refusal the original checks for comes from the enter hook of the resume's
        // category, which nothing here has yet (travel-states.md 3, 4).
        int allowed = 0;
        for (int resume = 0; resume < CreatureStateTable.Count; resume++)
        {
            CreatureState creature = Fresh();
            CreatureStateMachine.Begin(creature, Sleeping);
            CreatureStateMachine.BeginTravel(creature, EnRoute, resume);
            if (CreatureStateMachine.Arrive(creature)) allowed++;
            Assert.Equal(CreatureStateMachine.Idle, creature.UtilityNewState);
        }

        Assert.Equal(CreatureStateTable.Count, allowed);
    }

    [Fact]
    public void ACreatureInTransitIsJudgedByWhereItIsHeading()
    {
        // The substitution is the rule's, not ours, and it is why the resume slot had to
        // exist before the gate could: a move out of a travel state is tested against the
        // errand rather than against the walk.
        CreatureState creature = Fresh();
        CreatureStateMachine.Begin(creature, Idle);
        CreatureStateMachine.BeginTravel(creature, EnRoute, Sleeping);

        Assert.Equal(Sleeping,
            CreatureStateTransitions.ResolveComparisonState(creature.Dk2StateId, Sleeping));

        // With Sleeping substituted in, the sibling Lair state is refused exactly as it is
        // from Sleeping itself - the walk has stopped shielding it.
        Assert.Equal(
            CreatureStateTransitions.Classify(Sleeping, LookingForLair, Sleeping),
            CreatureStateMachine.Classify(creature, LookingForLair));
    }

    [Theory]
    [InlineData(JobType.Research)]
    [InlineData(JobType.Manufacture)]
    [InlineData(JobType.Sleep)]
    [InlineData(JobType.Sulk)]
    [InlineData(JobType.Guard)]
    [InlineData(JobType.Praying)]
    [InlineData(JobType.Train)]
    [InlineData(JobType.CombatPitSpectate)]
    [InlineData(JobType.Drink)]
    public void EveryJobsWalkAndItsEndAreLegalFromIdle(JobType job)
    {
        // The chooser runs in idle, the handler walks in state 7 resuming in the job's
        // state, and a job that ends drops back to idle. Each step through the rule, and
        // the room helper's no-walk shortcut (1, then the job state) as well.
        int state = JobRules.StateFor(job)!.Value;

        CreatureState walker = Fresh();
        CreatureStateMachine.Begin(walker, Idle);
        Assert.Equal(StateTransition.Allowed, CreatureStateMachine.Classify(walker, EnRouteToRoom));
        CreatureStateMachine.BeginTravel(walker, EnRouteToRoom, state);
        Assert.Equal(EnRouteToRoom, walker.Dk2StateId);
        Assert.True(CreatureStateMachine.Arrive(walker));
        Assert.True(CreatureStateMachine.TrySetState(walker, Idle));

        CreatureState standing = Fresh();
        CreatureStateMachine.Begin(standing, Idle);
        Assert.True(CreatureStateMachine.TrySetState(standing, Idle));
        Assert.True(CreatureStateMachine.TrySetState(standing, state));
        Assert.True(CreatureStateMachine.TrySetState(standing, Idle));
    }

    [Fact]
    public void TheCaptiveChainIsLegalFromAdmissionAndFromADrop()
    {
        // prison.md: an imp admits a captive into 114 and a hand drop lands in 120, both then
        // 115. 115 walks in 6 to resume in 116, and the next roll goes back through 115. A
        // hungry captive goes 115 -> 117, walks in 6 to its chicken in 118, eats, 119, 115;
        // with no chicken 117 goes to 116.
        CreatureState captive = Fresh();
        CreatureStateMachine.Begin(captive, 114);
        Assert.True(CreatureStateMachine.TrySetState(captive, 115));
        CreatureStateMachine.BeginTravel(captive, EnRoute, 116);
        Assert.Equal(EnRoute, captive.Dk2StateId);
        Assert.True(CreatureStateMachine.Arrive(captive));
        Assert.Equal(116, captive.Dk2StateId);
        Assert.True(CreatureStateMachine.TrySetState(captive, 115));
        Assert.True(CreatureStateMachine.TrySetState(captive, 117));
        CreatureStateMachine.BeginTravel(captive, EnRoute, 118);
        Assert.True(CreatureStateMachine.Arrive(captive));
        Assert.True(CreatureStateMachine.TrySetState(captive, 119));
        Assert.True(CreatureStateMachine.TrySetState(captive, 115));
        Assert.True(CreatureStateMachine.TrySetState(captive, 117));
        Assert.True(CreatureStateMachine.TrySetState(captive, 116));

        // The hand drops a creature into 77, whose handler 0x0046F1F0 sends it to 115 in a
        // Prison and 130 in a Torture Chamber; 130 straps it on and moves to 131. 120 shares
        // that handler through a thunk, but in its own group 130 is refused from it.
        CreatureState dropped = Fresh();
        CreatureStateMachine.Begin(dropped, 77);
        Assert.True(CreatureStateMachine.TrySetState(dropped, 115));

        CreatureState victim = Fresh();
        CreatureStateMachine.Begin(victim, 77);
        Assert.True(CreatureStateMachine.TrySetState(victim, 130));
        Assert.True(CreatureStateMachine.TrySetState(victim, 131));
        Assert.Equal(StateTransition.Refused, CreatureStateTransitions.Classify(120, 130, CreatureStateMachine.Idle));
    }

    [Fact]
    public void ThePaydayChainIsLegalFromIdleAndEachStateDropsBackToIt()
    {
        // payday.md 4.1-4.4: the decision sets 187 from idle, 187 walks in 6 resuming in
        // 188, 188 moves to 189, and 189 returns failure, which is idle. 187 and 188 fail
        // to idle as well. 189's other exit, the Drink job's walk (Q421), goes in 7.
        CreatureState payee = Fresh();
        CreatureStateMachine.Begin(payee, Idle);
        Assert.True(CreatureStateMachine.TrySetState(payee, PaydayRules.FindTheMoneyState));
        CreatureStateMachine.BeginTravel(payee, EnRoute, PaydayRules.ArrivingState);
        Assert.Equal(EnRoute, payee.Dk2StateId);
        Assert.True(CreatureStateMachine.Arrive(payee));
        Assert.Equal(PaydayRules.ArrivingState, payee.Dk2StateId);
        Assert.True(CreatureStateMachine.TrySetState(payee, PaydayRules.TakeItState));
        Assert.Equal(StateTransition.Allowed, CreatureStateMachine.Classify(payee, EnRouteToRoom));
        Assert.True(CreatureStateMachine.TrySetState(payee, Idle));

        foreach (int state in new[] { PaydayRules.FindTheMoneyState, PaydayRules.ArrivingState })
        {
            CreatureState failing = Fresh();
            CreatureStateMachine.Begin(failing, Idle);
            Assert.True(CreatureStateMachine.TrySetState(failing, state));
            Assert.True(CreatureStateMachine.TrySetState(failing, Idle));
        }
    }

    [Theory]
    [InlineData(TileWorkKind.Dig)]
    [InlineData(TileWorkKind.DigOwnWall)]
    [InlineData(TileWorkKind.DigEnemyWall)]
    [InlineData(TileWorkKind.MineGold)]
    [InlineData(TileWorkKind.Claim)]
    [InlineData(TileWorkKind.AttackTile)]
    [InlineData(TileWorkKind.ConvertRoom)]
    [InlineData(TileWorkKind.Reinforce)]
    [InlineData(TileWorkKind.RepairWall)]
    public void EveryImpWorkChainIsLegalFromIdleThroughTaskSelection(TileWorkKind kind)
    {
        // imp-state-exits.md: 1 reaches 10 on a later tick, 10's handler walks in 6 resuming
        // in the odd entry state, the entry moves to the even work state, and the even state
        // sets 10 once the tile is done. A failed walk returns 0 from 6, which is 1.
        (int entry, int work) = ImpStates.For(kind);

        CreatureState imp = Fresh();
        CreatureStateMachine.Begin(imp, Idle);
        Assert.True(CreatureStateMachine.TrySetState(imp, ImpStates.TaskSelection));
        CreatureStateMachine.BeginTravel(imp, EnRoute, entry);
        Assert.Equal(EnRoute, imp.Dk2StateId);
        Assert.True(CreatureStateMachine.Arrive(imp));
        Assert.Equal(entry, imp.Dk2StateId);
        Assert.True(CreatureStateMachine.TrySetState(imp, work));
        Assert.True(CreatureStateMachine.TrySetState(imp, ImpStates.TaskSelection));
        Assert.True(CreatureStateMachine.TrySetState(imp, Idle));

        CreatureState lost = Fresh();
        CreatureStateMachine.Begin(lost, Idle);
        Assert.True(CreatureStateMachine.TrySetState(lost, ImpStates.TaskSelection));
        CreatureStateMachine.BeginTravel(lost, EnRoute, entry);
        Assert.True(CreatureStateMachine.TrySetState(lost, Idle));

        // The entry state gives up to 10 as well, when the task has gone on arrival.
        CreatureState late = Fresh();
        CreatureStateMachine.Begin(late, Idle);
        Assert.True(CreatureStateMachine.TrySetState(late, ImpStates.TaskSelection));
        Assert.True(CreatureStateMachine.TrySetState(late, entry));
        Assert.True(CreatureStateMachine.TrySetState(late, ImpStates.TaskSelection));
    }

    [Fact]
    public void TheImpGoldChainIsLegalFromTaskSelectionMiningAndPickingUp()
    {
        // imp-state-exits.md 27-29: 10 walks in 6 to 27; 27 and 26 walk in 6 to 28 through
        // the store chooser; 28 moves to 29, which sets 10, and 10 walks on to 28 again.
        CreatureState imp = Fresh();
        CreatureStateMachine.Begin(imp, Idle);
        Assert.True(CreatureStateMachine.TrySetState(imp, ImpStates.TaskSelection));
        CreatureStateMachine.BeginTravel(imp, EnRoute, ImpStates.PickingUpGold);
        Assert.True(CreatureStateMachine.Arrive(imp));
        Assert.Equal(ImpStates.PickingUpGold, imp.Dk2StateId);
        CreatureStateMachine.BeginTravel(imp, EnRoute, ImpStates.AtTheStore);
        Assert.Equal(EnRoute, imp.Dk2StateId);
        Assert.True(CreatureStateMachine.Arrive(imp));
        Assert.True(CreatureStateMachine.TrySetState(imp, ImpStates.PuttingDownGold));
        Assert.True(CreatureStateMachine.TrySetState(imp, ImpStates.TaskSelection));
        CreatureStateMachine.BeginTravel(imp, EnRoute, ImpStates.AtTheStore);
        Assert.Equal(EnRoute, imp.Dk2StateId);
        Assert.True(CreatureStateMachine.TrySetState(imp, Idle));

        CreatureState miner = Fresh();
        CreatureStateMachine.Begin(miner, Idle);
        Assert.True(CreatureStateMachine.TrySetState(miner, ImpStates.TaskSelection));
        Assert.True(CreatureStateMachine.TrySetState(miner, ImpStates.For(TileWorkKind.MineGold).Work));
        CreatureStateMachine.BeginTravel(miner, EnRoute, ImpStates.AtTheStore);
        Assert.Equal(EnRoute, miner.Dk2StateId);

        CreatureState emptyHanded = Fresh();
        CreatureStateMachine.Begin(emptyHanded, Idle);
        Assert.True(CreatureStateMachine.TrySetState(emptyHanded, ImpStates.TaskSelection));
        Assert.True(CreatureStateMachine.TrySetState(emptyHanded, ImpStates.PickingUpGold));
        Assert.True(CreatureStateMachine.TrySetState(emptyHanded, ImpStates.TaskSelection));
    }

    [Theory]
    [InlineData(ObjectFlags.TypeSpellBook, 31)]
    [InlineData(ObjectFlags.TypeSpecial, 33)]
    [InlineData(ObjectFlags.TypeCrate, 32)]
    public void TheImpObjectChainIsLegalFromTaskSelection(ObjectFlags flag, int carrying)
    {
        // 10 walks in 6 to 30; 30 walks in 6 to the kind's carry state through 0x0046E480;
        // the carry state stores and sets 10, or re-routes through the helper to itself.
        Assert.Equal(carrying, ImpStates.CarryingStateFor(flag));

        CreatureState imp = Fresh();
        CreatureStateMachine.Begin(imp, Idle);
        Assert.True(CreatureStateMachine.TrySetState(imp, ImpStates.TaskSelection));
        CreatureStateMachine.BeginTravel(imp, EnRoute, ImpStates.PickingUpObject);
        Assert.True(CreatureStateMachine.Arrive(imp));
        CreatureStateMachine.BeginTravel(imp, EnRoute, carrying);
        Assert.Equal(EnRoute, imp.Dk2StateId);
        Assert.True(CreatureStateMachine.Arrive(imp));
        CreatureStateMachine.BeginTravel(imp, EnRoute, carrying);
        Assert.Equal(EnRoute, imp.Dk2StateId);
        Assert.True(CreatureStateMachine.Arrive(imp));
        Assert.Equal(carrying, imp.Dk2StateId);
        Assert.True(CreatureStateMachine.TrySetState(imp, ImpStates.TaskSelection));

        CreatureState empty = Fresh();
        CreatureStateMachine.Begin(empty, Idle);
        Assert.True(CreatureStateMachine.TrySetState(empty, ImpStates.TaskSelection));
        Assert.True(CreatureStateMachine.TrySetState(empty, ImpStates.PickingUpObject));
        Assert.True(CreatureStateMachine.TrySetState(empty, ImpStates.TaskSelection));
    }

    /// <summary>From 10 along walks in 6 and hand-overs, ending with a move to 10.</summary>
    private static void WalksThenBackToSelection(int[] walks)
    {
        CreatureState imp = Fresh();
        CreatureStateMachine.Begin(imp, Idle);
        Assert.True(CreatureStateMachine.TrySetState(imp, ImpStates.TaskSelection));
        foreach (int resume in walks)
        {
            CreatureStateMachine.BeginTravel(imp, EnRoute, resume);
            Assert.Equal(EnRoute, imp.Dk2StateId);
            Assert.True(CreatureStateMachine.Arrive(imp));
            Assert.Equal(resume, imp.Dk2StateId);
        }

        Assert.True(CreatureStateMachine.TrySetState(imp, ImpStates.TaskSelection));
    }

    [Fact]
    public void TheImpBodyChainsAreLegalFromTaskSelection()
    {
        // imp-state-exits.md 40-48: a corpse 40 into 41, re-routing to itself; the keeper's
        // own casualty 42 into 45; an enemy 42 into 43, 44 and then 46 when admitted or 47 and
        // 48 when refused. Every chain ends in 10.
        WalksThenBackToSelection([ImpStates.PickingUpCorpse, ImpStates.CarryingCorpse, ImpStates.CarryingCorpse]);
        WalksThenBackToSelection([ImpStates.PickingUpCasualty, ImpStates.CarryingToLair]);
        WalksThenBackToSelection([ImpStates.PickingUpCasualty, ImpStates.CarryingToPrison,
            ImpStates.AtThePrisonDoor, ImpStates.LeavingThePrisoner]);
        WalksThenBackToSelection([ImpStates.PickingUpCasualty, ImpStates.CarryingToPrison,
            ImpStates.AtThePrisonDoor, ImpStates.TakingTheRefusedBody,
            ImpStates.PuttingDownTheRefusedBody]);
    }

    [Fact]
    public void ATorturersStatesReachEachOtherOnlyThroughIdle()
    {
        // Q419, Q426. 127, 128 and 129 are group 8, category 0: the equal-tags branch
        // refuses a category-0 target in the same group, so 128 and 129 hop through idle,
        // SetState(1) then the other, as their handlers do. With category 0's bit at 1
        // (ADR-0195) each drops to idle, so a torturer can stop, eat and die.
        const int Torturing = 127, Facing = 128, Whipping = 129;
        foreach (int state in new[] { Torturing, Facing, Whipping })
        {
            Assert.Equal(CreatureStateCategory.Refused, CreatureStateTable.Get(state).Category);
            Assert.Equal(StateTransition.Allowed, CreatureStateTransitions.Classify(state, Idle, Idle));
        }

        Assert.Equal(StateTransition.Refused, CreatureStateTransitions.Classify(Facing, Whipping, Idle));
        Assert.Equal(StateTransition.Refused, CreatureStateTransitions.Classify(Whipping, Facing, Idle));
        Assert.Equal(StateTransition.Allowed, CreatureStateTransitions.Classify(Idle, Whipping, Idle));
        Assert.Equal(StateTransition.Allowed, CreatureStateTransitions.Classify(Idle, Facing, Idle));
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

                // The rule's third argument is the resume slot, which a fresh creature
                // carries at DK2's constructor value of 0; feed the rule the same thing
                // the gate reads or this compares two different questions.
                StateTransition expected = CreatureStateTransitions.Classify(from, to, 0);
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
