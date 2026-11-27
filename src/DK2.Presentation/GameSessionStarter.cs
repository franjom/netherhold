// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Systems;

namespace DK2.Presentation;

/// <summary>Selects the simulation a session starts with: a restored save's,
/// the front end's map world, a campaign level's, or an original DK2 save's grid. A restored <c>.dk2g</c> comes from
/// <see cref="SessionLoader"/>; a campaign level's records from <see cref="CampaignLevelData"/>.</summary>
public static class GameSessionStarter
{
    /// <summary>A campaign level's simulation: <paramref name="resumed"/> when a save was loaded, else the front end's map
    /// world (<see cref="FrontEnd"/>), else a fresh level (<see cref="CampaignLevelData.CreateSimulation"/>).</summary>
    public static GameSimulation Campaign(CampaignLevelData campaign, GameSimulation? resumed, bool frontEnd, ulong seed,
        bool fogOfWarEnabled)
    {
        if (campaign is null)
        {
            throw new ArgumentNullException(nameof(campaign));
        }

        return resumed ?? (frontEnd ? FrontEnd(campaign, seed) : campaign.CreateSimulation(seed, fogOfWarEnabled));
    }

    /// <summary>The front end's world: the level's map and players, no things, scripts or fog.</summary>
    public static GameSimulation FrontEnd(CampaignLevelData campaign, ulong seed)
    {
        if (campaign is null)
        {
            throw new ArgumentNullException(nameof(campaign));
        }

        return new GameSimulation(campaign.Database, WorldState.FromMap(campaign.Database, campaign.Map,
            campaign.Players.Select(player => player.PlayerId), fogOfWarEnabled: false), seed);
    }

    /// <summary>An original DK2 save's grid (terrain and ownership, nobody in it) with the default systems; its players are
    /// <see cref="PlayersOf"/>.</summary>
    public static GameSimulation FromDk2Save(GameDatabase database, LevelMap map, ulong seed, bool fogOfWarEnabled)
    {
        return new GameSimulation(database, WorldState.FromMap(database, map, PlayersOf(map), fogOfWarEnabled: fogOfWarEnabled), seed,
            SimulationSystems.Default());
    }

    /// <summary>The usual local keeper, or an owner present in the imported grid when that keeper has no tiles.</summary>
    public static PlayerId ImportedSaveViewer(WorldState world, PlayerId preferred)
    {
        if (world.HasPlayer(preferred))
        {
            return preferred;
        }

        IReadOnlyList<PlayerId> players = world.PlayerIds;
        foreach (PlayerId player in players)
        {
            if (PlayerIds.IsKeeper(player.Value))
            {
                return player;
            }
        }

        return players[0];
    }

    /// <summary>The terrain a native checkpoint of an imported session can restart from.</summary>
    internal static LevelMap TerrainOf(WorldState world)
    {
        var tiles = new MapTile[world.Width * world.Height];
        for (int y = 0; y < world.Height; y++)
        {
            for (int x = 0; x < world.Width; x++)
        {
            ref TileState tile = ref world.Tile(x, y);
            tiles[y * world.Width + x] = new MapTile(tile.TerrainId, tile.OwnerPlayerId, 0, 0);
        }
        }

        return new LevelMap(world.Width, world.Height, tiles);
    }

    /// <summary>Whoever the map says owns a tile, ascending: the rule the harness uses when a scenario names no players.</summary>
    public static byte[] PlayersOf(LevelMap map)
    {
        if (map is null)
        {
            throw new ArgumentNullException(nameof(map));
        }

        var seen = new SortedSet<byte>();
        foreach (MapTile tile in map.Tiles)
        {
            seen.Add(tile.PlayerId);
        }

        byte[] players = new byte[seen.Count];
        seen.CopyTo(players);
        return players;
    }

    /// <summary>The ticks per second the ordinary camera scrolls at: the campaign level's, else the original game's.</summary>
    public static int CameraTicksPerSecond(CampaignLevelData? campaign)
    {
        return campaign?.Level.TicksPerSecond ?? SimulationTiming.OriginalTicksPerGameSecond;
    }
}
