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
/// <para>The whole-map sweep is kept per world and revalidated against every tile's terrain and
/// owner and the database instance, never invalidated (ADR-0466). Its list and rooms are shared
/// until the map changes, so neither may be mutated. The other two entry points are uncached.</para>
/// </remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/rooms-and-gold.md")]
public static class RoomIndex
{
    /// <summary>Orthogonal only — two tiles joined at a corner are two rooms.</summary>
    private static readonly int[] NeighbourDx = [0, -1, 1, 0];
    private static readonly int[] NeighbourDy = [-1, 0, 0, 1];

    [ThreadStatic] private static bool[]? s_visited;
    [ThreadStatic] private static RoomDef?[]? s_wanted;

    /// <summary>A visited array of at least <paramref name="count"/> tiles, all clear. One per
    /// thread: a sweep hands it to each fill, and a standalone fill takes it whole, so no caller
    /// may hold one across another call on the same thread, which none does.</summary>
    private static bool[] ClearedVisited(int count)
    {
        bool[]? visited = s_visited;
        if (visited is null || visited.Length < count) s_visited = visited = new bool[count];
        else Array.Clear(visited, 0, count);
        return visited;
    }

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
    public static IReadOnlyList<RoomInstance> Build(WorldState world, GameDatabase database)
    {
        Cache cache = world.RoomSweepCache;
        if (cache.Matches(world, database)) return cache.Rooms!;

        List<RoomInstance> rooms = Sweep(world, database);
        cache.Remember(world, database, rooms);
        return rooms;
    }

    /// <summary>The sweep <see cref="Build(WorldState, GameDatabase)"/> caches.</summary>
    private static List<RoomInstance> Sweep(WorldState world, GameDatabase database)
    {
        var rooms = new List<RoomInstance>();
        bool[] visited = ClearedVisited(world.TileCount);

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
    /// The rooms <see cref="Build(WorldState, GameDatabase)"/> returns whose definition passes
    /// <paramref name="include"/>, in the same order, without filling the others: rooms are
    /// disjoint, so a room left unfilled changes no other room's tiles. For a caller that needs one
    /// kind of room, such as the navigation cache's DoubleQuad floors, it is the cost of those
    /// rooms rather than of the map.
    /// </summary>
    public static List<RoomInstance> Build(WorldState world, GameDatabase database, Predicate<RoomDef> include)
    {
        if (include is null) throw new ArgumentNullException(nameof(include));
        var rooms = new List<RoomInstance>();

        // Which terrain ids hold a wanted room, asked once per id rather than once per tile.
        RoomDef?[] wanted = s_wanted ??= new RoomDef?[256];
        Array.Clear(wanted, 0, wanted.Length);
        bool any = false;
        for (int terrain = 0; terrain < wanted.Length; terrain++)
        {
            RoomDef? definition = database.RoomByTerrain((byte)terrain);
            if (definition is null || !include(definition)) continue;
            wanted[terrain] = definition;
            any = true;
        }
        if (!any) return rooms;

        bool[] visited = ClearedVisited(world.TileCount);
        for (int i = 0; i < world.TileCount; i++)
        {
            if (visited[i]) continue;

            ref TileState tile = ref world.TileAtIndex(i);
            RoomDef? definition = wanted[tile.TerrainId];
            if (definition is null) continue;

            rooms.Add(Fill(world, definition, tile.TerrainId, tile.OwnerPlayerId, i, visited));
        }

        return rooms;
    }

    /// <summary>A world's last whole-map sweep and the terrain and owner of every tile it read.
    /// Derived state, never saved or hashed.</summary>
    internal sealed class Cache
    {
        private GameDatabase? _database;
        private ushort[] _tiles = [];
        private int _count = -1;

        public IReadOnlyList<RoomInstance>? Rooms { get; private set; }

        /// <summary>Whether <see cref="Rooms"/> is what a sweep of the world would give now: the
        /// same database, and each tile's terrain and owner as the sweep saw them.</summary>
        public bool Matches(WorldState world, GameDatabase database)
        {
            if (Rooms is null || !ReferenceEquals(_database, database) || _count != world.TileCount) return false;
            ushort[] seen = _tiles;
            for (int i = 0; i < seen.Length; i++)
            {
                ref TileState tile = ref world.TileAtIndex(i);
                if (seen[i] != Key(tile)) return false;
            }
            return true;
        }

        public void Remember(WorldState world, GameDatabase database, List<RoomInstance> rooms)
        {
            int count = world.TileCount;
            if (_tiles.Length != count) _tiles = new ushort[count];
            for (int i = 0; i < count; i++) _tiles[i] = Key(world.TileAtIndex(i));
            _database = database;
            _count = count;
            Rooms = rooms;
        }

        private static ushort Key(in TileState tile) => (ushort)((tile.TerrainId << 8) | tile.OwnerPlayerId);
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
        PlayerId ownerPlayerId,
        int startIndex,
        bool[]? visited)
    {
        bool[] localVisited = visited ?? ClearedVisited(world.TileCount);
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
        room.InteriorTileIndices = SurroundedWithin(world, room.TileIndices);
        room.InteriorRimTileCount = room.InteriorTileIndices.Count
            - SurroundedWithin(world, room.InteriorTileIndices).Length;
        return room;
    }

    /// <summary>
    /// The members whose four orthogonal neighbours are all members too. Everything
    /// else is rim — including a tile against the map border, which counts that side
    /// as rim rather than as room.
    /// <para>Applied to a room's tiles this gives the interior; applied to that
    /// interior it gives the interior's own core, and what is left is the ring a
    /// graveyard puts its graves on (Q60). One function, so both rings generalise to
    /// an L-shape the same way rather than by a bounding-box formula.</para>
    /// </summary>
    private static int[] SurroundedWithin(WorldState world, IReadOnlyList<int> members)
    {
        var member = new HashSet<int>(members);
        var interior = new List<int>(members.Count);

        foreach (int index in members)
        {
            int cx = index % world.Width;
            int cy = index / world.Width;
            bool surrounded = true;

            for (int i = 0; i < NeighbourDx.Length; i++)
            {
                int nx = cx + NeighbourDx[i];
                int ny = cy + NeighbourDy[i];

                if (world.Contains(nx, ny) && member.Contains((ny * world.Width) + nx)) continue;

                surrounded = false;
                break;
            }

            if (surrounded) interior.Add(index);
        }

        // The input is already ascending, so this is too.
        return [.. interior];
    }
}
