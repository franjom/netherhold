// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>Guard duty's geometry: the post a guard walks to (state 97) and the legs it walks
/// round the room's edge (state 98), from <c>guard-room.md</c> (ADR-0212).</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/guard-room.md")]
public static class GuardRules
{
    /// <summary>State 97, to the nearest post.</summary>
    public const int ToThePostState = 97;

    /// <summary>State 98, round the edge.</summary>
    public const int PatrolState = 98;

    /// <summary>State 99, a failed leg: wait a turn, then 97 again.</summary>
    public const int LostState = 99;

    /// <summary>State 100, arrived at a Guard Post: back to the room.</summary>
    public const int AtThePostState = 100;

    /// <summary>No post slot (<c>0x00462BB0</c>'s miss).</summary>
    public const int NoSlot = 8;

    /// <summary>The nine <c>(mask, facing)</c> pairs at <c>0x00698540</c>; the mask is the
    /// tile's neighbours on the keeper's Guard Room, west 1, east 2, north 4, south 8.</summary>
    private static readonly (int Mask, int Facing)[] Slots =
        [(0, 1), (1, 1), (2, 0), (4, 3), (8, 1), (5, 2), (9, 1), (6, 3), (10, 0)];

    /// <summary>The dwords from <c>0x00698588</c>: eight <c>(x, y)</c> offsets, one per facing,
    /// then the first word of the source-path string that follows (<c>0x006985C8</c>).</summary>
    private static readonly int[] Offsets =
        [0x600, 0x600, 0xA00, 0x600, 0xA00, 0xA00, 0x600, 0xA00, 0x600, 0x600, 0xA00, 0x600, 0xA00, 0xA00, 0x600, 0xA00, 0x445C3A44];

    /// <summary>Whether (x, y) is a tile of <paramref name="owner"/>'s Guard Room.</summary>
    public static bool IsOwnGuardRoom(WorldState world, GameDatabase database, int x, int y, byte owner)
    {
        if (!world.Contains(new TilePos(x, y))) return false;
        ref TileState tile = ref world.Tile(x, y);
        return tile.OwnerPlayerId == owner && RoomRules.RoomOn(database, tile)?.RoomId == (byte)RoomTypeId.GuardRoom;
    }

    /// <summary><c>0x00462BB0</c>: the post slot of (x, y), or <see cref="NoSlot"/>. The tile
    /// may hold any room; the mask counts the keeper's Guard Room around it.</summary>
    public static int Slot(WorldState world, GameDatabase database, int x, int y, byte owner, out int facing)
    {
        facing = 0;
        if (!world.Contains(new TilePos(x, y)) || RoomRules.RoomOn(database, world.Tile(x, y)) is null) return NoSlot;
        int mask = (IsOwnGuardRoom(world, database, x - 1, y, owner) ? 1 : 0)
                   | (IsOwnGuardRoom(world, database, x + 1, y, owner) ? 2 : 0)
                   | (IsOwnGuardRoom(world, database, x, y - 1, owner) ? 4 : 0)
                   | (IsOwnGuardRoom(world, database, x, y + 1, owner) ? 8 : 0);
        for (int i = 0; i < Slots.Length; i++)
        {
            if (Slots[i].Mask != mask) continue;
            facing = Slots[i].Facing;
            return i;
        }

        return NoSlot;
    }

    /// <summary>State 97's square spiral from (x, y): the first tile whose slot is not
    /// <see cref="NoSlot"/>, or (0, 0) once a leg reaches twice the map's larger side.</summary>
    public static TilePos NearestPost(WorldState world, GameDatabase database, int x, int y, byte owner)
    {
        if (IsPost(world, database, x, y, owner)) return new TilePos(x, y);
        int limit = 2 * Math.Max(world.Width, world.Height);
        int step = 1, length = 1;
        x++;
        while (true)
        {
            for (int i = 0; i < length; i++, y += step)
                if (IsPost(world, database, x, y, owner)) return new TilePos(x, y);
            length++;
            step = -step;
            for (int i = 0; i < length; i++, x += step)
                if (IsPost(world, database, x, y, owner)) return new TilePos(x, y);
            if (length >= limit) return new TilePos(0, 0);
        }
    }

    private static bool IsPost(WorldState world, GameDatabase database, int x, int y, byte owner) =>
        world.Contains(new TilePos(x, y)) && Slot(world, database, x, y, owner, out _) != NoSlot;

    /// <summary>State 97's target for a post of <paramref name="slot"/>: the offsets are read
    /// four bytes into <see cref="Offsets"/>, so slot 7's y is the source-path string's first
    /// word and lands off the map. Null for the miss, whose pair lies wholly in the string.</summary>
    public static (int X, int Y)? PostPoint(TilePos post, int slot)
    {
        if (slot >= NoSlot) return null;
        return ((post.X << 12) | Offsets[(2 * slot) + 1], (post.Y << 12) | Offsets[(2 * slot) + 2]);
    }

    /// <summary>State 98's target for a leg ending on <paramref name="at"/> with
    /// <paramref name="facing"/>: the quarter of the tile against its wall.</summary>
    public static (int X, int Y) LegPoint(TilePos at, int facing) =>
        ((at.X << 12) | Offsets[2 * facing], (at.Y << 12) | Offsets[(2 * facing) + 1]);

    /// <summary>One leg of state 98's left-hand wall follow from (x, y) with
    /// <paramref name="facing"/>: the tile it ends on and the new facing.</summary>
    /// <remarks>DK2's loop has no bound; ours stops after as many steps as the map has tiles,
    /// which a room cannot reach.</remarks>
    public static (TilePos To, int Facing) Leg(WorldState world, GameDatabase database, int x, int y, int facing, byte owner)
    {
        (int dx, int dy, int wx, int wy, int blocked, int opens) = Heading(facing);
        for (int n = 0; n < world.Width * world.Height; n++)
        {
            if (!IsOwnGuardRoom(world, database, x + dx, y + dy, owner)) return (new TilePos(x, y), blocked);
            x += dx;
            y += dy;
            if (IsOwnGuardRoom(world, database, x + wx, y + wy, owner)) return (new TilePos(x, y), opens);
        }

        return (new TilePos(x, y), facing);
    }

    /// <summary>The jump table at <c>0x00463120</c>: step, wall side, and the facings for a
    /// blocked step and an opening in the wall.</summary>
    private static (int Dx, int Dy, int Wx, int Wy, int Blocked, int Opens) Heading(int facing) => facing switch
    {
        0 or 5 => (1, 0, 0, -1, 1, 4),
        1 or 6 => (0, 1, 1, 0, 2, 5),
        2 or 7 => (-1, 0, 0, 1, 3, 6),
        _ => (0, -1, -1, 0, 0, 7),
    };
}
