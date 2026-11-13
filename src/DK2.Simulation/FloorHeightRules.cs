// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>DK2's floor height under a point (<c>Tile_FloorHeight</c>, <c>0x004545C0</c>), in the
/// world's 1/4096 units with the ordinary floor at <see cref="Ordinary"/>: a Portal's or the Heart's
/// sunken centre, a bridge's deck, a room's floor slot and its rim zones, and a liquid's banks.
/// Thing_SetPosition (<c>0x004B5020</c>) stores it in a thing's floor word
/// <c>+0x26</c>; ours asks it from the point when needed, which is the same value while every position
/// write goes through that setter, as native's do (Q795).</summary>
/// <remarks>Native reads the cell's cached piece and neighbour masks (<c>+0xC</c>, kept by
/// <c>0x00450470</c>); ours derives them from the neighbouring tiles. Approximations: a Portal's piece 4
/// is its four orthogonal neighbours of the same terrain and owner (native also asks the same room
/// thing), the Heart's centre piece is the tile whose 5 x 5 square is all Heart, and the floor slot's per-type rule
/// (<c>0x00454110</c>) is the interior test alone (a Graveyard cell holding a crypt is not refused).</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/creature-height.md")]
public static class FloorHeightRules
{
    /// <summary>The ordinary floor, one tile above zero.</summary>
    public const int Ordinary = 0x1000;

    /// <summary>A bridge's deck (room types 8, 17 and 22).</summary>
    public const int Bridge = 0x1199;

    /// <summary>Open liquid, away from any bank.</summary>
    public const int Liquid = 0xA66;

    // Neighbour bit k, clockwise from north (0x006BC370): N, NE, E, SE, S, SW, W, NW.
    private static readonly int[] Dx = [0, 1, 1, 1, 0, -1, -1, -1];
    private static readonly int[] Dy = [-1, -1, 0, 1, 1, 1, 0, -1];

    /// <summary>The floor height at world point (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public static int At(WorldState world, GameDatabase database, int x, int y)
    {
        var at = new TilePos(x >> 12, y >> 12);
        if (!world.Contains(at)) return Ordinary;
        TileState tile = world.Tile(at);
        if (database.RoomByTerrain(tile.TerrainId) is { } room) return RoomFloor(world, database, room, at, x, y);

        TerrainDef terrain = database.TerrainById(tile.TerrainId);
        if (!terrain.IsWater && !terrain.IsLava) return Ordinary;
        return BankFloor(world, database, tile, at, x, y);
    }

    /// <summary><see cref="At"/> less the ordinary floor: the floor in the frame of
    /// <see cref="CreatureState.PosZ"/>, whose zero is the ordinary floor.</summary>
    public static int RelativeAt(WorldState world, GameDatabase database, int x, int y) =>
        At(world, database, x, y) - Ordinary;

    private static int RoomFloor(WorldState world, GameDatabase database, RoomDef room, TilePos at, int x, int y)
    {
        if (BridgeRules.IsBridge(room)) return Bridge;
        TileState tile = world.Tile(at);
        switch (room.TypeId)
        {
            case RoomTypeId.Portal when IsPortalCentre(world, tile, at):
                return 0;
            case RoomTypeId.DungeonHeart when IsHeartCentre(world, tile, at):
                return 0;
        }

        int mask = SameRoomMask(world, tile, at);
        // The floor slot (0x00454110): an interior cell, every neighbour of the same room.
        if (mask == 0xFF && room.TypeId is not (RoomTypeId.Portal or RoomTypeId.DungeonHeart))
        {
            return room.TypeId switch
            {
                RoomTypeId.Hatchery => 0xE00,
                RoomTypeId.Temple => 0,
                RoomTypeId.Graveyard or RoomTypeId.Crypt => 0xF00,
                RoomTypeId.CombatPit => 0x400,
                _ => Ordinary,
            };
        }

        int required = RequiredBits(x & 0xFFF, y & 0xFFF);
        switch (room.TypeId)
        {
            case RoomTypeId.Hatchery or RoomTypeId.Graveyard or RoomTypeId.Crypt:
                return (mask & required) == required ? 0xE00 : Ordinary;
            case RoomTypeId.CombatPit:
                int interior = 0;
                for (int k = 0; k < 8; k++)
                {
                    var next = new TilePos(at.X + Dx[k], at.Y + Dy[k]);
                    if (world.Contains(next) && SameRoom(world, tile, next)
                        && SameRoomMask(world, world.Tile(next), next) == 0xFF)
                        interior |= 1 << k;
                }
                return (interior & required) == 0 ? Ordinary : 0x1200;
            default:
                return Ordinary;
        }
    }

