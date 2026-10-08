// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation;

/// <summary>A room's type slots +0x05 (freed), +0x09 (tiles changed) and +0x0D (owner changed), and
/// <c>0x004E44E0</c> for one holder when a neighbouring tile's terrain changes, kick place-holders to
/// the type's return state: Temple 107, Prison 126, Training Room 113, Library 62, Workshop 70,
/// Casino 154. Each waits a turn and goes back in (room-return-states.md §1, the handlers there).</summary>
/// <remarks>Ours sees the change at the holder's next turn, by the room's stamp
/// (<see cref="SlabChain.HoldersStamp"/>) against the one it held when it claimed its place
/// (<see cref="CreatureState.HeldPlaceStamp"/>), which a neighbour's change replaces with
/// <see cref="SlabChain.KickedMark"/> for one holder, and a freed room by the place's tile no longer
/// being one of the type; not inside the change. Not built: the Library's load slot +0x11 (ADR-0406).</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/refused-states.md")]
public static class RoomEvictRules
{
    /// <summary>The room's return state for its holders, or null for a type with no such slot built.</summary>
    public static int? ReturnStateOf(RoomTypeId type) => type switch
    {
        RoomTypeId.Temple => TempleStep.ReturningState,
        RoomTypeId.Prison => PrisonRules.ReturningState,
        RoomTypeId.TrainingRoom => TrainingRules.ReturningState,
        RoomTypeId.Library => ResearchStep.ReturningState,
        RoomTypeId.Workshop => WorkshopStep.ReturningState,
        RoomTypeId.Casino => CasinoStep.ReturningState,
        _ => null,
    };

    /// <summary>The return states' shared body (62 <c>0x00476320</c>, 70 <c>0x00470C10</c>, 154
    /// <c>0x00456FB0</c>): at <c>Age</c> 1 or less 1; then on a tile of its own keeper's room of
    /// <paramref name="type"/> <c>SetState(<paramref name="back"/>)</c> and 1 whether or not it is
    /// taken; anywhere else 0, idle. 62 and 70 end a running action every turn first.</summary>
    internal static bool BackIn(
        SimulationContext context, string system, CreatureState creature, RoomTypeId type, int back, bool endsAction)
    {
        if (endsAction) creature.ActionUntilTick = 0;
        if (context.Tick.Value - creature.StateEnteredTick <= 1) return true;
        WorldState world = context.World;
        if (!world.Contains(creature.Tile)
            || RoomIndex.RoomAt(world, context.Database, (creature.Tile.Y * world.Width) + creature.Tile.X) is not { } room
            || room.Definition.TypeId != type || room.OwnerPlayerId != creature.OwnerPlayerId)
            return false;
        StateShadow.TryMove(context, system, creature, back);
        return true;
    }

    /// <summary>Whether the room of <paramref name="type"/> that the creature holds a place in was
    /// freed, rebuilt, taken or kicked by a neighbour's change since its claim.</summary>
    public static bool Kicked(WorldState world, GameDatabase database, CreatureState creature, RoomTypeId type)
    {
        if (!creature.HeldPlace.IsHeld) return false;
        return RoomIndex.RoomAt(world, database, creature.HeldPlace.TileIndex) is not { } held
            || held.Definition.TypeId != type
            || SlabChain.HoldersStamp(world, held) != creature.HeldPlaceStamp;
    }

    /// <summary>Kicks the creature to the return state of <paramref name="type"/>, the room its job
    /// holds a place in, when that room was <see cref="Kicked"/>; true when it was kicked.</summary>
    public static bool Evict(SimulationContext context, string system, CreatureState creature, RoomTypeId type)
    {
        if (ReturnStateOf(type) is not int state || creature.Dk2StateId == state
            || !Kicked(context.World, context.Database, creature, type))
            return false;
        context.Emit(system, "room_evicted", $"creature={creature.Id} room={type} to={state}");
        creature.StopMoving();
        return StateShadow.TryMove(context, system, creature, state);
    }
}
