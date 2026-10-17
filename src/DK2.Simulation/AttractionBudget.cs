// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>DK2's second attraction gate: a per-creature-type budget, not a yes/no.
/// The original builds a value for each type from the keeper's matching rooms, takes
/// <b>one</b> off it for every creature of that type already held, and admits the type
/// only while the value stays above zero (<c>0x004DBF90</c>).
/// <para>Whole numbers here where the original uses 12-bit fixed point: every
/// contribution is an integer shifted left 12 and every subtraction is exactly 1.0, so
/// the comparison is the same one. <c>PortalRules.IsAttracted</c> is the separate
/// prerequisite gate and runs first.</para></summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/binary-entrance.md")]
public static class AttractionBudget
{
    /// <summary>Whether the budget still admits this type for this keeper.</summary>
    public static bool Admits(
        WorldState world, CreatureDef creature,
        IReadOnlyList<RoomInstance> keeperRooms, PlayerId playerId)
    {
        if (creature.Attractions.Count == 0) return true;

        // The builder reads slot 0 alone, so a two-slot creature budgets on its first
        // room and the second room only has to reach its size (PortalRules.IsAttracted).
        CreatureAttraction first = creature.Attractions[0];
        if (first.Present == 0 || first.RoomId == 0) return true;

        // The Lair arm stores a flat 1.0 and jumps past the draw-down (0x004DC18E),
        // so a Lair-attracted type is never capped however many are held. It walks no
        // room list either (0x004DC153), so the type needs no Lair: Level 4's first Goblin.
        if (first.RoomId == (byte)RoomTypeId.Lair) return true;

        int budget = 0;
        for (int i = 0; i < keeperRooms.Count; i++)
        {
            RoomInstance room = keeperRooms[i];
            if (room.Definition.RoomId != first.RoomId) continue;
            budget += Contribution(world, room);
        }

        return budget > PortalRules.CreatureCountOfType(world, playerId, creature.CreatureId);
    }

    /// <summary>What one room adds. Special-cased by room type in the original, which
    /// is why sixteen room source files exist rather than one parameterised room.</summary>
    public static int Contribution(WorldState world, RoomInstance room)
    {
        return room.Definition.TypeId switch
        {
            // 2 only while the Hand stands in it, else nothing: a Temple too small to
            // place one attracts no Dark Angel, which is what the owner saw.
            RoomTypeId.Temple => HoldsTempleHand(world, room) ? 2 : 0,

            // Both land on 0x00454110's arm 9, an unconditional true, but the function
            // tests the cell's interior byte (+0x0C == 0xFF) before its type switch, so
            // only interior cells count and the rim never does (read 2026-09-30).
            RoomTypeId.Prison or RoomTypeId.CombatPit => room.InteriorTileIndices.Count,

            _ => AutoFurnitureRules.TryTotalCapacity(world, room, out int capacity)
                ? capacity
                : 0,
        };
    }

    private static bool HoldsTempleHand(WorldState world, RoomInstance room)
    {
        IReadOnlyList<ObjectState> objects = world.Objects;
        for (int i = 0; i < objects.Count; i++)
        {
            ObjectState thing = objects[i];
            if (thing.ObjectTypeId != AutoFurnitureRules.TempleHandObjectId) continue;
            if (thing.OwnerPlayerId != room.OwnerPlayerId) continue;
            if (room.TileIndices.Contains(thing.TileIndex)) return true;
        }

        return false;
    }
}
