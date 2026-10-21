// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using DK2.Assets;

namespace DK2.Presentation;

/// <summary>What fills a tile's four quarters: nothing, a player-coloured floor or a
/// player-coloured wall top.</summary>
public enum QuarterKind : byte
{
    None,
    Floor,
    Top,
}

/// <summary>
/// The pieces a plain terrain tile draws, as DK2's builder picks them (<c>0x0043E860</c>,
/// floor-and-terrain-pieces.md §5, Q71): a face (art 1) on each open side, then a top (art 2), a
/// whole floor (art 0), or four player quarters chosen by the neighbour mask. A tag does not change
/// the pieces: it tints every piece the tile draws (<see cref="TileView.Tagged"/>, tag-highlight.md).
/// Sides are N, E, S, W (0..3) and a face's rotation is its side; quadrants are NW, NE, SE, SW
/// (0..3). Rooms, water and lava draw nothing here.
/// </summary>
public readonly struct TerrainPieces : IEquatable<TerrainPieces>
{
    // Bits 0-3 the faces, 4 a top, 6 a whole floor, 7-8 the quarter kind,
    // 9-20 four 3-bit quarter pieces, 21-23 the player set.
    private const int TopBit = 1 << 4, FloorBit = 1 << 6;
    private const int KindShift = 7, PieceShift = 9, SetShift = 21;

    // The rotation each quadrant's piece takes, NW, NE, SE, SW (0x004400E9's four arms).
    private static readonly int[] QuadrantRotations = [2, 3, 0, 1];

    private readonly uint _bits;

    private TerrainPieces(uint bits) => _bits = bits;

    public bool IsEmpty => _bits == 0;

    public bool HasFace(int side) =>
        side is >= 0 and <= 3 ? (_bits & (1u << side)) != 0 : throw new ArgumentOutOfRangeException(nameof(side));

    public bool HasTop => (_bits & TopBit) != 0;

    public bool HasFloor => (_bits & FloorBit) != 0;

    public QuarterKind Quarters => (QuarterKind)((_bits >> KindShift) & 3);

    /// <summary>The player set the quarters come from: the owner's id − 1.</summary>
    public int QuarterSet => (int)((_bits >> SetShift) & 7);

    /// <summary>Quadrant <paramref name="quadrant"/>'s piece, 0..4.</summary>
    public int QuarterPiece(int quadrant) => quadrant is >= 0 and <= 3
        ? (int)((_bits >> (PieceShift + (3 * quadrant))) & 7)
        : throw new ArgumentOutOfRangeException(nameof(quadrant));

    /// <summary>The rotation a quadrant's piece is drawn at (fixed per quadrant).</summary>
    public static int QuarterRotation(int quadrant) => QuadrantRotations[quadrant];

    internal TerrainPieces WithFace(int side) => new(_bits | (1u << side));

    internal TerrainPieces WithTop() => new(_bits | TopBit);

    internal TerrainPieces WithFloor() => new(_bits | FloorBit);

    internal TerrainPieces WithQuarters(QuarterKind kind, int set, int pieces) =>
        new(_bits | ((uint)kind << KindShift) | ((uint)pieces << PieceShift) | ((uint)set << SetShift));

    public bool Equals(TerrainPieces other) => _bits == other._bits;

    public override bool Equals(object? obj) => obj is TerrainPieces other && Equals(other);

    public override int GetHashCode() => (int)_bits;

    public static bool operator ==(TerrainPieces left, TerrainPieces right) => left.Equals(right);

    public static bool operator !=(TerrainPieces left, TerrainPieces right) => !left.Equals(right);
}

/// <summary>A terrain's piece keys (<see cref="AssetCatalogue.ForTerrainPieces"/>), one shared
/// instance per terrain so a tile carries a reference, not twelve keys.</summary>
public sealed class TerrainPieceKeys
{
    private readonly AssetRef[] _quarters;
    private readonly AssetRef[] _beds = new AssetRef[AssetCatalogue.BedPieces];
    private readonly AssetRef[] _heroFaces = new AssetRef[AssetCatalogue.HeroWallPieces];

    internal TerrainPieceKeys(string terrainName)
    {
        for (int k = 0; k < _beds.Length; k++) _beds[k] = AssetCatalogue.TerrainBed(terrainName, k);
        Side = AssetCatalogue.TerrainPiece(terrainName, AssetCatalogue.TerrainSide);
        Top = AssetCatalogue.TerrainPiece(terrainName, AssetCatalogue.TerrainTop);
        // Art 3, "TOP TAGGED": keyed because the install has it, drawn by nothing (its consumer is
        // unread; the tag look is a tint, tag-highlight.md).
        TopTagged = AssetCatalogue.TerrainPiece(terrainName, AssetCatalogue.TerrainTopTagged);
        Floor = AssetCatalogue.TerrainPiece(terrainName, AssetCatalogue.TerrainFloor);
        for (int k = 0; k < _heroFaces.Length; k++)
            _heroFaces[k] = AssetCatalogue.HeroWallPiece(terrainName, AssetCatalogue.FirstHeroWallPiece + k);
        _quarters = new AssetRef[AssetCatalogue.ClaimedSets * AssetCatalogue.ClaimedPieces];
        for (int set = 0; set < AssetCatalogue.ClaimedSets; set++)
            for (int piece = 0; piece < AssetCatalogue.ClaimedPieces; piece++)
                _quarters[(set * AssetCatalogue.ClaimedPieces) + piece] =
                    AssetCatalogue.TerrainQuarter(terrainName, set, piece);
    }

    public AssetRef Side { get; }
    public AssetRef Top { get; }
    public AssetRef TopTagged { get; }
    public AssetRef Floor { get; }

    public AssetRef Quarter(int set, int piece) => _quarters[(set * AssetCatalogue.ClaimedPieces) + piece];

    /// <summary>A hero-owned wall's face piece 4..8 (<see cref="TileView.Walls"/>); empty otherwise.</summary>
    public AssetRef HeroFace(int piece) =>
        piece >= AssetCatalogue.FirstHeroWallPiece && piece < AssetCatalogue.FirstHeroWallPiece + _heroFaces.Length
            ? _heroFaces[piece - AssetCatalogue.FirstHeroWallPiece] : default;

    /// <summary>A water or lava terrain's bed piece 0..7.</summary>
    public AssetRef Bed(int k) => _beds[k];

    /// <summary>Every key the set can draw, for decoding ahead; not <see cref="TopTagged"/>, which
    /// nothing draws. Keys the install lacks are among them.</summary>
    public IEnumerable<AssetRef> All()
    {
        yield return Side;
        yield return Top;
        yield return Floor;
        foreach (AssetRef quarter in _quarters) yield return quarter;
        foreach (AssetRef bed in _beds) yield return bed;
        foreach (AssetRef face in _heroFaces) yield return face;
    }
}
