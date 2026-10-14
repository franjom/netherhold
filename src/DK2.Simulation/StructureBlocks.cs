// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>The tiles a room's own structure stands on, which the path grid and the Hand treat as
/// unwalkable. DK2's captured cache holds floor on a heart and byte 4 on a portal's centre;
/// ADR-0452 separately closes live Heart platforms in our routing mesh.</summary>
/// <remarks>
/// <para>CONFIRMED by the owner (Q88): a portal's centre and a heart's centre are not walked on;
/// the portal's is reserved for the arriving animation and for the Hand (<see cref="HandRules"/>).</para>
/// <para>Two shapes. A portal blocks its single centre tile, the one <see cref="PortalRules"/>
/// already spawns a new creature on (ADR-0042). A heart blocks its centre 3x3: a keeper with no
/// treasury tops out at 16000 gold against a <c>MaxGoldPerDungeonHeartTile</c> of 1000, so 16 of the
/// heart's 25 tiles store gold and the other 9 are structure (Q52; <see cref="RoomInstance.IsCentrePlatform"/>).</para>
/// <para>The original keeps creatures off with objects carrying the <c>Obstacle</c> flags, fed to
/// <c>Repel_Obstacle</c> (<see cref="Navigation.WorldSteering.CollectObstacles"/>); this block is the grid's and the Hand's (structure-blocks.md §5).</para></remarks>
[Dk2Behavior(Confidence.Confirmed, "docs/research/structure-blocks.md")]
public sealed class StructureBlocks
{
    private static readonly StructureBlocks Empty = new(new HashSet<int>(), new HashSet<int>());

    private readonly HashSet<int> _blocked;
    private readonly HashSet<int> _portalCentres;

    private StructureBlocks(HashSet<int> blocked, HashSet<int> portalCentres)
    {
        _blocked = blocked;
        _portalCentres = portalCentres;
    }

    /// <summary>The centre tile of every heart with a platform: where its obstacle object stands
    /// (<see cref="Navigation.WorldSteering.CollectObstacles"/>).</summary>
    public IReadOnlyList<int> HeartCentres { get; private init; } = [];

    /// <summary>How many tiles are blocked. Zero on a map with no portal or heart.</summary>
    public int Count => _blocked.Count;

    /// <summary>Whether a room's structure stands on this tile index.</summary>
    public bool Blocks(int tileIndex) => _blocked.Contains(tileIndex);

    /// <summary>Whether this tile is a portal's centre hole — byte 4 in DK2's navigation cache,
    /// where a heart's platform is plain floor (<see cref="Navigation.WorldSteering.ByteOf"/>).</summary>
    public bool IsPortalCentre(int tileIndex) => _portalCentres.Contains(tileIndex);

    /// <summary>Every portal centre, for a caller that visits them rather than asking per tile.</summary>
    public IReadOnlyCollection<int> PortalCentres => _portalCentres;

    /// <summary>Works out which tiles a portal's or a heart's structure occupies. Scans for the two
    /// room terrains first and returns the shared empty instance when there are none — the common
    /// case for a test map — so the cost is one byte compare per tile plus a flood fill over portal
    /// and heart tiles only. Compute once and share it: <see cref="Navigation.WorldSteering.Refresh"/> reads it once per refresh.</summary>
    public static StructureBlocks For(WorldState world, GameDatabase database)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        if (database is null) throw new ArgumentNullException(nameof(database));

        // The terrain ids up front, from the ROOMS records' own terrain column, so the per-tile
        // test is a byte compare and not a dictionary lookup.
        byte portalTerrain = 0;
        byte heroPortalTerrain = 0;
        byte heartTerrain = 0;
        foreach (RoomDef room in database.Rooms.Values)
        {
            if (room.TypeId == RoomTypeId.Portal) portalTerrain = room.TerrainId;
            else if (room.TypeId == RoomTypeId.HeroPortal) heroPortalTerrain = room.TerrainId;
            else if (room.TypeId == RoomTypeId.DungeonHeart) heartTerrain = room.TerrainId;
        }

        if (portalTerrain == 0 && heroPortalTerrain == 0 && heartTerrain == 0) return Empty;

