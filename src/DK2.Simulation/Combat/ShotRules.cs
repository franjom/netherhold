// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;
using DK2.Simulation.Navigation;

namespace DK2.Simulation.Combat;

/// <summary>What a shot does — docs/research/shots.md: it is placed and launched at its
/// definition's speed (§3), finds the first thing of its collide class within its radius but
/// never its caster (§4a), deals its data word flat on contact (§7), hurts a linear area each tick
/// when its damage type says so (§7), and chains hit and death shots that inherit its words (§4).
/// The status process types run on the first tick or the creature struck (§8, <see cref="Process"/>).
/// Height, the overlap test's exact shape, doors and objects as targets are not modelled (Q398).</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/shots.md")]
public static class ShotRules
{
    private const string SystemName = "shots";

    /// <summary>The definition's <c>Flags</c> bits the flight reads (§5).</summary>
    public const uint DiesOnSolid = 1u;
    public const uint DiesOnThing = 2u;
    public const uint CountsDown = 4u;
    public const uint Bounces = 8u;

    /// <summary><c>CollideType</c>: whose things the shot can hit (§4a).</summary>
    public const byte CollideNone = 0;
    public const byte CollideFriends = 1;
    public const byte CollideEnemies = 2;
    public const byte CollideBoth = 3;
    public const byte CollideBodies = 4;

    /// <summary><c>DamageType</c>: 7 on contact, 2 an area each tick (§7). The others do nothing here.</summary>
    public const byte ContactDamageType = 7;
    public const byte AreaDamageType = 2;

    /// <summary><c>ShotProcessFlags</c> events (§4): the handler runs when its bit is set.</summary>
    public const uint FirstTickEvent = 0x40u;
    public const uint PreMoveEvent = 0x2u;
    public const uint HitThingEvent = 0x4u;
    public const uint HitSolidEvent = 0x8u;
    public const uint DeathEvent = 0x80u;

    /// <summary><c>ProcessType</c>s with a creature side built (§8, creature-status-effects.md).</summary>
    public const byte ModifyHealthProcess = 3;
    /// <summary>Type 2, <c>0x004ADE10</c>: the shot's death puts an object of its first data word
    /// down where it died; the Call to Arms flag (10) is refused while the keeper's standing flag
    /// is in the hand. Its Heart-edge nudge (<c>0x00455000</c>) is unread and absent.</summary>
    public const byte CreateObjectProcess = 2;
    public const byte InstantProcess = 34;
    public const byte SpeedProcess = 4;
    public const byte ArmourProcess = 5;
    public const byte LightningProcess = 9;
    public const byte InvulnerableProcess = 10;
    public const byte FreezeProcess = 11;
    public const byte InvisibleProcess = 20;
    public const byte ReaperStunProcess = 29;
    /// <summary>Type 15, <c>0x004AE2A0</c>: the keeper's Chicken on the creature struck (<see cref="ChickenRules"/>).</summary>
    public const byte ChickenProcess = 15;
    /// <summary>Type 30, <c>0x004AF660</c>: the keeper's Inferno before each move (<see cref="InfernoRules"/>).</summary>
    public const byte InfernoProcess = 30;
    public const byte DrainProcess = 35;
    public const byte PoisonProcess = 38;

    /// <summary>The Disruption shot: its type-3 handler sets the target to five percent or kills it (§8).</summary>
    public const byte DisruptionShotId = 43;

    /// <summary>A new shot from a definition, its data words the caller's (a spell's two words, or the
    /// parent's for a chained shot), its lifetime the definition's health, not yet moving.</summary>
    public static ShotState Spawn(
        WorldState world, ShotDef definition, byte owner, int x, int y, int data1, int data2, int casterId,
        byte targetKind, int targetId, byte targetPlayerId, int targetTileIndex, int targetX, int targetY)
    {
        ShotState shot = world.SpawnShot(definition.ShotId, owner, x, y);
        shot.Health = definition.Health;
        shot.Data1 = data1;
        shot.Data2 = data2;
        shot.CasterId = casterId;
        shot.TargetKind = targetKind;
        shot.TargetId = targetId;
        shot.TargetPlayerId = targetPlayerId;
        shot.TargetTileIndex = targetTileIndex;
        shot.TargetX = targetX;
        shot.TargetY = targetY;
        return shot;
    }

