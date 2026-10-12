// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;

namespace DK2.Simulation;

/// <summary>The activity a creature's DK2 state names (ADR-0210): what
/// <see cref="CreatureState.Activity"/> must agree with, read from the state id, the resume
/// slot for a walk in state 6 or 7, and the job for the Sleep job's 81. A corpse, holding no state, is <see cref="CreatureActivity.Dead"/>.</summary>
/// <remarks>The activities are ours, so this table is too: it is the inverse of the moves each
/// system makes beside them. Null for a state no system moves a creature into.</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/creature-state-table.md")]
public static class ActivityRules
{
    public static CreatureActivity? Of(CreatureState creature)
    {
        if (creature is null) throw new ArgumentNullException(nameof(creature));
        int state = creature.Dk2StateId;
        if (state == CreatureStateMachine.NoState) return CreatureActivity.Dead;
        if (state is Systems.StateShadow.TravelToPoint or Systems.StateShadow.TravelToRoom)
            return Walking(creature, creature.UtilityNewState);
        return Standing(creature, state);
    }

    /// <summary>A walk is named by the state it resumes; one to a job's state is the job's
    /// walk, and a torturer's steps to face a victim or strap itself on are its job.</summary>
    private static CreatureActivity? Walking(CreatureState creature, int resume) => resume switch
    {
        LairRules.GoingToBedState => creature.CurrentJob == JobType.Sleep ? CreatureActivity.GoingToJob : CreatureActivity.GoingToSleep,
        TortureRules.TorturerFacingState or TortureRules.StrappingState => CreatureActivity.AtWork,
        GuardRules.PatrolState or 100 => CreatureActivity.AtWork,
        CombatPitRules.WaitingState => CreatureActivity.PitFighting,
        HeroJobRules.WaitingState or HeroJobRules.RoamingState => CreatureActivity.AtWork,
        HeroJobRules.AtThePointState or HeroJobRules.HuntingState or HeroJobRules.AtTheHeartState
            or HeroJobRules.FollowingState => CreatureActivity.GoingToJob,
        StealGoldRules.InTheTreasuryState => CreatureActivity.GoingToJob,
        StealGoldRules.AtTheGoldState => CreatureActivity.AtWork,
        StealGoldRules.EscapingState => CreatureActivity.Leaving,
        TunnelRules.TunnellingState or TunnelRules.HandBackState or TunnelRules.WanderState => CreatureActivity.AtWork,
        TunnelRules.AdvancingState => CreatureActivity.GoingToJob,
        CombatPitSpectatorRules.FacingState => CreatureActivity.AtWork,
        CasinoJackpotRules.ArrivedState => CreatureActivity.CollectingWinnings,
        _ when ResearchStep.IsResearchState(resume) && resume != ResearchStep.ChoosingState => CreatureActivity.AtWork,
        _ when TempleStep.IsTempleState(resume) && resume != TempleStep.EnteringState => CreatureActivity.AtWork,
        WorkshopStep.AtTheMachineState => CreatureActivity.AtWork,
        _ when CasinoStep.IsCasinoState(resume) && resume != CasinoStep.ChoosingState => CreatureActivity.AtWork,
        JailBreakRules.BreakingOutState => CreatureActivity.Rescuing,
        CallToArmsRules.ArrivedState or CallToArmsRules.MillingState => CreatureActivity.HeedingCallToArms,
        _ when JobStates.Contains(resume) => CreatureActivity.GoingToJob,
        _ => Standing(creature, resume),
    };

