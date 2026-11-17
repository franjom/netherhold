// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation;

/// <summary>The two worker bits changed together by prison delivery and task release.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/creature-pickup.md")]
internal static class WorkerPickupRules
{
    public static void SetEnabled(CreatureState creature, bool enabled) =>
        creature.Flags = enabled ? (byte)(creature.Flags | 9) : (byte)(creature.Flags & ~9);

    /// <summary><c>WorkerJob_Leave</c> (<c>0x004647D0</c>): ends a running action, drops what the worker holds
    /// (<see cref="DropHeld"/>), then releases by the state it leaves.</summary>
    public static void OnTaskReleased(in StateHookContext hooks, CreatureState creature)
    {
        if (creature.ClaimAction.Kind != ClaimActionKind.None) CreatureStateMachine.EndAction(creature);
        // The dispatcher chooses the hook from the saved caller, but this hook reads the resume.
        int state = creature.Dk2StateId is 6 or 7 ? creature.UtilityNewState : creature.Dk2StateId;
        DropHeld(in hooks, creature);
        if (state is 43 or 44 or 46 or 49) SetEnabled(creature, true);
    }

    /// <summary>The drop-first arm of <c>0x004647D0</c>, before its release switch: a non-zero <c>+0x3C3</c> calls
    /// <c>Creature_DropCarriedThing</c> (<c>0x0048B3A0</c>, call <c>0x00464815</c>; the gem's slot, ours shares <c>CarriedObjectId</c>) and a
    /// non-zero <c>+0x3C5</c> <c>Creature_DropDraggedThing</c> (<c>0x0048B500</c>, call <c>0x00464827</c>), whatever state the worker leaves for:
    /// a threat flight, a fight, a slap or a stun puts the thing down at the worker's point. A creature put down keeps
    /// the state it had (a collapsed body stays collapsed and its dying clock runs again, as the dragged drop's
    /// <c>+0x70</c> clears its carried flag). Ours also forgets the walk's now meaningless target.</summary>
    private static void DropHeld(in StateHookContext hooks, CreatureState creature)
    {
        WorldState world = hooks.World;
        if (creature.CarriedObjectId >= 0)
        {
            int id = creature.CarriedObjectId;
            if (ObjectCarryRules.DropWhatever(world, creature))
            {
                hooks.Emit("ImpTaskSystem", "object_dropped",
                    $"imp={creature.Id} object={id} at=({creature.Tile.X},{creature.Tile.Y}) reason=left_worker_job");
            }

            creature.TargetTileIndex = -1;
        }

        if (creature.CarriedCreatureId < 0) return;
        if (world.CreatureById(creature.CarriedCreatureId) is { } body)
        {
            CarryRules.DropWhatever(world, creature);
            hooks.Emit("ImpTaskSystem", "body_dropped",
                $"imp={creature.Id} body={body.Id} at=({creature.Tile.X},{creature.Tile.Y}) reason=left_worker_job");
        }
        else creature.CarriedCreatureId = -1;

        creature.TargetBodyId = -1;
        creature.TargetTileIndex = -1;
    }
}
