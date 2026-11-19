// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Data.Model;

namespace DK2.Presentation;

/// <summary>One RGBA float texel per tile: packed ambient RGB, packed quarter RGB, tag/red bits, fog byte.
/// Each packed integer is exactly representable as a float. State-source -1 reads no state;
/// a tint source below -1 reads quarter colour from tile -source-2.</summary>
public static class TerrainTileState
{
    /// <summary>Whether only texel state changed; fog still uses its material path.</summary>
    public static bool GeometryEquals(in TileView a, in TileView b) =>
        a.Position == b.Position && a.Ground == b.Ground && a.Furniture == b.Furniture && a.OwnerPlayerId == b.OwnerPlayerId
        && a.Solid == b.Solid && a.GoldHeld == b.GoldHeld && a.HealthPermille == b.HealthPermille
        && a.Walls.Equals(b.Walls) && a.Pieces.Equals(b.Pieces) && Equals(a.PieceKeys, b.PieceKeys)
        && a.Explored == b.Explored && a.HasDoor == b.HasDoor && a.RoomFloor.Equals(b.RoomFloor)
        && Equals(a.RoomKeys, b.RoomKeys) && a.Bed.Equals(b.Bed) && Equals(a.BedKeys, b.BedKeys)
        && a.Surface == b.Surface && a.Scanned == b.Scanned && a.PointLight == b.PointLight
        && a.SightFogOverlay == b.SightFogOverlay && a.SightFogOpacity == b.SightFogOpacity
        && a.FaceTexture == b.FaceTexture && a.ExtraRoomPiece == b.ExtraRoomPiece;

    public static Vector4 Encode(in TileView tile) => new(Pack(tile.AmbientTint), Pack(tile.QuarterAmbientTint),
        (tile.Tagged ? 1 : 0) | (tile.RedHighlighted ? 2 : 0), tile.SightFogOpacity);

    public static TerrainLightColour DecodeColour(float packed)
    {
        int value = (int)packed;
        return new(value & 255, (value >> 8) & 255, (value >> 16) & 255);
    }

    /// <summary>The texel shader's added light, before the vertex diffuse clamp; material reference is TerrainMaterialKey.For.</summary>
    public static Vector3 AddedLight(in ClassicMaterialRecipe recipe, Vector4 own, Vector4 tag, Vector4 tint, bool quarter)
    {
        Vector3 added = new(recipe.AddedLight);
        if (!recipe.TakesAddedLight) return added;
        if (((int)own.Z & 2) != 0) return added + TerrainMaterialLook.RedLight;
        if (((int)tag.Z & 1) != 0) return added + TerrainMaterialLook.TagTint;
        TerrainLightColour colour = DecodeColour(quarter ? tint.Y : tint.X);
        return added + new Vector3(colour.Red / 255f, colour.Green / 255f, colour.Blue / 255f);
    }

    public static Vector3 Overlay(Vector4 own, Vector4 tag) => ((int)own.Z & 2) != 0
        ? TerrainMaterialLook.HoverOverlayColour : ((int)tag.Z & 1) != 0 ? TerrainMaterialLook.TagOverlayColour : Vector3.Zero;

    private static int Pack(TerrainLightColour colour) =>
        (colour.Red & 255) | ((colour.Green & 255) << 8) | ((colour.Blue & 255) << 16);
}
