// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>
/// A room's wall phases, <c>0x004ECEB0</c> (room-floors-and-liquids.md §5): walking the slab chain,
/// the first straight wall on a north or south side sets the north-south phase (room <c>+0x92</c>)
/// to x &amp; 1, on an east or west side the east-west phase (<c>+0x96</c>) to 1 − (y &amp; 1); −1
/// while none is found. Bridges take 0 and 0. The decorated wall pieces and, in a Torture Chamber or
/// Casino, the torches read them (wall-torches.md §2).
/// <para>Deviates: DK2 keeps a phase once set while the room grows, and the level start turns a
/// level room's −1 into 0; this finds it afresh from the room as it stands.</para>
/// </summary>
public static class RoomWallPhases
{
    private static readonly int[] Dx8 = [0, 1, 1, 1, 0, -1, -1, -1];
    private static readonly int[] Dy8 = [-1, -1, 0, 1, 1, 1, 0, -1];

    public static (int NorthSouth, int EastWest) Of(WorldState world, GameDatabase database, RoomInstance room)
    {
        if (BridgeRules.IsBridge(room.Definition)) return (0, 0);
        int northSouth = -1, eastWest = -1, width = world.Width;
        foreach (int tile in SlabChain.Order(world, room))
        {
            int x = tile % width, y = tile / width;
            for (int side = 0; side < 4; side++)
            {
                if (!StraightWall(world, database, room, x, y, side)) continue;
                if ((side & 1) == 0) { if (northSouth < 0) northSouth = x & 1; }
                else if (eastWest < 0) eastWest = 1 - (y & 1);
            }
            if (northSouth >= 0 && eastWest >= 0) break;
        }
        return (northSouth, eastWest);
    }

    /// <summary>A straight wall on a side: the tile outside AllowRoomWalls, the two tiles along the
    /// wall in this room, and both diagonals outside solid and AllowRoomWalls.</summary>
    private static bool StraightWall(WorldState world, GameDatabase database, RoomInstance room, int x, int y, int side)
    {
        int d = side * 2;
        return WallTile(world, database, x + Dx8[d], y + Dy8[d], solid: false)
            && InRoom(world, room, x + Dx8[(d + 6) & 7], y + Dy8[(d + 6) & 7])
            && InRoom(world, room, x + Dx8[(d + 2) & 7], y + Dy8[(d + 2) & 7])
            && WallTile(world, database, x + Dx8[(d + 7) & 7], y + Dy8[(d + 7) & 7], solid: true)
            && WallTile(world, database, x + Dx8[(d + 1) & 7], y + Dy8[(d + 1) & 7], solid: true);
    }

    private static bool WallTile(WorldState world, GameDatabase database, int x, int y, bool solid)
    {
        if (!world.Contains(x, y)) return false;
        TerrainDef terrain = database.TerrainById(world.Tile(x, y).TerrainId);
        return ((terrain.Flags & TerrainFlags.AllowRoomWalls) == TerrainFlags.AllowRoomWalls) && (!solid || terrain.IsSolid);
    }

    private static bool InRoom(WorldState world, RoomInstance room, int x, int y) =>
        world.Contains(x, y) && room.Contains((y * world.Width) + x);
}