    /// <summary>The launch (§3): the definition's speed along the line to the aim, position units a
    /// tick; a shot already at its aim does not move.</summary>
    public static void Launch(ShotState shot, ShotDef definition, int aimX, int aimY)
    {
        long dx = aimX - shot.PosX, dy = aimY - shot.PosY;
        long dist = Isqrt((dx * dx) + (dy * dy));
        if (dist == 0) return;
        shot.VelX = (int)(definition.Speed * dx / dist);
        shot.VelY = (int)(definition.Speed * dy / dist);
    }

    /// <summary>A hit or death shot (§4): made where the parent is, aimed at the parent's target
    /// position, inheriting its data words, caster and target, and not launched — the shipped
    /// explosions and clouds stand where they are made.</summary>
    public static ShotState? Chain(SimulationContext context, ShotState parent, byte childType)
    {
        if (childType == 0 || !context.Database.Shots.TryGetValue(childType, out ShotDef? definition)) return null;
        ShotState child = Spawn(context.World, definition, parent.OwnerPlayerId, parent.PosX, parent.PosY, parent.Data1, parent.Data2,
            parent.CasterId, parent.TargetKind, parent.TargetId, parent.TargetPlayerId, parent.TargetTileIndex, parent.TargetX, parent.TargetY);
        context.Emit(SystemName, "shot_chained", $"shot={parent.Id} child={child.Id} type={definition.ShotId} data1={child.Data1} data2={child.Data2}");
        return child;
    }

    /// <summary>The collision's answer (§4a): a forced collision hits the target word's creature if
    /// its tag is live, whatever the overlap, and spends the word; otherwise the overlap finder's,
    /// the nearest standing creature the collide class admits — an enemy by alliance, a friend by
    /// owner — that <see cref="Overlaps"/> the shot, never the caster unless it is the target.
    /// Doors, objects and bodies are not looked for.</summary>
    public static CreatureState? FindHit(WorldState world, GameDatabase database, ShotState shot, ShotDef definition)
    {
        if (shot.ForcedCollision)
        {
            // 0x004ABD60: the word is cleared either way, and a dead target, or one off the map
            // (layer +0x16 = 2: in a hand or carried), is a miss with no search.
            shot.ForcedCollision = false;
            CreatureState? forced = shot.TargetKind == FightTargetKind.Creature ? world.CreatureById(shot.TargetId) : null;
            return forced is null || forced.Activity == CreatureActivity.Dead || !CombatPitRules.IsOnTheMap(forced) ? null : forced;
        }
        byte mode = definition.CollideType;
        if (mode is CollideNone or CollideBodies) return null;
        CreatureState? best = null;
        int bestDistance = int.MaxValue;
        IReadOnlyList<CreatureState> creatures = world.Creatures;
        for (int i = 0; i < creatures.Count; i++)
        {
            CreatureState candidate = creatures[i];
            // Never the caster, unless caster and target are one (§4a): a self-cast Invisible lands.
            bool selfTarget = shot.TargetKind == FightTargetKind.Creature && shot.TargetId == shot.CasterId;
            if ((candidate.Id == shot.CasterId && !selfTarget) || CreatureDeathRules.IsDown(candidate)) continue;
            bool friend = candidate.OwnerPlayerId == shot.OwnerPlayerId;
            bool enemy = CombatRules.IsHostile(world, shot.OwnerPlayerId, candidate.OwnerPlayerId);
            if (mode == CollideEnemies && !enemy) continue;
            if (mode == CollideFriends && !friend) continue;
            if (mode == CollideBoth && !friend && !enemy) continue;
            if (!Overlaps(shot, definition, candidate, database.CreatureById(candidate.CreatureTypeId))) continue;
            int distance = CombatRules.DistanceSquared(shot.PosX, shot.PosY, candidate.PosX, candidate.PosY);
            if (distance >= bestDistance) continue;
            best = candidate;
            bestDistance = distance;
        }
        return best;
    }

