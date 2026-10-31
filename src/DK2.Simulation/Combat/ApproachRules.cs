// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;
using DK2.Simulation.Navigation;
using DK2.Simulation.Steering;

namespace DK2.Simulation.Combat;

/// <summary>State 8, the walk to a fight — docs/research/combat-approach-and-victory.md §1–§4:
/// entered by <c>spFighting</c> out of reach with a route to the target; every tick it drops a
/// target gone, reselects an ordinary creature target and ends when the choice changes, counts
/// the fear counter without the withdrawal test, gives up a retreating target after the chase
/// variable, ends strictly in reach with line of sight, and refreshes its route every ten calls
/// even when the target has not moved — a Flanker aiming behind the target. It is a walk resuming
/// 199 (<see cref="CombatStates.Approaching"/>), so it keeps the Combat category's place and pays
/// no combat experience. Behind the target is read from its velocity, not its facing (Q400).</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/combat-approach-and-victory.md")]
public static class ApproachRules
{
    private const string SystemName = "combat";

    /// <summary><c>+0xF5</c>: handler calls between route refreshes.</summary>
    public const int RefreshCalls = 10;

    /// <summary>The Flanker's point behind the target: two tiles for a spell engagement, a quarter
    /// tile for melee (§3).</summary>
    public const int FlankerSpellOffset = 2 * Fp32.One;
    public const int FlankerMeleeOffset = Fp32.One / 4;

    /// <summary>The entry's option word, 2: the creature's own locked doors are passable (§1).</summary>
    public const Navigation.MoveOptions EntryOptions = (Navigation.MoveOptions)0x02;

    /// <summary>The four neighbours a door's approach tries, in table <c>0x0069A070</c>'s order:
    /// north, east, south, west.</summary>
    private static readonly (int X, int Y)[] Neighbours = [(0, -1), (1, 0), (0, 1), (-1, 0)];

    /// <summary>The entry (<c>0x00471D10</c>, §1): a route to the target's position with option word
    /// 2, requested directly (<c>0x004D0F60</c>), so a target on the creature's own tile is walked to:
    /// within one triangle the route is the target itself (<c>0x004CF490</c>, ADR-0388). For a door that fails, the first of its four neighbour centres a route reaches with option 0,
    /// pulled a quarter tile back toward the door, is routed to with option 0 (door-blocking.md). On
    /// success the state is entered with its refresh countdown at ten; failure leaves the creature in
    /// <c>spFighting</c> to try again next tick.</summary>
    public static bool Enter(SimulationContext context, CreatureState me, int targetX, int targetY)
    {
        if (!context.Navigation.TryRouteToStance(me, targetX, targetY, EntryOptions)
            && !(me.FightTargetKind == FightTargetKind.Door && BesideDoor(context, me, targetX, targetY)))
            return false;
        // SetState(8), then 199 into the resume slot: a walk from 199 back to 199, so no hook runs.
        CreatureStateMachine.BeginTravel(context.Hooks, me, CombatStates.Approaching, CombatStates.Engaging);
        me.RouteRefreshCountdown = RefreshCalls;
        context.Emit(SystemName, "approaches", $"creature={me.Id} target={me.FightTargetId} kind={(byte)me.FightTargetKind} to={targetX},{targetY}");
        return true;
    }

    private static bool BesideDoor(SimulationContext context, CreatureState me, int doorX, int doorY)
    {
        WorldSteering navigation = context.Navigation;
        MoverProfile profile = WorldSteering.ProfileOf(context.Database.CreatureById(me.CreatureTypeId), me.OwnerPlayerId, MoveOptions.Plain);
        var from = new Fixed16Vector(me.PosX << 4, me.PosY << 4);
        foreach ((int dx, int dy) in Neighbours)
        {
            int x = doorX + (dx * Fp32.One), y = doorY + (dy * Fp32.One);
            Scratch.Clear();
            if (!navigation.Route(from, new Fixed16Vector(x << 4, y << 4), profile, Scratch)) continue;
            // The original enters the state whatever this last request answers.
            navigation.TryRouteToStance(me, x - (dx * Fp32.One / 4), y - (dy * Fp32.One / 4), MoveOptions.Plain);
            return true;
        }
        return false;
    }

    [ThreadStatic] private static List<Fixed16Vector>? _scratch;
    private static List<Fixed16Vector> Scratch => _scratch ??= [];

    /// <summary>Stops the approach and enters the current resume state, including one
    /// replaced during reselection.</summary>
    public static void End(CreatureState me, string reason, SimulationContext context)
    {
        me.StopMoving();
        if (Systems.StateShadow.IsTravelling(me)) CombatStates.Arrive(context, me);
        context.Emit(SystemName, "approach_ends", $"creature={me.Id} reason={reason}");
    }

    /// <summary>The seconds a chase may last, by the chaser's side (§2 step 4): variable 213 for the
    /// Good player, 214 for everyone else, in ticks.</summary>
    public static int ChaseLimitTicks(GameDatabase database, CreatureState me)
    {
        int seconds = database[me.OwnerPlayerId == PlayerIds.Good
            ? VariableTypeId.GoodCreaturesChaseEnemyDurationSeconds
            : VariableTypeId.EvilCreaturesChaseEnemyDurationSeconds];
        return seconds * SimulationTiming.OriginalTicksPerGameSecond;
    }

    /// <summary><c>0x0045F000</c>: a target in state 6 resuming 251 or 202, walking to raise the alarm or
    /// to withdraw (ADR-0584). Fleeing in 203 itself is not retreating.</summary>
    public static bool IsRetreating(CreatureState target) =>
        target.Dk2StateId == Systems.StateShadow.TravelToPoint
        && target.UtilityNewState is CombatStates.Withdrawn or CombatStates.RespondingToThreat;

