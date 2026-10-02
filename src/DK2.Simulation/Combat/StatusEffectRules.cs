// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Combat;

/// <summary>What a status shot leaves on the creature it hits — docs/research/creature-status-effects.md:
/// tick countdowns on the creature (§1), the setters the shot handlers call (§2), and the health
/// routine's multipliers in 128ths that every blow and shot pass through (§3). Fire is cosmetic in
/// v1.70 and has no word here; the per-player damage byte, the pit fighter, the Stone Knight's blow
/// path and the stun's early end are not modelled (Q399).</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/creature-status-effects.md")]
public static class StatusEffectRules
{
    private const string SystemName = "status";

    /// <summary>The health routine's multiplier, in 128ths (§3).</summary>
    public const int Base = 128;
    public const int FrozenBonus = 32;
    public const int ArmourDiscount = 64;
    public const int StunnedBonus = 128;

    /// <summary>The second flag word's bit 0 — King Reginald, Stone Knight and Jack: the Lightning
    /// handler skips them (its only reader, <c>0x004AF1E0</c>).</summary>
    public const CreatureFlags2 LightningImmune = CreatureFlags2.Unk0;

    public static bool IsFrozen(CreatureState creature) => creature.FrozenTicks > 0;
    public static bool IsInvulnerable(CreatureState creature) => creature.InvulnerableTicks > 0;
    public static bool IsInvisible(CreatureState creature) => creature.InvisibleTicks > 0;
    public static bool IsArmoured(CreatureState creature) => creature.ArmourTicks > 0;
    public static bool IsStunned(CreatureState creature) => creature.Dk2StateId is 209 or 255 or 256 or 257;

    /// <summary>Standing in the frozen or the stunned state: no fight, no job, no move.</summary>
    public static bool IsIncapacitated(CreatureState creature) =>
        creature.Activity is CreatureActivity.Frozen or CreatureActivity.Stunned;

    /// <summary>The multiplier a negative health change is scaled by (§3): 128, plus 32 frozen,
    /// minus 64 armoured or possessed, plus 128 stunned, then for a Combat Pit fighter less 128 and
    /// plus <c>PitPercentageDamageTakenOfNormalCombat</c> (20), floored at zero.</summary>
    public static int DamageMultiplier(WorldState world, GameDatabase database, CreatureState target)
    {
        int m = Base;
        if (IsFrozen(target)) m += FrozenBonus;
        if (IsArmoured(target) || CreatureDeathRules.IsPossessed(world, target)) m -= ArmourDiscount;
        if (IsStunned(target)) m += StunnedBonus;
        if (target.CombatPitFighter) m += database[VariableTypeId.PitPercentageDamageTakenOfNormalCombat] - Base;
        return Math.Max(0, m);
    }

    /// <summary>The health routine's creature branch for damage (<c>0x004B2CE0</c>, §3): nothing for
    /// an <c>IsHorny</c> or <c>OnlyAttackableByHorny</c> definition or a creature in the hand; else
    /// <c>damage × m / 128</c> toward zero through <see cref="CreatureHealthRules.Apply"/>. Returns
    /// what landed, as a negative number. The invulnerable word is the caller's refusal, as in DK2.</summary>
    public static int Hurt(WorldState world, GameDatabase database, CreatureDef definition, CreatureState target, int damage)
    {
        if (damage <= 0) return 0;
        if (definition.Flags.HasFlag(CreatureFlags.IsHorny) || definition.Flags.HasFlag(CreatureFlags.OnlyAttackableByHorny)) return 0;
        if (HandRules.IsHeld(target)) return 0;
        int scaled = (int)((long)damage * DamageMultiplier(world, database, target) / Base);
        return CreatureHealthRules.Apply(database, definition, target, -scaled);
    }

    /// <summary>Haste and Slow (<c>0x0048CAD0</c>, §2): refused while a speed effect runs; else the
    /// body's speed is <paramref name="percent"/> of itself for <paramref name="seconds"/>. Stored as
    /// the percent and applied by the movement cap each tick (<see cref="SpeedCap"/>).</summary>
    public static bool ModifySpeed(SimulationContext context, CreatureState creature, int percent, int seconds)
    {
        if (creature.SpeedEffectTicks > 0) return false;
        creature.SpeedEffectTicks = seconds * SimulationTiming.OriginalTicksPerGameSecond;
        creature.SpeedPercent = percent;
        context.Emit(SystemName, "speed_changed", $"creature={creature.Id} percent={percent} ticks={creature.SpeedEffectTicks}");
        return true;
    }

