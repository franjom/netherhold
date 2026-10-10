// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using DK2.Assets;
using DK2.Data.Model;

namespace DK2.Presentation;

/// <summary>
/// One of DK2's per-tile piece lists as a builder fills it (wall-pieces.md, conventions): four
/// quarters, each a piece and a rotation, and at most one whole piece. A room floor's pieces are
/// the room's numbered meshes (<see cref="AssetCatalogue.RoomPiece"/>), a bed's the liquid's
/// (<see cref="AssetCatalogue.TerrainBed"/>). Quadrants are NW, NE, SE, SW (0..3).
/// </summary>
public readonly struct PieceSet : IEquatable<PieceSet>
{
    // Bits 0-23 four quarters of 4-bit piece and 2-bit rotation, 24-27 which quarters are present,
    // 28-31 the whole piece, 32-33 its rotation, 34 whole present.
    private const int PresentShift = 24, WholeShift = 28;
    private const ulong WholeBit = 1ul << 34;

    private readonly ulong _bits;

    private PieceSet(ulong bits) => _bits = bits;

    public bool IsEmpty => _bits == 0;

    public bool HasQuarters => ((_bits >> PresentShift) & 0xF) != 0;

    /// <summary>Whether quadrant <paramref name="quadrant"/> holds a piece (a builder may fill only
    /// some, as DoubleQuad's door tile fills one).</summary>
    public bool HasQuarter(int quadrant) => ((_bits >> (PresentShift + Quadrant(quadrant))) & 1) != 0;

    public bool HasWhole => (_bits & WholeBit) != 0;

    public int QuarterPiece(int quadrant) => (int)((_bits >> (6 * Quadrant(quadrant))) & 0xF);

    public int QuarterRotation(int quadrant) => (int)((_bits >> ((6 * Quadrant(quadrant)) + 4)) & 3);

    public int WholePiece => (int)((_bits >> WholeShift) & 0xF);

    public int WholeRotation => (int)((_bits >> (WholeShift + 4)) & 3);

    internal PieceSet WithQuarter(int quadrant, int piece, int rotation) =>
        new(_bits | (1ul << (PresentShift + Quadrant(quadrant)))
            | ((ulong)((piece & 0xF) | ((rotation & 3) << 4)) << (6 * quadrant)));

    internal PieceSet WithWhole(int piece, int rotation) =>
        new(_bits | WholeBit | ((ulong)((piece & 0xF) | ((rotation & 3) << 4)) << WholeShift));

    private static int Quadrant(int quadrant) =>
        quadrant is >= 0 and <= 3 ? quadrant : throw new ArgumentOutOfRangeException(nameof(quadrant));

    public bool Equals(PieceSet other) => _bits == other._bits;

    public override bool Equals(object? obj) => obj is PieceSet other && Equals(other);

    public override int GetHashCode() => _bits.GetHashCode();

    public static bool operator ==(PieceSet left, PieceSet right) => left.Equals(right);

    public static bool operator !=(PieceSet left, PieceSet right) => !left.Equals(right);
}

/// <summary>The liquid surface DK2's per-tile pass draws over a tile (<c>0x0043CC30</c>,
/// room-floors-and-liquids.md §6): water's (<c>0x005A2570</c>), lava's (<c>0x005A2BE0</c>), or none.
/// The Temple's pool (<c>0x005A2F10</c>) is not built.</summary>
public enum LiquidSurface : byte
{
    None,
    Water,
    Lava,
}

/// <summary>A room's numbered piece keys, one shared instance per room type.</summary>
public sealed class RoomPieceKeys
{
    private readonly AssetRef[] _pieces;

    internal RoomPieceKeys(RoomDef room, bool destroyedHeart = false)
    {
        _pieces = new AssetRef[destroyedHeart ? 6 : AssetCatalogue.RoomPieceCount(room)];
        for (int k = 0; k < _pieces.Length; k++) _pieces[k] = destroyedHeart
            ? AssetCatalogue.DestroyedHeartPiece(room.Name, k) : AssetCatalogue.RoomPiece(room.Name, k);
        // The ruined builder's piece 5 uses DUNGEON0 while room +0x63 is zero.
        if (destroyedHeart) _pieces[5] = AssetCatalogue.RoomPiece(room.Name, 0);
    }

    /// <summary>Piece <paramref name="k"/>; empty past the pieces the loader builds.</summary>
    public AssetRef Piece(int k) => k >= 0 && k < _pieces.Length ? _pieces[k] : default;
}