    /// <summary>The overlap test (<c>0x004B5590</c>, §4a): a box, not a circle — on each axis the
    /// distance is under half the sum of the two radii, the shot's being twice its <c>Radius</c>
    /// (1638 for every creature shot) and the creature's its footprint. The height test is dropped
    /// with the rest of the flight's height (Q398). Sampled once a tick, so a fast shot can pass a
    /// standing creature between two positions, as DK2's does.</summary>
    public static bool Overlaps(ShotState shot, ShotDef definition, CreatureState creature, CreatureDef creatureDef)
    {
        int half = ((definition.Radius << 1) + creatureDef.FootprintRadius.Raw) >> 1;
        return Math.Abs(shot.PosX - creature.PosX) < half && Math.Abs(shot.PosY - creature.PosY) < half;
    }

    /// <summary>A shot aimed at a heart reaches it within its radius and an object's reach of the
    /// heart tile it was sent at (Q363's stand-in for the Arch Pillar it would strike).</summary>
    public static bool HitsHeart(WorldState world, ShotState shot, ShotDef definition)
    {
        if (shot.TargetKind != FightTargetKind.Heart || shot.TargetTileIndex < 0) return false;
        ref PlayerState owner = ref world.Player(shot.TargetPlayerId);
        if (owner.HeartHealth <= 0 || owner.HeartRuined) return false;
        int reach = CombatRules.ReachSquared(definition.Radius + CombatRules.ObjectReach);
        return CombatRules.DistanceSquared(shot.PosX, shot.PosY,
            CombatTargetRules.TileCentreX(world, shot.TargetTileIndex), CombatTargetRules.TileCentreY(world, shot.TargetTileIndex)) < reach;
    }

    /// <summary>Contact damage (§7, <c>0x004AC420</c>): the data word flat, refused while the target's
    /// invulnerable word runs, through the melee blow's health routine and its multipliers. The fire a
    /// fire-category hit lights is display in v1.70 (creature-status-effects.md §2).</summary>
    public static void ContactDamage(SimulationContext context, ShotState shot, ShotDef definition, CreatureState target)
    {
        if (definition.DamageType != ContactDamageType || shot.Data1 <= 0 || CreatureDeathRules.IsDown(target)) return;
        if (StatusEffectRules.IsInvulnerable(target)) return;
        GameDatabase database = context.Database;
        int landed = StatusEffectRules.Hurt(context.World, database, database.CreatureById(target.CreatureTypeId), target, shot.Data1);
        context.Emit(SystemName, "shot_hit", $"shot={shot.Id} type={definition.ShotId} caster={shot.CasterId} target={target.Id} damage={shot.Data1} landed={landed} health={target.Health}");
    }

    /// <summary>Contact damage on a heart: the data word flat, with the shooter's owner as the
    /// source — in DK2 the shot strikes an Arch Pillar and the health routine hands the delta to
    /// the owner's heart (<c>0x004DA200</c>, Q363).</summary>
    public static void ContactDamageHeart(SimulationContext context, ShotState shot, ShotDef definition)
    {
        if (definition.DamageType != ContactDamageType || shot.Data1 <= 0) return;
        WorldState world = context.World;
        if (world.Player(shot.TargetPlayerId).HeartHealth <= 0) return;
        bool killed = world.DamageHeart(shot.TargetPlayerId, shot.Data1, shot.OwnerPlayerId);
        CombatMessageRules.HeartDamaged(context, shot.TargetPlayerId);
        context.Emit(SystemName, "shot_heart_hit",
            $"shot={shot.Id} caster={shot.CasterId} heart={shot.TargetPlayerId} damage={shot.Data1} health={world.Player(shot.TargetPlayerId).HeartHealth}{(killed ? " destroyed" : "")}");
    }

