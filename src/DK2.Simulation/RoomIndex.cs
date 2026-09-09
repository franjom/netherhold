// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>Finds the <see cref="RoomInstance"/>s on a map.</summary>
/// <remarks>
/// <para>Two entry points that agree by construction, sharing one flood fill; the choice is about
/// cost. <see cref="RoomAt"/> answers for a single tile and walks only that room;
/// <see cref="Build"/> sweeps the whole map once and is what a caller looping over every tile
/// should use.</para>
/// <para>Nothing is cached. A cache would need invalidating on every path that changes a tile's
/// terrain or owner, and the first one anybody forgets gives a room that disagrees with the map —
/// the failure keeping membership in <see cref="TileState"/> would have risked. Recomputing is
/// cheap: a single-room fill is bounded by the room, a full sweep visits each tile once.</para>
/// </remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/rooms-and-gold.md")]
public static class RoomIndex
{
    /// <summary>Orthogonal only — two tiles joined at a corner are two rooms.</summary>
    private static readonly int[] NeighbourDx = [0, -1, 1, 0];
    private static readonly int[] NeighbourDy = [-1, 0, 0, 1];

    /// <summary>
    /// The room the tile at <paramref name="tileIndex"/> belongs to, or null if
    /// the tile is not room terrain.
    /// </summary>
    public static RoomInstance? RoomAt(WorldState world, GameDatabase database, int tileIndex)
    {
        if (tileIndex < 0 || tileIndex >= world.TileCount) return null;

        ref TileState tile = ref world.TileAtIndex(tileIndex);
        RoomDef? definition = database.RoomByTerrain(tile.TerrainId);
        if (definition is null) return null;

        return Fill(world, definition, tile.TerrainId, tile.OwnerPlayerId, tileIndex, visited: null);
    }

    /// <summary>
    /// Every room on the map, in ascending order of their lowest tile index.
    ///
    /// One sweep, each tile visited once. Use this rather than
    /// <see cref="RoomAt"/> in a loop.
    /// </summary>
    public static List<RoomInstance> Build(WorldState world, GameDatabase database)
    {
        var rooms = new List<RoomInstance>();
        var visited = new bool[world.TileCount];

        for (int i = 0; i < world.TileCount; i++)
        {
            if (visited[i]) continue;

            ref TileState tile = ref world.TileAtIndex(i);
            RoomDef? definition = database.RoomByTerrain(tile.TerrainId);
            if (definition is null)
            {
                visited[i] = true;
                continue;
            }

            rooms.Add(Fill(world, definition, tile.TerrainId, tile.OwnerPlayerId, i, visited));
        }

        return rooms;
    }

    /// <summary>
    /// Flood-fills one room from <paramref name="startIndex"/>, orthogonally,
    /// across tiles of the same terrain with the same owner.
    ///
    /// <para>Terrain <i>and</i> owner: two keepers' treasuries meeting along a
    /// line are two treasuries, which matters the moment a rule asks how big
    /// "this" room is. A shared <paramref name="visited"/> array makes the
    /// whole-map sweep linear; passing null gives a standalone fill.</para>
    /// </summary>
    private static RoomInstance Fill(
        WorldState world,
        RoomDef definition,
        byte terrainId,
        byte ownerPlayerId,
        int startIndex,
        bool[]? visited)
    {
        var localVisited = visited ?? new bool[world.TileCount];
        var tiles = new List<int>();
        var frontier = new List<int> { startIndex };
        localVisited[startIndex] = true;

        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;

        for (int head = 0; head < frontier.Count; head++)
        {
            int current = frontier[head];
            tiles.Add(current);

            int cx = current % world.Width;
            int cy = current / world.Width;
            if (cx < minX) minX = cx;
            if (cx > maxX) maxX = cx;
            if (cy < minY) minY = cy;
            if (cy > maxY) maxY = cy;

            for (int i = 0; i < NeighbourDx.Length; i++)
            {
                int nx = cx + NeighbourDx[i];
                int ny = cy + NeighbourDy[i];
                if (!world.Contains(nx, ny)) continue;

                int n = (ny * world.Width) + nx;
                if (localVisited[n]) continue;

                ref TileState neighbour = ref world.TileAtIndex(n);
                if (neighbour.TerrainId != terrainId) continue;
                if (neighbour.OwnerPlayerId != ownerPlayerId) continue;

                localVisited[n] = true;
                frontier.Add(n);
            }
        }

        // Ascending tile order, so that anything iterating a room — or picking
        // one tile of several — is reproducible. The fill order is not.
        tiles.Sort();

        var room = new RoomInstance(
            definition, ownerPlayerId, [.. tiles], minX, minY, maxX, maxY);
        room.EdgeTileCount = CountEdgeTiles(world, room);
        return room;
    }

    /// <summary>
    /// Tiles of the room with at least one orthogonal neighbour that is not part
    /// of it — including the map edge, so a room against the border counts that
    /// side as rim.
    /// </summary>
    private static int CountEdgeTiles(WorldState world, RoomInstance room)
    {
        var member = new HashSet<int>(room.TileIndices);
        int edges = 0;

        foreach (int index in room.TileIndices)
        {
            int cx = index % world.Width;
            int cy = index / world.Width;

            for (int i = 0; i < NeighbourDx.Length; i++)
            {
                int nx = cx + NeighbourDx[i];
                int ny = cy + NeighbourDy[i];

                if (!world.Contains(nx, ny) || !member.Contains((ny * world.Width) + nx))
                {
                    edges++;
                    break;
                }
            }
        }

        return edges;
    }
}