    /// <summary>The speed effect on the movement cap (§2): the cap × percent / 100, no less than a
    /// quarter of the level-scaled walk and no more than ticks-a-second tiles a tick. The cap is
    /// in 16.16 tiles a tick, as <see cref="Systems.CreatureMovementSystem.MaxSpeedOf(GameDatabase, CreatureDef, int, bool)"/> gives it.</summary>
    public static int SpeedCap(GameDatabase database, CreatureDef definition, CreatureState creature, int cap)
    {
        if (creature.SpeedEffectTicks <= 0) return cap;
        int perTick = cap >> 4;
        int walk = SimulationTiming.LoaderRatePerTick(CreatureLevelRules.SpeedRaw(database, definition, creature.Level));
        int modified = Math.Clamp(perTick * creature.SpeedPercent / 100, walk / 4, SimulationTiming.OriginalTicksPerGameSecond << 12);
        return modified << 4;
    }

    /// <summary>Freeze (<c>0x0048CB60</c>, §2): refused for <c>IsHorny</c> and while frozen; the
    /// current action ends and the creature stands in the frozen state while the word runs.</summary>
    public static bool Freeze(SimulationContext context, CreatureState creature, CreatureDef definition, int ticks)
    {
        if (ticks <= 0 || IsFrozen(creature) || definition.Flags.HasFlag(CreatureFlags.IsHorny)) return false;
        if (CreatureDeathRules.IsDown(creature)) return false;
        creature.FrozenTicks = ticks;
        Stand(context, creature, CombatStates.Frozen);
        context.Emit(SystemName, "frozen", $"creature={creature.Id} ticks={ticks}");
        return true;
    }

    /// <summary>The stun (<c>0x00491830(creature, 255)</c>, §2): refused for a creature in the hand,
    /// an <c>IsHorny</c> definition, the possessed creature and one already down; else it stands in
    /// state 255 for <c>StunDuration</c> and gets up idle (the getting-up animation's length is
    /// Q376). A second stun restarts the clock.</summary>
    public static bool Stun(SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        WorldState world = context.World;
        if (HandRules.IsHeld(creature) || definition.Flags.HasFlag(CreatureFlags.IsHorny)) return false;
        if (CreatureDeathRules.IsPossessed(world, creature) || CreatureDeathRules.IsDown(creature)) return false;
        // 0x00491840: a refused SetState(255) is no stun at all, so a frozen creature is not stunned.
        if (CreatureStateMachine.Classify(creature, CombatStates.Stunned) == StateTransition.Refused) return false;
        creature.StunTicks = CombatRules.TicksOf(definition.StunDuration);
        Stand(context, creature, CombatStates.Stunned);
        // 0x00491862, after SetState(255): the stunned creature's tree sees event 15.
        context.FireCreatureEvent(creature, CreatureTriggerEvent.Stunned);
        context.Emit(SystemName, "stunned", $"creature={creature.Id} ticks={creature.StunTicks}");
        return true;
    }

    /// <summary><c>0x00491830(255)</c> as the landing calls it (ADR-0226): <c>SetState(255)</c>,
    /// action 52 and event 15, with none of <see cref="Stun"/>'s callers' refusals. A refused state
    /// is no stun.</summary>
    public static bool StunOnLanding(SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        if (CreatureStateMachine.Classify(creature, CombatStates.Stunned) == StateTransition.Refused) return false;
        creature.StunTicks = CombatRules.TicksOf(definition.StunDuration);
        Stand(context, creature, CombatStates.Stunned);
        context.FireCreatureEvent(creature, CreatureTriggerEvent.Stunned);
        context.Emit(SystemName, "stunned", $"creature={creature.Id} ticks={creature.StunTicks} by=landing");
        return true;
    }

    /// <summary>Invisible (§2): the word set, then every attacker on my slots that is fighting me
    /// loses its target, my slots are emptied, and <see cref="CombatRules.IsAttackable"/> refuses me
    /// while the word runs.</summary>
    public static void MakeInvisible(SimulationContext context, CreatureState creature, int ticks)
    {
        if (ticks <= 0) return;
        creature.InvisibleTicks = ticks;
        DropAttackers(context.World, creature);
        context.Emit(SystemName, "invisible", $"creature={creature.Id} ticks={ticks}");
    }