    /// <summary>Area damage each tick (§7, <c>0x004AC6D0</c>): every standing creature not of the
    /// shot's owner within <c>data2</c> tiles takes <c>data1 × (R − d) / R</c>. A zero radius hurts
    /// nobody — the Firebomb's explosion inherits its spell's zero (CONFIRMED, §7).</summary>
    public static void AreaDamage(SimulationContext context, ShotState shot, ShotDef definition)
    {
        if (definition.DamageType != AreaDamageType || shot.Data1 <= 0 || shot.Data2 <= 0) return;
        WorldState world = context.World;
        GameDatabase database = context.Database;
        int radius = shot.Data2 * Fp32.One;
        int radiusSquared = (int)(((long)radius * radius) >> 12);
        IReadOnlyList<CreatureState> creatures = world.Creatures;
        for (int i = 0; i < creatures.Count; i++)
        {
            CreatureState target = creatures[i];
            if (target.OwnerPlayerId == shot.OwnerPlayerId || CreatureDeathRules.IsDown(target) || StatusEffectRules.IsInvulnerable(target)) continue;
            int distanceSquared = CombatRules.DistanceSquared(shot.PosX, shot.PosY, target.PosX, target.PosY);
            if (distanceSquared >= radiusSquared) continue;
            long distance = Isqrt((long)distanceSquared << 12);
            int damage = (int)(shot.Data1 * (radius - distance) / radius);
            if (damage <= 0) continue;
            int landed = StatusEffectRules.Hurt(world, database, database.CreatureById(target.CreatureTypeId), target, damage);
            context.Emit(SystemName, "shot_area_hit", $"shot={shot.Id} type={definition.ShotId} target={target.Id} damage={damage} landed={landed} health={target.Health}");
        }
    }

    /// <summary>The process handler (§8) for one event, when the definition's flags wire it: on the
    /// creature struck, else the shot's target creature (<c>+0x147</c>, which a hit overwrites). The
    /// data words are the spell's; seconds become ticks at the original rate. Types without a
    /// creature side built (28 guided, 34 instant, and the unread ones) do nothing (Q394, Q395).</summary>
    public static void Process(SimulationContext context, ShotState shot, ShotDef definition, uint eventBit, CreatureState? struck)
    {
        if ((definition.ShotProcessFlags & eventBit) == 0) return;
        WorldState world = context.World;
        if (definition.ProcessType == CreateObjectProcess)
        {
            CreateObject(context, shot);
            return;
        }
        if (definition.ProcessType == InfernoProcess)
        {
            InfernoRules.Burn(context, shot);
            return;
        }
        CreatureState? target = struck ?? (shot.TargetKind == FightTargetKind.Creature ? world.CreatureById(shot.TargetId) : null);
        if (target is null)
        {
            if (definition.ProcessType == InstantProcess) shot.Health = 0;
            return;
        }
        CreatureDef definitionOfTarget = context.Database.CreatureById(target.CreatureTypeId);
        int perSecond = SimulationTiming.OriginalTicksPerGameSecond;
        switch (definition.ProcessType)
        {
            case InstantProcess:
                // The shot is moved onto its target and the collision forced (§8, type 34): here it stands on
                // the target and the finder takes it this tick; no live target and the shot dies.
                if (struck is null && !CreatureDeathRules.IsDown(target))
                {
                    shot.PosX = target.PosX;
                    shot.PosY = target.PosY;
                    shot.ForcedCollision = true;
                }
                else if (struck is null) shot.Health = 0;
                break;
            case ModifyHealthProcess: ModifyHealth(context, shot, definition, target, definitionOfTarget); break;
            case SpeedProcess: StatusEffectRules.ModifySpeed(context, target, shot.Data1, shot.Data2); break;
            case ArmourProcess: target.ArmourTicks = shot.Data1; break;
            case LightningProcess: StatusEffectRules.Lightning(context, target, definitionOfTarget, shot.Data1, shot.CasterId); break;
            case InvulnerableProcess: target.InvulnerableTicks = shot.Data1 * perSecond; break;
            case FreezeProcess: StatusEffectRules.Freeze(context, target, definitionOfTarget, shot.Data1 * perSecond); break;
            case InvisibleProcess: StatusEffectRules.MakeInvisible(context, target, shot.Data1 * perSecond); break;
            case ReaperStunProcess: StatusEffectRules.Stun(context, target, definitionOfTarget); break;
            case DrainProcess: StatusEffectRules.Drain(context, target, definitionOfTarget, world.CreatureById(shot.CasterId), shot.Data1); break;
            case PoisonProcess: StatusEffectRules.Poison(context, target, shot.Data2, shot.Data1, shot.OwnerPlayerId); break;
            case ChickenProcess: ChickenRules.Strike(context, shot, target, definitionOfTarget); break;
        }
    }

