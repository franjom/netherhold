// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>Whether a room has a place free: DK2's capacity test <c>0x004E53B0</c>, the claim
/// <c>0x004E5A70</c> a work category's enter hook makes and the release <c>0x004E5E00</c> its
/// leave hook makes (ADR-0205, ADR-0309, <c>state-category-hooks.md</c> §3.5).</summary>
/// <remarks>The test admits while <c>UsedCapacity + units ≤ TotalCapacity</c>. <c>UsedCapacity</c>
/// is the sum of the <see cref="CreatureState.HeldPlace"/>s in the room, so it moves only when a
/// place is claimed or released. Only the rooms whose
/// <see cref="AutoFurnitureRules.TryTotalCapacity"/> is read are limited. The Workshop's extra is
/// built (ADR-0214); the Library's and the Guard Room's are not.</remarks>
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

    /// <summary>The room's <c>UsedCapacity</c> (<c>+0x6B</c>): the units of the places held in
    /// <paramref name="room"/>, leaving out <paramref name="exceptId"/>'s.</summary>
    public static int Occupants(WorldState world, RoomInstance room, int exceptId = -1)
    {
        int used = 0;
        foreach (CreatureState c in world.Creatures)
        {
            if (c.Id != exceptId && c.HeldPlace.In(room)) used += c.HeldPlace.Units;
        }

        return used;
    }

    /// <summary><c>0x004E53B0</c>: true while <paramref name="units"/> places are free, and
    /// always for a room whose capacity is not read. A Workshop must also have space for its
    /// crates (<c>0x004F6140</c>, <see cref="CrateRules.WorkshopHasRoom"/>).</summary>
    public static bool HasRoom(
        WorldState world, GameDatabase database, RoomInstance room, int exceptId = -1, int units = Units)
    {
        if (room.Definition.TypeId == RoomTypeId.Workshop && !CrateRules.WorkshopHasRoom(world, database, room))
            return false;
        if (!AutoFurnitureRules.TryTotalCapacity(world, room, out int capacity)) return true;
        return Occupants(world, room, exceptId) + units <= capacity;
    }

    /// <summary>The holder of a place in <paramref name="room"/> with the lowest id, or null.
    /// DK2 takes the head of the room's list, the latest claimant (the claim inserts at the head).</summary>
    public static CreatureState? FirstOccupant(WorldState world, RoomInstance room)
    {
        foreach (CreatureState c in world.Creatures)
        {
            if (c.HeldPlace.In(room)) return c;
        }

        return null;
    }

    /// <summary>A claiming enter hook's room lookup and claim (<c>0x0047AFB0</c>,
    /// <c>0x004E7F80</c>, <c>0x004E5A70</c>). False refuses the enter, which leaves the creature
    /// in state 1: no room underfoot, another keeper's room of a type 0x004E7F80 does not admit,
    /// or no place free. Only the last raises message <c>0x2A</c>, and only when
    /// <see cref="AnotherHasRoom"/> finds none.</summary>
    internal static bool Claim(in StateHookContext hooks, CreatureState creature)
    {
        WorldState world = hooks.World;
        if (!world.Contains(creature.Tile)) return false;
        int tile = (creature.Tile.Y * world.Width) + creature.Tile.X;
        RoomInstance? room = RoomIndex.RoomAt(world, hooks.Database, tile);
        if (room is null || !Admits(room, creature)) return false;

        // A Combat Pit fighter links in without the test or the units. The Prison's position
        // tests (0x00453A20(11), 0x00454110) and its overcrowding roll are not built (Q504).
        if (room.Definition.TypeId == RoomTypeId.CombatPit && creature.CombatPitFighter)
        {
            creature.HeldPlace = new RoomPlace(tile, 0);
            return true;
        }

        if (!HasRoom(world, hooks.Database, room, creature.Id)) return false;
        creature.HeldPlace = new RoomPlace(tile, Units);
        return true;
    }

    /// <summary>The release <c>0x004E5E00</c>, behind the leave hooks' <c>+0x3B9</c> test.</summary>
    /// <remarks>DK2 subtracts the definition's units only while the room's count holds them, so a
    /// fighter's release takes a spectator's unit; ours frees what the place added (Q504).</remarks>
    internal static void Release(CreatureState creature) => creature.HeldPlace = RoomPlace.None;

    /// <summary><c>0x004E7F80</c>: the creature's own keeper's room, or any keeper's Prison,
    /// Torture Chamber, Temple, Combat Pit or Crypt.</summary>
    private static bool Admits(RoomInstance room, CreatureState creature) =>
        room.OwnerPlayerId == creature.OwnerPlayerId
        || room.Definition.TypeId is RoomTypeId.Prison or RoomTypeId.TortureChamber or RoomTypeId.Temple
            or RoomTypeId.CombatPit or RoomTypeId.Crypt;

    /// <summary>The claim's fallback <c>0x004C6AE0</c>: whether another of the keeper's rooms
    /// of <paramref name="full"/>'s type has a place; <paramref name="full"/> itself fails the
    /// same test, so it need not be skipped. It only decides whether message
    /// <c>0x2A</c> is raised; either way the creature goes to state 1. Ours skips its
    /// reachability test (<c>0x004D0E60</c>) and its preference for rooms none of whose
    /// occupants the alignment objection test <c>0x0045C640</c> objects to, neither changing
    /// the answer's use.</summary>
    public static bool AnotherHasRoom(
        WorldState world, GameDatabase database, RoomInstance full, int exceptId)
    {
        foreach (RoomInstance room in RoomIndex.Build(world, database))
        {
            if (room.OwnerPlayerId != full.OwnerPlayerId || room.Definition.TypeId != full.Definition.TypeId)
                continue;
            if (room.TileIndices.Count == 0) continue;
            if (HasRoom(world, database, room, exceptId)) return true;
        }

        return false;
    }
}
