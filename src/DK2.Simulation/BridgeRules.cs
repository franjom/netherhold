// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>What a bridge is built over, and so what is left when it goes.</summary>
/// <remarks>Rule: a sold bridge reverts to the water or lava it spanned — CONFIRMED (owner,
/// 2026-09-03); <see cref="TerrainDef.DestroyedTypeTerrainId"/> cannot say so, because every room
/// terrain, both bridges included, gives Claimed Path. The hazard is in
/// <see cref="TileState.Flags"/>, the third of the MAP record's four bytes a tile (Q16): 1 in all
/// 66 shipped levels except bridge tiles over lava, where it is 2. STRONGLY INFERRED
/// (rooms-and-gold.md, external-resources.md). Not implemented: what becomes of whatever stood on a bridge that burns away (Q67).</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/rooms-and-gold.md")]
public static class BridgeRules
{
    /// <summary>The value <see cref="TileState.Flags"/> takes on a bridge tile spanning lava.
    /// Every other tile in every shipped level carries 1.</summary>
    public const byte SpansLava = 2;

    /// <summary>The default, and what a bridge over water carries.</summary>
    public const byte SpansWater = 1;

    /// <summary>Whether this room goes over a hazard — asked of the room's own placement flags,
    /// so nothing here knows what a bridge is called.</summary>
    public static bool IsBridge(RoomDef room) =>
        room.PlaceableOnWater || room.PlaceableOnLava;

    /// <summary>The terrain a bridge tile stands over, or null if this tile is not a bridge.</summary>
    public static TerrainDef? SpannedTerrain(GameDatabase database, in TileState tile)
    {
        RoomDef? room = database.RoomByTerrain(tile.TerrainId);
        if (room is null || !IsBridge(room)) return null;

        return tile.Flags == SpansLava ? database.LavaTerrain : database.WaterTerrain;
    }

    /// <summary>The marker to record on a tile about to be bridged, given the terrain covered.
    /// Used when a player builds a bridge: the level file's byte covers only shipped bridges, so
    /// one laid mid-game must record its own hazard or selling it reverts to the wrong thing.</summary>
    public static byte MarkerFor(TerrainDef covered) =>
        covered.IsLava ? SpansLava : SpansWater;

    /// <summary>How long a bridge laid on this terrain lasts, in ticks, or 0 for ever. A wooden
    /// bridge over lava burns away after <c>WoodBridgeLifeOnLavaSeconds</c> (30), and the clock
    /// starts when the bridge is built — not when a creature steps on it, and not as tile damage.
    /// CONFIRMED (owner, 2026-09-03); a stone bridge lasts for ever. The one rule here that names
    /// a room type, because DK2's variable does: Wooden and Stone Bridge carry identical flags (0x30000034) and health and neither carries <see cref="TerrainFlags.Decay"/>.</summary>
    public static int LifetimeTicks(GameDatabase database, RoomDef room, TerrainDef covered)
    {
        if (!covered.IsLava) return 0;
        if (room.TypeId != RoomTypeId.WoodenBridge) return 0;

        return SimulationTiming.TicksFromSeconds(
            database[VariableTypeId.WoodBridgeLifeOnLavaSeconds]);
    }

    /// <summary>What a tile becomes when whatever is on it is taken away — a bridge back to its
    /// hazard, anything else to its terrain's destroyed type. The one place that decision is made,
    /// so selling a bridge and beating one down cannot disagree.</summary>
    public static byte RevertsTo(GameDatabase database, in TileState tile)
    {
        TerrainDef? spanned = SpannedTerrain(database, tile);
        return spanned?.TerrainId ?? database.TerrainById(tile.TerrainId).DestroyedTypeTerrainId;
    }
}
