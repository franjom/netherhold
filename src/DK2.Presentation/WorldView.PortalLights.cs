// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Collections.Generic;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

public sealed partial class WorldView
{
    private WorldState? _portalLightWorld;
    private readonly List<TilePos> _portalLightTiles = new();
    private static readonly TilePos[] PortalNeighbours = { new(0, -1), new(1, 0), new(0, 1), new(-1, 0) };

    /// <summary>Append-only light requests, including repeated refreshes at the same tile.</summary>
    public IReadOnlyList<TilePos> PortalLightTiles(WorldState world)
    {
        if (ReferenceEquals(_portalLightWorld, world)) return _portalLightTiles;
        if (_portalLightWorld is not null) _portalLightWorld.RoomTilesChanged -= CapturePortalRefresh;
        _portalLightWorld = world;
        _portalLightTiles.Clear();
        CaptureLoadedPortals(world);
        world.RoomTilesChanged += CapturePortalRefresh;
        return _portalLightTiles;
    }

    private void CapturePortalRefresh(IReadOnlyList<TilePos> chain)
    {
        if (chain.Count == 9 && IsEntrance(_portalLightWorld!, chain[0])) _portalLightTiles.Add(chain[4]);
    }

    private bool IsEntrance(WorldState world, TilePos tile) =>
        _database.RoomByTerrain(world.Tile(tile).TerrainId)?.TypeId is RoomTypeId.Portal or RoomTypeId.HeroPortal;

    private void CaptureLoadedPortals(WorldState world)
    {
        // Replay the authored row-major joins: a room can reach nine before its final size.
        var chains = new Dictionary<TilePos, List<TilePos>>();
        for (int y = 0; y < world.Height; y++)
            for (int x = 0; x < world.Width; x++)
            {
                var tile = new TilePos(x, y);
                if (!IsEntrance(world, tile)) continue;
                List<TilePos>? chain = null;
                foreach (TilePos offset in PortalNeighbours)
                {
                    TilePos neighbour = tile + offset;
                    if (!chains.TryGetValue(neighbour, out List<TilePos>? other) || ReferenceEquals(chain, other)) continue;
                    if (world.Tile(neighbour).TerrainId != world.Tile(tile).TerrainId
                        || world.Tile(neighbour).OwnerPlayerId != world.Tile(tile).OwnerPlayerId) continue;
                    if (chain is null)
                    {
                        chain = other;
                        chain.Add(tile);
                    }
                    else
                    {
                        foreach (TilePos moved in other) chains[moved] = chain;
                        chain.AddRange(other);
                    }
                }
                chain ??= new List<TilePos> { tile };
                chains[tile] = chain;
                if (chain.Count == 9) _portalLightTiles.Add(chain[4]);
            }
    }
}
