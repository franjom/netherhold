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
[Dk2Behavior(Confidence.Confirmed, "docs/research/creature-status-effects.md")]
public sealed class CreatureStatusSystem : ISimulationSystem
{
    private const string SystemName = "status";

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        IReadOnlyList<CreatureState> creatures = world.Creatures;
        for (int i = 0; i < creatures.Count; i++)
        {
            CreatureState creature = creatures[i];
            CreatureDef definition = database.CreatureById(creature.CreatureTypeId);

            if (creature.Activity == CreatureActivity.Stunned && !TremorRules.IsRecovering(creature))
            {
                if (creature.Dk2StateId == Combat.CombatStates.GettingUp)
                {
                    // 257 (0x00481200) holds while its get-up action plays (0x00496D00), then runs the
                    // victim-or-door pick 0x00481400 (not built) and asks the room underfoot (ADR-0226,
                    // ADR-0345). A worker idles instead.
                    if (context.Tick.Value >= creature.ActionUntilTick)
                    {
                        context.Emit(SystemName, "recovered", $"creature={creature.Id}");
                        if (definition.Flags.HasFlag(CreatureFlags.IsWorker)) Combat.CombatStates.Idle(context.Hooks, creature);
                        else DropLandingRules.AfterGettingUp(context, SystemName, creature, definition);
                    }
                }
                else
                {
                    if (creature.StunTicks > 0) creature.StunTicks--;
                    if (creature.StunTicks == 0)
                    {
                        // The stun's end: action 53 (getting up) and SetState(257).
                        Combat.CombatStates.Enter(context, creature, Combat.CombatStates.GettingUp);
                        creature.ActionUntilTick = context.Tick.Value + DropLandingRules.GetUpTicks(definition);
                        context.Emit(SystemName, "getting_up", $"creature={creature.Id} until={creature.ActionUntilTick}");
                    }
                }
            }

            if (definition.Flags.HasFlag(CreatureFlags.IsHorny)) continue;

            if (creature.ArmourTicks > 0) creature.ArmourTicks--;
            if (creature.InvulnerableTicks > 0) creature.InvulnerableTicks--;
            if (creature.InvisibleTicks > 0) creature.InvisibleTicks--;
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
                    int landed = StatusEffectRules.Hurt(world, database, definition, creature, creature.PoisonAmount);
                    context.Emit(SystemName, "poison_tick", $"creature={creature.Id} landed={landed} health={creature.Health} left={creature.PoisonTicks}");
                }
            }
        }
    }
}
