// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>00519F90 selects269 outside the two large gates; 004626A0 reveals
/// the creature at age8 and enters the shared action-driven recovery.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/party-arrival-lifecycle.md")]
public static class PartyArrivalRules
{
    public const int WaitingState = 269;

    public static void Begin(in StateHookContext hooks, CreatureState creature)
    {
        CreatureStateMachine.TrySetState(hooks, creature, WaitingState, hooks.World.CurrentTick.Value);
        creature.ReaperHidden = true;
    }

    internal static bool Update(SimulationContext context, CreatureState creature)
    {
        if (creature.Dk2StateId != WaitingState) return false;
        if (creature.Possessed || HandRules.IsHeld(creature)) return true;
        creature.TremorRoutePaused = true;
        if (++creature.ReaperStateAgeTicks != 8) return true;
        // Native Z0x2000 is one tile above floor0x1000; ours uses floor0.
        creature.PosZ = creature.LastPosZ = Fp32.One;
        creature.ReaperHidden = false;
        creature.ArrivalHeightActive = true;
        CreatureStateMachine.TrySetState(context.Hooks, creature, TremorRules.KnockdownState, context.Tick.Value);
        return true;
    }

    internal static void StepHeight(SimulationContext context, CreatureState creature)
    {
        if (!creature.ArrivalHeightActive) return;
        creature.LastPosZ = creature.PosZ;
        if (creature.PosZ <= 0)
        {
            creature.PosZ = creature.CreationVelocityZ = 0;
            creature.ArrivalHeightActive = false;
            return;
        }
        if (creature.PosZ > 0)
        {
            if (!WaterSpeedRules.IsFlying(context.Database.CreatureById(creature.CreatureTypeId), creature))
                creature.CreationVelocityZ -= unchecked((byte)context.Database[VariableTypeId.GravityConstant]) * 0x100;
            creature.PosZ += creature.CreationVelocityZ / 16;
        }
        if (creature.PosZ > 0) return;
        creature.PosZ = creature.CreationVelocityZ = 0;
    }
}
