// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Systems;

/// <summary>A hero's turn outside a fight (hero-party-walk.md, ADR-0216): the hero branch of state 1,
/// state 90's standing job and the states its arms enter, one original turn at a time. Heroes are
/// not given jobs by <see cref="CreatureJobSystem"/>; DK2's state 1 never reaches the choosers for
/// them. A hero worker is a hero too: state 1 tests the hero owner before the worker flag
/// (<c>0x004773D0</c>). A walk's arrival hands over to the state it resumes, which runs from the
/// next turn, as DK2's dispatcher runs one handler a turn. State 1 first asks
/// <see cref="Combat.FightReturnRules"/> (ADR-0225). StealGold's states are <see cref="StealGoldRules"/>
/// (ADR-0219), tunnelling's <see cref="TunnelRules"/> (ADR-0222).</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/hero-party-walk.md")]
public sealed class HeroSystem : ISimulationSystem
{
    private const string SystemName = "Hero";

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        if (!SimulationTiming.OnOriginalTick(context.Tick.Value)) return;
        GameDatabase database = context.Database;
        IReadOnlyList<CreatureState> creatures = context.World.Creatures;
        for (int i = 0; i < creatures.Count; i++)
        {
            CreatureState creature = creatures[i];
            if (!HeroJobRules.IsHero(creature)) continue;
            if (HandRules.IsHeld(creature) || PrisonRules.IsImprisoned(creature)) continue;
            if (CreatureDeathRules.IsDown(creature) || Combat.CombatRules.Preoccupied(creature)) continue;
            Turn(context, creature);
            // State 169 removes the thief: the next creature has moved into its slot.
            if (i < creatures.Count && !ReferenceEquals(creatures[i], creature)) i--;
        }
    }

    private static void Turn(SimulationContext context, CreatureState creature)
    {
        // The creature update's room arm runs before any state handler, whatever the state.
        if (RoomAttackRules.TryBegin(context, SystemName, creature)) return;

        if (StateShadow.IsTravelling(creature))
        {
            if (!IsHeroResume(creature.UtilityNewState)) return;
            if (creature.HasRoute) return;
            creature.StopMoving();
            StateShadow.Arrive(context, SystemName, creature);
            return;
        }

        bool stays = creature.Dk2StateId switch
        {
            CreatureStateMachine.Idle => Combat.FightReturnRules.TryReturn(context, creature)
                || StateShadow.TryMove(context, SystemName, creature, HeroJobRules.StandingJobState),
            // A worker with no task goes 10, 12, 87 and a walk back to 1 (imp-idle-and-wandering.md
            // §5), where a hero's goes to 90. Ours: the wander is not built, so 10 idles at once.
            ImpStates.TaskSelection => false,
            ImpStates.Wandering => ImpTaskSystem.Wander(context, creature, context.Database.CreatureById(creature.CreatureTypeId)),
            HeroJobRules.StandingJobState => StandingJob(context, creature),
            HeroJobRules.WaitFailedState => context.Tick.Value <= creature.StateDeadlineTick,
            HeroJobRules.WaitingState => HeroJobRules.Wait(context, creature),
            HeroJobRules.ToThePointState => HeroJobRules.ToThePoint(context, creature),
            HeroJobRules.AtThePointState => HeroJobRules.AtThePoint(context, SystemName, creature),
            HeroJobRules.RoamingState => HeroJobRules.Roam(context, SystemName, creature),
            HeroJobRules.HuntingState => HeroJobRules.Hunt(context, creature),
            HeroJobRules.AtTheHeartState => false,
            HeroJobRules.FollowingState => HeroJobRules.Follow(context, creature),
            HeroJobRules.NoPointState => true,
            StealGoldRules.ToTheTreasuryState => StealGoldRules.ToTheTreasury(context, SystemName, creature),
            StealGoldRules.InTheTreasuryState => StealGoldRules.InTheTreasury(context, SystemName, creature),
            StealGoldRules.AtTheGoldState => StealGoldRules.AtTheGold(context, SystemName, creature),
            StealGoldRules.EscapingState => Escape(context, creature),
            StealGoldRules.RunTheJobState => HeroJobRules.Dispatch(context, SystemName, creature),
            TunnelRules.TunnellingState => TunnelRules.Tunnel(context, SystemName, creature),
            TunnelRules.AdvancingState => TunnelRules.Advance(context, creature),
            TunnelRules.HandBackState => TunnelRules.HandBack(context, SystemName, creature),
            RoomAttackRules.DestroyingRoomsState => RoomAttackRules.DestroyRoomTile(context, creature),
            _ => true,
        };
        if (!stays) StateShadow.ToIdle(context, SystemName, creature);
    }

    /// <summary>State 90 (<c>0x00477850</c>): a follower goes to 222; anyone else runs its standing
    /// job, and on failure waits twelve turns in state 4 before idling to try again.</summary>
    private static bool StandingJob(SimulationContext context, CreatureState creature)
    {
        if (HeroJobRules.IsFollower(creature))
        {
            StateShadow.TryMove(context, SystemName, creature, HeroJobRules.FollowingState);
            return true;
        }
        if (HeroJobRules.Dispatch(context, SystemName, creature))
        {
            context.Emit(SystemName, "hero_job",
                $"creature={creature.Id} job={creature.GoodJob} player={creature.GoodJobPlayerId} " +
                $"value={creature.GoodJobActionPointId} state={creature.Dk2StateId}");
            return true;
        }
        creature.StateDeadlineTick = context.Tick.Value + HeroJobRules.RetryTurns;
        return CreatureStateMachine.TrySetState(context.Hooks, creature, HeroJobRules.WaitFailedState);
    }

    private static bool Escape(SimulationContext context, CreatureState creature)
    {
        StealGoldRules.Escaped(context, SystemName, creature);
        return true;
    }

    /// <summary>A walk this system started, and so one it finishes: the other systems' walks resume
    /// their own states.</summary>
    private static bool IsHeroResume(int state) => state is CreatureStateMachine.Idle or HeroJobRules.WaitingState
        or HeroJobRules.AtThePointState or HeroJobRules.RoamingState or HeroJobRules.HuntingState
        or HeroJobRules.AtTheHeartState or HeroJobRules.FollowingState
        or StealGoldRules.InTheTreasuryState or StealGoldRules.AtTheGoldState or StealGoldRules.EscapingState
        or TunnelRules.TunnellingState or TunnelRules.AdvancingState or TunnelRules.HandBackState
        or RoomAttackRules.DestroyingRoomsState;
}
