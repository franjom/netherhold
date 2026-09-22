// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Navigation;
using DK2.Simulation.Steering;

namespace DK2.Simulation.Combat;

/// <summary>What a fighting creature does each tick — DK2's <c>spFighting</c> (<c>0x0045F030</c>,
/// its own name) for a melee engagement, docs/research/combat-fight-state.md: keep the target
/// while it is valid, stand still while a swing winds up, swing when in reach and the recharge
/// allows, otherwise look again and walk toward the target. The blow is the swing action's
/// handler (<c>0x00498E30</c>): it lands <c>MeleeHitDelay</c> after the swing began on the target
/// the swing was started at, through <see cref="MeleeDamageRules"/>. Not built and named there:
/// fear and withdrawal, the celebration over a kill, the spell arms and the step-back.</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/combat-fight-state.md")]
public static class FightRules
{
    private const string SystemName = "combat";

    /// <summary>Whether the engagement still has something to hit: a live, attackable creature, or a
    /// heart that stands. A creature target is also refused while its owner fights from the pit.</summary>
    public static bool TargetIsValid(WorldState world, CreatureState me)
    {
        switch (me.FightTargetKind)
        {
            case FightTargetKind.Creature:
                CreatureState? target = world.CreatureById(me.FightTargetId);
                return target != null && CombatRules.IsAttackable(target);
            case FightTargetKind.Heart:
                ref PlayerState owner = ref world.Player(me.FightTargetPlayerId);
                return owner.HeartHealth > 0 && !owner.HeartRuined;
            default:
                return false;
        }
    }

    /// <summary>Where the target stands, in position units.</summary>
    public static bool TargetPosition(WorldState world, CreatureState me, out int x, out int y, out TilePos tile)
    {
        if (me.FightTargetKind == FightTargetKind.Creature)
        {
            CreatureState? target = world.CreatureById(me.FightTargetId);
            if (target != null)
            {
                x = target.PosX;
                y = target.PosY;
                tile = target.Tile;
                return true;
            }
        }
        else if (me.FightTargetKind == FightTargetKind.Heart && me.FightTargetTileIndex >= 0)
        {
            x = CombatTargetRules.TileCentreX(world, me.FightTargetTileIndex);
            y = CombatTargetRules.TileCentreY(world, me.FightTargetTileIndex);
            tile = new TilePos(me.FightTargetTileIndex % world.Width, me.FightTargetTileIndex / world.Width);
            return true;
        }
        x = y = 0;
        tile = default;
        return false;
    }

    /// <summary><c>0x0045E8F0</c>: within <c>AttackRangeSquared</c> and seen.</summary>
    public static bool InRange(ISightBlocker sight, CreatureState me, int targetX, int targetY) =>
        CombatRules.DistanceSquared(me.PosX, me.PosY, targetX, targetY) < me.AttackRangeSquared
        && LineOfSight.Clear(sight, me.PosX, me.PosY, targetX, targetY);

    /// <summary>The approach (<c>0x00471D10</c>): a route to the target's position with the plain
    /// option word, requested again only when the target has changed tile.</summary>
    public static void Approach(WorldSteering navigation, CreatureState me, int targetX, int targetY, TilePos targetTile)
    {
        if (me.IsRouted && me.Destination == targetTile) return;
        navigation.TryRouteTo(me, targetX, targetY, MoveOptions.Plain);
    }

    /// <summary>Begins the swing — action 17 — stamping <c>TimeLastUsedMelee</c> and copying the
    /// target into the action, then tells the target it is attacked (§5). A zero wind-up lands now.</summary>
    public static void StartSwing(SimulationContext context, ISightBlocker sight, CreatureState me, CreatureDef mine, List<Fixed16Vector> scratch)
    {
        me.SwingInProgress = true;
        me.SwingStartTick = context.Tick.Value;
        me.TimeLastUsedMelee = context.Tick.Value;
        me.SwingTargetKind = me.FightTargetKind;
        me.SwingTargetId = me.FightTargetId;
        me.SwingTargetPlayerId = me.FightTargetPlayerId;
        me.StopMoving();
        context.Emit(SystemName, "swing", $"creature={me.Id} type={mine.Name} target={me.SwingTargetId} heart={me.SwingTargetPlayerId}");

        if (me.SwingTargetKind == FightTargetKind.Creature)
        {
            CreatureState? target = context.World.CreatureById(me.SwingTargetId);
            if (target != null) Retaliate(context, target, me);
        }

        if (CombatRules.BlowLands(me, mine, context.Tick.Value)) LandBlow(context, me, mine);
    }

