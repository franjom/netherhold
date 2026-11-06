// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Combat;

/// <summary>What a creature's threat came from: DK2's <c>ThreatThing</c> (<c>+0x533</c>), a thing tag. Ours
/// names the kinds that record one: a trap and an object (the Boulder) through
/// <see cref="TrapRules.ShareThreat(WorldState, ISightBlocker, PlayerId, int, int, int, int, ThreatSource)"/>, and
/// a creature through the fear accumulator (<c>0x0045E4F0</c>'s inlined record, <see cref="FearRules.Radiate"/>).</summary>
public readonly record struct ThreatSource(byte Kind, int Id)
{
    public const byte None = 0, Trap = 1, Object = 2, Creature = 3;

    public static ThreatSource OfTrap(TrapState trap) => new(Trap, trap.Id);

    public static ThreatSource OfObject(ObjectState thing) => new(Object, thing.Id);

    public static ThreatSource OfCreature(CreatureState creature) => new(Creature, creature.Id);
}

/// <summary><c>Creature_Update</c>'s flight from its threat (<c>0x00489AD3</c>-<c>0x00489E27</c>, ADR-0678),
/// after the fear accumulator (<c>0x00489ACE</c>) and before the state dispatcher.</summary>
/// <remarks>For a creature not possessed, not dying, not <c>IsFearless</c> and past its <c>FearlessUntil</c>,
/// when <see cref="CreatureState.CurrentFear"/> is over the level-scaled threshold, the state is
/// set, the creature attackable (<c>+0x3EA</c> bit 0x40) and not already on a walk to 221, it fires creature
/// event 12 (<c>0x0049AA30</c>). Outside the Combat category, with a live <c>ThreatThing</c>, it clears a
/// creature, or a thing 25 tiles squared (five tiles) away or more; nearer, it takes the tile
/// offsets to it, <c>random(2)</c> for each zero one (Creature.cpp 2098, 2100), and up to five tries of
/// <c>random(|4dx|)</c>, <c>random(|4dy|)</c> whole tiles away (2105, 2106) for a point the world holds
/// (<c>0x00509050</c>) with a clear line to it (<c>0x004512F0</c>). With one it sets state 1 and walks
/// there resuming 221 (word 0x80 for an Imp, else 0), whose handler (<c>0x00484940</c>) returns 0. Either
/// way the thing is cleared. Its other source, a timed effect (<c>+0x463</c>), is not built.</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/combat-fear.md")]
public static class ThreatFleeRules
{
    /// <summary>State 221, the end of the flight.</summary>
    public const int FledState = 221;

    /// <summary>The flight's reach, in the fixed-point squared distance (0x19000, 25 tiles squared).</summary>
    private const long Reach = 0x19000;

    /// <summary><c>0x0045C5D0</c>'s record: a positive threat names its source, the threshold
    /// (<c>+0x535</c>) staying 0.</summary>
    public static void Record(CreatureState creature, int amount, ThreatSource source)
    {
        if (amount <= 0 || source.Kind == ThreatSource.None) return;
        creature.ThreatThingKind = source.Kind;
        creature.ThreatThingId = source.Id;
    }

    /// <summary>A walk to 221, or 221 itself.</summary>
    public static bool IsFleeing(CreatureState creature) =>
        creature.Dk2StateId == FledState || (StateShadow.IsTravelling(creature) && creature.UtilityNewState == FledState);

    /// <summary>The arm, run in the creature's update after the fear accumulator.</summary>
    public static void Turn(SimulationContext context, ISightBlocker sight, CreatureState me, CreatureDef definition)
    {
        // 0x00489AD3: possessed (+0x3E6 bit 0x80), dying (0x200000), IsFearless, or before FearlessUntil.
        if (CreatureDeathRules.IsPossessed(context.World, me) || CreatureDeathRules.IsDying(me)
            || ((definition.Flags & CreatureFlags.IsFearless) == CreatureFlags.IsFearless) || CasinoRules.IsFearlessFromGambling(context.World, me)) return;
        if (me.CurrentFear <= FearRules.FearThreshold(context.Database, definition, me.Level)) return;
        if (!CreatureStateMachine.HasState(me) || !CombatRules.IsAttackable(me)) return;
        if (me.Dk2StateId == StateShadow.TravelToPoint && me.UtilityNewState == FledState) return;
        context.FireCreatureEvent(me, CreatureTriggerEvent.Afraid);
        if (NeedRules.ResolvedCategory(me) == CreatureStateCategory.Combat) return;
        if (Source(context.World, me) is not (bool creature, int tx, int ty)) return;   // gone: kept

        long dx = Math.Abs((long)me.PosX - tx), dy = Math.Abs((long)me.PosY - ty);
        if (!creature && ((dx * dx) >> 12) + ((dy * dy) >> 12) < Reach
            && FearRules.AwayFrom(context.World, sight, context.Rng, me, new TilePos(tx >> 12, ty >> 12)) is (int x, int y)
            && CreatureStateMachine.TrySetState(context.Hooks, me, CreatureStateMachine.Idle))
        {
            var options = me.CreatureTypeId == CombatRules.ImpCreatureTypeId ? Navigation.MoveOptions.Loose : Navigation.MoveOptions.Plain;
            if (context.Navigation.TryRouteTo(me, x, y, options))
                CreatureStateMachine.BeginTravel(context.Hooks, me, StateShadow.TravelToPoint, FledState);
        }
        me.ThreatThingKind = ThreatSource.None;
        me.ThreatThingId = 0;
    }

    /// <summary>The walk to 221 and 221's handler, in the dispatcher's place: the walk ends in 221, and
    /// 221 returns 0, so the dispatcher idles the creature. True when the creature was in either.</summary>
    public static bool Dispatch(SimulationContext context, string system, CreatureState me)
    {
        if (StateShadow.IsTravelling(me))
        {
            if (me.UtilityNewState != FledState) return false;
            if (me.HasRoute) return true;
            me.StopMoving();
            StateShadow.Arrive(context, system, me);
            return true;
        }
        if (me.Dk2StateId != FledState) return false;
        StateShadow.ToIdle(context, system, me);
        return true;
    }

    /// <summary>The live thing (<c>0x006ED540</c>): whether it is a creature (class 0, cleared with no flight)
    /// and its position; null with none or a dead one, which is kept.</summary>
    private static (bool Creature, int X, int Y)? Source(WorldState world, CreatureState me) => me.ThreatThingKind switch
    {
        ThreatSource.Trap when world.TrapById(me.ThreatThingId) is TrapState trap => (false, trap.PosX, trap.PosY),
        ThreatSource.Object when world.ObjectById(me.ThreatThingId) is ObjectState thing => (false, thing.PosX, thing.PosY),
        ThreatSource.Creature when world.CreatureById(me.ThreatThingId) is CreatureState c && !CreatureDeathRules.IsCorpse(c)
            => (true, c.PosX, c.PosY),
        _ => null,
    };
}
