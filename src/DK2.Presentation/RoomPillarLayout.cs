// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
namespace DK2.Presentation;
/// <summary>The common room-pillar pass shared by decorative drawing and roof closure
/// (furniture-placement.md section 5; the simulation still keeps these pillars virtual).</summary>
internal static class RoomPillarLayout
{
    internal static ObjectTypeId? ObjectType(RoomTypeId? type) => type switch
    {
        RoomTypeId.Treasury => new(76),
        RoomTypeId.Hatchery => new(78),
        RoomTypeId.CombatPit => new(79),
        RoomTypeId.TortureChamber => new(82),
        RoomTypeId.Graveyard => new(84),
        RoomTypeId.Casino => new(85),
        RoomTypeId.Crypt => new(141),
        _ => null,
    };
    internal static void MarkClosed(WorldState world, GameDatabase database, Span<bool> closed)
    {
        foreach (RoomInstance room in RoomIndex.Build(world, database))
        {
            if (!ObjectType(room.Definition.TypeId).HasValue)
            {
                continue;
            }

            foreach (int index in room.TileIndices)
            {
                foreach (int _ in AutoFurnitureRules.RoomPillarCorners(world, room, new TilePos(index % world.Width, index / world.Width)))
                {
                    closed[index] = true;
                    break;
                }
            }
        }
    }
}
