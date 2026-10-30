// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Presentation;

[Dk2Behavior(Confidence.StronglyInferred, "docs/research/minimap-controls.md#full-map-markers-and-legend")]
public static class ClassicFullMapMarkers
{
    private static readonly string?[] RoomNames = { null, "treasury", "lair", "portal", "hatchery", "heart",
        "dark_library", "training", "Bridge_Wooden", "guard_room", "workshop", "prison", "torture", "temple",
        "graveyard", "casino", "pit", "Bridge_Stone", null, null, null, "guard_room", "Bridge_Wooden" };
    public static string? RoomArt(RoomTypeId roomType) => (byte)roomType < RoomNames.Length
        && RoomNames[(byte)roomType] is string name ? "gui\\rooms\\" + name : null;

    public static bool TryRoom(RoomTypeId roomType, TilePos centre, bool explored, out MinimapMarker marker)
    {
        marker = default;
        if (!explored || RoomArt(roomType) == null) return false;
        marker = new(MinimapMarkerKind.FullMapRoom,
            new FpPosition3(new Fp32(centre.X * Fp32.One), new Fp32(centre.Y * Fp32.One), default),
            0xffffffff, RoomType: roomType);
        return true;
    }

    public static bool TryCreature(FpPosition3 position, PlayerId owner, bool inHand, bool explored,
        MinimapPalette palette, out MinimapMarker marker)
    {
        marker = default;
        if (inHand || !explored || owner.Value > 7) return false;
        marker = new(MinimapMarkerKind.FullMapCreature, position, palette.Colours[17 + owner.Value]);
        return true;
    }
}
