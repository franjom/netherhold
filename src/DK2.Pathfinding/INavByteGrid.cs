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

    public static bool IsSolid(byte nav) => (nav & Solid) != 0;
    public static int Category(byte nav) => nav & 3;
}
