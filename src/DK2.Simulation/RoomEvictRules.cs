// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation;

/// <summary>A room's type slots +0x05 (freed), +0x09 (tiles changed) and +0x0D (owner changed) kick
/// every place-holder to the type's return state: a Temple's to 107 (<c>0x004F15D0</c>), a Prison's
/// to 126 (<c>0x004E2B30</c>, <c>0x004E2C00</c>, <c>0x004E2C30</c>), a Training Room's to 113
/// (<c>0x004F2EA0</c>, <c>0x004F2F20</c>). Each waits a turn and goes back in (temple-states.md §7,
/// refused-states.md).</summary>
/// <remarks>Ours sees the change at the holder's next turn, by the room's size stamp
/// (<see cref="SlabChain.SizeChangedTick"/>) against the one it held when it claimed its place
/// (<see cref="CreatureState.HeldPlaceStamp"/>), not inside the rebuild. A freed room or a new owner
/// does not stamp; the job's room test or the prison's own release takes those.</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/refused-states.md")]
public static class RoomEvictRules
{
    /// <summary>The room's return state for its holders, or null for a type with no such slot built.</summary>
    public static int? ReturnStateOf(RoomTypeId type) => type switch
    {
        RoomTypeId.Temple => TempleStep.ReturningState,
        RoomTypeId.Prison => PrisonRules.ReturningState,
        RoomTypeId.TrainingRoom => TrainingRules.ReturningState,
        _ => null,
    };

    /// <summary>Kicks the creature to its room's return state when the room was rebuilt since its
    /// claim; true when it was kicked.</summary>
    public static bool Evict(SimulationContext context, string system, CreatureState creature)
    {
        if (!creature.HeldPlace.IsHeld) return false;
        WorldState world = context.World;
        if (RoomIndex.RoomAt(world, context.Database, creature.HeldPlace.TileIndex) is not { } held
            || held.Definition.TypeId is not RoomTypeId type || ReturnStateOf(type) is not int state
            || creature.Dk2StateId == state
            || SlabChain.SizeChangedTick(world, held) == creature.HeldPlaceStamp)
            return false;
        context.Emit(system, "room_evicted", $"creature={creature.Id} room={held.Definition.TypeId} to={state}");
        creature.StopMoving();
        return StateShadow.TryMove(context, system, creature, state);
    }
}