    /// <summary>The handler before movement (<c>0x00470E30</c>, §2) and the refresh (§3), one call.
    /// True when the creature is still approaching after it.</summary>
    public static bool Tick(
        SimulationContext context, ISightBlocker sight, HeartSites hearts, RoomCache rooms, CreatureState me, CreatureDef mine,
        List<Fixed16Vector> scratch)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;

        // 1. A target gone from the world, or none: a victor's or one let go while walking.
        if (me.FightTargetKind == FightTargetKind.None)
        {
            End(me, "no_target", context);
            return false;
        }
        CreatureState? target = me.FightTargetKind == FightTargetKind.Creature ? world.CreatureById(me.FightTargetId) : null;
        if (me.FightTargetKind == FightTargetKind.Creature && target is null)
        {
            UnlinkTarget(world, me);
            End(me, "target_gone", context);
            return false;
        }

        // 0x00470EAB: release only the outgoing engagement before choosing again. The native
        // unlink preserves the attack fields; a failed choice resumes the fight with incoming slots.
        if (target != null && !me.AttackAbnormalTargets)
        {
            int previous = me.FightTargetId;
            UnlinkTarget(world, me);
            CombatTargetRules.SelectAVictim(context, sight, hearts, rooms, me, mine, scratch);
            if (me.FightTargetKind != FightTargetKind.Creature || me.FightTargetId != previous)
            {
                End(me, "new_target", context);
                return false;
            }
        }

        // 3. The fear counter, without the withdrawal test, when an escape exists.
        if (!FearRules.Cornered(context, me, mine, scratch) && FearRules.OverThreat(world, database, me, mine))
        {
            // A follower holds on and keeps its leader (0x00470F0A).
            if (me.FollowLeaderId != 0) return true;
            FearRules.EnterFlee(context, me, mine, fromApproach: true);
            return false;
        }

        // 4. The chase limit on a retreating creature target; any other creature target zeroes the count,
        // a thing target leaves it. The give-up leaves it over the limit, for the 60-turn lapse to clear
        // (0x004710E6-0x0047118B, ADR-0584).
        if (target != null)
        {
            if (IsRetreating(target))
            {
                me.ChaseTicks = unchecked((byte)(me.ChaseTicks + 1));
                if (me.ChaseTicks > ChaseLimitTicks(database, me))
                {
                    me.RetreatingCreature = target.Id;
                    me.AvoidRetreatersCounter = 0;
                    context.Emit(SystemName, "gives_up_chase", $"creature={me.Id} target={target.Id} ticks={me.ChaseTicks}");
                    CombatRules.Unlink(world, me);
                    End(me, "retreating", context);
                    return false;
                }
            }
            else me.ChaseTicks = 0;
        }

        // 5. Strictly in reach with line of sight: spFighting swings next tick.
        if (!FightRules.TargetPosition(world, context.Database, me, out int x, out int y, out TilePos targetTile))
        {
            End(me, "no_position", context);
            return false;
        }
        if (FightRules.InRange(sight, me, x, y))
        {
            End(me, "in_reach", context);
            return false;
        }

        // Arrival by the route (0x00470C80 entering the state held at +0xDF).
        if (!me.HasRoute)
        {
            End(me, "arrived", context);
            return false;
        }

        // §3. The ten-call refresh.
        if (--me.RouteRefreshCountdown > 0) return true;
        Destination(context, me, mine, target, x, y, out int destX, out int destY);
        if (!ShotRules.Passable(world, context.Navigation, destX, destY))
        {
            End(me, "bad_destination", context);
            return false;
        }
        if (destX == me.PosX && destY == me.PosY)
        {
            me.RouteRefreshCountdown = RefreshCalls;
            return true;
        }
        if (!context.Navigation.TryRouteToStance(me, destX, destY, MoveOptions.Plain))
        {
            End(me, "no_route", context);
            return false;
        }
        me.RouteRefreshCountdown = RefreshCalls;
        context.Emit(SystemName, "reroutes", $"creature={me.Id} to={destX},{destY}");
        return true;
    }

    private static void UnlinkTarget(WorldState world, CreatureState me)
    {
        CombatRules.AttackMode attack = me.AttackType;
        int reach = me.AttackRangeSquared;
        CombatRules.Unlink(world, me);
        me.AttackType = attack;
        me.AttackRangeSquared = reach;
    }

    /// <summary>The refreshed destination (§3): the target's position, or for a Flanker the point
    /// behind the target's heading — two tiles back at attack nibble 0, a quarter tile otherwise —
    /// from the sine tables (<c>0x00470E30</c>, <c>0x006C1488</c>/<c>0x006C1C88</c>), moving or not;
    /// a point on impassable ground gives the target's position (Q400).</summary>
    public static void Destination(
        SimulationContext context, CreatureState me, CreatureDef mine, CreatureState? target, int targetX, int targetY, out int x, out int y)
    {
        x = targetX;
        y = targetY;
        if (mine.FightStyle != FightStyle.Flanker || target is null) return;
        long offset = me.AttackType == CombatRules.AttackMode.SpellAtCreature ? FlankerSpellOffset : FlankerMeleeOffset;
        int bx = (int)(targetX - ((offset * TrapRules.Sine(target.Heading)) >> 16));
        int by = (int)(targetY + ((offset * TrapRules.Cosine(target.Heading)) >> 16));
        if (!ShotRules.Passable(context.World, context.Navigation, bx, by)) return;
        x = bx;
        y = by;
    }
}