    /// <summary>The blow (<c>0x00498E30</c> past its gate): the swing's own target, whether or not
    /// the engagement has moved on; a creature no longer standing takes nothing; the health change
    /// is <see cref="MeleeDamageRules.Blow"/> with no stance, the bearing from target to attacker,
    /// and no player modifier. A heart takes it as <see cref="WorldState.DamageHeart"/> (Q363).</summary>
    public static void LandBlow(SimulationContext context, CreatureState me, CreatureDef mine)
    {
        me.SwingInProgress = false;
        WorldState world = context.World;
        GameDatabase database = context.Database;

        if (me.SwingTargetKind == FightTargetKind.Creature)
        {
            CreatureState? target = world.CreatureById(me.SwingTargetId);
            if (target is null || CreatureDeathRules.IsDown(target)) return;
            int blow = Blow(database, me, mine, target.PosX, target.PosY);
            if (blow == 0) return;
            int landed = CreatureHealthRules.Apply(database, database.CreatureById(target.CreatureTypeId), target, blow);
            context.Emit(SystemName, "hit",
                $"creature={me.Id} target={target.Id} blow={blow} landed={landed} health={target.Health}");
        }
        else if (me.SwingTargetKind == FightTargetKind.Heart)
        {
            ref PlayerState owner = ref world.Player(me.SwingTargetPlayerId);
            if (owner.HeartHealth <= 0 || owner.HeartRuined || me.FightTargetTileIndex < 0) return;
            int blow = Blow(database, me, mine,
                CombatTargetRules.TileCentreX(world, me.FightTargetTileIndex), CombatTargetRules.TileCentreY(world, me.FightTargetTileIndex));
            if (blow == 0) return;
            bool killed = world.DamageHeart(owner.PlayerId, -blow, me.OwnerPlayerId);
            context.Emit(SystemName, "heart_hit",
                $"creature={me.Id} heart={owner.PlayerId} blow={blow} health={owner.HeartHealth}{(killed ? " destroyed" : "")}");
        }
    }

    private static int Blow(GameDatabase database, CreatureState me, CreatureDef mine, int targetX, int targetY) =>
        MeleeDamageRules.Blow(database, mine, me.Level, MeleeDamageRules.Stance.None,
            Bearing.Of(me.PosX - targetX, me.PosY - targetY), 0, 0);

    /// <summary><c>0x0045AA20</c>, the target's side of a swing beginning. A creature already
    /// fighting turns on its attacker when it has no live target of its own or its target is
    /// already fought by more than one; one not fighting, if it may fight at all and is not an Imp,
    /// walks toward the attacker and acquires it in its own time. Spell survey not built: melee.</summary>
    public static void Retaliate(SimulationContext context, CreatureState target, CreatureState attacker)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        if (!CombatRules.MayLookForAFight(world, target, force: false)) return;

        if (CombatRules.IsFighting(target))
        {
            bool turn = true;
            if (target.FightTargetKind == FightTargetKind.Creature)
            {
                CreatureState? current = world.CreatureById(target.FightTargetId);
                if (current != null)
                {
                    if (current.Id == attacker.Id) return;
                    turn = CombatRules.AttackerCount(current) > 1;
                    if (turn) CombatRules.Unregister(current, target.Id);
                }
            }
            if (!turn) return;

            target.FightTargetKind = FightTargetKind.Creature;
            target.FightTargetId = attacker.Id;
            target.FightTargetPlayerId = 0;
            target.FightTargetTileIndex = -1;
            target.AttackType = CombatRules.AttackTypes.MeleeAtCreature;
            if (!CombatRules.Register(attacker, target.Id, ranged: false))
            {
                target.FightTargetKind = FightTargetKind.None;
                target.FightTargetId = CombatRules.NoTarget;
                return;
            }
            target.AttackRangeSquared = CombatRules.MeleeReachSquared(
                database.CreatureById(target.CreatureTypeId), database.CreatureById(attacker.CreatureTypeId));
            context.Emit(SystemName, "retaliates", $"creature={target.Id} target={attacker.Id}");
            return;
        }

        if (target.CreatureTypeId == CombatRules.ImpCreatureTypeId) return;
        target.CurrentJob = JobType.None;
        target.JobTileIndex = -1;
        target.Activity = CreatureActivity.Idle;
        if (context.Navigation.TryRoute(target, attacker.Tile, MoveOptions.Plain))
            context.Emit(SystemName, "turns_toward", $"creature={target.Id} attacker={attacker.Id}");
    }
}
