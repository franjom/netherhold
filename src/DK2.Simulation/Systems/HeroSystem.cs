// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Systems;

/// <summary>A hero's turn outside a fight (hero-party-walk.md, ADR-0216): the hero branch of state 1,
/// state 90's standing job and the states its arms enter, one original turn at a time. Heroes are
/// not given jobs by <see cref="CreatureJobSystem"/>; DK2's state 1 never reaches the choosers for
/// them. Not built: state 1's pending-point walk (<c>+0x4FE</c>), the StealGold states, the Dwarf
/// tunnelling of 223/224, and the knock-out re-lead of a party.</summary>
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
            if (database.CreatureById(creature.CreatureTypeId).Flags.HasFlag(CreatureFlags.IsWorker)) continue;
            if (HandRules.IsHeld(creature) || PrisonRules.IsImprisoned(creature)) continue;
            if (CreatureDeathRules.IsDown(creature) || Combat.CombatRules.Preoccupied(creature)) continue;
            Turn(context, creature);
        }
    }

    private static void Turn(SimulationContext context, CreatureState creature)
    {
        if (StateShadow.IsTravelling(creature))
        {
            if (!IsHeroResume(creature.UtilityNewState)) return;
            if (creature.HasRoute) return;
            creature.StopMoving();
            StateShadow.Arrive(context, SystemName, creature);
        }

        bool stays = creature.Dk2StateId switch
        {
            CreatureStateMachine.Idle => StateShadow.TryMove(context, SystemName, creature, HeroJobRules.StandingJobState),
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
        return CreatureStateMachine.TrySetState(creature, HeroJobRules.WaitFailedState);
    }

    /// <summary>A walk this system started, and so one it finishes: the other systems' walks resume
    /// their own states.</summary>
    private static bool IsHeroResume(int state) => state is HeroJobRules.WaitingState
        or HeroJobRules.AtThePointState or HeroJobRules.RoamingState or HeroJobRules.HuntingState
        or HeroJobRules.AtTheHeartState or HeroJobRules.FollowingState;
}
