// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;

namespace DK2.Presentation;

/// <summary>
/// The wall pieces a room tile draws against its four sides, as DK2's room wall chooser picks
/// them (<c>0x00444F10</c>, wall-pieces.md, Q71). A side is a whole straight piece (7, or 8 the
/// decorated one on the room's wall phase, wall-pieces.md §2) or two halves: piece 4 or 6 on its A
/// half, 5 or 6 on its B half, 6 where the wall runs on past this tile. Pieces are the room's <c>&lt;art 2 name&gt;&lt;k&gt;</c>
/// meshes. Sides are N, E, S, W (0..3); a piece's rotation is its side, and side s's A half is
/// quadrant s, its B half quadrant s + 1.
/// </summary>
public readonly struct RoomWalls : IEquatable<RoomWalls>
{
    // Four bits a side: present, whole, then for halves A runs on and B runs on; for a whole
    // side the A bit is the decoration.
    private const int Present = 1, Whole = 2, BitA = 4, BitB = 8;

    private readonly ulong _bits;

    private RoomWalls(ulong bits) => _bits = bits;

    /// <summary>No side has a wall.</summary>
    public bool IsEmpty => _bits == 0;

    public bool HasWall(int side) => (Side(side) & Present) != 0;

    public bool IsWhole(int side) => (Side(side) & (Present | Whole)) == (Present | Whole);

    /// <summary>The whole piece on a side: normal walls use 7 or decorated 8;
    /// 3×1 gates override the piece and rotation per side. Zero if not whole.</summary>
    public int WholePiece(int side) => !IsWhole(side) ? 0 : HasCustomWhole(side)
        ? (int)((_bits >> (16 + side * 7)) & 0xF) : (Side(side) & BitA) != 0 ? 8 : 7;

    public int WholeRotation(int side) => HasCustomWhole(side)
        ? (int)((_bits >> (20 + side * 7)) & 3) : side;

    private bool HasCustomWhole(int side) => ((_bits >> (22 + side * 7)) & 1) != 0;

    internal RoomWalls WithWholePiece(int side, int piece, int rotation) =>
        new(_bits | ((ulong)(Present | Whole) << (side * 4))
            | ((ulong)(0x40 | (piece & 0xF) | ((rotation & 3) << 4)) << (16 + side * 7)));

    /// <summary>The A half's piece (quadrant <paramref name="side"/>): 6 if the wall runs on that
    /// way, else 4; 0 if the side has no halves.</summary>
    public int HalfAPiece(int side) => !HasWall(side) || IsWhole(side) ? 0 : (Side(side) & BitA) != 0 ? 6 : 4;

    /// <summary>The B half's piece (quadrant <paramref name="side"/> + 1): 6 if the wall runs on
    /// that way, else 5; 0 if the side has no halves.</summary>
    public int HalfBPiece(int side) => !HasWall(side) || IsWhole(side) ? 0 : (Side(side) & BitB) != 0 ? 6 : 5;

    internal RoomWalls WithWhole(int side, bool decorated = false) =>
        new(_bits | ((ulong)(Present | Whole | (decorated ? BitA : 0)) << (side * 4)));

    internal RoomWalls WithHalves(int side, bool aRunsOn, bool bRunsOn) =>
        new(_bits | ((ulong)(Present | (aRunsOn ? BitA : 0) | (bRunsOn ? BitB : 0)) << (side * 4)));

    private int Side(int side) =>
        side is >= 0 and <= 3 ? (int)((_bits >> (side * 4)) & 0xF) : throw new ArgumentOutOfRangeException(nameof(side));

    public bool Equals(RoomWalls other) => _bits == other._bits;

    public override bool Equals(object? obj) => obj is RoomWalls other && Equals(other);

    public override int GetHashCode() => _bits.GetHashCode();

    public static bool operator ==(RoomWalls left, RoomWalls right) => left.Equals(right);

    public static bool operator !=(RoomWalls left, RoomWalls right) => !left.Equals(right);
}
