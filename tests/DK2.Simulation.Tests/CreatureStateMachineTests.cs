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
[StatesUnrecorded]
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

    /// <summary>The one refusal that is not the rule's: a claiming category's enter hook finds no
    /// room under (0, 0), where <see cref="Fresh"/> stands, and leaves the creature in 1 (ADR-0309).</summary>
    private static bool RefusedByTheClaim(CreatureState creature, int target) =>
        RoomCapacityRules.Claims(CreatureStateTable.Get(target).Category) && creature.Dk2StateId == Idle;

    /// <summary>A context whose (0, 0) is a one-place room of <paramref name="type"/>, so a claim
    /// made there succeeds.</summary>
    private static StateHookContext OnARoom(RoomTypeId type)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral]);
        RoomRules.Place(db, ref world.Tile(0, 0), db.Room(type), 0);
        if (type == RoomTypeId.TortureChamber) world.SpawnObject(db, TortureRules.WheelObjectId, 0, 0).AutoPlaced = true;
        return new StateHookContext(world, db);
    }

    [Fact]
    public void AFreshCreatureIsIdleAsTheConstructorLeavesIt()
    {
        // 0x00476B40 writes +0x10 = 1, +0x40 = 0 and +0xDF = 0 (travel-states.md 2.4).
        CreatureState creature = Fresh();

        Assert.True(CreatureStateMachine.HasState(creature));
        Assert.Equal((Idle, 0, 0),
            (creature.Dk2StateId, creature.StateBeforeUtilityState, creature.UtilityNewState));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(281)]
    [InlineData(int.MaxValue)]
    public void AStateIdOutsideTheTableIsRejected(int notAState)
    {
        CreatureState creature = Fresh();
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CreatureStateMachine.TrySetState(DetachedHooks.Context, creature, notAState));
        Assert.Equal(Idle, creature.Dk2StateId);
    }

    [Fact]
    public void ARefusedMoveLeavesTheCreatureWhereItWasAndIsNotAnError()
    {
        // DK2's SetState returns a boolean and the caller carries on; a refusal is an
        // answer, not a fault (creature-state-table.md 2.2).
        CreatureState creature = Fresh();
        StatePlacement.Place(creature, Sleeping);

        // Same group tag, different category: the equal-tags half refuses outright.
        Assert.Equal(CreatureStateCategory.Sleeping, CreatureStateTable.Get(Sleeping).Category);
        Assert.Equal(CreatureStateCategory.Lair, CreatureStateTable.Get(LookingForLair).Category);
        Assert.Equal(
            CreatureStateTable.Get(Sleeping).Group,
            CreatureStateTable.Get(LookingForLair).Group);

        Assert.Equal(StateTransition.Refused,
            CreatureStateMachine.Classify(creature, LookingForLair));
        Assert.False(CreatureStateMachine.TrySetState(DetachedHooks.Context, creature, LookingForLair));
        Assert.Equal(Sleeping, creature.Dk2StateId);
    }

    [Fact]
    public void EnteringATravelStateRecordsWhatItInterrupted()
    {
        // SetState saves the caller as it enters a utility state (travel-states.md 3), and
        // the route helper writes the resume slot only after that call returns - so the
        // save is the caller's state, never the resume.
        CreatureState creature = Fresh();
        StatePlacement.Place(creature, LeavingDungeon);
        Assert.Equal(0, creature.StateBeforeUtilityState);

        Assert.True(CreatureStateTable.Get(EnRoute).IsUtility);
        Assert.True(CreatureStateMachine.TrySetState(DetachedHooks.Context, creature, EnRoute));

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
        StatePlacement.Place(creature, LeavingDungeon);
        CreatureStateMachine.BeginTravel(DetachedHooks.Context, creature, EnRoute, Idle);
        Assert.Equal(LeavingDungeon, creature.StateBeforeUtilityState);

        Assert.True(CreatureStateMachine.Arrive(DetachedHooks.Context, creature));
        Assert.Equal(Idle, creature.Dk2StateId);
        Assert.Equal(LeavingDungeon, creature.StateBeforeUtilityState);

        // Travel to travel: the save is 1, not the walk it came from.
        CreatureStateMachine.BeginTravel(DetachedHooks.Context, creature, EnRoute, Sleeping);
        Assert.Equal(Idle, creature.StateBeforeUtilityState);
        Assert.True(CreatureStateMachine.TrySetState(DetachedHooks.Context, creature, EnRouteToRoom));
        Assert.Equal(CreatureStateMachine.Idle, creature.StateBeforeUtilityState);
    }

    [Fact]
    public void AWalkIsOneCallInAndOneCallOut()
    {
        // The protocol, as the two route helpers and the arrival branch have it: enter the
        // travel state, then write the slot; on arrival hand over and reset the slot to 1.
        // A caller's whole contribution is the target, the resume state and the options.
        CreatureState creature = Fresh();
        StatePlacement.Place(creature, HatcheryWalkCaller);

        CreatureStateMachine.BeginTravel(DetachedHooks.Context, creature, EnRouteToRoom, Eating);
        Assert.Equal(EnRouteToRoom, creature.Dk2StateId);
        Assert.Equal(HatcheryWalkCaller, creature.StateBeforeUtilityState);
        Assert.Equal(Eating, creature.UtilityNewState);

        Assert.True(CreatureStateMachine.Arrive(DetachedHooks.Context, creature));
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
        // category: here only the room claim, with no room underfoot (travel-states.md 3, 4).
        int allowed = 0;
        for (int resume = 0; resume < CreatureStateTable.Count; resume++)
        {
            CreatureState creature = Fresh();
            StatePlacement.Place(creature, Sleeping);
            CreatureStateMachine.BeginTravel(DetachedHooks.Context, creature, EnRoute, resume);
            if (CreatureStateMachine.Arrive(DetachedHooks.Context, creature) || RefusedByTheClaim(creature, resume)) allowed++;
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
        StatePlacement.Place(creature, Idle);
        CreatureStateMachine.BeginTravel(DetachedHooks.Context, creature, EnRoute, Sleeping);

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
        // A claiming job's state is legal but its enter hook refuses it here, with no room.
        int state = JobRules.StateFor(job)!.Value;
        bool claims = RoomCapacityRules.Claims(CreatureStateTable.Get(state).Category);

        CreatureState walker = Fresh();
        StatePlacement.Place(walker, Idle);
        Assert.Equal(StateTransition.Allowed, CreatureStateMachine.Classify(walker, EnRouteToRoom));
        CreatureStateMachine.BeginTravel(DetachedHooks.Context, walker, EnRouteToRoom, state);
        Assert.Equal(EnRouteToRoom, walker.Dk2StateId);
        Assert.Equal(StateTransition.Allowed, CreatureStateMachine.Classify(walker, state));
        Assert.Equal(!claims, CreatureStateMachine.Arrive(DetachedHooks.Context, walker));
        Assert.True(CreatureStateMachine.TrySetState(DetachedHooks.Context, walker, Idle));

        CreatureState standing = Fresh();
        StatePlacement.Place(standing, Idle);
        Assert.True(CreatureStateMachine.TrySetState(DetachedHooks.Context, standing, Idle));
        Assert.Equal(StateTransition.Allowed, CreatureStateMachine.Classify(standing, state));
        Assert.Equal(!claims, CreatureStateMachine.TrySetState(DetachedHooks.Context, standing, state));
        Assert.True(CreatureStateMachine.TrySetState(DetachedHooks.Context, standing, Idle));
    }

    [Fact]
    public void TheCaptiveChainIsLegalFromAdmissionAndFromADrop()
    {
        // prison.md: an imp admits a captive into 114 and a hand drop lands in 120, both then
        // 115. 115 walks in 6 to resume in 116, and the next roll goes back through 115. A
        // hungry captive goes 115 -> 117, walks in 6 to its chicken in 118, eats, 119, 115;
        // with no chicken 117 goes to 116.
        CreatureState captive = Fresh();
        StatePlacement.Place(captive, 114);
        Assert.True(CreatureStateMachine.TrySetState(DetachedHooks.Context, captive, 115));
        CreatureStateMachine.BeginTravel(DetachedHooks.Context, captive, EnRoute, 116);
        Assert.Equal(EnRoute, captive.Dk2StateId);
        Assert.True(CreatureStateMachine.Arrive(DetachedHooks.Context, captive));
        Assert.Equal(116, captive.Dk2StateId);
        Assert.True(CreatureStateMachine.TrySetState(DetachedHooks.Context, captive, 115));
        Assert.True(CreatureStateMachine.TrySetState(DetachedHooks.Context, captive, 117));
        CreatureStateMachine.BeginTravel(DetachedHooks.Context, captive, EnRoute, 118);
        Assert.True(CreatureStateMachine.Arrive(DetachedHooks.Context, captive));
        Assert.True(CreatureStateMachine.TrySetState(DetachedHooks.Context, captive, 119));
        Assert.True(CreatureStateMachine.TrySetState(DetachedHooks.Context, captive, 115));
        Assert.True(CreatureStateMachine.TrySetState(DetachedHooks.Context, captive, 117));
        Assert.True(CreatureStateMachine.TrySetState(DetachedHooks.Context, captive, 116));

        // The hand drops a creature into 77, whose handler 0x0046F1F0 sends it to 115 in a
        // Prison and 130 in a Torture Chamber; 130 straps it on and moves to 131. 120 shares
        // that handler through a thunk, but in its own group 130 is refused from it.
        CreatureState dropped = Fresh();
        StatePlacement.Place(dropped, 77);
        Assert.True(CreatureStateMachine.TrySetState(OnARoom(RoomTypeId.Prison), dropped, 115));

        CreatureState victim = Fresh();
        StatePlacement.Place(victim, 77);
        StateHookContext chamber = OnARoom(RoomTypeId.TortureChamber);
        Assert.True(CreatureStateMachine.TrySetState(chamber, victim, 130));
        Assert.True(CreatureStateMachine.TrySetState(chamber, victim, 131));
        Assert.Equal(StateTransition.Refused, CreatureStateTransitions.Classify(120, 130, CreatureStateMachine.Idle));
    }

    [Fact]
    public void ThePaydayChainIsLegalFromIdleAndEachStateDropsBackToIt()
    {
        // payday.md 4.1-4.4: the decision sets 187 from idle, 187 walks in 6 resuming in
        // 188, 188 moves to 189, and 189 returns failure, which is idle. 187 and 188 fail
        // to idle as well. 189's other exit, the Drink job's walk (Q421), goes in 7.
        CreatureState payee = Fresh();
        StatePlacement.Place(payee, Idle);
        Assert.True(CreatureStateMachine.TrySetState(DetachedHooks.Context, payee, PaydayRules.FindTheMoneyState));
        CreatureStateMachine.BeginTravel(DetachedHooks.Context, payee, EnRoute, PaydayRules.ArrivingState);
        Assert.Equal(EnRoute, payee.Dk2StateId);
        Assert.True(CreatureStateMachine.Arrive(DetachedHooks.Context, payee));
        Assert.Equal(PaydayRules.ArrivingState, payee.Dk2StateId);
        Assert.True(CreatureStateMachine.TrySetState(DetachedHooks.Context, payee, PaydayRules.TakeItState));
        Assert.Equal(StateTransition.Allowed, CreatureStateMachine.Classify(payee, EnRouteToRoom));
        Assert.True(CreatureStateMachine.TrySetState(DetachedHooks.Context, payee, Idle));

        foreach (int state in new[] { PaydayRules.FindTheMoneyState, PaydayRules.ArrivingState })
        {
            CreatureState failing = Fresh();
            StatePlacement.Place(failing, Idle);
            Assert.True(CreatureStateMachine.TrySetState(DetachedHooks.Context, failing, state));
            Assert.True(CreatureStateMachine.TrySetState(DetachedHooks.Context, failing, Idle));
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
        StatePlacement.Place(imp, Idle);
        Assert.True(CreatureStateMachine.TrySetState(DetachedHooks.Context, imp, ImpStates.TaskSelection));
        CreatureStateMachine.BeginTravel(DetachedHooks.Context, imp, EnRoute, entry);
        Assert.Equal(EnRoute, imp.Dk2StateId);
        Assert.True(CreatureStateMachine.Arrive(DetachedHooks.Context, imp));
        Assert.Equal(entry, imp.Dk2StateId);
        Assert.True(CreatureStateMachine.TrySetState(DetachedHooks.Context, imp, work));
        Assert.True(CreatureStateMachine.TrySetState(DetachedHooks.Context, imp, ImpStates.TaskSelection));
        Assert.True(CreatureStateMachine.TrySetState(DetachedHooks.Context, imp, Idle));

        CreatureState lost = Fresh();
        StatePlacement.Place(lost, Idle);
        Assert.True(CreatureStateMachine.TrySetState(DetachedHooks.Context, lost, ImpStates.TaskSelection));
        CreatureStateMachine.BeginTravel(DetachedHooks.Context, lost, EnRoute, entry);
        Assert.True(CreatureStateMachine.TrySetState(DetachedHooks.Context, lost, Idle));

        // The entry state gives up to 10 as well, when the task has gone on arrival.
        CreatureState late = Fresh();
        StatePlacement.Place(late, Idle);
        Assert.True(CreatureStateMachine.TrySetState(DetachedHooks.Context, late, ImpStates.TaskSelection));
        Assert.True(CreatureStateMachine.TrySetState(DetachedHooks.Context, late, entry));
        Assert.True(CreatureStateMachine.TrySetState(DetachedHooks.Context, late, ImpStates.TaskSelection));
    }

    [Fact]
    public void TheImpGoldChainIsLegalFromTaskSelectionMiningAndPickingUp()
    {
        // imp-state-exits.md 27-29: 10 walks in 6 to 27; 27 and 26 walk in 6 to 28 through
        // the store chooser; 28 moves to 29, which sets 10, and 10 walks on to 28 again.
        CreatureState imp = Fresh();
        StatePlacement.Place(imp, Idle);
        Assert.True(CreatureStateMachine.TrySetState(DetachedHooks.Context, imp, ImpStates.TaskSelection));
        CreatureStateMachine.BeginTravel(DetachedHooks.Context, imp, EnRoute, ImpStates.PickingUpGold);
        Assert.True(CreatureStateMachine.Arrive(DetachedHooks.Context, imp));
        Assert.Equal(ImpStates.PickingUpGold, imp.Dk2StateId);
        CreatureStateMachine.BeginTravel(DetachedHooks.Context, imp, EnRoute, ImpStates.AtTheStore);
        Assert.Equal(EnRoute, imp.Dk2StateId);
        Assert.True(CreatureStateMachine.Arrive(DetachedHooks.Context, imp));
        Assert.True(CreatureStateMachine.TrySetState(DetachedHooks.Context, imp, ImpStates.PuttingDownGold));
        Assert.True(CreatureStateMachine.TrySetState(DetachedHooks.Context, imp, ImpStates.TaskSelection));
        CreatureStateMachine.BeginTravel(DetachedHooks.Context, imp, EnRoute, ImpStates.AtTheStore);
        Assert.Equal(EnRoute, imp.Dk2StateId);
        Assert.True(CreatureStateMachine.TrySetState(DetachedHooks.Context, imp, Idle));

        CreatureState miner = Fresh();
        StatePlacement.Place(miner, Idle);
        Assert.True(CreatureStateMachine.TrySetState(DetachedHooks.Context, miner, ImpStates.TaskSelection));
        Assert.True(CreatureStateMachine.TrySetState(DetachedHooks.Context, miner, ImpStates.For(TileWorkKind.MineGold).Work));
        CreatureStateMachine.BeginTravel(DetachedHooks.Context, miner, EnRoute, ImpStates.AtTheStore);
        Assert.Equal(EnRoute, miner.Dk2StateId);

        CreatureState emptyHanded = Fresh();
        StatePlacement.Place(emptyHanded, Idle);
        Assert.True(CreatureStateMachine.TrySetState(DetachedHooks.Context, emptyHanded, ImpStates.TaskSelection));
        Assert.True(CreatureStateMachine.TrySetState(DetachedHooks.Context, emptyHanded, ImpStates.PickingUpGold));
        Assert.True(CreatureStateMachine.TrySetState(DetachedHooks.Context, emptyHanded, ImpStates.TaskSelection));
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
        StatePlacement.Place(imp, Idle);
        Assert.True(CreatureStateMachine.TrySetState(DetachedHooks.Context, imp, ImpStates.TaskSelection));
        CreatureStateMachine.BeginTravel(DetachedHooks.Context, imp, EnRoute, ImpStates.PickingUpObject);
        Assert.True(CreatureStateMachine.Arrive(DetachedHooks.Context, imp));
        CreatureStateMachine.BeginTravel(DetachedHooks.Context, imp, EnRoute, carrying);
        Assert.Equal(EnRoute, imp.Dk2StateId);
        Assert.True(CreatureStateMachine.Arrive(DetachedHooks.Context, imp));
        CreatureStateMachine.BeginTravel(DetachedHooks.Context, imp, EnRoute, carrying);
        Assert.Equal(EnRoute, imp.Dk2StateId);
        Assert.True(CreatureStateMachine.Arrive(DetachedHooks.Context, imp));
        Assert.Equal(carrying, imp.Dk2StateId);
        Assert.True(CreatureStateMachine.TrySetState(DetachedHooks.Context, imp, ImpStates.TaskSelection));

        CreatureState empty = Fresh();
        StatePlacement.Place(empty, Idle);
        Assert.True(CreatureStateMachine.TrySetState(DetachedHooks.Context, empty, ImpStates.TaskSelection));
        Assert.True(CreatureStateMachine.TrySetState(DetachedHooks.Context, empty, ImpStates.PickingUpObject));
        Assert.True(CreatureStateMachine.TrySetState(DetachedHooks.Context, empty, ImpStates.TaskSelection));
    }

    /// <summary>From 10 along walks in 6 and hand-overs, ending with a move to 10.</summary>
    private static void WalksThenBackToSelection(int[] walks)
    {
        CreatureState imp = Fresh();
        StatePlacement.Place(imp, Idle);
        Assert.True(CreatureStateMachine.TrySetState(DetachedHooks.Context, imp, ImpStates.TaskSelection));
        foreach (int resume in walks)
        {
            CreatureStateMachine.BeginTravel(DetachedHooks.Context, imp, EnRoute, resume);
            Assert.Equal(EnRoute, imp.Dk2StateId);
            Assert.True(CreatureStateMachine.Arrive(DetachedHooks.Context, imp));
            Assert.Equal(resume, imp.Dk2StateId);
        }

        Assert.True(CreatureStateMachine.TrySetState(DetachedHooks.Context, imp, ImpStates.TaskSelection));
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
                StatePlacement.Place(creature, from);

                // The rule's third argument is the resume slot, which a fresh creature
                // carries at DK2's constructor value of 0; feed the rule the same thing
                // the gate reads or this compares two different questions.
                StateTransition expected = CreatureStateTransitions.Classify(from, to, 0);
                Assert.Equal(expected, CreatureStateMachine.Classify(creature, to));

                bool took = CreatureStateMachine.TrySetState(DetachedHooks.Context, creature, to);
                bool claimRefused = !took && expected == StateTransition.Allowed && RefusedByTheClaim(creature, to);
                Assert.Equal(expected == StateTransition.Allowed, took || claimRefused);
                Assert.Equal(took ? to : claimRefused ? Idle : from, creature.Dk2StateId);
                checked_++;
            }
        }

        Assert.Equal(CreatureStateTable.Count * (CreatureStateTable.Count - 1), checked_);
    }
}
