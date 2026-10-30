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
    /// <summary>0x0048EBD0: replace both burning words, including a shorter re-arm.</summary>
    public static void SetBurning(CreatureState creature, ushort durationTicks, PlayerId owner)
    {
        creature.BurningTicks = durationTicks;
        creature.BurningOwnerTag = owner.Value;
    }

    /// <summary>Fire ignition is tested before health changes, using unsigned, truncated percent.</summary>
    public static void IgniteWeakTarget(SimulationContext context, ShotDef shot, CreatureState target,
        ushort durationTicks, PlayerId owner)
    {
        if (shot.AttackCategory != 4) return;
        if (context.Database.TerrainById(context.World.Tile(target.Tile).TerrainId).IsWater) return;
        CreatureDef definition = context.Database.CreatureById(target.CreatureTypeId);
        uint max = (uint)CreatureHealthRules.MaxHealth(context.Database, definition, target);
        if (max != 0 && unchecked((uint)(target.Health * 100)) / max <= 20)
            SetBurning(target, durationTicks, owner);
    }

    /// <summary>0x0048E160's last status arm: nine simulation draws, including the expiry tick.</summary>
    public static void UpdateBurning(SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        if (creature.BurningTicks == 0) return;
        creature.BurningTicks--;
        int height = CreatureLevelRules.Scale((ushort)definition.Height.Raw,
            CreatureLevelRules.Percent(context.Database, CreatureStatId.HeightTiles, creature.Level));
        int footprint = (ushort)definition.FootprintRadius.Raw;
        for (int i = 0; i < 3; i++)
        {
            int z = unchecked(creature.PosZ + context.Rng.NextInt(height));
            int x = unchecked(creature.PosX - footprint / 2 + context.Rng.NextInt(footprint));
            int y = unchecked(creature.PosY - footprint / 2 + context.Rng.NextInt(footprint));
            context.RequestEffect(SystemName, new ViewEffectRequest(new EffectId(88), creature.OwnerPlayerId,
                new FpPosition3(new Fp32(x), new Fp32(y), new Fp32(z))));
        }
        if (creature.BurningTicks == 0) creature.BurningOwnerTag = 0;
    }

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
    /// what landed, as a negative number. The invulnerable word is the caller's refusal, as in DK2.
    /// <paramref name="source"/> (the attacker's, shot's or caster's owner) is stamped first, before
    /// every refusal, as the routine writes <c>+0x36</c> on entry.</summary>
    public static int Hurt(WorldState world, GameDatabase database, CreatureDef definition, CreatureState target, int damage, byte source)
    {
        target.HealthSourcePlayerId = source;
        if (damage <= 0) return 0;
        if (definition.Flags.HasFlag(CreatureFlags.IsHorny) || definition.Flags.HasFlag(CreatureFlags.OnlyAttackableByHorny)) return 0;
        if (HandRules.IsHeld(target)) return 0;
        int scaled = (int)((long)damage * DamageMultiplier(world, database, target) / Base);
        if (IsStunned(target) && FearRules.NonImpAmongAttackers(world, target)) LengthenStun(database, definition, target);
        return CreatureHealthRules.Apply(database, definition, target, -scaled, source);
    }

    /// <summary><c>0x00481360</c>, from the damage routine (<c>0x004B2EBA</c>) for a stunned creature
    /// with a live non-Imp among its attackers: the stun left grows by
    /// <c>StunTimePercentageIncreaseWhenHit</c> (variable 181, 25) percent of itself, toward zero, and
    /// never past the full <c>StunDuration</c>. DK2 moves the state's start turn (<c>+0x18</c>); ours
    /// counts the stun left down, so the same arithmetic acts on <c>StunTicks</c>. 257 and 209 pass the
    /// same test in DK2, but neither handler reads the start turn, so only 255 and 256 change (ADR-0348).</summary>
    public static void LengthenStun(GameDatabase database, CreatureDef definition, CreatureState target)
    {
        if (target.Dk2StateId is not (CombatStates.Stunned or 256) || target.StunTicks <= 0) return;
        int full = CombatRules.TicksOf(definition.StunDuration);
        int left = target.StunTicks;
        int more = left * database[VariableTypeId.StunTimePercentageIncreaseWhenHit] / 100;
        target.StunTicks = left + more > full ? full : left + more;
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
    /// quarter of the definition's walk and no more than ticks-a-second tiles a tick, then the word
    /// SetSpeed stores, at most <c>0xFFF</c> (<c>0x0048D010</c>, ADR-0397). The cap is
    /// in 16.16 tiles a tick, as <see cref="Systems.CreatureMovementSystem.MaxSpeedOf(CreatureDef, bool)"/> gives it.</summary>
    public static int SpeedCap(GameDatabase database, CreatureDef definition, CreatureState creature, int cap)
    {
        if (creature.SpeedEffectTicks <= 0) return cap;
        int perTick = cap >> 4;
        int walk = SimulationTiming.LoaderRatePerTick(definition.Speed.Raw);
        int modified = Math.Clamp(perTick * creature.SpeedPercent / 100, walk / 4, SimulationTiming.OriginalTicksPerGameSecond << 12);
        return Math.Min(modified, Systems.CreatureMovementSystem.MaxSpeedPerTick) << 4;
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

    /// <summary>Creature Dazed Effect (<c>0x65</c>): stars and a sparkle circling the stunned creature.</summary>
    public const ushort DazedEffectId = 101;

    /// <summary>255's first call (<c>0x004810E0</c>, Age 1), while its duration hold remains, asks for
    /// <see cref="DazedEffectId"/> at the
    /// point the creature's mesh tags 5000, its head (world <c>+4→+0x10</c> slot <c>+0x10C</c>,
    /// <c>0x0059D260</c>), else at its position, and ties the effect to it (effect <c>+0x133</c> the
    /// creature, <c>+0x135</c> <c>0x1388</c>); the effect update <c>0x00434E80</c> moves it and its
    /// elements with that point. Ours asks at the creature's position on the floor and leaves the
    /// point to the view, which has the mesh.</summary>
    public static void RequestDazedEffect(SimulationContext context, CreatureState creature) =>
        context.RequestEffect(SystemName, new ViewEffectRequest(new EffectId(DazedEffectId),
            new PlayerId(creature.OwnerPlayerId),
            new FpPosition3(new Fp32(creature.PosX), new Fp32(creature.PosY), new Fp32(Fp32.One)),
            new Dk2Angle((ushort)creature.Heading), creature.Id, DazedAttachPoint));

    /// <summary>The mesh point 255 ties the dazed effect to (<c>0x1388</c>), the head.</summary>
    public const uint DazedAttachPoint = 5000;

    /// <summary><c>0x00491830(255)</c> as the landing calls it (ADR-0226): <c>SetState(255)</c>,
    /// action 52 and event 15, with none of <see cref="Stun"/>'s callers' refusals. A refused state
    /// is no stun.</summary>
    public static bool StunOnLanding(SimulationContext context, CreatureState creature, CreatureDef definition) =>
        StunInto(context, creature, definition, CombatStates.Stunned, "landing");

    /// <summary><c>0x00491830(state)</c>: <c>SetState</c>, action 52 and event 15. State 256, the
    /// boulder's, runs 255's handler (<c>0x004810D0</c> jumps to <c>0x004810E0</c>), so it lies for
    /// <c>StunDuration</c> and gets up in 257 the same way.</summary>
    public static bool StunInto(SimulationContext context, CreatureState creature, CreatureDef definition, int state, string by)
    {
        if (CreatureStateMachine.Classify(creature, state) == StateTransition.Refused) return false;
        creature.StunTicks = CombatRules.TicksOf(definition.StunDuration);
        Stand(context, creature, state);
        context.FireCreatureEvent(creature, CreatureTriggerEvent.Stunned);
        context.Emit(SystemName, "stunned", $"creature={creature.Id} ticks={creature.StunTicks} by={by}");
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
    public static bool Poison(SimulationContext context, CreatureState creature, int ticks, int amount, PlayerId owner)
    {
        if (ticks <= 0 || amount <= 0 || CreatureDeathRules.IsDown(creature)) return false;
        creature.PoisonTicks = ticks;
        creature.PoisonAmount = amount;
        creature.PoisonOwner = owner.Value;
        context.Emit(SystemName, "poisoned", $"creature={creature.Id} amount={amount} ticks={ticks} owner={owner}");
        return true;
    }

    /// <summary>Drain (shots.md §8, type 35): the lesser of the target's health and
    /// <paramref name="percent"/> of its level-scaled maximum leaves the target through the health
    /// routine, and the same amount reaches the caster. Nothing on a target already down.</summary>
    public static void Drain(SimulationContext context, CreatureState target, CreatureDef targetDef, CreatureState? caster, int percent, byte source)
    {
        if (percent <= 0 || CreatureDeathRules.IsDown(target)) return;
        WorldState world = context.World;
        GameDatabase database = context.Database;
        int amount = Math.Min(target.Health, CreatureHealthRules.MaxHealth(database, targetDef, target) * percent / 100);
        if (amount <= 0) return;
        // 0x004AFE29/0x004AFE38: both calls pass the shot's owner (+0x28) as the source.
        int landed = Hurt(world, database, targetDef, target, amount, source);
        int gained = 0;
        if (caster != null && !CreatureDeathRules.IsDown(caster))
            gained = CreatureHealthRules.Apply(database, database.CreatureById(caster.CreatureTypeId), caster, amount, source);
        context.Emit(SystemName, "drained", $"target={target.Id} amount={amount} landed={landed} caster={caster?.Id ?? 0} gained={gained}");
    }

    /// <summary>Lightning (<c>0x004AF1E0</c>, type 9): nothing for a target down or immune; the data
    /// word, doubled on a water tile, through the health routine; then the stun.</summary>
    public static void Lightning(SimulationContext context, CreatureState target, CreatureDef targetDef, int damage, int casterId, byte source)
    {
        if (CreatureDeathRules.IsDown(target) || (targetDef.Flags2 & LightningImmune) != 0) return;
        WorldState world = context.World;
        GameDatabase database = context.Database;
        TilePos at = target.Tile;
        bool water = world.Contains(at) && database.TerrainById(world.Tile(at.X, at.Y).TerrainId).IsWater;
        int dealt = water ? damage * 2 : damage;
        int landed = Hurt(world, database, targetDef, target, dealt, source); // 0x004AF2D1: the shot's owner
        context.Events.Emit(new SimEvent(context.Tick, SystemName, "lightning",
            $"target={target.Id} caster={casterId} damage={dealt} landed={landed} health={target.Health}")
        {
            ElectrifiedCreature = new CreatureId(target.Id),
        });
        if (target.PrisonOwnerPlayerId != PrisonRules.NoPrison
            || NeedRules.ResolvedCategory(target) == CreatureStateCategory.Dying) return;
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