    /// <summary>The neighbour bits a sub-tile zone needs (<c>0x00454735</c>-<c>0x00454949</c>): the
    /// corner zones three, the edge strips one, the centre none.</summary>
    private static int RequiredBits(int fx, int fy) =>
        fx < 0x400 ? fy < 0x400 ? 0xC1 : fy > 0xC00 ? 0x70 : 0x40
        : fx > 0xC00 ? fy < 0x400 ? 0x07 : fy > 0xC00 ? 0x1C : 0x04
        : fy < 0x400 ? 0x01 : fy > 0xC00 ? 0x10 : 0;

    /// <summary>A liquid tile's floor: open liquid, or a bank sloping from the ordinary floor at the
    /// dry side's edge down to <see cref="Liquid"/> at the centre.</summary>
    private static int BankFloor(WorldState world, GameDatabase database, TileState tile, TilePos at, int x, int y)
    {
        int liquid = 0;
        for (int k = 0; k < 8; k++)
        {
            var next = new TilePos(at.X + Dx[k], at.Y + Dy[k]);
            if (!world.Contains(next) || world.Tile(next).TerrainId == tile.TerrainId
                || (BridgeRules.SpannedTerrain(database, world.Tile(next)) is { } under && under.TerrainId == tile.TerrainId))
                liquid |= 1 << k;
        }
        if (liquid == 0xFF) return Liquid;

        int dx = x - CreatureState.CentreOf(at.X), dy = y - CreatureState.CentreOf(at.Y);
        int octant = (Steering.Bearing.Of(dx, dy) >> 8) & 7;
        int cardinal = octant & 6;
        int dry = ~liquid; // bits 8 and up stay set, as native's 32-bit NOT leaves them
        int near = ((7 << cardinal) | (7 >> (8 - cardinal))) & 0xFF;
        if ((dry & near) == 0) return Liquid;
        int ax = Math.Abs(dx), ay = Math.Abs(dy);
        bool larger = (octant & 1) == 0
            ? (dry & (1 << cardinal)) != 0
            : (dry & ((4 << cardinal) | (4 >> (8 - cardinal)))) != 0;
        int distance = larger ? Math.Max(ax, ay) : Math.Min(ax, ay);
        return Liquid + ((distance * 0x59A) >> 11);
    }

    private static bool SameRoom(WorldState world, TileState tile, TilePos next)
    {
        TileState other = world.Tile(next);
        return other.TerrainId == tile.TerrainId && other.OwnerPlayerId == tile.OwnerPlayerId;
    }

    private static int SameRoomMask(WorldState world, TileState tile, TilePos at)
    {
        int mask = 0;
        for (int k = 0; k < 8; k++)
        {
            var next = new TilePos(at.X + Dx[k], at.Y + Dy[k]);
            if (world.Contains(next) && SameRoom(world, tile, next)) mask |= 1 << k;
        }
        return mask;
    }

    private static bool IsPortalCentre(WorldState world, TileState tile, TilePos at)
    {
        foreach (int k in (ReadOnlySpan<int>)[0, 2, 4, 6])
        {
            var next = new TilePos(at.X + Dx[k], at.Y + Dy[k]);
            if (!world.Contains(next) || !SameRoom(world, tile, next)) return false;
        }
        return true;
    }

    /// <summary>The Heart's centre piece (<c>+0xC == 0x40</c>): the tile whose whole 5 x 5 square is the
    /// same room, which only the middle of a Heart has.</summary>
    private static bool IsHeartCentre(WorldState world, TileState tile, TilePos at)
    {
        for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
            {
                var next = new TilePos(at.X + dx, at.Y + dy);
                if (!world.Contains(next) || !SameRoom(world, tile, next)) return false;
            }
        return true;
    }
}
