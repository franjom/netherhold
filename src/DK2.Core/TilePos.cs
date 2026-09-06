// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Core;

/// <summary>
/// A tile coordinate on the dungeon grid.
///
/// DK2 addresses tiles with small integer coordinates (CONFIRMED: Flame decodes the
/// position structs its room record uses, the first slab among them, as byte pairs
/// and ushort pairs).
/// </summary>
public readonly record struct TilePos(int X, int Y)
{
    public static TilePos operator +(TilePos a, TilePos b) => new(a.X + b.X, a.Y + b.Y);
    public static TilePos operator -(TilePos a, TilePos b) => new(a.X - b.X, a.Y - b.Y);

    /// <summary>Chebyshev distance — adjacency including diagonals.</summary>
    public int ChebyshevDistance(TilePos other) =>
        Math.Max(Math.Abs(X - other.X), Math.Abs(Y - other.Y));

    public int ManhattanDistance(TilePos other) =>
        Math.Abs(X - other.X) + Math.Abs(Y - other.Y);

    public override string ToString() => $"({X},{Y})";
}
