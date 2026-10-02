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
/// even when the target has not moved — a Flanker aiming behind the target. The state is a flag
/// on the creature (<see cref="CreatureState.Approaching"/>); <c>spFighting</c> runs when it is
/// clear. Behind the target is read from its velocity, not its facing (Q400).</summary>
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
    public const int EntryOptions = 0x02;

    /// <summary>The four neighbours a door's approach tries, in table <c>0x0069A070</c>'s order:
    /// north, east, south, west.</summary>
    private static readonly (int X, int Y)[] Neighbours = [(0, -1), (1, 0), (0, 1), (-1, 0)];

    /// <summary>The entry (<c>0x00471D10</c>, §1): a route to the target's position with option word
    /// 2. For a door that fails, the first of its four neighbour centres a route reaches with option 0,
    /// pulled a quarter tile back toward the door, is routed to with option 0 (door-blocking.md). On
    /// success the state is entered with its refresh countdown at ten; failure leaves the creature in
    /// <c>spFighting</c> to try again next tick.</summary>
    public static bool Enter(SimulationContext context, CreatureState me, int targetX, int targetY)
    {
        if (!context.Navigation.TryRouteTo(me, targetX, targetY, EntryOptions)
            && !(me.FightTargetKind == FightTargetKind.Door && BesideDoor(context, me, targetX, targetY)))
            return false;
        me.Approaching = true;
        me.RouteRefreshCountdown = RefreshCalls;
        me.ChaseTicks = 0;
        context.Emit(SystemName, "approaches", $"creature={me.Id} target={me.FightTargetId} kind={me.FightTargetKind} to={targetX},{targetY}");
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
            navigation.TryRouteTo(me, x - (dx * Fp32.One / 4), y - (dy * Fp32.One / 4), MoveOptions.Plain);
            return true;
        }
        return false;
    }

    [ThreadStatic] private static List<Fixed16Vector>? _scratch;
    private static List<Fixed16Vector> Scratch => _scratch ??= [];

    /// <summary>Leaves state 8 for <c>spFighting</c>, which runs next tick: the route is dropped.</summary>
    public static void End(CreatureState me, string reason, SimulationContext context)
    {
        me.Approaching = false;
        me.StopMoving();
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

    /// <summary>A target walking away from the fight — DK2's en route to state 202 or 251, the flee
    /// run and the withdrawal.</summary>
    public static bool IsRetreating(CreatureState target) =>
        target.Activity is CreatureActivity.Fleeing or CreatureActivity.Withdrawing;

    /// <summary>The handler before movement (<c>0x00470E30</c>, §2) and the refresh (§3), one call.
    /// True when the creature is still approaching after it.</summary>
    public static bool Tick(
        SimulationContext context, ISightBlocker sight, HeartSites hearts, RoomCache rooms, CreatureState me, CreatureDef mine,
        List<Fixed16Vector> scratch)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;

        // 1. A target gone from the world.
        CreatureState? target = me.FightTargetKind == FightTargetKind.Creature ? world.CreatureById(me.FightTargetId) : null;
        if (me.FightTargetKind == FightTargetKind.Creature && target is null)
        {
            CombatRules.Disengage(context.Hooks, me);
            End(me, "target_gone", context);
            return false;
        }

        // 2. An ordinary creature target is chosen afresh; a different choice ends the approach. (The
        // body flag AttackAbnormalTargets that exempts a creature is not modelled: always clear.)
        if (target != null)
        {
            // The target stays in place for the choice: the engage helper keeps the route for the
            // same target and drops it for a new one, which ends the approach anyway.
            int previous = me.FightTargetId;
            byte previousKind = me.FightTargetKind;
            if (!CombatTargetRules.SelectAVictim(context, sight, hearts, rooms, me, mine, scratch) || !CombatRules.IsFighting(me))
            {
                if (CombatRules.IsFighting(me)) CombatRules.Disengage(context.Hooks, me);
                End(me, "nobody", context);
                return false;
            }
            if (me.FightTargetKind != previousKind || me.FightTargetId != previous)
            {
                End(me, "new_target", context);
                return false;
            }
        }

        // 3. The fear counter, without the withdrawal test, when an escape exists.
        if (!FearRules.Cornered(context, me, mine, scratch) && FearRules.OverThreat(world, database, me, mine))
        {
            me.Approaching = false;
            FearRules.EnterFlee(context, me, mine);
            return false;
        }

        // 4. The chase limit on a retreating target.
        if (target != null && IsRetreating(target))
        {
            me.ChaseTicks++;
            if (me.ChaseTicks > ChaseLimitTicks(database, me))
            {
                me.RetreatingCreature = target.Id;
                context.Emit(SystemName, "gives_up_chase", $"creature={me.Id} target={target.Id} ticks={me.ChaseTicks}");
                me.ChaseTicks = 0;
                CombatRules.Disengage(context.Hooks, me);
                End(me, "retreating", context);
                return false;
            }
        }
        else me.ChaseTicks = 0;

        // 5. Strictly in reach with line of sight: spFighting swings next tick.
        if (!FightRules.TargetPosition(world, me, out int x, out int y, out TilePos targetTile))
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
        if (!context.Navigation.TryRouteTo(me, destX, destY, MoveOptions.Plain))
        {
            End(me, "no_route", context);
            return false;
        }
        me.RouteRefreshCountdown = RefreshCalls;
        context.Emit(SystemName, "reroutes", $"creature={me.Id} to={destX},{destY}");
        return true;
    }

    /// <summary>The refreshed destination (§3): the target's position, or for a Flanker the point
    /// behind a moving target — two tiles back for a spell engagement, a quarter tile for melee —
    /// when that point is on passable ground; a standing target gives its own position.</summary>
    public static void Destination(
        SimulationContext context, CreatureState me, CreatureDef mine, CreatureState? target, int targetX, int targetY, out int x, out int y)
    {
        x = targetX;
        y = targetY;
        if (mine.FightStyle != FightStyle.Flanker || target is null) return;
        long vx = target.Body.Vel.X, vy = target.Body.Vel.Y;
        if (vx == 0 && vy == 0) return;
        long length = ShotRules.Isqrt((vx * vx) + (vy * vy));
        int offset = me.AttackType == CombatRules.AttackTypes.SpellAtCreature ? FlankerSpellOffset : FlankerMeleeOffset;
        int bx = (int)(targetX - (vx * offset / length));
        int by = (int)(targetY - (vy * offset / length));
        if (!ShotRules.Passable(context.World, context.Navigation, bx, by)) return;
        x = bx;
        y = by;
    }
}
