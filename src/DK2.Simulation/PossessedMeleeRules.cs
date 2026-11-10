// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Simulation.Combat;

namespace DK2.Simulation;

/// <summary>The possessed melee action's independent completion and shared-clip ownership.</summary>
public struct PossessedMeleeState
{
    public bool Active;
    public bool AnimationComplete;
    public bool OwnsClip;
}

/// <summary>Action 17's shared starter and mode-3 runner for possession.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/possessed-melee-draw.md")]
internal static class PossessedMeleeRules
{
    private const byte ArtSlot = 5;

    internal static void Begin(SimulationContext context, CreatureState creature)
    {
        CreatureStateMachine.EndAction(creature);
        ActionAnimationClock.Begin(creature, context.Database.CreatureById(creature.CreatureTypeId), ArtSlot, context.Rng);
        creature.PossessedMelee = new PossessedMeleeState { Active = true, OwnsClip = true };
        creature.ActionHandlerDone = false;
    }

    internal static void Poll(SimulationContext context, CreatureState creature)
    {
        if (!creature.PossessedMelee.Active || !context.PossessedMeleePolledThisTick.Add(creature)) return;
        if (!creature.ActionHandlerDone)
        {
            var definition = context.Database.CreatureById(creature.CreatureTypeId);
            if (creature.SwingInProgress && CombatRules.BlowLands(creature, definition, context.Tick.Value))
                FightRules.LandBlow(context, creature, definition);
            creature.ActionHandlerDone = !creature.SwingInProgress;
        }
        creature.PossessedMelee.AnimationComplete |= ActionAnimationClock.AnimationDone(creature);
        if (creature.ActionHandlerDone && creature.PossessedMelee.AnimationComplete)
        {
            Cancel(creature);
        }
    }

    internal static void Advance(SimulationContext context, CreatureState creature)
    {
        if (!creature.PossessedMelee.OwnsClip || !context.PossessedMeleeAdvancedThisTick.Add(creature)) return;
        if (!creature.PossessedMelee.Active)
        {
            if (creature.ActionArtSlot == ArtSlot) ActionAnimationClock.Clear(creature);
            creature.PossessedMelee = default;
        }
        else if (!StatusEffectRules.IsFrozen(creature))
            ActionAnimationClock.Advance(creature, context.Database.CreatureById(creature.CreatureTypeId));
    }

    internal static void Cancel(CreatureState creature)
    {
        if (!creature.PossessedMelee.OwnsClip) return;
        // Ending or replacing the action clears its pending handler and current target.
        creature.SwingInProgress = false;
        creature.SwingTargetKind = FightTargetKind.None;
        creature.SwingTargetId = CombatRules.NoTarget;
        creature.PossessedMelee = new PossessedMeleeState { OwnsClip = true };
        creature.ActionHandlerDone = false;
        creature.ActionCount = 0;
    }
}