    private static CreatureActivity? Standing(CreatureState creature, int state) => state switch
    {
        Systems.CreatureJobSystem.HornyIdleState => CreatureActivity.UnbuiltHandler,
        Systems.CreatureJobSystem.NeutralIdleState or DropDeliveryRules.CelebratingState => CreatureActivity.Idle,
        ReaperRules.ArrivingState or ReaperRules.MaterializingState or ReaperRules.GreetingState => CreatureActivity.ReaperArriving,
        PossessionRules.FollowingState => CreatureActivity.Following,
        ReaperRules.DepartingState => CreatureActivity.Leaving,
        CreatureStateMachine.Idle or Systems.CreatureJobSystem.IdleChooserState or Systems.CreatureJobSystem.DoingNothingState or ImpStates.TaskSelection or ImpStates.AfterDrop or ImpStates.Resting or ImpStates.Wandering => CreatureActivity.Idle,
        HeroJobRules.WaitFailedState or HeroJobRules.StandingJobState or HeroJobRules.WaitingState
            or HeroJobRules.NoPointState or HeroJobRules.FollowingState => CreatureActivity.Idle,
        >= HeroJobRules.ToThePointState and <= HeroJobRules.RoamingState => CreatureActivity.AtWork,
        HeroJobRules.HuntingState or HeroJobRules.AtTheHeartState => CreatureActivity.AtWork,
        >= StealGoldRules.ToTheTreasuryState and <= StealGoldRules.AtTheGoldState => CreatureActivity.AtWork,
        StealGoldRules.EscapingState => CreatureActivity.Leaving,
        StealGoldRules.RunTheJobState => CreatureActivity.Idle,
        TunnelRules.TunnellingState or TunnelRules.WanderState => CreatureActivity.AtWork,
        TrainingRules.ReturningState => CreatureActivity.AtWork,
        PrisonRules.ReturningState => CreatureActivity.Imprisoned,
        TunnelRules.AdvancingState => CreatureActivity.GoingToJob,
        TunnelRules.HandBackState => CreatureActivity.Idle,
        RoomAttackRules.DestroyingRoomsState => CreatureActivity.AtWork,
        13 or 15 or 17 or 19 or 21 or 23 or 25 => CreatureActivity.MovingToJob,
        14 or 16 or 18 or 20 or 22 or 24 or 26 => CreatureActivity.Working,
        ImpStates.PickingUpGold => CreatureActivity.FetchingGold,
        ImpStates.AtTheStore or ImpStates.PuttingDownGold => CreatureActivity.DeliveringGold,
        ImpStates.PickingUpObject => CreatureActivity.FetchingObject,
        31 or 32 or 33 => CreatureActivity.CarryingObject,
        ImpStates.PickingUpCorpse or ImpStates.PickingUpCasualty => CreatureActivity.FetchingBody,
        >= ImpStates.CarryingCorpse and <= ImpStates.PuttingDownTheRefusedBody => CreatureActivity.CarryingBody,
        SlapRules.ReactionState or SlapRules.WorkshopState or SlapRules.PrisonState => CreatureActivity.Slapped,
        DropLandingRules.LandingState or DropLandingRules.StandingState => CreatureActivity.Idle,
        HandRules.PreparationState => CreatureActivity.BeingLifted,
        PortalRules.JoiningState => CreatureActivity.Idle,
        CallToArmsRules.GoingState or CallToArmsRules.ArrivedState or CallToArmsRules.MillingState => CreatureActivity.HeedingCallToArms,
        LairRules.MakingAHomeState => CreatureActivity.SeekingLair,
        LairRules.GoingToBedState => creature.CurrentJob == JobType.Sleep ? CreatureActivity.AtWork : CreatureActivity.GoingToSleep,
        LairRules.LyingDownState => CreatureActivity.GoingToSleep,
        LairRules.SleepingState => CreatureActivity.Sleeping,
        EatingRules.FindHatcheryState or EatingRules.InHatcheryState or EatingRules.AfterMealState => CreatureActivity.SeekingFood,
        EatingRules.EatState => CreatureActivity.FetchingChicken,
        ChickenRules.ChickenState => CreatureActivity.Chicken,
        JackInTheBoxRules.SpringState => CreatureActivity.JackInTheBox,
        >= PrisonRules.AdmittedState and <= 121 => CreatureActivity.Imprisoned,
        TortureRules.TorturerFacingState or TortureRules.TorturerWhippingState => CreatureActivity.AtWork,
        GuardRules.PatrolState or GuardRules.LostState or 100 => CreatureActivity.AtWork,
        TortureRules.StrappingState or TortureRules.TorturedState or 132 or RescueRules.FreedState
            or TortureRules.ReleasedState => CreatureActivity.BeingTortured,
        RescueRules.RescuingState or JailBreakRules.BreakingOutState => CreatureActivity.Rescuing,
        >= PaydayRules.FindTheMoneyState and <= PaydayRules.TakeItState => CreatureActivity.SeekingWages,
        >= LeavingRules.ChoosingState and <= LeavingRules.SackedState => CreatureActivity.Leaving,
        CombatStates.Engaging or 200 => CreatureActivity.Fighting,
        CombatStates.Withdrawn => CreatureActivity.Withdrawing,
        CombatStates.Fleeing => CreatureActivity.Fleeing,
        CreatureDeathRules.FallingState or CreatureDeathRules.DyingState => CreatureActivity.Dying,
        CombatStates.Frozen => CreatureActivity.Frozen,
        CombatStates.RespondingToThreat or CombatStates.StandingDown => CreatureActivity.RaisingAlarm,
        CombatStates.ReportingDanger => CreatureActivity.Reporting,
        >= CombatStates.Stunned and <= CombatStates.GettingUp => CreatureActivity.Stunned,
        TremorRules.KnockdownState or TremorRules.WaitingState or TremorRules.GettingUpState => CreatureActivity.Stunned,
        CombatStates.AtTheBody => CreatureActivity.GoingToBody,
        CombatStates.Celebrating => CreatureActivity.Celebrating,
        CrateRules.CrateMadeState => CreatureActivity.AtWork,
        CombatPitSpectatorRules.FacingState or CombatPitSpectatorRules.CheeringState => CreatureActivity.AtWork,
        _ when CasinoStep.IsCasinoState(state) || ResearchStep.IsResearchState(state) || TempleStep.IsTempleState(state)
            || WorkshopStep.IsWorkshopState(state) => CreatureActivity.AtWork,
        _ when CasinoJackpotRules.IsWinnerState(state) => CreatureActivity.CollectingWinnings,
        CrateRules.FetchTrapCrateState or CrateRules.FetchDoorCrateState => CreatureActivity.FetchingObject,
        CrateRules.InstallTrapState or CrateRules.InstallDoorState => CreatureActivity.CarryingObject,
        >= CombatPitRules.ArrivingState and <= CombatPitRules.WaitingState or CombatPitRules.ThrownOutState => CreatureActivity.PitFighting,
        _ when JobStates.Contains(state) => CreatureActivity.AtWork,
        _ => null,
    };

    /// <summary>The states the job handlers work in (<see cref="JobRules.StateFor"/>), and Training's
    /// cap refusal 110, which a capped creature passes through for a turn (ADR-0393).</summary>
    private static readonly HashSet<int> JobStates =
        [.. ((JobType[])Enum.GetValues(typeof(JobType))).Select(JobRules.StateFor).OfType<int>(), TrainingRules.CapRefusalState];
}
