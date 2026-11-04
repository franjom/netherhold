// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation;

/// <summary><c>0x004E8D30</c>: when a room rectangle closes (<c>0x004F04F0</c>: a build wave, trigger
/// action 22's room arm) or a keeper puts down a door or trap blueprint (<c>0x004C33B0</c>,
/// <c>0x004C3860</c>), each of that player's creatures in state 3, 4 or 7, or in 6 resuming 1, stops
/// and is kicked to 1, so a walk chosen before the change is chosen again (ADR-0654).</summary>
/// <remarks>DK2 walks the player's own creature list, newest first, which
/// <see cref="WorldState.OwnedCreatures"/> mirrors. A kick is <c>SetState(1)</c> and nothing more
/// (<c>0x0048AD30</c>); the stop comes first and is unconditional.</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/blueprint-placement.md")]
public static class WalkerResetRules
{
    /// <summary>The states <c>0x004E8D30</c> resets: 3, 4 and 7, and 6 while its resume is 1.</summary>
    public static bool Resets(CreatureState creature) =>
        creature.Dk2StateId is 3 or 4 or StateShadow.TravelToRoom
        || (creature.Dk2StateId == StateShadow.TravelToPoint && creature.UtilityNewState == CreatureStateMachine.Idle);

    public static void Reset(SimulationContext context, string system, PlayerId player) =>
        Reset(context.Hooks, context.World, context.Tick.Value, player,
            (creature, from, taken) => context.Emit(system, "walker_reset",
                $"creature={creature.Id} player={player} from={from} refused={(taken ? 0 : 1)}"));

    /// <summary>The reset for a caller holding only the hooks, a trigger action's.</summary>
    public static void Reset(
        in StateHookContext hooks, WorldState world, long tick, PlayerId player,
        Action<CreatureState, int, bool>? report = null)
    {
        // Chosen first: a kick runs the leave and enter hooks, which must not edit the list being walked.
        List<CreatureState>? walkers = null;
        foreach (CreatureState creature in world.OwnedCreatures(player))
            if (Resets(creature)) (walkers ??= []).Add(creature);
        if (walkers is null) return;

        foreach (CreatureState creature in walkers)
        {
            int from = creature.Dk2StateId;
            creature.StopMoving();
            bool taken = CreatureStateMachine.TrySetState(hooks, creature, CreatureStateMachine.Idle, tick);
            report?.Invoke(creature, from, taken);
        }
    }
}
