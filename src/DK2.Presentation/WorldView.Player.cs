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
        if (terrain.IsSolid)
        {
            flags |= PlayerTileFlags.Solid;
        }

        if (terrain.IsTaggable)
        {
            flags |= PlayerTileFlags.Diggable;
        }

        if (terrain.GoldValue != 0)
        {
            flags |= PlayerTileFlags.GoldSeam;
        }

        if (terrain.IsWater)
        {
            flags |= PlayerTileFlags.Water;
        }

        if (terrain.IsLava)
        {
            flags |= PlayerTileFlags.Lava;
        }

        if (ShowsTag(tile, terrain, x, y))
        {
            flags |= PlayerTileFlags.Tagged;
        }

        if (!admitted)
        {
            return new PlayerTile(flags, default, default);
        }
        // A player sees its own traps and blueprints; another keeper's stay hidden.
        if (world.Traps.Count > 0 && world.TrapAt(x, y, new PlayerId(_viewingPlayerId)) is not null)
        {
            flags |= PlayerTileFlags.OwnTrap;
        }

        PlayerId owner = terrain.IsOwnable ? tile.OwnerPlayerId : (byte)0;
        RoomTypeId room = _database.RoomByTerrain(tile.TerrainId)?.TypeId ?? default;
        return new PlayerTile(flags, owner, room);
    }

    /// <summary>Other players' traps as <see cref="Structures"/> draws them for this viewer: on a visible tile, built and
    /// not hidden. <c>ReachTiles</c> is how far its target finder looks (traps.md sections 5 and 6): a tile trap only its
    /// own tile (0); a sight or Trigger trap its definition's range, plus two when it turns toward a target (flag 0x80,
    /// the Sentry's, Hero Sentry's and Magic Door's).</summary>
    internal void PlayerEnemyTraps(WorldState world, List<PlayerVisibleTrap> into)
    {
        into.Clear();
        foreach (TrapState trap in world.Traps)
        {
            var tile = new TilePos(trap.X, trap.Y);
            if (trap.PlayerId == _viewingPlayerId || trap.Hidden || trap.UnderConstruction || !world.Contains(tile.X, tile.Y)
                || !IsVisible(world, tile) || !_database.Traps.TryGetValue(trap.TrapId, out TrapDef? definition))
            {
                continue;
            }

            int reach = definition.TriggerType == TrapRules.TileSelector ? 0
                : definition.RangeTiles + ((definition.Flags & TrapRules.TurnsToTarget) != 0 ? 2 : 0);
            into.Add(new PlayerVisibleTrap(tile, trap.TrapId, new PlayerId(trap.PlayerId), reach));
        }
    }
}
