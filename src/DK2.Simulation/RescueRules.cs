// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>Freeing comrades from a Torture Chamber: DK2's state 134 (ADR-0201,
/// <c>state-category-hooks.md</c> §3.3).</summary>
/// <remarks>The creature update <c>0x00488930</c>, on the turn a creature steps onto a room
/// tile it is not registered in, calls <c>0x004E8660</c>. In an enemy keeper's Torture
/// Chamber that holds an ally of its on a device, a non-worker whose <c>Flags</c> bit 5 is set
/// goes to 134. Only a level's creature records and triggers set the bit; a portal creature
/// never has it. 134 (<c>0x00484950</c>) waits half of variable 19 in turns, then frees an
/// ally on its own tile (the victim's 133, then idle) and goes idle itself, or walks to the
/// nearest one in the chamber resuming 134, or idles.</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/state-category-hooks.md")]
public static class RescueRules
{
    /// <summary>The creature <c>Flags</c> bit (<c>+0x46F</c>) the rescue asks for. The name is
    /// ours, from what it does; the level loader copies it from the record's own bit 5.</summary>
    public const byte FreesFriendsFlag = 0x20;

    /// <summary>134, freeing comrades: group 1, category 0.</summary>
    public const int RescuingState = 134;

    /// <summary>133, the freed victim's state: it resets the animation and returns 0, idle.</summary>
    public const int FreedState = 133;

    /// <summary>Turns in 134 before it acts: variable 19 halved, <c>+0x1C</c> against it.</summary>
    public const int WaitTurns = SimulationTiming.OriginalTicksPerGameSecond >> 1;

    /// <summary>The record bits the loader copies into <c>Flags</c> (<c>0x00520391</c> on):
    /// a good creature's all eight, a neutral or keeper creature's all but bits 1 and 2.</summary>
    public const byte GoodCreatureFlagMask = 0xFF;

    /// <inheritdoc cref="GoodCreatureFlagMask"/>
    public const byte OtherCreatureFlagMask = 0xF9;

    /// <summary>Whether stepping onto <paramref name="tileIndex"/> sends this creature to 134:
    /// <c>0x004E8660</c>'s Torture Chamber arm.</summary>
    public static bool StartsARescue(WorldState world, GameDatabase database, CreatureState creature, int tileIndex) =>
        StateFor(world, database, creature, tileIndex) == RescuingState;

    /// <summary><c>0x004E8660</c>: 134 for its Torture Chamber arm, 122 for its Prison arm
    /// (<see cref="JailBreakRules.StartsABreakOut"/>, ADR-0229), or null.</summary>
    public static int? StateFor(WorldState world, GameDatabase database, CreatureState creature, int tileIndex)
    {
        if ((creature.Flags & FreesFriendsFlag) == 0) return null;
        if ((database.CreatureById(creature.CreatureTypeId).Flags & CreatureFlags.IsWorker) != 0) return null;

        RoomInstance? room = RoomIndex.RoomAt(world, database, tileIndex);
        if (room is null) return null;
        if (room.Definition.TypeId == RoomTypeId.Prison)
            return JailBreakRules.StartsABreakOut(world, database, creature, room) ? JailBreakRules.BreakingOutState : null;
        if (room.Definition.TypeId != RoomTypeId.TortureChamber) return null;
        if (world.AreAllied(creature.OwnerPlayerId, room.OwnerPlayerId)) return null;
        return AlliedVictims(world, database, creature, room).Any() ? RescuingState : null;
    }

    /// <summary>The creatures on this chamber's devices that are allies of the rescuer, in
    /// creature-list order. DK2 walks the room's attached list and skips the dying.</summary>
    public static IEnumerable<CreatureState> AlliedVictims(
        WorldState world, GameDatabase database, CreatureState rescuer, RoomInstance room)
    {
        foreach (CreatureState victim in world.Creatures)
        {
            if (victim.Id == rescuer.Id || !TortureRules.IsBeingTortured(victim) || HandRules.IsHeld(victim)) continue;
            if (CreatureDeathRules.IsDown(victim) || victim.Dk2StateId == FreedState) continue;
            if (TortureRules.KeeperOf(victim) != room.OwnerPlayerId) continue;
            if (!EatingRules.Contains(room, (victim.Tile.Y * world.Width) + victim.Tile.X)) continue;
            if (world.AreAllied(rescuer.OwnerPlayerId, victim.OwnerPlayerId)) yield return victim;
        }
    }
}
