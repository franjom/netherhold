// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;

namespace DK2.Simulation.Systems;

/// <summary>The timed-effects update (docs/research/creature-status-effects.md §1, <c>0x0048E160</c>),
/// once a tick for every creature and skipped whole for an <c>IsHorny</c> definition: each status
/// word steps down while nonzero; the frozen state ends idle when its word is spent, the speed
/// effect puts the walk back, poison bleeds its amount a tick while the creature is not frozen, and a
/// spent chicken timer sends the creature to idle whatever it is doing. The
/// stun is a state with its own clock (§2, state 255) and counts here too.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/creature-status-effects.md")]
public sealed class CreatureStatusSystem : ICreatureUpdateStep
{
    private const string SystemName = "status";

    public string Name => SystemName;

    /// <summary>The handlers of 255, 256 and 257. They are state handlers, so in the creature's update
    /// they follow its fight scan (<c>0x00488930</c>): the turn 255 ends has no scan, and a scan that
    /// engages a creature in 257 comes before its get-up step.</summary>
    public ICreatureUpdateStep Recovery { get; } = new RecoveryStep();

    /// <summary>Alone, the timed effects and then the stun's handlers for each creature.</summary>
    public void Execute(SimulationContext context) => CreatureUpdateSystem.RunAlone(context, this, Recovery);

    private sealed class RecoveryStep : ICreatureUpdateStep
    {
        public string Name => "stun states";

        public void Execute(SimulationContext context) => CreatureUpdateSystem.RunAlone(context, this);

        public void Update(SimulationContext context, CreatureState creature)
        {
            if (CreatureDeathRules.IsCorpse(creature)) return;
            WorldState world = context.World;
            GameDatabase database = context.Database;
            CreatureDef definition = database.CreatureById(creature.CreatureTypeId);
            if (creature.Dk2StateId == CreatureDeathRules.FallingState)
            {
                DeathFallRules.Fall(context, creature, definition);
                return;
            }
            if (creature.Activity == CreatureActivity.Stunned && !TremorRules.IsRecovering(creature))
            {
                if (creature.Dk2StateId == Combat.CombatStates.GettingUp)
                {
                    // 257 holds its get-up action, then tries combat before the room-drop fallback.
                    if (context.Tick.Value >= creature.ActionUntilTick)
                    {
                        context.Emit(SystemName, "recovered", $"creature={creature.Id}");
                        DropLandingRules.StepAfterGettingUp(context, creature, definition);
                        if (!context.DropCombat.TryAcquire(context, creature, definition))
                        {
                            // 257's room-drop tail has no worker exclusion (0x00481351): a stunned keeper's
                            // Imp off any room stands guard in 250 like any creature (Q759, the owner's capture).
                            DropLandingRules.AfterGettingUp(context, SystemName, creature, definition);
                        }
                        if (world.CreatureById(creature.Id) is null)
                        {
                            return;
                        }
                    }
                }
                else if (creature.Dk2StateId is Combat.CombatStates.Stunned or 256
                         && context.Tick.Value == creature.StateEnteredTick)
                {
                    // 255's handler (0x004810E0; 256 jumps to it) first runs the turn after SetState,
                    // with Age 1; the stun set this turn has not started counting.
                }
                else
                {
                    if (creature.StunTicks > 0) creature.StunTicks--;
                    if (creature.StunTicks == 0)
                    {
                        // The stun's end: action 53 (getting up) and SetState(257). 255's end arm then clears
                        // PrisonOwner (+0x3CB := 0 at 0x004811E5): a freed prisoner was its jailer's to lift
                        // while it lay stunned, and is no longer once up (owner, 2026-11-01).
                        bool from255 = creature.Dk2StateId is Combat.CombatStates.Stunned or 256;
                        Combat.CombatStates.Enter(context, creature, Combat.CombatStates.GettingUp);
                        if (from255 && creature.Dk2StateId == Combat.CombatStates.GettingUp)
                            creature.PrisonOwnerPlayerId = PrisonRules.NoPrison;
                        creature.ActionUntilTick = context.Tick.Value + DropLandingRules.GetUpTicks(definition);
                        context.Emit(SystemName, "getting_up", $"creature={creature.Id} until={creature.ActionUntilTick}");
                    }
                    else if (creature.Dk2StateId is Combat.CombatStates.Stunned or 256
                        && context.Tick.Value - creature.StateEnteredTick == 1)
                        Combat.StatusEffectRules.RequestDazedEffect(context, creature);
                }
            }
        }
    }

    public void Update(SimulationContext context, CreatureState creature)
    {
        // Native corpses are class6, outside Creature_Update and its timed effects.
        if (CreatureDeathRules.IsCorpse(creature)) return;
        WorldState world = context.World;
        GameDatabase database = context.Database;

        CreatureDef definition = database.CreatureById(creature.CreatureTypeId);
        ReaperRules.AdvanceTimer(context, creature, definition);
        FinaleRules.HornyAmbience(context, SystemName, creature, definition);

        // 67, 77 and 120, the slap's reaction (0x0046F1F0, ADR-0401), for every creature.
        if (creature.Activity == CreatureActivity.Slapped) SlapRules.React(context, SystemName, creature, definition);

        if (((definition.Flags & CreatureFlags.IsHorny) == CreatureFlags.IsHorny)) return;

        if (creature.ArmourTicks > 0) creature.ArmourTicks--;
        if (creature.InvulnerableTicks > 0) creature.InvulnerableTicks--;
        if (creature.InvisibleTicks > 0) creature.InvisibleTicks--;
        if (creature.TurncoatTicks > 0) creature.TurncoatTicks--;
        if (creature.FrozenTicks > 0) creature.FrozenTicks--;
        if (creature.ChickenTicks > 0 && --creature.ChickenTicks == 0) ChickenRules.TimerEnded(context, creature);
        if (creature.Activity == CreatureActivity.Frozen && creature.FrozenTicks == 0)
        {
            Combat.CombatStates.Idle(context.Hooks, creature);
            context.Emit(SystemName, "thawed", $"creature={creature.Id}");
        }

        if (creature.SpeedEffectTicks > 0 && --creature.SpeedEffectTicks == 0)
        {
            creature.SpeedPercent = 100;
            context.Emit(SystemName, "speed_restored", $"creature={creature.Id}");
        }

        if (creature.PoisonTicks > 0 && !StatusEffectRules.IsFrozen(creature))
        {
            creature.PoisonTicks--;
            if (!CreatureDeathRules.IsDown(creature))
            {
                int landed = StatusEffectRules.Hurt(world, database, definition, creature, creature.PoisonAmount, creature.PoisonOwner); // 0x0048E35F: +0x461
                context.Emit(SystemName, "poison_tick", $"creature={creature.Id} landed={landed} health={creature.Health} left={creature.PoisonTicks}");
            }
        }

        StatusEffectRules.UpdateBurning(context, creature, definition);
        // The low-health branch's stamp half, before the handler reads the recharges (0x004896C8).
        LowHealthSpeedRules.DelayRecharge(database, definition, creature, context.Tick.Value);
    }
}
