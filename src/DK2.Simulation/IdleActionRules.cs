// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

public enum IdleActionId : byte { None = 0, First = 43, Second = 44 }

/// <summary>The inner idle chooser's counted actions, polled before state handlers and
/// advanced after them (0x004776C0, 0x00496AA0, ADR-0701).</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/idle-action-clock.md")]
internal static class IdleActionRules
{
    internal static bool TryBegin(SimulationContext context, CreatureState creature, IdleActionId action)
    {
        int slot = action switch
        {
            IdleActionId.First => 20,
            IdleActionId.Second => 31,
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };
        CreatureDef definition = context.Database.CreatureById(creature.CreatureTypeId);
        if (!AnimationTimingRules.ResolvesPresent(definition, slot)) return false;
        if (creature.IdleAction == IdleActionId.None) CreatureStateMachine.EndAction(creature);
        creature.ActionUntilTick = 0;
        ActionAnimationClock.BeginCounted(creature, definition,
            (byte)slot, 1, context.Rng);
        creature.IdleAction = action;
        creature.ActionHandlerDone = false;
        context.IdleActionsPolledThisTick.Add(creature);
        return true;
    }

    internal static void Poll(SimulationContext context, CreatureState creature)
    {
        if (creature.IdleAction == IdleActionId.None || !context.IdleActionsPolledThisTick.Add(creature)) return;
        creature.ActionHandlerDone = true;
        if (creature.InHand || ActionAnimationClock.CountedActionEnded(creature)) Cancel(creature);
    }

    internal static void Advance(SimulationContext context, CreatureState creature)
    {
        if (creature.IdleAction == IdleActionId.None || !context.IdleActionsAdvancedThisTick.Add(creature)) return;
        ActionAnimationClock.Advance(creature, context.Database.CreatureById(creature.CreatureTypeId));
    }

    internal static void Cancel(CreatureState creature)
    {
        if (creature.IdleAction == IdleActionId.None) return;
        creature.IdleAction = IdleActionId.None;
        ActionAnimationClock.Clear(creature);
    }
}
