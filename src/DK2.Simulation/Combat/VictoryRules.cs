// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Navigation;

namespace DK2.Simulation.Combat;

/// <summary>What a fighter does when its target dies — docs/research/combat-approach-and-victory.md
/// §5–§7: the death leaves its attackers with the <c>0xF</c> nibble and no target; <c>spFighting</c>
/// then takes the victory arm (<c>0x00460FE0</c>): state 260 with action 55, the celebration, or
/// for <c>NeedBodyForFightIdle</c> a walk to the nearest dead body within <c>0x2000</c> squared
/// and the same pause there. DK2 ends the pause when the animation instance ends; ours lasts
/// <see cref="PauseTicks"/> — a stand-in until slot 35's length is known (Q376).</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/combat-approach-and-victory.md")]
public static class VictoryRules
{
    private const string SystemName = "combat";

    /// <summary>HYPOTHESIS, ours: two seconds for every creature. DK2 plays animation slot 35 once and
    /// idles when the instance ends; no creature's slot-35 length has been read (Q376).</summary>
    public const int PauseTicks = 8;

    /// <summary>The body search's reach (§5): fixed-point squared distance strictly under <c>0x2000</c>,
    /// two square tiles — about one and a half tiles.</summary>
    public const int BodyReachSquared = 0x2000;

    /// <summary>The death unlink's mark on an attacker whose target has died (combat-fight-state.md
    /// §6): the target is let go and the attack nibble becomes <c>0xF</c>, which the next
    /// <c>spFighting</c> tick reads as the victory arm.</summary>
    public static void TargetDied(SimulationContext context, CreatureState me)
    {
        WorldState world = context.World;
        if (me.FightTargetKind == FightTargetKind.Creature && me.FightTargetId != CombatRules.NoTarget)
        {
            CreatureState? target = world.CreatureById(me.FightTargetId);
            if (target != null) CombatRules.Unregister(target, me.Id);
        }
        int victim = me.FightTargetId;
        me.FightTargetId = CombatRules.NoTarget;
        me.FightTargetKind = FightTargetKind.None;
        me.FightTargetPlayerId = 0;
        me.FightTargetTileIndex = -1;
        me.AttackType = CombatRules.AttackTypes.Victorious;
        me.AttackRangeSquared = 0;
        me.Approaching = false;
        me.StopMoving();
        context.Emit(SystemName, "target_died", $"creature={me.Id} victim={victim}");
    }

    /// <summary>The victory arm (<c>0x00460FE0</c>, §5): the celebration where the creature stands,
    /// or for <c>NeedBodyForFightIdle</c> a route to the nearest corpse in reach — the celebration
    /// at once when none is near or no route exists (the last is ours, Q400).</summary>
    public static void Victory(SimulationContext context, CreatureState me, CreatureDef mine)
    {
        if (mine.Flags.HasFlag(CreatureFlags.NeedBodyForFightIdle))
        {
            CreatureState? body = NearestBody(context.World, me);
            if (body != null && context.Navigation.TryRouteTo(me, body.PosX, body.PosY, MoveOptions.Plain))
            {
                me.BodyTargetId = body.Id;
                me.AttackType = 0;
                CombatStates.Travel(context, me, CombatStates.AtTheBody);
                context.Emit(SystemName, "seeks_body", $"creature={me.Id} body={body.Id}");
                return;
            }
        }
        Celebrate(context, me);
    }

    /// <summary>The nearest corpse (a creature lying dead, <see cref="CreatureDeathRules.IsCorpse"/>)
    /// strictly within <see cref="BodyReachSquared"/>. DK2 searches the neutral player's dead-body
    /// list; ours keeps a corpse under its owner, so every corpse is looked at (Q400).</summary>
    public static CreatureState? NearestBody(WorldState world, CreatureState me)
    {
        CreatureState? best = null;
        int bestDistance = BodyReachSquared;
        IReadOnlyList<CreatureState> creatures = world.Creatures;
        for (int i = 0; i < creatures.Count; i++)
        {
            CreatureState candidate = creatures[i];
            if (!CreatureDeathRules.IsCorpse(candidate)) continue;
            int distance = CombatRules.DistanceSquared(me.PosX, me.PosY, candidate.PosX, candidate.PosY);
            if (distance >= bestDistance) continue;
            best = candidate;
            bestDistance = distance;
        }
        return best;
    }

    /// <summary>State 260 with action 55 (§7): standing for <see cref="PauseTicks"/>; the creature stays
    /// attackable and does not look for a fight or turn on an attacker until it is over.</summary>
    public static void Celebrate(SimulationContext context, CreatureState me)
    {
        me.AttackType = 0;
        me.BodyTargetId = CombatRules.NoTarget;
        me.CelebrationTicks = PauseTicks;
        me.StopMoving();
        me.Running = false;
        CombatStates.Arrive(context, me);
        CombatStates.Enter(context, me, CombatStates.Celebrating);
        context.Emit(SystemName, "celebrates", $"creature={me.Id} ticks={PauseTicks}");
    }

    /// <summary>Standing at the body: within half a tile of it, squared. DK2's position test
    /// <c>0x004B2AE0</c> is unread in its exact reach (Q400).</summary>
    public const int BodyStandSquared = 0x400;

    /// <summary>State 259 each tick (<c>0x00461130</c>, §6): the remembered body must still be a corpse,
    /// else the state ends idle; standing at it, or the route spent, the celebration begins there.</summary>
    public static void GoingToBody(SimulationContext context, CreatureState me)
    {
        CreatureState? body = context.World.CreatureById(me.BodyTargetId);
        if (body is null || !CreatureDeathRules.IsCorpse(body))
        {
            me.BodyTargetId = CombatRules.NoTarget;
            me.StopMoving();
            CombatStates.Idle(context.Hooks, me);
            context.Emit(SystemName, "body_gone", $"creature={me.Id}");
            return;
        }
        bool there = CombatRules.DistanceSquared(me.PosX, me.PosY, body.PosX, body.PosY) < BodyStandSquared;
        if (there || !me.HasRoute) Celebrate(context, me);
    }

    /// <summary>State 260 each tick: the pause counts down and ends in state 1, idle.</summary>
    public static void Celebrating(SimulationContext context, CreatureState me)
    {
        if (me.CelebrationTicks > 0) me.CelebrationTicks--;
        if (me.CelebrationTicks > 0) return;
        CombatStates.Idle(context.Hooks, me);
        context.Emit(SystemName, "celebration_over", $"creature={me.Id}");
    }
}
