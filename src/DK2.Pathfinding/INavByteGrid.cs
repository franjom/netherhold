// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Pathfinding;

/// <summary>The navigation-cache byte of every tile, which is the only thing DK2's mesh builder
/// reads (docs/research/navigation-mesh-and-search.md §2.1, structure-blocks.md §5.4).</summary>
/// <remarks>Values as the original writes them: <c>8</c> solid, <c>1</c> water, <c>3</c> lava,
/// <c>0</c> floor; a door adds its owner in the high nibble and two state bits. Two tiles with
/// different bytes always have a constraint edge between them; a run of one byte never does.
/// That single fact decides where every gate, and so every turn in a route, falls.</remarks>
public interface INavByteGrid
{
    int Width { get; }
    int Height { get; }
    byte NavByte(int x, int y);
}

/// <summary>The navigation categories the router distinguishes, as the cost function reads them
/// (navigation-mesh-and-search.md §4): the low two bits of the byte, and bit 3 for solid.</summary>
public static class NavBytes
{
    public const byte Floor = 0;
    public const byte Water = 1;
    public const byte Lava = 3;
    public const byte Solid = 8;

    /// <summary>The marker the mesh writes into a dead triangle's byte.</summary>
    public const byte Dead = 0xFF;

    /// <summary>Bit 2 of a door's byte: its lock mode is not zero (`0x004A66F0`).</summary>
    public const byte DoorLocked = 4;

    /// <summary>The same bit on a plain tile: a portal's centre hole. DK2's cache (captured on
    /// Level 7, 2026-09-17) holds 4 on exactly the two portal centres and plain floor on every
    /// heart tile; the cost function crosses a bit-2 boundary only for a mover whose state passed
    /// option bit 2, which is how a leaving creature enters the hole.</summary>
    public const byte PortalHole = 4;

    /// <summary>Bit 1 of a door's byte: the tile's own flag bit 0, the one the task pool reads
    /// as "tagged" (`0x00450FC0`); a foreign door carrying it stops every mover.</summary>
    public const byte DoorTagged = 2;

    public static bool IsSolid(byte nav) => (nav & Solid) != 0;
    public static int Category(byte nav) => nav & 3;

    /// <summary>A closed, completed door: the owner's id in the high nibble, so never zero.</summary>
    public static bool IsDoor(byte nav) => (nav & 0xF0) != 0;
    public static int Owner(byte nav) => nav >> 4;

    /// <summary>The byte `0x004A66F0` writes for a closed door; an open one writes plain floor.</summary>
    public static byte Door(byte owner, bool locked, bool tagged) =>
        (byte)(((owner & 0xF) << 4) | (locked ? DoorLocked : 0) | (tagged ? DoorTagged : 0));
}
