// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>A room-list lifetime, with a tile fallback for untracked fixture rooms.</summary>
public readonly record struct RoomReference(RoomListPosition Position, TilePos Tile)
{
    public static readonly RoomReference None = new(default, new TilePos(-1, -1));
    public bool IsNone
    {
        get
        {
            return Tile.X < 0;
        }
    }

    public static RoomReference Capture(WorldState world, RoomInstance room)
    {
        int first = room.TileIndices[0];
        return new(world.TileAtIndex(first).RoomListPosition, new TilePos(first % world.Width, first / world.Width));
    }

    public bool RefersTo(WorldState world, RoomInstance room)
    {
        return !IsNone &&
        (Position.Value != 0 ? world.TileAtIndex(room.TileIndices[0]).RoomListPosition == Position
            : world.Contains(Tile) && room.Contains(world.TileIndex(Tile)));
    }

    public RoomInstance? Resolve(WorldState world, GameDatabase database, PlayerId owner, RoomTypeId type)
    {
        foreach (RoomInstance room in RoomIndex.Build(world, database))
        {
            if (room.OwnerPlayerId == owner && room.Definition.TypeId == type && RefersTo(world, room))
            {
                return room;
            }
        }

        return null;
    }
}
