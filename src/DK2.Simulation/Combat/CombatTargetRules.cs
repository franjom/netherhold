// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Navigation;
using DK2.Simulation.Steering;

namespace DK2.Simulation.Combat;

/// <summary>DK2's <c>SelectAVictim</c> (<c>0x0045CB00</c>, its own name) as far as the Blocker arm
/// goes: the shared scan over every creature in the game, the Blocker's score — plain distance —
/// the acceptance by sight or by a walkable route, the route check on the winner, and the
/// fight-starter that registers the engagement and sets the reach
/// (docs/research/combat-acquisition.md §6–§8). Built for <c>FightStyle.Blocker</c> only: the
/// four other arms, the spell survey, the scan over who is already attacking me, the door and
/// dungeon-heart scans and the flee check are named there and not here.</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/combat-acquisition.md")]
public static class CombatTargetRules
{
    /// <summary>One tile squared in the scans' units: closer than this, a creature fights hand to hand.</summary>
    public const int MeleeDecisionDistance = 0x1000;

    /// <summary>Whether this simulation runs an arm for the style yet.</summary>
    public static bool IsBuilt(FightStyle style) => style == FightStyle.Blocker;

    /// <summary>Finds and engages a target for <paramref name="me"/>. True when it now fights.</summary>
    public static bool SelectAVictim(
        SimulationContext context, ISightBlocker sight, CreatureState me, CreatureDef definition, List<Fixed16Vector> scratch)
    {
        if (!IsBuilt(definition.FightStyle)) return false;

        WorldState world = context.World;
        GameDatabase database = context.Database;
        int sightSq = CombatRules.RangeSquared(definition.DistanceCanSee);
        int hearSq = CombatRules.RangeSquared(definition.DistanceCanHear);

        CreatureState? chosen = Scan(context, sight, me, definition, sightSq, hearSq, scratch);
        if (chosen is null) return false;

        // The winner must be walkable to; a creature with an attack spell may keep one it can only
        // see, and none here has one, so the route decides.
        int distance = CombatRules.DistanceSquared(me.PosX, me.PosY, chosen.PosX, chosen.PosY);
        if (!CanReach(context.Navigation, me, definition, chosen.PosX, chosen.PosY, int.MaxValue, scratch)) return false;

        // The last gate, on a third of the distance: within hearing, or within sight and seen.
        int third = distance / 3;
        if (third >= hearSq && (third >= sightSq || !LineOfSight.Clear(sight, me.PosX, me.PosY, chosen.PosX, chosen.PosY)))
            return false;

        return Engage(context, me, definition, chosen, database.CreatureById(chosen.CreatureTypeId));
    }

    /// <summary>The Blocker arm <c>0x0045B140</c>: every hostile creature in the game, scored by its
    /// discounted distance, lower wins; a candidate that beats the best so far must be attackable
    /// and then either seen within sight range or walkable to within hearing range.</summary>
    private static CreatureState? Scan(
        SimulationContext context, ISightBlocker sight, CreatureState me, CreatureDef definition,
        int sightSq, int hearSq, List<Fixed16Vector> scratch)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        int best = int.MaxValue;
        CreatureState? chosen = null;

        IReadOnlyList<CreatureState> creatures = world.Creatures;
        for (int i = 0; i < creatures.Count; i++)
        {
            CreatureState candidate = creatures[i];
            if (!CombatRules.IsHostile(world, me.OwnerPlayerId, candidate.OwnerPlayerId)) continue;

            CreatureDef theirs = database.CreatureById(candidate.CreatureTypeId);
            int d = CombatRules.DistanceSquared(me.PosX, me.PosY, candidate.PosX, candidate.PosY);
            if (CombatRules.OppositeAlignment(definition, theirs)) d = CombatRules.Discounted(d);

            int score = d;
            if (score >= best) continue;
            if (!CombatRules.IsAttackable(candidate)) continue;

            bool seen = d < sightSq && LineOfSight.Clear(sight, me.PosX, me.PosY, candidate.PosX, candidate.PosY);
            if (!seen && !(d < hearSq && CanReach(context.Navigation, me, definition, candidate.PosX, candidate.PosY, hearSq, scratch)))
                continue;

            best = score;
            chosen = candidate;
        }
        return chosen;
    }

    /// <summary><c>0x0045AFF0</c>: not a distance but a route. Asks the mesh for a path to the
    /// point and sums its legs with the octile length; true when the squared length is under
    /// <paramref name="rangeSq"/>. Standing on the point, or an empty route, is true; no route is
    /// false. Imps route with no door rule, everyone else respects doors.</summary>
    public static bool CanReach(
        WorldSteering navigation, CreatureState me, CreatureDef definition, int toX, int toY, int rangeSq, List<Fixed16Vector> scratch)
    {
        if (me.PosX == toX && me.PosY == toY) return true;

        int options = me.CreatureTypeId == CombatRules.ImpCreatureTypeId ? MoveOptions.Plain : MoveOptions.Loose;
        scratch.Clear();
        bool routed = navigation.Route(
            new Fixed16Vector(me.PosX << 4, me.PosY << 4),
            new Fixed16Vector(toX << 4, toY << 4),
            WorldSteering.ProfileOf(definition, me.OwnerPlayerId, options),
            scratch,
            refresh: false);
        if (!routed) return false;
        if (scratch.Count == 0) return true;

        int length = 0, px = me.PosX, py = me.PosY;
        for (int i = 0; i < scratch.Count; i++)
        {
            int qx = scratch[i].X >> 4, qy = scratch[i].Y >> 4;
            int dx = Math.Abs(qx - px), dy = Math.Abs(qy - py);
            length += dx + dy - (Math.Min(dx, dy) >> 1);
            px = qx;
            py = qy;
        }
        return unchecked((length >> 12) * length) < rangeSq;
    }

    /// <summary>Commit and start: the target and its attack type on me — melee, since the spell
    /// survey is not built — my id in the target's melee slots, the reach, and the state. A target
    /// with all eight melee slots taken refuses the engagement and leaves me as I was.</summary>
    private static bool Engage(SimulationContext context, CreatureState me, CreatureDef mine, CreatureState target, CreatureDef theirs)
    {
        if (!CombatRules.Register(target, me.Id, ranged: false)) return false;

        me.FightTargetId = target.Id;
        me.AttackType = CombatRules.AttackTypes.MeleeAtCreature;
        me.AttackRangeSquared = CombatRules.MeleeReachSquared(mine, theirs);
        // Ours: the state change drops the route; spFighting's own approach is the next piece.
        me.StopMoving();
        me.Activity = CreatureActivity.Fighting;
        context.Emit("combat", "engaged",
            $"creature={me.Id} type={mine.Name} player={me.OwnerPlayerId} target={target.Id} reach2={me.AttackRangeSquared}");
        return true;
    }
}