    /// <summary><c>0x0045EE60</c>: the attackers registered on me whose target is me let go — they
    /// stay in their fight state with no target, and look again on their next tick.</summary>
    public static void DropAttackers(WorldState world, CreatureState me)
    {
        for (int i = 0; i < CombatRules.Slots; i++)
        {
            Drop(world, me, me.EngagedMelee[i]);
            Drop(world, me, me.EngagedRanged[i]);
            me.EngagedMelee[i] = CombatRules.NoTarget;
            me.EngagedRanged[i] = CombatRules.NoTarget;
        }
    }

    private static void Drop(WorldState world, CreatureState me, int attackerId)
    {
        if (attackerId == CombatRules.NoTarget) return;
        CreatureState? attacker = world.CreatureById(attackerId);
        if (attacker is null || attacker.FightTargetKind != FightTargetKind.Creature || attacker.FightTargetId != me.Id) return;
        attacker.FightTargetId = CombatRules.NoTarget;
        attacker.FightTargetKind = FightTargetKind.None;
    }

    /// <summary>Poison Spit (<c>0x0048CC50</c>, §2): <paramref name="amount"/> a tick for
    /// <paramref name="ticks"/> ticks while not frozen, credited to <paramref name="owner"/>. Refused
    /// on a creature already down.</summary>
    public static bool Poison(SimulationContext context, CreatureState creature, int ticks, int amount, byte owner)
    {
        if (ticks <= 0 || amount <= 0 || CreatureDeathRules.IsDown(creature)) return false;
        creature.PoisonTicks = ticks;
        creature.PoisonAmount = amount;
        creature.PoisonOwner = owner;
        context.Emit(SystemName, "poisoned", $"creature={creature.Id} amount={amount} ticks={ticks} owner={owner}");
        return true;
    }

    /// <summary>Drain (shots.md §8, type 35): the lesser of the target's health and
    /// <paramref name="percent"/> of its level-scaled maximum leaves the target through the health
    /// routine, and the same amount reaches the caster. Nothing on a target already down.</summary>
    public static void Drain(SimulationContext context, CreatureState target, CreatureDef targetDef, CreatureState? caster, int percent)
    {
        if (percent <= 0 || CreatureDeathRules.IsDown(target)) return;
        WorldState world = context.World;
        GameDatabase database = context.Database;
        int amount = Math.Min(target.Health, CreatureHealthRules.MaxHealth(database, targetDef, target) * percent / 100);
        if (amount <= 0) return;
        int landed = Hurt(world, database, targetDef, target, amount);
        int gained = 0;
        if (caster != null && !CreatureDeathRules.IsDown(caster))
            gained = CreatureHealthRules.Apply(database, database.CreatureById(caster.CreatureTypeId), caster, amount);
        context.Emit(SystemName, "drained", $"target={target.Id} amount={amount} landed={landed} caster={caster?.Id ?? 0} gained={gained}");
    }

    /// <summary>Lightning (<c>0x004AF1E0</c>, type 9): nothing for a target down or immune; the data
    /// word, doubled on a water tile, through the health routine; then the stun.</summary>
    public static void Lightning(SimulationContext context, CreatureState target, CreatureDef targetDef, int damage, int casterId)
    {
        if (CreatureDeathRules.IsDown(target) || (targetDef.Flags2 & LightningImmune) != 0) return;
        WorldState world = context.World;
        GameDatabase database = context.Database;
        TilePos at = target.Tile;
        bool water = world.Contains(at) && database.TerrainById(world.Tile(at.X, at.Y).TerrainId).IsWater;
        int dealt = water ? damage * 2 : damage;
        int landed = Hurt(world, database, targetDef, target, dealt);
        context.Emit(SystemName, "lightning", $"target={target.Id} caster={casterId} damage={dealt} landed={landed} health={target.Health}");
        Stun(context, target, targetDef);
    }

    /// <summary>Into a standing state: the engagement let go, the action ended, the route dropped.
    /// For a fighter DK2's Combat leave hook lets go too (ADR-0319); for anyone else letting the
    /// target go is ours, and the creature looks again when it comes round either way.</summary>
    private static void Stand(SimulationContext context, CreatureState creature, int stateId)
    {
        CombatRules.Disengage(context.Hooks, creature);
        creature.ClearJob();
        creature.CurrentJob = JobType.None;
        creature.JobTileIndex = -1;
        creature.SwingInProgress = false;
        creature.Running = false;
        CombatStates.Enter(context, creature, stateId);
    }
}
