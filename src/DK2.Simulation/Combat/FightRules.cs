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
/// the swing was started at, through <see cref="MeleeDamageRules"/>. The spell arms are here too
/// (combat-spells.md, ADR-0167): a cast is action 16, its shot leaving after the slot's wind-up.
/// Not built and named there: the celebration over a kill and the ally arm.</summary>
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
        me.SwingKind = SpellRules.MeleeSwing;
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
    /// the engagement has moved on; a creature no longer standing, or invulnerable, takes nothing; the
    /// health change is <see cref="MeleeDamageRules.Blow"/> with no stance, the bearing from target to
    /// attacker and no player modifier, through <see cref="StatusEffectRules.Hurt"/>'s multipliers.
    /// A heart takes it flat through <see cref="WorldState.DamageHeart"/>, as DK2's health routine
    /// hands a pillar's blow to the owner's heart (<c>0x004DA200</c>, Q363); DK2 also raises the
    /// "under attack" notice there, which the <c>heart_hit</c> event stands for.</summary>
    public static void LandBlow(SimulationContext context, CreatureState me, CreatureDef mine)
    {
        me.SwingInProgress = false;
        WorldState world = context.World;
        GameDatabase database = context.Database;

        if (me.SwingTargetKind == FightTargetKind.Creature)
        {
            CreatureState? target = world.CreatureById(me.SwingTargetId);
            if (target is null || CreatureDeathRules.IsDown(target) || StatusEffectRules.IsInvulnerable(target)) return;
            int blow = Blow(database, me, mine, target.PosX, target.PosY);
            if (blow == 0) return;
            int landed = StatusEffectRules.Hurt(world, database, database.CreatureById(target.CreatureTypeId), target, -blow);
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

    /// <summary>The spell arm's cast (combat-spells.md §6) — action 16 begun on the engagement's
    /// target from <paramref name="slot"/>: the slot is stamped with this turn at once, the target
    /// copied into the action, and a creature target told it is attacked. The shot leaves when
    /// <see cref="CastFires"/> says so. The ally arm passes <paramref name="tell"/> false: the ally is not told.</summary>
    public static void StartCast(SimulationContext context, CreatureState me, CreatureDef mine, int slot, bool tell = true)
    {
        me.SwingInProgress = true;
        me.SwingKind = SpellRules.SpellCast;
        me.SwingSpellSlot = (byte)slot;
        me.SwingStartTick = context.Tick.Value;
        me.SwingTargetKind = me.FightTargetKind;
        me.SwingTargetId = me.FightTargetId;
        me.SwingTargetPlayerId = me.FightTargetPlayerId;
        me.SpellStamps[slot] = context.Tick.Value;
        me.StopMoving();
        context.Emit(SystemName, "cast",
            $"creature={me.Id} type={mine.Name} slot={slot} spell={mine.Spells[slot].CreatureSpellId} target={me.SwingTargetId} heart={me.SwingTargetPlayerId}");

        if (tell && me.SwingTargetKind == FightTargetKind.Creature)
        {
            CreatureState? target = context.World.CreatureById(me.SwingTargetId);
            if (target != null) Retaliate(context, target, me);
        }
    }

    /// <summary><c>spFighting</c> step 8 (combat-fight-state.md §2): a Support lets its target go and
    /// selects afresh every tick; a choice replaces the engagement, and none restores the old one,
    /// registration included. False when the creature has left the fight state.</summary>
    public static bool Reacquire(
        SimulationContext context, ISightBlocker sight, HeartSites hearts, RoomCache rooms, CreatureState me, CreatureDef mine,
        List<Fixed16Vector> scratch)
    {
        WorldState world = context.World;
        byte kind = me.FightTargetKind, attack = me.AttackType, slot = me.AttackSpellSlot, player = me.FightTargetPlayerId;
        int id = me.FightTargetId, tile = me.FightTargetTileIndex, range = me.AttackRangeSquared;
        CreatureState? old = kind == FightTargetKind.Creature ? world.CreatureById(id) : null;
        bool ranged = attack is CombatRules.AttackTypes.SpellAtCreature or CombatRules.AttackTypes.SpellAtThing;
        if (old != null) CombatRules.Unregister(old, me.Id);
        me.FightTargetKind = FightTargetKind.None;
        me.FightTargetId = CombatRules.NoTarget;
        me.AttackType = 0;
        if (CombatTargetRules.SelectAVictim(context, sight, hearts, rooms, me, mine, scratch)) return CombatRules.IsFighting(me);
        if (!CombatRules.IsFighting(me)) return false;
        me.FightTargetKind = kind;
        me.FightTargetId = id;
        me.FightTargetPlayerId = player;
        me.FightTargetTileIndex = tile;
        me.AttackType = attack;
        me.AttackSpellSlot = slot;
        me.AttackRangeSquared = range;
        if (old != null && attack != CombatRules.AttackTypes.SpellAtAlly) CombatRules.Register(old, me.Id, ranged);
        return true;
    }

    /// <summary>The ally-spell arm (combat-spells.md §8, <c>0x00460011</c>): action 16 on the ally from the
    /// engagement's slot, the ally not told; then the target and the low nibble are cleared with the
    /// slot kept, so the caster casts once and looks again next tick.</summary>
    public static void CastOnAlly(SimulationContext context, CreatureState me, CreatureDef mine)
    {
        int slot = me.AttackSpellSlot;
        StartCast(context, me, mine, slot, tell: false);
        context.Emit(SystemName, "casts_on_ally", $"creature={me.Id} ally={me.FightTargetId} slot={slot}");
        me.FightTargetId = CombatRules.NoTarget;
        me.FightTargetKind = FightTargetKind.None;
        me.FightTargetPlayerId = 0;
        me.FightTargetTileIndex = -1;
        me.AttackType = 0;
        me.AttackRangeSquared = 0;
    }

    /// <summary>Action 16's wind-up (combat-spells.md §7 step 2): the shot leaves once the turn is
    /// past the start plus the slot's delay — strictly, so a zero delay fires the next tick.</summary>
    public static bool CastFires(CreatureState me, CreatureDef mine, long tick) =>
        me.SwingInProgress && me.SwingKind == SpellRules.SpellCast
        && me.SwingSpellSlot < mine.Spells.Count
        && tick > me.SwingStartTick + SpellRules.DelayTicks(mine, me.SwingSpellSlot);

    /// <summary>Action 16's shot (combat-spells.md §7 steps 3–7): from the caster's position — the
    /// slot's origin vector is not carried (Q398) — at the target's centre led by its last move
    /// times the whole tiles between them and a half, carrying the spell's two data words, launched
    /// at the shot definition's speed. A target gone or a heart fallen fires nothing.</summary>
    public static void FireSpell(SimulationContext context, CreatureState me, CreatureDef mine)
    {
        me.SwingInProgress = false;
        me.SwingKind = SpellRules.MeleeSwing;
        WorldState world = context.World;
        GameDatabase database = context.Database;
        CreatureSpellDef? spell = SpellRules.SpellOf(database, mine, me.SwingSpellSlot);
        if (spell is null) return;
        if (!database.Shots.TryGetValue(spell.ShotTypeId, out ShotDef? shotDef))
        {
            context.Emit(SystemName, "cast_failed", $"creature={me.Id} spell={spell.CreatureSpellId} reason=no_shot_type");
            return;
        }

        int aimX, aimY;
        int targetTile = -1;
        if (me.SwingTargetKind == FightTargetKind.Creature)
        {
            CreatureState? target = world.CreatureById(me.SwingTargetId);
            if (target is null || CreatureDeathRules.IsDown(target)) return;
            int distanceSquared = CombatRules.DistanceSquared(me.PosX, me.PosY, target.PosX, target.PosY);
            long tiles = ShotRules.Isqrt((long)distanceSquared << 4) >> 8;
            aimX = target.PosX + (int)((target.PosX - target.LastPosX) * tiles * 3 / 2);
            aimY = target.PosY + (int)((target.PosY - target.LastPosY) * tiles * 3 / 2);
        }
        else if (me.SwingTargetKind == FightTargetKind.Heart && me.FightTargetTileIndex >= 0)
        {
            ref PlayerState owner = ref world.Player(me.SwingTargetPlayerId);
            if (owner.HeartHealth <= 0 || owner.HeartRuined) return;
            targetTile = me.FightTargetTileIndex;
            aimX = CombatTargetRules.TileCentreX(world, targetTile);
            aimY = CombatTargetRules.TileCentreY(world, targetTile);
        }
        else
        {
            return;
        }

        ShotState shot = ShotRules.Spawn(world, shotDef, me.OwnerPlayerId, me.PosX, me.PosY, (int)spell.ShotData1, (int)spell.ShotData2,
            me.Id, me.SwingTargetKind, me.SwingTargetId, me.SwingTargetPlayerId, targetTile, aimX, aimY);
        ShotRules.Launch(shot, shotDef, aimX, aimY);
        context.Emit(SystemName, "shot_fired",
            $"creature={me.Id} spell={spell.CreatureSpellId} shot={shot.Id} type={shotDef.ShotId} aim={aimX},{aimY} vel={shot.VelX},{shot.VelY} data1={shot.Data1}");
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
        if (target.Activity == CreatureActivity.Fleeing) return;
        // A celebrating creature is not switched out of state 260 by a blow (combat-approach-and-victory.md §7).
        if (target.Activity is CreatureActivity.Celebrating or CreatureActivity.GoingToBody) return;
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
        CombatStates.Idle(target);
        if (context.Navigation.TryRoute(target, attacker.Tile, MoveOptions.Plain))
            context.Emit(SystemName, "turns_toward", $"creature={target.Id} attacker={attacker.Id}");
    }
}
