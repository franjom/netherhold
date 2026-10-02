// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>One room as a thing, rather than a scattering of tiles that share a terrain type.</summary>
/// <remarks>
/// <para>Derived, never stored: an instance is a connected group of tiles with the same room
/// terrain and owner, recomputed whenever asked for. Membership kept in <see cref="TileState"/>
/// would need maintaining on every path that changes a tile, splits and merges would need their own
/// logic, and it would enter the determinism hash — a pure function of already-hashed state needs no field, no snapshot change and no <c>HashVersion</c> bump.</para>
/// <para>Connectivity is orthogonal — two tiles joined only at a corner are two rooms. STRONGLY
/// INFERRED from the work that builds rooms being orthogonal (Q27). Answers Q45, Q52, Q60 and Q61.</para></remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/rooms-and-gold.md")]
public sealed class RoomInstance
{
    internal RoomInstance(RoomDef definition, byte ownerPlayerId, int[] tileIndices,
                          int minX, int minY, int maxX, int maxY)
    {
        Definition = definition;
        OwnerPlayerId = ownerPlayerId;
        TileIndices = tileIndices;
        MinX = minX;
        MinY = minY;
        MaxX = maxX;
        MaxY = maxY;
    }

    /// <summary>What kind of room this is.</summary>
    public RoomDef Definition { get; }

    public byte OwnerPlayerId { get; }

    /// <summary>Every tile in this room, in ascending tile-index order. The order is fixed so that
    /// anything iterating a room is reproducible, which matters when a rule picks one tile of several.</summary>
    public IReadOnlyList<int> TileIndices { get; }

    public int TileCount => TileIndices.Count;

    /// <summary>Whether a tile is one of this room's.</summary>
    public bool Contains(int tileIndex) => Array.BinarySearch((int[])TileIndices, tileIndex) >= 0;

    // The smallest axis-aligned box containing the room; a room need not fill it (an L-shaped treasury has empty corners), so a shape rule must ask the tiles.
    public int MinX { get; }
    public int MinY { get; }
    public int MaxX { get; }
    public int MaxY { get; }

    public int Width => MaxX - MinX + 1;
    public int Height => MaxY - MinY + 1;

    /// <summary>DK2's room position words <c>+0x27/+0x29</c>, aimed at by the withdraw and the alarm:
    /// the box's centre rounded down, one tile further on each axis for a Dungeon Heart. STRONGLY
    /// INFERRED: three captured 5×5 hearts read the offset, its writer is unread (Q515; ADR-0323).</summary>
    public int CentreFieldX => ((MinX + MaxX) / 2) + (Definition.TypeId == RoomTypeId.DungeonHeart ? 1 : 0);

    public int CentreFieldY => ((MinY + MaxY) / 2) + (Definition.TypeId == RoomTypeId.DungeonHeart ? 1 : 0);

    /// <summary>Whether the room fills its own bounding box — i.e. it is a solid rectangle.</summary>
    public bool IsRectangular => TileCount == Width * Height;

    /// <summary>Whether this room is big enough to do anything: the bounding box is at least
    /// <see cref="RoomDef.RecommendedSizeX"/> by <c>RecommendedSizeY</c>. STRONGLY INFERRED, and the
    /// field is misnamed — it recommends nothing and does not stop a keeper building smaller (a 1x1
    /// temple is buildable, Q36); it is the size below which the room does nothing. A graveyard needs
    /// 3x3 for its first grave and a temple 5x5, both matching their records (Q45); the Prison's says
    /// 5x5 yet a working 3x3 prison holds two and raises Skeletons (Q179), so this is not yet the
    /// rule. Measured on the box, not the tile count; which the original uses is UNKNOWN. Read only
    /// by <c>PortalRules</c>; a prison's capacity is its own geometry (<see cref="PrisonRules.Capacity"/>).</summary>
    public bool IsFunctional =>
        Width >= Definition.RecommendedSizeX && Height >= Definition.RecommendedSizeY;

    /// <summary>Tiles with at least one orthogonal neighbour outside the room — the rim, and a
    /// temple's capacity (Q60). CONFIRMED against three tooltip readings: 3x3 holds 8, 4x4 holds 12,
    /// 5x5 holds 16, which is <c>2(w+h) − 4</c> for a solid rectangle. Counting the boundary rather
    /// than evaluating the formula lets an L-shaped temple answer too; the room is a pool with a walkway, and creatures stand on the walkway.</summary>
    public int EdgeTileCount => TileCount - InteriorTileIndices.Count;

    /// <summary>The tiles wholly surrounded by the room, in ascending order. <b>A set, not a
    /// count</b>, because a prison's rim is where its bars are: a prisoner is confined to these
    /// and a Skeleton raised from one leaves through the gate (owner, 2026-09-11, Q306).</summary>
    public IReadOnlyList<int> InteriorTileIndices { get; internal set; } = [];

    /// <summary>Tiles wholly surrounded by the room — <see cref="TileCount"/> less the rim.</summary>
    public int InteriorTileCount => InteriorTileIndices.Count;

    /// <summary>The rim of the <i>interior</i>: interior tiles not themselves wholly
    /// surrounded by interior tiles. <b>A graveyard's graves sit here</b>, on five printed
    /// capacities (Q60); a temple's is the room's own rim, one ring further out.</summary>
    public int InteriorRimTileCount { get; internal set; }

    /// <summary>Whether this tile of the room is one of the wholly surrounded ones.</summary>
    public bool IsInterior(int tileIndex) => InteriorTileIndices.Contains(tileIndex);

    /// <summary>Whether a tile is one of the middle nine — the raised platform a dungeon heart's
    /// structure stands on, and what makes a heart hold 16000 and not 25000 (Q52). CONFIRMED: a
    /// keeper with no treasury tops out at 16000 and <c>MaxGoldPerDungeonHeartTile</c> is 1000, so
    /// sixteen of twenty-five tiles store gold — 25 − 16 = 9 = 3x3, and a screenshot shows a raised
    /// 3x3 centre ringed by gold piles. Computed from the bounding box's centre, so it needs no
    /// <c>FiveByFiveRotated</c> field; under 3 wide either way it answers false, keeping a half-destroyed heart from swallowing its storage.</summary>
    public bool IsCentrePlatform(int x, int y)
    {
        if (Width < 3 || Height < 3) return false;

        int cx = (MinX + MaxX) / 2;
        int cy = (MinY + MaxY) / 2;
        return Math.Abs(x - cx) <= 1 && Math.Abs(y - cy) <= 1;
    }
}
