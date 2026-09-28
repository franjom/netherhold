// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.AI;

public sealed class KeeperRoomView
{
    internal KeeperRoomView(RoomInstance room)
        : this(room.Definition, room.OwnerPlayerId, room.TileIndices,
            room.MinX, room.MinY, room.MaxX, room.MaxY) { }

    internal KeeperRoomView(RoomDef definition, byte ownerPlayerId, IEnumerable<int> tiles,
        int minX, int minY, int maxX, int maxY)
    {
        Definition = definition;
        OwnerPlayerId = ownerPlayerId;
        TileIndices = Array.AsReadOnly(tiles.ToArray());
        MinX = minX;
        MinY = minY;
        MaxX = maxX;
        MaxY = maxY;
    }

    public RoomDef Definition { get; }
    public byte OwnerPlayerId { get; }
    public IReadOnlyList<int> TileIndices { get; }
    public int MinX { get; }
    public int MinY { get; }
    public int MaxX { get; }
    public int MaxY { get; }
}
