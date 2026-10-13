// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

[Flags]
public enum CaptureEdges : byte { None = 0, West = 1, North = 2, East = 4, South = 8, All = 15 }

public enum CaptureEffectStyle { Native, Edges }

public static class CaptureEffectLayout
{
    public const ushort LandEffectId = 5;
    public const ushort RoomEffectId = 8;

    public static CaptureEdges Boundary(WorldState world, GameDatabase database, ViewEffectRequest request)
    {
        if (request.EffectId.Value != RoomEffectId || !world.Contains(request.Tile)) return CaptureEdges.All;
        RoomDef? room = database.RoomByTerrain(world.Tile(request.Tile).TerrainId);
        if (room == null) return CaptureEdges.All;
        PlayerId owner = new(world.Tile(request.Tile).OwnerPlayerId);
        CaptureEdges edges = CaptureEdges.None;
        Check(new(request.Tile.X - 1, request.Tile.Y), CaptureEdges.West);
        Check(new(request.Tile.X, request.Tile.Y - 1), CaptureEdges.North);
        Check(new(request.Tile.X + 1, request.Tile.Y), CaptureEdges.East);
        Check(new(request.Tile.X, request.Tile.Y + 1), CaptureEdges.South);
        return edges;

        void Check(TilePos neighbour, CaptureEdges edge)
        {
            if (!world.Contains(neighbour)
                || database.RoomByTerrain(world.Tile(neighbour).TerrainId)?.TypeId != room.TypeId
                || world.Tile(neighbour).OwnerPlayerId != owner.Value) edges |= edge;
        }
    }
}
