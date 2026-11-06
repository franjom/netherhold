// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Systems;

namespace DK2.Simulation;

/// <summary>Action7's independent current-action point and completion bit (00496980/00496AA0).</summary>
public struct ReinforcementActionState
{
    public bool Active;
    public TilePos Target;
    public bool AnimationComplete;
}

/// <summary>The ordinary reinforcement action7 (00497960), using art12 and mode3 (004950C0).</summary>
public static class ReinforcementActionRules
{
    private const byte ArtSlot = 12;
    private const ActionAnimationFlags Cycle = ActionAnimationFlags.Complete | ActionAnimationFlags.Wrapped;
    private static readonly TileWorkSystem Work = new();

    internal static bool UsesClock(CreatureState creature, CreatureDef definition) =>
        creature.TargetTileIndex >= 0 && creature.TargetWork == TileWorkKind.Reinforce
        && creature.Dk2StateId == 22 && ActionAnimationClock.CanPlay(definition, ArtSlot);

    internal static void Begin(SimulationContext context, CreatureState creature, CreatureDef definition, TilePos target)
    {
        CreatureStateMachine.EndAction(creature);
        ActionAnimationClock.Begin(creature, definition, ArtSlot, context.Rng);
        creature.ReinforcementAction = new ReinforcementActionState { Active = true, Target = target };
        creature.ActionHandlerDone = false;
    }

    // The common poll precedes ProcessFight (00488930 at00489A68), independent of the current job.
    internal static void Poll(SimulationContext context, CreatureState creature)
    {
        if (!context.ReinforcementActionsPolledThisTick.Add(creature) || !creature.ReinforcementAction.Active) return;
        if (!creature.ActionHandlerDone)
        {
            TilePos target = creature.ReinforcementAction.Target;
            if (Workable(context, creature, target))
            {
                context.World.QueueWork(new TileWorkOrder(target.Y * context.World.Width + target.X,
                    TileWorkKind.Reinforce, creature.OwnerPlayerId, creature.Id));
                Work.Execute(context);
            }
            if (Workable(context, creature, target)) creature.ActionHandlerDone = true;
            else
            {
                bool onCycle = (creature.ActionAnimationFlags & Cycle) != 0;
                creature.ActionAnimationFlags |= ActionAnimationFlags.Stop;
                creature.ActionHandlerDone = onCycle;
            }
        }
        PollAnimation(creature);
        if (creature.ActionHandlerDone && creature.ReinforcementAction.AnimationComplete) Cancel(creature);
    }

    private static bool Workable(SimulationContext context, CreatureState creature, TilePos target) =>
        context.World.Contains(target)
        && TileWorkRules.Applies(context.Database, context.World.Tile(target), TileWorkKind.Reinforce, creature.OwnerPlayerId)
        && OwnFloorRules.Touches(context.Database, context.World, target, creature.OwnerPlayerId);

    // 00496AA0 mode3: completion is sticky; a nonzero count is not an immediate end.
    private static void PollAnimation(CreatureState creature)
    {
        if (creature.ReinforcementAction.AnimationComplete) return;
        if (creature.ActionAnimationRate == 0) creature.ReinforcementAction.AnimationComplete = true;
        if ((creature.ActionAnimationFlags & Cycle) == 0) return;
        if (creature.ActionCount == 0) creature.ReinforcementAction.AnimationComplete = true;
        else if (creature.ActionCount == 1)
        {
            if ((creature.ActionAnimationFlags & ActionAnimationFlags.Complete) != 0) creature.ActionCount = byte.MaxValue;
        }
        else
        {
            creature.ActionCount--;
            if (creature.ActionCount == 1) creature.ActionAnimationFlags &= ~ActionAnimationFlags.Loop;
            else creature.ActionAnimationFlags &= ~Cycle;
        }
    }

    internal static void Advance(CreatureState creature, CreatureDef definition)
    {
        if (creature.ActionArtSlot != ArtSlot
            || (!creature.ReinforcementAction.Active && !((definition.Flags & CreatureFlags.IsWorker) == CreatureFlags.IsWorker))) return;
        if (!creature.ReinforcementAction.Active) ActionAnimationClock.Clear(creature);
        else if (!StatusEffectRules.IsFrozen(creature)) ActionAnimationClock.Advance(creature, definition);
    }

    // 00496D20 clears the action block, leaving the animation object for the later chooser.
    internal static void Cancel(CreatureState creature)
    {
        if (!creature.ReinforcementAction.Active) return;
        creature.ReinforcementAction = default;
        creature.ActionHandlerDone = false;
        creature.ActionCount = 0;
    }
}
