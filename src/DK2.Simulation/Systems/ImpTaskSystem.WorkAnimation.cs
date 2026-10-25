// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Simulation.Systems;

public sealed partial class ImpTaskSystem
{
    /// <summary>Actions 1 (dig, <c>0x00496E00</c>) and 2 (mine, <c>0x004971D0</c>) play art slot 5.</summary>
    private const byte WorkArtSlot = 5;

    /// <summary>Whether the creature's work runs on the looping slot-5 action whose end the stored
    /// animation clock decides: digging (all three kinds) and mining, in their work states, for a
    /// creature whose slot 5 has timing data.</summary>
    private static bool PlaysWorkLoop(CreatureState creature, CreatureDef definition) =>
        creature.TargetTileIndex >= 0
        && creature.TargetWork is TileWorkKind.Dig or TileWorkKind.DigOwnWall
            or TileWorkKind.DigEnemyWall or TileWorkKind.MineGold
        && creature.Dk2StateId == ImpStates.For(creature.TargetWork).Work
        && ActionAnimationClock.CanPlay(definition, WorkArtSlot);

    /// <summary>What the action handler tests before striking: dig needs a diggable wall
    /// (<c>0x00496E00</c>), mining a workable seam (<c>0x004971D0</c>).</summary>
    private static bool LoopTileWorkable(SimulationContext context, CreatureState creature)
    {
        TileState tile = context.World.TileAtIndex(creature.TargetTileIndex);
        return creature.TargetWork == TileWorkKind.MineGold
            ? TileWorkRules.Applies(context.Database, tile, TileWorkKind.MineGold, creature.OwnerPlayerId)
            : TileWorkRules.TryResolveDigTask(context.Database, tile, creature.TargetWork, creature.OwnerPlayerId, out _);
    }

    /// <summary>Whether the work state's running action has ended, so the state may look at its
    /// task (<c>0x004655D0</c> and <c>0x00465870</c> return at once while one runs). The mode-3
    /// runner (<c>0x00496AA0</c>) calls the handler until it first returns done, and ends the
    /// action when the handler and the animation are both done. A handler still being called
    /// whose tile is gone sets the stop bit and stays busy until the clip completes; one that
    /// already struck is not called again, so its action ends at the wrap. No action running
    /// ends at once.</summary>
    private static bool WorkActionEnded(SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        if (!PlaysWorkLoop(creature, definition))
            return WorkCadence.ActionEnded(creature.TargetWork, context.Tick.Value, creature.StateEnteredTick);
        if (creature.ActionArtSlot == 0) return true;
        if ((creature.ActionAnimationFlags & ActionAnimationFlags.Stop) == 0 && !creature.ActionHandlerDone)
        {
            if (!LoopTileWorkable(context, creature)) creature.ActionAnimationFlags |= ActionAnimationFlags.Stop;
            // A standing wall whose job is gone: DK2's handler, which ignores the job, strikes once
            // more and returns done. Ours does not strike work it no longer holds.
            else creature.ActionHandlerDone = true;
        }
        return (creature.ActionAnimationFlags & ActionAnimationFlags.Stop) != 0
            ? (creature.ActionAnimationFlags & ActionAnimationFlags.Complete) != 0
            : ActionAnimationClock.AnimationDone(creature);
    }

    /// <summary>The end of the creature's update: the work action starts on the turn after the
    /// work state is entered and its clip advances every turn after that (mining-finish.md).
    /// A handler still being called that finds its tile gone asks the clip to stop, as does one
    /// whose own strike this turn emptied it (the handlers retest after striking). An action
    /// whose handler and clip were both done ended at this turn's poll, and the state began
    /// the next, whose handler runs again. Out of work, the standing or walking clip replaces it.</summary>
    private static void StepWorkAnimation(SimulationContext context, CreatureState creature, CreatureDef definition, bool struck)
    {
        if (creature.TargetTileIndex >= 0 && creature.SecretDoorTask
            && creature.Dk2StateId == ImpStates.SecretDoorWork)
        {
            ActionAnimationClock.Advance(creature, definition);
            return;
        }
        if (!PlaysWorkLoop(creature, definition))
        {
            if (PlaysIdleAction(creature)) ActionAnimationClock.Advance(creature, definition);
            else if (creature.ActionArtSlot != 0 || creature.ActionHandlerDone) ActionAnimationClock.Clear(creature);
            return;
        }
        if (context.Tick.Value <= creature.StateEnteredTick) return;

        bool workable = LoopTileWorkable(context, creature);
        if (creature.ActionArtSlot != WorkArtSlot)
        {
            if (!workable) return;
            ActionAnimationClock.Begin(creature, definition, WorkArtSlot, context.Rng);
            creature.ActionHandlerDone = false;
        }
        else if (!workable && (struck || !creature.ActionHandlerDone))
        {
            creature.ActionAnimationFlags |= ActionAnimationFlags.Stop;
            creature.ActionHandlerDone = false;
        }
        else if (creature.ActionHandlerDone && ActionAnimationClock.AnimationDone(creature))
        {
            creature.ActionHandlerDone = false;
        }
        ActionAnimationClock.Advance(creature, definition);
    }
}
