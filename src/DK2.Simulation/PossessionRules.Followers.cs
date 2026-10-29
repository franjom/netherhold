// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Navigation;
using DK2.Simulation.Systems;

namespace DK2.Simulation;

/// <summary>The possessed creature's followers (possession-arms-and-inputs.md §4): the slot-3
/// recruit, state 246, the dismissal, and what the follow changes elsewhere.</summary>
public static partial class PossessionRules
{
    /// <summary>State 246, following the possessed creature (<c>0x00461A10</c>; category 29, no hooks).</summary>
    public const int FollowingState = 246;

    /// <summary><c>0x00515D50</c>'s squared-distance limit from the possessed creature, strict: 7 tiles.</summary>
    public const uint FollowerScanLimit = 0x31000;

    /// <summary>State 246 walks within this squared distance of the leader and runs beyond it.</summary>
    public const uint FollowerWalkLimit = 0x5000;

    /// <summary>How long a follower keeps its leader's last melee target: <c>3 ×</c> variable 19.</summary>
    public const int PossessedTargetTicks = 3 * SimulationTiming.OriginalTicksPerGameSecond;

    /// <summary><c>0x00491AE0</c>: not following already, not Horny, not in a group-1 state, no
    /// <c>+0x3CB</c> (a prison's or a Combat Pit's owner), not rebelling, and happy.</summary>
    internal static bool MayFollow(GameDatabase database, CreatureState c)
    {
        if (!CreatureStateMachine.HasState(c)) return false;
        CreatureDef def = database.CreatureById(c.CreatureTypeId);
        return c.FollowLeaderId == 0
            && !def.Flags.HasFlag(CreatureFlags.IsHorny)
            && CreatureStateTable.Get(c.Dk2StateId).Group != 1
            && c.PrisonOwnerPlayerId == PrisonRules.NoPrison && !c.CombatPitFighter
            && !c.RebelActive
            && CreatureAngerRules.MoodOf(def, c) == CreatureMood.Content;
    }

    /// <summary>The slot-3 arm (<c>0x0051503C</c>): one pick; below the cap a successful start counts.
    /// It never stamps the slot.</summary>
    private static void Recruit(SimulationContext context, ISightBlocker sight, CreatureState me, PossessionAim aim)
    {
        if (PickFollower(context.World, context.Database, sight, me, aim) is not { } pick) return;
        if (me.FollowerCount >= (context.Database[VariableTypeId.MaximumGroupNumberOfFollowers] & 0xFF)) return;
        if (!StartFollowing(context, pick, me.Id)) return;
        me.FollowerCount++;
        context.Emit(ActSystem, "possessed_recruit", $"creature={me.Id} follower={pick.Id} count={me.FollowerCount}");
    }

    /// <summary><c>0x00515D50</c>: every player not the creature's enemy, its own included, in list
    /// order; per player its creatures newest first. The predicate, the distance from the creature
    /// (not the pose), the cone from the pose and sight from the creature; the strictly lowest
    /// 4096 − dot below the accuracy threshold wins.</summary>
    internal static CreatureState? PickFollower(
        WorldState world, GameDatabase database, ISightBlocker sight, CreatureState me, PossessionAim aim)
    {
        (int px, int py, int pz) = aim.PacketOrigin;
        var pose = new Vec3(px, py, pz);
        Vec3 aimVector = Normalised(RawAim(aim.Yaw, aim.Pitch));
        int best = (int)(unchecked((uint)(database[VariableTypeId.FirstPersonAccuracyAngleDegrees] * 0x800)) / 360u);
        CreatureState? chosen = null;
        for (int i = 0; i < world.PlayerCount; i++)
        {
            PlayerId player = world.PlayerAtIndex(i).PlayerId;
            if (CombatRules.IsHostile(world, me.OwnerPlayerId, player.Value)) continue;
            foreach (CreatureState c in world.OwnedCreatures(player))
            {
                if (!MayFollow(database, c)) continue;
                if (TrapRules.DistanceSquared(me.PosX, me.PosY, c.PosX, c.PosY) >= FollowerScanLimit) continue;
                var at = new Vec3(c.PosX, c.PosY, c.PosZ);
                if (!InAimCone(aim.Yaw.Value, pose, at)) continue;
                if (!LineOfSight.Clear(sight, me.PosX, me.PosY, c.PosX, c.PosY)) continue;
                int score = 4096 - Dot(Normalised(at - pose), aimVector);
                if (score >= best) continue;
                best = score;
                chosen = c;
            }
        }
        return chosen;
    }

    /// <summary><c>0x00461980</c>: a re-follow of the same leader clears the field first; the
    /// predicate and state 246 must both take, then the leader, the route deadline and a stop. The
    /// leader's count is the caller's.</summary>
    public static bool StartFollowing(SimulationContext context, CreatureState c, int leaderId)
    {
        if (leaderId != 0 && leaderId == c.FollowLeaderId) c.FollowLeaderId = 0;
        long now = context.Tick.Value;
        if (MayFollow(context.Database, c) && CreatureStateMachine.TrySetState(context.Hooks, c, FollowingState, now))
        {
            c.FollowLeaderId = leaderId;
            c.FollowRouteTick = now;
            c.StopMoving();
            return true;
        }
        c.FollowLeaderId = 0;
        return false;
    }

