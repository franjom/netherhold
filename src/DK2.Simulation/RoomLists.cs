// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>A room's insertion into its owner's list; zero is an untracked fixture tile.</summary>
public readonly record struct RoomListPosition(long Value);

/// <summary>Persistent newest-first player room order, independent of the derived map sweep.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/room-list.md")]
public static class RoomLists
{
    internal static RoomListPosition Next(WorldState world) => new(world.NextRoomListPosition++);

    internal static void Set(WorldState world, IEnumerable<int> tiles, RoomListPosition position)
    {
        foreach (int tile in tiles) world.TileAtIndex(tile).RoomListPosition = position;
    }

    public static IReadOnlyList<RoomInstance> Of(WorldState world, GameDatabase database, PlayerId owner) =>
        RoomIndex.Build(world, database).Where(room => room.OwnerPlayerId == owner)
            .OrderByDescending(room => world.TileAtIndex(room.TileIndices[0]).RoomListPosition.Value)
            .ThenByDescending(room => room.TileIndices[0]).ToArray();

    /// <summary><c>0x004C63D0</c>: with no rooms, null and no draw; otherwise one random(count) over
    /// the whole list, bridges included, and null when the drawn entry is a bridge — the walk's
    /// counter goes negative there and runs off the list's end.</summary>
    public static RoomInstance? RandomNonBridgeRoom(IReadOnlyList<RoomInstance> rooms, SimRandom rng)
    {
        if (rooms.Count == 0) return null;
        RoomInstance drawn = rooms[rng.NextInt(rooms.Count)];
        return BridgeRules.IsBridge(drawn.Definition) ? null : drawn;
    }

    internal static void Initialize(WorldState world, GameDatabase database)
    {
        int[] parent = Enumerable.Repeat(-1, world.TileCount).ToArray();
        RoomListPosition[] positions = new RoomListPosition[world.TileCount];
        int Root(int tile)
        {
            while (parent[tile] != tile)
            {
                parent[tile] = parent[parent[tile]];
                tile = parent[tile];
            }
            return tile;
        }
        bool Same(int a, int b) => b >= 0 && parent[b] >= 0
            && world.TileAtIndex(a).TerrainId == world.TileAtIndex(b).TerrainId
            && world.TileAtIndex(a).OwnerPlayerId == world.TileAtIndex(b).OwnerPlayerId;

        for (int i = 0; i < world.TileCount; i++)
        {
            if (database.RoomByTerrain(world.TileAtIndex(i).TerrainId) is null) continue;
            int north = i - world.Width, west = i % world.Width == 0 ? -1 : i - 1;
            // Only N and W have arrived during a row-major load; native precedence is N/E/S/W.
            int joined = Same(i, north) ? Root(north) : Same(i, west) ? Root(west) : -1;
            if (joined < 0)
            {
                parent[i] = i;
                positions[i] = Next(world);
            }
            else
            {
                parent[i] = joined;
                if (Same(i, west)) parent[Root(west)] = joined;
            }
        }
        for (int i = 0; i < parent.Length; i++)
            if (parent[i] >= 0) world.TileAtIndex(i).RoomListPosition = positions[Root(i)];
    }
}
