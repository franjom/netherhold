// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Presentation;

/// <summary>Piece-entry colour boundaries; see classic-lighting.md section2.</summary>
public static class ClassicTerrainTint
{
    public static TerrainLightColour PieceColour(TerrainLightColour colour) => new(
        colour.Red & 255, colour.Green & 255, colour.Blue & 255);

    public static TerrainLightColour RoomQuarterColour(TileConstruction construction,
        RoomTypeId room, bool overLava, TerrainLightColour colour) => construction switch
        {
            TileConstruction.Normal or TileConstruction.DoubleQuad => colour,
            TileConstruction.QuadConstruction when room == RoomTypeId.WoodenBridge && overLava => colour,
            _ => default,
        };
}