    /// <summary>State 246 (<c>0x00461A10</c>). The follow ends, to idle, when the leader is gone, not a
    /// living creature, of another owner or no longer possessed; its count is not touched. Otherwise,
    /// once the deadline has passed, a walk to the leader resuming 246, walking when near and running
    /// beyond; a failed route retries a second later. A taken route leaves the deadline, so each
    /// return to 246 routes again.</summary>
    public static bool Follow(SimulationContext context, CreatureState me)
    {
        CreatureState? leader = context.World.CreatureById(me.FollowLeaderId);
        if (leader is null || !CreatureStateMachine.HasState(leader)
            || leader.OwnerPlayerId != me.OwnerPlayerId || !leader.Possessed)
        {
            me.FollowLeaderId = 0;
            return false;
        }

        long now = context.Tick.Value;
        if (unchecked((ulong)me.FollowRouteTick) > unchecked((ulong)now)) return true;
        if (!context.Navigation.TryRouteTo(me, leader.PosX, leader.PosY, MoveOptions.Loose))
        {
            me.FollowRouteTick = now + SimulationTiming.OriginalTicksPerGameSecond;
            return true;
        }
        CreatureStateMachine.BeginTravel(context.Hooks, me, StateShadow.TravelToPoint, FollowingState);
        me.Running = TrapRules.DistanceSquared(leader.PosX, leader.PosY, me.PosX, me.PosY) >= FollowerWalkLimit;
        return true;
    }

    /// <summary>A follower's turn, for the systems that dispatch states: state 1 re-follows first
    /// (<c>0x0047740F</c>; refused, the field is cleared and the leader's count kept), a walk resuming
    /// 246 arrives, and 246 runs. True when the turn was the follow's.</summary>
    internal static bool FollowerTurn(SimulationContext context, string system, CreatureState c)
    {
        // 1's arm, not in the turn a combat handler set 1 (0x00476FC0).
        if (c.Dk2StateId == CreatureStateMachine.Idle && c.FollowLeaderId != 0)
            return context.IdleHandlerWaits(c) || StartFollowing(context, c, c.FollowLeaderId);
        if (StateShadow.IsTravelling(c) && c.UtilityNewState == FollowingState)
        {
            if (c.HasRoute) return true;
            c.StopMoving();
            StateShadow.Arrive(context, system, c);
            return true;
        }
        if (c.Dk2StateId != FollowingState) return false;
        if (!Follow(context, c)) StateShadow.ToIdle(context, system, c);
        return true;
    }

    /// <summary>Action 38 (<c>0x00516E40</c>): every creature of the player with a leader, whichever,
    /// lets it go and the possessed creature's count drops once for each; no state changes, so each
    /// ends on its next state-246 turn. A follower never counted takes the count below zero.</summary>
    internal static void Dismiss(SimulationContext context, PlayerId player, CreatureState me)
    {
        foreach (CreatureState c in context.World.OwnedCreatures(player))
        {
            if (c.FollowLeaderId == 0) continue;
            c.FollowLeaderId = 0;
            me.FollowerCount--;
        }
        context.Emit(ActSystem, "possessed_dismiss", $"creature={me.Id} count={me.FollowerCount}");
    }

    /// <summary>The death bookkeeping (<c>0x00487E6A</c>), run once for each creature freed, by death
    /// or removal: a follower lowers its leader's count with no liveness test. DK2 frees the creature
    /// with its field set; ours clears it, so a corpse that is later removed does not count twice.</summary>
    public static void LeaderLosesFollower(WorldState world, CreatureState c)
    {
        if (c.FollowLeaderId == 0) return;
        if (world.CreatureById(c.FollowLeaderId) is { } leader) leader.FollowerCount--;
        c.FollowLeaderId = 0;
    }

    /// <summary>Fear in a fight (<c>0x0045F425</c>): the follower lets go and its leader's count drops.</summary>
    public static void DropFollowFromFear(WorldState world, CreatureState c) => LeaderLosesFollower(world, c);

    /// <summary><c>0x0045D5BF</c>: a follower whose leader and its last melee target are both live
    /// takes that target ahead of its fight-style arm; owner, age and class are not tested. Only a
    /// creature target is carried here.</summary>
    public static CreatureState? LeaderTarget(WorldState world, CreatureState me)
    {
        if (me.FollowLeaderId == 0 || world.CreatureById(me.FollowLeaderId) is not { } leader
            || !CreatureStateMachine.HasState(leader)) return null;
        if (leader.PossessedTargetKind != FightTargetKind.Creature) return null;
        return world.CreatureById(leader.PossessedTargetId) is { } target && CreatureStateMachine.HasState(target)
            ? target : null;
    }

    /// <summary>Creature_Update (<c>0x0048A78E</c>): while possessed, the last melee target is
    /// forgotten three seconds after it was struck. A creature no longer possessed keeps it.</summary>
    internal static void ForgetStaleTarget(SimulationContext context, CreatureState c)
    {
        if (!c.Possessed) return;
        if (unchecked((ulong)context.Tick.Value) <= unchecked((ulong)(c.PossessedTargetTick + PossessedTargetTicks))) return;
        c.PossessedTargetKind = FightTargetKind.None;
        c.PossessedTargetId = 0;
    }
}