        // The mask is a function of the structure tiles alone: which tiles carry one of the three
        // terrains, with what owner, since a fill crosses only tiles of its seed's terrain and
        // owner, plus whether a heart tile's keeper has had its heart ruined. So the world's last
        // mask stands while that list is the same, and a scan that compares the list is the whole
        // cost of asking again (ADR-0454).
        Cache cache = world.StructureCache;
        List<long> seeds = cache.Scratch;
        seeds.Clear();
        for (int i = 0; i < world.TileCount; i++)
        {
            ref TileState tile = ref world.TileAtIndex(i);
            byte terrainId = tile.TerrainId;
            if (terrainId == 0) continue;
            if (terrainId != portalTerrain && terrainId != heroPortalTerrain && terrainId != heartTerrain) continue;

            long ruined = terrainId == heartTerrain && world.HasPlayer(tile.OwnerPlayerId)
                && world.Player(tile.OwnerPlayerId).HeartRuined ? 1L << 40 : 0;
            seeds.Add(ruined | ((long)i << 16) | ((long)terrainId << 8) | tile.OwnerPlayerId);
        }

        if (cache.Blocks is not null && ReferenceEquals(cache.Database, database) && SameSeeds(seeds, cache.Seeds))
            return cache.Blocks;

        StructureBlocks blocks = Compute(world, database, seeds);
        cache.Database = database;
        cache.Blocks = blocks;
        (cache.Seeds, cache.Scratch) = (seeds, cache.Seeds);
        return blocks;
    }

    /// <summary>The world's last mask and the structure tiles it was computed from.</summary>
    internal sealed class Cache
    {
        internal GameDatabase? Database;
        internal StructureBlocks? Blocks;
        internal List<long> Seeds = [];
        internal List<long> Scratch = [];
    }

    private static bool SameSeeds(List<long> a, List<long> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (a[i] != b[i]) return false;
        return true;
    }

    private static StructureBlocks Compute(WorldState world, GameDatabase database, List<long> seedKeys)
    {
        if (seedKeys.Count == 0) return Empty;

        var blocked = new HashSet<int>();
        var portalCentres = new HashSet<int>();
        var heartCentres = new List<int>();
        var seen = new HashSet<int>();

        foreach (long key in seedKeys)
        {
            int seed = (int)((key >> 16) & 0xFFFFFF);
            if (seen.Contains(seed)) continue;

            RoomInstance? room = RoomIndex.RoomAt(world, database, seed);
            if (room is null)
            {
                seen.Add(seed);
                continue;
            }

            foreach (int index in room.TileIndices) seen.Add(index);
            Add(world, room, blocked, portalCentres, heartCentres);
        }

        return blocked.Count == 0 ? Empty : new StructureBlocks(blocked, portalCentres) { HeartCentres = heartCentres };
    }

    private static void Add(WorldState world, RoomInstance room, HashSet<int> blocked, HashSet<int> portalCentres, List<int> heartCentres)
    {
        switch (room.Definition.TypeId)
        {
            // A Hero Portal carries the Portal's handler set byte for byte, its tile pass 0x004DCD20
            // included, so its centre is the same hole (portals-room-handler-table.md).
            case RoomTypeId.Portal:
            case RoomTypeId.HeroPortal:
                // The single centre tile: the hole a creature arrives through, and the one a keeper
                // drops a creature into to be rid of it. A room with no middle has no structure —
                // the same guard RoomInstance.IsCentrePlatform makes for the heart: a portal
                // smaller than 3x3 does not exist in DK2, and blocking the only tile of a 1x1 test
                // portal would wall off the thing a deserter is walking to.
                if (room.Width >= 3 && room.Height >= 3)
                {
                    int centre = PortalRules.EntranceTileIndex(world, room);
                    blocked.Add(centre);
                    portalCentres.Add(centre);
                }
                break;

            case RoomTypeId.DungeonHeart:
                if (world.HasPlayer(room.OwnerPlayerId) && world.Player(room.OwnerPlayerId).HeartRuined) break;
                // The middle nine, the raised platform Q52's 16000 pinned. IsCentrePlatform answers
                // false for a room narrower than 3, so a half-destroyed heart blocks nothing and
                // does not trap the creatures standing in it.
                bool platform = false;
                foreach (int index in room.TileIndices)
                {
                    int x = index % world.Width;
                    int y = index / world.Width;
                    if (room.IsCentrePlatform(x, y)) { blocked.Add(index); platform = true; }
                }
                if (platform)
                    heartCentres.Add((((room.MinY + room.MaxY) / 2) * world.Width) + ((room.MinX + room.MaxX) / 2));
                break;
        }
    }
}