    private static void CreateObject(SimulationContext context, ShotState shot)
    {
        WorldState world = context.World;
        var tile = new TilePos(shot.PosX >> 12, shot.PosY >> 12);
        if (!world.Contains(tile) || context.Database.ObjectById((byte)shot.Data1) is not { } definition) return;
        if (shot.Data1 == CallToArmsRules.ObjectTypeId && world.HasPlayer(shot.OwnerPlayerId))
        {
            ref PlayerState player = ref world.Player(shot.OwnerPlayerId);
            if (CallToArmsRules.IsLive(world, player)
                && player.Hand.Contains(new HandItem(HandItemKind.Object, player.CallToArms.ObjectId)))
            {
                context.Emit(SystemName, "shot_object_refused", $"shot={shot.Id} type={shot.Data1} reason=flag_in_hand");
                return;
            }
        }
        ObjectState made = world.SpawnObject(context.Database, (byte)shot.Data1, shot.OwnerPlayerId, tile.Y * world.Width + tile.X);
        made.ProcessState = definition.StartState;
        if (definition.IsGold) made.GoldHeld = shot.Data2;
        context.Emit(SystemName, "shot_object_created", $"shot={shot.Id} object={made.Id} type={shot.Data1} tile={tile.X},{tile.Y}");
    }

    /// <summary>Type 3, <c>0x004ADB30</c> (§8): nothing for <c>IsHorny</c>; a fire-category shot's ignition is
    /// display; the Disruption shot sets health to five percent of the level-scaled maximum, or
    /// kills under it; every other carrier heals the lesser of its data word and the missing
    /// health — through the health routine, which refuses <c>OnlyAttackableByHorny</c> and lets a
    /// target over its maximum lose the excess — after <c>SetState(1)</c>, which interrupts what the
    /// target was doing, its walk included, unless it is frozen, held, a Combat Pit fighter or
    /// attacking abnormal targets (rebellion.md §7.2).</summary>
    public static void ModifyHealth(SimulationContext context, ShotState shot, ShotDef definition, CreatureState target, CreatureDef targetDef)
    {
        if (targetDef.Flags.HasFlag(CreatureFlags.IsHorny) || CreatureDeathRules.IsDown(target)) return;
        WorldState world = context.World;
        GameDatabase database = context.Database;
        int max = CreatureHealthRules.MaxHealth(database, targetDef, target);
        if (definition.ShotId == DisruptionShotId)
        {
            int floor = max * 5 / 100;
            int landed = floor == 0 || target.Health < floor
                ? CreatureHealthRules.Apply(database, targetDef, target, -target.Health)
                : StatusEffectRules.Hurt(world, database, targetDef, target, target.Health - floor);
            context.Emit(SystemName, "disrupted", $"shot={shot.Id} target={target.Id} landed={landed} health={target.Health}");
            return;
        }
        int amount = Math.Min(shot.Data1, max - target.Health);
        if (amount > 0 && target.FrozenTicks == 0 && !target.InHand && !target.CombatPitFighter && !target.AttackAbnormalTargets
            && CreatureStateMachine.TrySetState(context.Hooks, target, CreatureStateMachine.Idle, context.Tick.Value))
            target.StopMoving(); // DK2 moves only a walking state (0x0048A23B), so the route goes with the state
        // 0x004B2CE0 refuses a creature whose kind is OnlyAttackableByHorny for a positive delta too.
        int healed = amount >= 0
            ? targetDef.Flags.HasFlag(CreatureFlags.OnlyAttackableByHorny) ? 0 : CreatureHealthRules.Apply(database, targetDef, target, amount)
            : StatusEffectRules.Hurt(world, database, targetDef, target, -amount);
        context.Emit(SystemName, "healed", $"shot={shot.Id} type={definition.ShotId} caster={shot.CasterId} target={target.Id} amount={amount} landed={healed} health={target.Health}");
    }

    /// <summary>A position the shot may occupy: on the map and not a solid tile.</summary>
    public static bool Passable(WorldState world, WorldSteering navigation, int x, int y)
    {
        int tx = x / Fp32.One, ty = y / Fp32.One;
        return x >= 0 && y >= 0 && world.Contains(tx, ty) && !NavBytes.IsSolid(navigation.NavByte(tx, ty));
    }

    public static long Isqrt(long value)
    {
        if (value <= 0) return 0;
        long root = (long)Math.Sqrt(value);
        while (root * root > value) root--;
        while ((root + 1) * (root + 1) <= value) root++;
        return root;
    }
}
