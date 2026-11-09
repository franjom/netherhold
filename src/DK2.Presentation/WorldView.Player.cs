// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Players;

namespace DK2.Presentation;

public sealed partial class WorldView
{
    /// <summary>
    /// One tile as a player observation carries it, through the same fog gate as <see cref="TileAt"/>:
    /// a hidden tile shows only the viewer's own tag; a scanned tile whose terrain reveals through fog
    /// shows its terrain but no owner or room; an admitted tile shows terrain, owner and room. It reads
    /// the facts a controller acts on without building render data (campaign player plan, task 2).
    /// Whether the viewer's own tag sticks on a hidden tile depends on what lies there (TileTagSystem
    /// refuses secret doors and non-taggable scanned ground), exactly as the marker a human sees.
    /// </summary>
    internal PlayerTile PlayerTileAt(WorldState world, int x, int y)
    {
        ref readonly TileState tile = ref world.Tile(x, y);
        TerrainDef terrain = _database.TerrainById(tile.TerrainId);
        bool scanned = tile.IsDiscoveredBy(_viewingPlayerId);
        bool explored = !world.FogOfWarEnabled || tile.IsExploredBy(_viewingPlayerId);
        bool admitted = explored || AdmitsUnexplored(world, new TilePos(x, y));
        if (!admitted && (!scanned || !terrain.IsRevealedThroughFog))
        {
            TerrainDef shown = (scanned ? _scanTerrain : _fogTerrain) ?? throw new InvalidOperationException(
                "Fog of war needs both 'Fog Of War' and 'Claim Scan' terrain definitions.");
            return new PlayerTile(ShowsTag(tile, shown, x, y) ? PlayerTileFlags.Tagged : PlayerTileFlags.None,
                default, default);
        }

        PlayerTileFlags flags = PlayerTileFlags.Known;
        if (terrain.IsSolid) flags |= PlayerTileFlags.Solid;
        if (terrain.IsTaggable) flags |= PlayerTileFlags.Diggable;
        if (terrain.GoldValue != 0) flags |= PlayerTileFlags.GoldSeam;
        if (terrain.IsWater) flags |= PlayerTileFlags.Water;
        if (terrain.IsLava) flags |= PlayerTileFlags.Lava;
        if (ShowsTag(tile, terrain, x, y)) flags |= PlayerTileFlags.Tagged;
        if (!admitted) return new PlayerTile(flags, default, default);

        PlayerId owner = terrain.IsOwnable ? tile.OwnerPlayerId : (byte)0;
        RoomTypeId room = _database.RoomByTerrain(tile.TerrainId)?.TypeId ?? default;
        return new PlayerTile(flags, owner, room);
    }
}
