// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;
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

    /// <summary>Whether the engagement still has something to hit: a live, attackable creature, a
    /// heart that stands, or a door or trap with health. A Combat Pit fighter needs only a live target on the map, attackable or not,
    /// since its opponent, a fighter too, is never attackable (combat-pit.md §4).</summary>
    public static bool TargetIsValid(WorldState world, GameDatabase database, CreatureState me)
    {
        switch (me.FightTargetKind)
        {
            case FightTargetKind.Creature:
                CreatureState? target = world.CreatureById(me.FightTargetId);
                if (target is null) return false;
                if (me.CombatPitFighter) return CreatureStateMachine.HasState(target) && CombatPitRules.IsOnTheMap(target);
                return CombatRules.IsAttackable(target);
            case FightTargetKind.Heart:
                return HeartPillarRules.TryResolve(world, database, me.FightTargetPlayerId, new HeartPillarId(me.FightTargetId), out HeartPillarTarget pillar)
                    && me.FightTargetTileIndex == pillar.Tile.Y * world.Width + pillar.Tile.X;
            case FightTargetKind.Door:
                // spFighting step 5: a thing target must be live with health above 0.
                return world.DoorById(me.FightTargetId) is { Health: > 0 };
            case FightTargetKind.Trap:
                // The same step for a trap: one sold, broken or removed by the owner test is gone.
                return world.TrapById(me.FightTargetId) is { Health: > 0 };
            default:
                return false;
        }
    }

    /// <summary>Where the target stands, in position units.</summary>
    public static bool TargetPosition(WorldState world, GameDatabase database, CreatureState me, out int x, out int y, out TilePos tile)
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
        else if (me.FightTargetKind == FightTargetKind.Heart
            && HeartPillarRules.TryResolve(world, database, me.FightTargetPlayerId, new HeartPillarId(me.FightTargetId), out HeartPillarTarget pillar)
            && me.FightTargetTileIndex == pillar.Tile.Y * world.Width + pillar.Tile.X)
        {
            tile = pillar.Tile;
            x = pillar.X.Raw;
            y = pillar.Y.Raw;
            return true;
        }
        else if (me.FightTargetKind == FightTargetKind.Door && world.DoorById(me.FightTargetId) is { } door)
        {
            x = CreatureState.CentreOf(door.X);
            y = CreatureState.CentreOf(door.Y);
            tile = new TilePos(door.X, door.Y);
            return true;
        }
        else if (me.FightTargetKind == FightTargetKind.Trap && world.TrapById(me.FightTargetId) is { } trap)
        {
            x = trap.PosX;
            y = trap.PosY;
            tile = new TilePos(trap.X, trap.Y);
            return true;
        }
        x = y = 0;
        tile = default;
        return false;
    }

    /// <summary><c>0x0045E8F0</c>: strictly within <c>AttackRangeSquared</c> and seen. A door on the
    /// target's tile sets the walk's first flag, under which no door stops sight, only a solid tile
    /// (<c>0x004511A0</c>, door-blocking.md).</summary>
    public static bool InRange(ISightBlocker sight, CreatureState me, int targetX, int targetY)
    {
        if (CombatRules.DistanceSquared(me.PosX, me.PosY, targetX, targetY) >= me.AttackRangeSquared) return false;
        if (sight is WorldSight world && world.DoorOn(targetX / Fp32.One, targetY / Fp32.One)) sight = world.SolidOnly;
        return LineOfSight.Clear(sight, me.PosX, me.PosY, targetX, targetY);
    }

    /// <summary>The approach (<c>0x00471D10</c>): a route to the target's position with the plain
    /// option word, requested again only when the target has changed tile.</summary>
    public static void Approach(WorldSteering navigation, CreatureState me, int targetX, int targetY, TilePos targetTile)
    {
        if (me.IsRouted && me.Destination == targetTile) return;
        navigation.TryRouteTo(me, targetX, targetY, MoveOptions.Plain);
    }

    /// <summary>Mode-3 melee holds past impact until the animation completion poll
    /// (kmf-animation-playback.md §7). <see cref="AnimationTimingRules.ActionHoldTicks"/> counts the
    /// start turn and ends on the poll turn, so the action is free at start + hold, as the victory
    /// pause it also times ends (Q376); DK2's one-hero hit intervals on Level 4 are Dwarf 8 and Thief 10,
    /// their holds, where a release a turn earlier gave 7 and 9 (ADR-0598).</summary>
    public static bool MeleeActionIsRunning(CreatureState creature, CreatureDef definition, GameTick tick)
    {
        if (creature.SwingInProgress) return true;
        if (creature.SwingKind != SpellRules.MeleeSwing
            || creature.SwingTargetKind == FightTargetKind.None
            || creature.TimeLastUsedMelee != creature.SwingStartTick
            || creature.StateEnteredTick > creature.SwingStartTick) return false;
        long elapsed = tick.Value - creature.SwingStartTick;
        return elapsed >= 0 && elapsed < AnimationTimingRules.ActionHoldTicks(definition.ArtSlot(5));
    }


    /// <summary>Begins the swing — action 17 — stamping <c>TimeLastUsedMelee</c> and copying the
    /// target into the action, then tells the target it is attacked (§5). A zero wind-up lands now.</summary>
    public static void StartSwing(SimulationContext context, ISightBlocker sight, CreatureState me, CreatureDef mine, List<Fixed16Vector> scratch)
    {
        me.SwingInProgress = true;
        me.SwingKind = SpellRules.MeleeSwing;
        me.SwingStartTick = context.Tick.Value;
        me.AttackCreatureHits = unchecked((ushort)(me.AttackCreatureHits + 1));   // 0x0048F3E0
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
    /// health change is <see cref="MeleeDamageRules.Blow"/> with the attacker's stance, the bearing from target to
    /// attacker and no player modifier, through <see cref="StatusEffectRules.Hurt"/>'s multipliers.
    /// A heart takes it flat through <see cref="WorldState.DamageHeart"/>, as DK2's health routine
    /// hands a pillar's blow to the owner's heart (<c>0x004DA200</c>, Q363), with no bearing bonus:
    /// that is the creature block's alone, and a pillar is an object (class 2). DK2 also raises the
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
            // 0x0049926C (0x0049924D for the Reaper-type arm): the swing passes the attacker's owner (+0x28) as the source.
            int landed = StatusEffectRules.Hurt(world, database, database.CreatureById(target.CreatureTypeId), target, -blow, me.OwnerPlayerId);
            context.Emit(SystemName, "hit",
                $"creature={me.Id} target={target.Id} blow={blow} landed={landed} health={target.Health}");
        }
        else if (me.SwingTargetKind == FightTargetKind.Heart)
        {
            if (!HeartPillarRules.TryResolve(world, database, me.SwingTargetPlayerId, new HeartPillarId(me.SwingTargetId), out HeartPillarTarget pillar)) return;
            ref PlayerState owner = ref world.Player(me.SwingTargetPlayerId);
            // A health change, negative as Blow's is, with no bearing term.
            int blow = MeleeDamageRules.ApplyPlayerModifiers(
                -MeleeDamageRules.Swing(database, mine, me.Level, MeleeDamageRules.StanceOf(me)), 0, 0);
            if (blow == 0) return;
            bool killed = world.DamageHeart(owner.PlayerId, -blow, me.OwnerPlayerId);
            if (blow < 0) CombatMessageRules.HeartDamaged(context, owner.PlayerId);
            context.Emit(SystemName, "heart_hit",
                $"creature={me.Id} heart={owner.PlayerId} blow={blow} health={owner.HeartHealth}{(killed ? " destroyed" : "")}");
        }
        else if (me.SwingTargetKind == FightTargetKind.Door)
        {
            // The swing's door arm (0x00498E30): the attacker's melee damage with no bearing bonus,
            // which is the creature arm's alone, applied flat through the health routine. The two
            // player modifiers (+0x1090, +0x1091) apply in DK2; ours passes 0, as the creature arm does.
            DoorState? door = world.DoorById(me.SwingTargetId);
            if (door is null || door.Health <= 0) return;
            int blow = MeleeDamageRules.ApplyPlayerModifiers(
                MeleeDamageRules.Swing(database, mine, me.Level,
                    MeleeDamageRules.StanceOf(me)), 0, 0);
            if (blow == 0) return;
            DoorRules.Damage(context, door, -blow, me.OwnerPlayerId);
            context.Emit(SystemName, "door_hit", $"creature={me.Id} door={door.Id} blow={blow} health={door.Health}");
        }
        else if (me.SwingTargetKind == FightTargetKind.Trap)
        {
            // The swing's trap arm (0x0049916D): as the door's, the impact pair from the trap's
            // definition (+0xBF, presentation), then the blow flat through the health routine, which
            // adds it to a trap with no refusal. The trap goes when the world next removes the dead.
            TrapState? trap = world.TrapById(me.SwingTargetId);
            if (trap is null || trap.Health <= 0) return;
            int blow = MeleeDamageRules.ApplyPlayerModifiers(
                MeleeDamageRules.Swing(database, mine, me.Level,
                    MeleeDamageRules.StanceOf(me)), 0, 0);
            if (blow == 0) return;
            TrapRules.Damage(trap, -blow);
            context.Emit(SystemName, "trap_hit", $"creature={me.Id} trap={trap.Id} blow={blow} health={trap.Health}");
        }
        else if (me.SwingTargetKind is FightTargetKind.Shot or FightTargetKind.Body or FightTargetKind.Object)
            LandThingBlow(context, me, mine);
    }

    /// <summary>The possessed swing's other classes (<c>0x00498E30</c> → <c>Thing_ModifyHealth</c>
    /// <c>0x004B2CE0</c>, possession-arms-and-inputs.md §2): the door's blow, flat. A shot's
    /// <see cref="ShotState.Health"/> (its lifetime or hit count) and a body's rot window both shrink;
    /// an object without Damageable (def <c>+0xF4</c> bit 21) refuses it. What removes a shot taken to
    /// 0 is unread: ours, <see cref="Systems.ShotSystem"/> on its next pass. Not built: the Horned
    /// Reaper's state 0xEF over a target the blow killed (its idle AI is unbuilt).</summary>
    private static void LandThingBlow(SimulationContext context, CreatureState me, CreatureDef mine)
    {
        WorldState world = context.World;
        int blow = MeleeDamageRules.ApplyPlayerModifiers(
            MeleeDamageRules.Swing(context.Database, mine, me.Level, MeleeDamageRules.StanceOf(me)), 0, 0);
        switch (me.SwingTargetKind)
        {
            case FightTargetKind.Shot:
                if (world.ShotById(me.SwingTargetId) is not { } shot) return;
                shot.Health = unchecked(shot.Health - blow);
                context.Emit(SystemName, "shot_struck", $"creature={me.Id} shot={shot.Id} blow={blow} health={shot.Health}");
                break;
            case FightTargetKind.Body:
                // The body's health is its rot timer counting down; ours counts the window up.
                if (world.CreatureById(me.SwingTargetId) is not { } body || !CreatureDeathRules.IsCorpse(body)) return;
                body.DyingTicks = unchecked(body.DyingTicks + blow);
                context.Emit(SystemName, "body_struck", $"creature={me.Id} body={body.Id} blow={blow} lain={body.DyingTicks}");
                break;
            case FightTargetKind.Object:
                if (world.ObjectById(me.SwingTargetId) is not { } thing) return;
                bool damageable = context.Database.ObjectById(thing.ObjectTypeId) is { } def && def.Flags.HasFlag(ObjectFlags.Damageable);
                // Only a Book Case reaches here, and it is not Damageable; a Damageable one would take
                // the blow on its health, which no shipped object the pick allows needs.
                context.Emit(SystemName, damageable ? "object_struck_unbuilt" : "object_refuses_blow",
                    $"creature={me.Id} object={thing.Id} type={thing.ObjectTypeId} blow={blow}");
                break;
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
        me.AttackCreatureHits = unchecked((ushort)(me.AttackCreatureHits + 1));   // 0x0048F3E0
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
        FightTargetKind kind = me.FightTargetKind;
        CombatRules.AttackMode attack = me.AttackType;
        byte slot = me.AttackSpellSlot, player = me.FightTargetPlayerId;
        int id = me.FightTargetId, tile = me.FightTargetTileIndex, range = me.AttackRangeSquared;
        CreatureState? old = kind == FightTargetKind.Creature ? world.CreatureById(id) : null;
        bool ranged = attack is CombatRules.AttackMode.SpellAtCreature or CombatRules.AttackMode.SpellAtThing;
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
        if (old != null && attack != CombatRules.AttackMode.SpellAtAlly) CombatRules.Register(old, me.Id, ranged);
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
    /// past the start plus the slot's delay — strictly, so a zero delay fires the next tick.
    /// Possession bypasses the delay.</summary>
    public static bool CastFires(CreatureState me, CreatureDef mine, long tick) =>
        me.SwingInProgress && me.SwingKind is SpellRules.SpellCast or SpellRules.FreeAimCast
        && me.SwingSpellSlot < mine.Spells.Count
        && (me.Possessed || tick > me.SwingStartTick + SpellRules.DelayTicks(mine, me.SwingSpellSlot));

    /// <summary>Action 16's shot (combat-spells.md §7 steps 3–7): from the slot's origin turned by the
    /// caster's heading (<see cref="ShotOrigin"/>, ADR-0489) — at the target's centre led by its last move
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
        else if (me.SwingTargetKind == FightTargetKind.Heart)
        {
            if (!HeartPillarRules.TryResolve(world, database, me.SwingTargetPlayerId, new HeartPillarId(me.SwingTargetId), out HeartPillarTarget pillar)) return;
            targetTile = pillar.Tile.Y * world.Width + pillar.Tile.X;
            aimX = pillar.X.Raw;
            aimY = pillar.Y.Raw;
        }
        else if (me.SwingTargetKind == FightTargetKind.Door && world.DoorById(me.SwingTargetId) is { Health: > 0 } door)
        {
            targetTile = (door.Y * world.Width) + door.X;
            aimX = CreatureState.CentreOf(door.X);
            aimY = CreatureState.CentreOf(door.Y);
        }
        else if (me.SwingTargetKind == FightTargetKind.Trap && world.TrapById(me.SwingTargetId) is { Health: > 0 } trap)
        {
            // The shot strikes the trap through the enemies predicate's overlap (ShotRules.FindTrapHit).
            targetTile = (trap.Y * world.Width) + trap.X;
            aimX = trap.PosX;
            aimY = trap.PosY;
        }
        else
        {
            return;
        }

        (int originX, int originY) = ShotOrigin(me, mine.Spells[me.SwingSpellSlot]);
        ShotState shot = ShotRules.Spawn(world, shotDef, me.OwnerPlayerId, originX, originY, (int)spell.ShotData1, (int)spell.ShotData2,
            me.Id, me.SwingTargetKind, me.SwingTargetId, me.SwingTargetPlayerId, targetTile, aimX, aimY);
        ShotRules.Launch(shot, shotDef, aimX, aimY);
        ShotRules.RequestTrail(context, shot, new Dk2Angle((ushort)me.Heading),
            new Fp32(Fp32.One + me.PosZ + mine.Spells[me.SwingSpellSlot].OriginZ));
        context.Emit(SystemName, "shot_fired",
            $"creature={me.Id} spell={spell.CreatureSpellId} shot={shot.Id} type={shotDef.ShotId} aim={aimX},{aimY} vel={shot.VelX},{shot.VelY} data1={shot.Data1}");
    }

    /// <summary>Where action 16 makes the shot (<c>0x00498989</c>–<c>0x004989F5</c>, combat-spells.md §7
    /// step 4): my position plus the slot's origin turned by my heading through the sine tables
    /// <c>0x006C1488</c>/<c>0x006C1C88</c>, each product shifted down sixteen on its own. A possessed
    /// creature adds only the height, which our flat flight drops (Q398).</summary>
    public static (int X, int Y) ShotOrigin(CreatureState me, CreatureSpellSlot slot)
    {
        if (me.Possessed) return (me.PosX, me.PosY);
        long sin = TrapRules.Sine(me.Heading), cos = TrapRules.Cosine(me.Heading);
        int dx = (int)((slot.OriginX * cos) >> 16) - (int)((slot.OriginY * sin) >> 16);
        int dy = (int)((slot.OriginX * sin) >> 16) + (int)((slot.OriginY * cos) >> 16);
        return (me.PosX + dx, me.PosY + dy);
    }

    /// <summary><c>CreatureAction_Begin(17 or 16, …)</c> as action 26 calls it (<c>0x00496700</c>):
    /// the action replaces the current one and records its target and start turn. No stamp, no
    /// stop and no answer from the target: those are <c>spFighting</c>'s, and action 26 stamps itself.
    /// The begin's refusal needs an authored zero interruption byte, which shipped data has not.</summary>
    public static void BeginPossessedAction(
        SimulationContext context, CreatureState me, byte kind, int slot, FightTargetKind targetKind, int targetId, byte targetPlayer = 0)
    {
        me.SwingInProgress = true;
        me.SwingKind = kind;
        me.SwingSpellSlot = (byte)slot;
        me.SwingStartTick = context.Tick.Value;
        me.SwingTargetKind = targetKind;
        me.SwingTargetId = targetId;
        me.SwingTargetPlayerId = targetPlayer;
        me.ClaimActionTile = -1;
        context.Emit(SystemName, kind == SpellRules.MeleeSwing ? "possessed_swing" : "possessed_cast",
            $"creature={me.Id} slot={slot} kind={(byte)targetKind} target={targetId}");
    }

    /// <summary><c>0x00496980(15, point, slot, pitch, range)</c>: the free-aim cast at a point.</summary>
    public static void BeginFreeAim(
        SimulationContext context, CreatureState me, int slot, int x, int y, int z, int pitch, int range)
    {
        BeginPossessedAction(context, me, SpellRules.FreeAimCast, slot, FightTargetKind.None, CombatRules.NoTarget);
        me.SwingPointX = x;
        me.SwingPointY = y;
        me.SwingPointZ = z;
        me.SwingPitch = pitch;
        me.SwingRange = range;
    }

    /// <summary>Action 15's shot (<c>0x004984D0</c>): the spell's shot from the caster's own position,
    /// with no horizontal muzzle offset when possessed, the spell's two data words, launched at the
    /// action's point; in SniperMode the shot carries <c>+0x165</c> (<see cref="ShotState.Sniper"/>).
    /// Height, pitch, the range word (<c>+0x155</c>) and the possessed flag (<c>+0x151</c>) are not
    /// represented: shots fly flat (Q398).</summary>
    public static void FireFreeAim(SimulationContext context, CreatureState me, CreatureDef mine)
    {
        me.SwingInProgress = false;
        me.SwingKind = SpellRules.MeleeSwing;
        CreatureSpellDef? spell = SpellRules.SpellOf(context.Database, mine, me.SwingSpellSlot);
        if (spell is null || !context.Database.Shots.TryGetValue(spell.ShotTypeId, out ShotDef? shotDef)) return;
        ShotState shot = ShotRules.Spawn(context.World, shotDef, me.OwnerPlayerId, me.PosX, me.PosY, (int)spell.ShotData1, (int)spell.ShotData2,
            me.Id, FightTargetKind.None, CombatRules.NoTarget, 0, -1, me.SwingPointX, me.SwingPointY);
        shot.Sniper = me.Possessed && me.SniperMode;
        ShotRules.Launch(shot, shotDef, me.SwingPointX, me.SwingPointY);
        ShotRules.RequestTrail(context, shot, new Dk2Angle((ushort)me.Heading), new Fp32(Fp32.One + me.PosZ));
        context.Emit(SystemName, "shot_fired",
            $"creature={me.Id} spell={spell.CreatureSpellId} shot={shot.Id} type={shotDef.ShotId} aim={me.SwingPointX},{me.SwingPointY} vel={shot.VelX},{shot.VelY} data1={shot.Data1}");
    }

    private static int Blow(GameDatabase database, CreatureState me, CreatureDef mine, int targetX, int targetY) =>
        MeleeDamageRules.Blow(database, mine, me.Level,
            MeleeDamageRules.StanceOf(me),
            Bearing.Of(me.PosX - targetX, me.PosY - targetY), 0, 0);

    /// <summary><c>0x0045AA20</c>, the target's side of a swing beginning, for an attacker that is not a
    /// trap. A creature already fighting turns on its attacker when it has no live target of its own
    /// or its target is already fought by more than one. One not fighting does nothing: the arm that
    /// walks it toward its attacker runs only for a trap (<c>0x0045AD68</c> compares the attacker's
    /// class with 3; <see cref="AnswerTrap"/>). Spell survey not built: melee.</summary>
    public static void Retaliate(SimulationContext context, CreatureState target, CreatureState attacker)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        if (target.Activity == CreatureActivity.Fleeing) return;
        // A celebrating creature is not switched out of state 260 by a blow (combat-approach-and-victory.md §7).
        if (target.Activity is CreatureActivity.Celebrating or CreatureActivity.GoingToBody) return;
        if (!CombatRules.MayLookForAFight(world, database, target, force: false)) return;

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
            target.AttackType = CombatRules.AttackMode.MeleeAtCreature;
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

    }

    /// <summary><c>0x0045AA20</c>'s second arm (<c>0x0045AD31</c>), for a trap firing at the creature:
    /// one outside the combat category that may fight (<c>0x0045A6D0(0)</c>), is not an Imp and has a
    /// route to the trap (<c>0x004D0E60</c>, no door rule) goes to state 1 and walks to the trap's
    /// position (<c>0x00471890</c>). One in the combat category does nothing.</summary>
    public static void AnswerTrap(SimulationContext context, CreatureState target, TrapState trap)
    {
        if (NeedRules.ResolvedCategory(target) == CreatureStateCategory.Combat) return;
        if (!CombatRules.MayLookForAFight(context.World, context.Database, target, force: false)) return;
        if (target.CreatureTypeId == CombatRules.ImpCreatureTypeId) return;
        WorldSteering navigation = context.Navigation;
        MoverProfile profile = WorldSteering.ProfileOf(context.Database.CreatureById(target.CreatureTypeId), target.OwnerPlayerId, MoveOptions.Plain);
        RouteScratch.Clear();
        if (!navigation.Route(new Fixed16Vector(target.PosX << 4, target.PosY << 4), new Fixed16Vector(trap.PosX << 4, trap.PosY << 4), profile, RouteScratch))
            return;
        target.CurrentJob = JobType.None;
        target.JobTileIndex = -1;
        CombatStates.Idle(context.Hooks, target);
        navigation.TryRouteTo(target, trap.PosX, trap.PosY, MoveOptions.Plain);
        context.Emit(SystemName, "walks_to_trap", $"creature={target.Id} trap={trap.Id}");
    }

    // Per thread, not per process: the original is single-threaded, the test host is not.
    [ThreadStatic] private static List<Fixed16Vector>? _routeScratch;

    private static List<Fixed16Vector> RouteScratch => _routeScratch ??= [];
}
