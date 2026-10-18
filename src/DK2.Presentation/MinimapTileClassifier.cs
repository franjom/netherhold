// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

[Flags]
public enum MinimapTileFlags : byte
{
    None = 0,
    DigHighlighted = 1,
    Explored = 2,
    Scanned = 4,
    NavigationSolid = 8,
}

public readonly record struct MinimapTileFacts(
    TerrainDef Terrain, RoomTypeId? Room, PlayerId Owner, MinimapTileFlags Flags);

[Dk2Behavior(Confidence.StronglyInferred, "docs/research/functions/00451C90.md")]
public static class MinimapTileClassifier
{
    public static MinimapColourClass Classify(in MinimapTileFacts facts)
    {
        TerrainDef terrain = facts.Terrain;
        MinimapTileFlags flags = facts.Flags;
        if ((flags & MinimapTileFlags.DigHighlighted) != 0) return MinimapColourClass.Tagged;
        bool scannedReveal = (flags & MinimapTileFlags.Scanned) != 0 && terrain.IsRevealedThroughFog;
        if ((flags & MinimapTileFlags.Explored) == 0 && !scannedReveal) return MinimapColourClass.Unknown;
        if (facts.Room == RoomTypeId.DungeonHeart) return OwnedClass(35, facts.Owner);
        if (facts.Owner.Value is not PlayerIds.Unowned and not PlayerIds.Neutral)
            return OwnedClass((flags & MinimapTileFlags.NavigationSolid) != 0 ? 17 : 26, facts.Owner);
        if (facts.Room.HasValue) return OwnedClass(26, facts.Owner);
        if (terrain.IsSolid)
        {
            if (terrain.GoldValue != 0) return new MinimapColourClass((ushort)(terrain.IsImpenetrable ? 5 : 7));
            return terrain.IsImpenetrable ? MinimapColourClass.Unknown : new MinimapColourClass(4);
        }
        if (terrain.IsWater) return new MinimapColourClass(9);
        if (terrain.IsLava) return new MinimapColourClass(11);
        return new MinimapColourClass((ushort)(TileWorkRules.HealsIntoAnother(terrain) ? 2 : 0));
    }

    private static MinimapColourClass OwnedClass(int bank, PlayerId owner) => new((ushort)(bank + owner.Value));
}
