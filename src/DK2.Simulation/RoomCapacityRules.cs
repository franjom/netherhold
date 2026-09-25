// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>Whether a room has a place free: DK2's capacity test <c>0x004E53B0</c> and the
/// occupants its enter and leave hooks count (ADR-0205, <c>state-category-hooks.md</c> §3.5).</summary>
/// <remarks>The test admits while <c>UsedCapacity + units ≤ TotalCapacity</c>. The job
/// handlers' room finders ask it, and so does the claim a work state's enter hook makes
/// (<c>0x004E5A70</c>). Ours derives <c>UsedCapacity</c> from the claiming creatures standing
/// in the room rather than counting it, and limits only the rooms whose
/// <see cref="AutoFurnitureRules.TryTotalCapacity"/> is read; the Library, Workshop and
/// Guard Room extras are not modelled.</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/state-category-hooks.md")]
public static class RoomCapacityRules
{
    /// <summary>The places one creature takes, the definition's <c>+0x672</c> byte (creature
    /// file <c>0xF4D</c>): 1 for all 46 shipped creatures.</summary>
    public const int Units = 1;

    /// <summary>The categories whose enter hook claims a place: Research and Guard Post
    /// (<c>0x0047AFB0</c>), Manufacture, Temple, Training, Prison, Torture, Combat Pit, Casino
    /// and Spectating.</summary>
    public static bool Claims(CreatureStateCategory category) =>
        category is CreatureStateCategory.Research or CreatureStateCategory.Manufacture
            or CreatureStateCategory.GuardPost or CreatureStateCategory.Temple
            or CreatureStateCategory.Training or CreatureStateCategory.Prison
            or CreatureStateCategory.Torture or CreatureStateCategory.CombatPit
            or CreatureStateCategory.Casino or CreatureStateCategory.Spectating;

    /// <summary>The places taken in <paramref name="room"/>, leaving out
    /// <paramref name="exceptId"/>.</summary>
    public static int Occupants(WorldState world, RoomInstance room, int exceptId = -1)
    {
        int used = 0;
        foreach (CreatureState c in world.Creatures)
        {
            if (c.Id == exceptId || !CreatureStateMachine.HasState(c) || HandRules.IsHeld(c)) continue;
            if (!Claims(CreatureStateTable.Get(c.Dk2StateId).Category)) continue;
            if (EatingRules.Contains(room, (c.Tile.Y * world.Width) + c.Tile.X)) used += Units;
        }

        return used;
    }

    /// <summary><c>0x004E53B0</c>: true while <paramref name="units"/> places are free, and
    /// always for a room whose capacity is not read.</summary>
    public static bool HasRoom(WorldState world, RoomInstance room, int exceptId = -1, int units = Units)
    {
        if (!AutoFurnitureRules.TryTotalCapacity(world, room, out int capacity)) return true;
        return Occupants(world, room, exceptId) + units <= capacity;
    }

    /// <summary>The claim's fallback <c>0x004C6AE0</c>: whether another of the keeper's rooms
    /// of <paramref name="full"/>'s type has a place; <paramref name="full"/> itself fails the
    /// same test, so it need not be skipped. It only decides whether message
    /// <c>0x2A</c> is raised; either way the creature goes to state 1. Ours skips its
    /// reachability test (<c>0x004D0E60</c>) and its preference for rooms none of whose
    /// occupants pass the unread <c>0x0045C640</c>, neither changing the answer's use.</summary>
    public static bool AnotherHasRoom(
        WorldState world, GameDatabase database, RoomInstance full, int exceptId)
    {
        foreach (RoomInstance room in RoomIndex.Build(world, database))
        {
            if (room.OwnerPlayerId != full.OwnerPlayerId || room.Definition.TypeId != full.Definition.TypeId)
                continue;
            if (room.TileIndices.Count == 0) continue;
            if (HasRoom(world, room, exceptId)) return true;
        }

        return false;
    }
}
