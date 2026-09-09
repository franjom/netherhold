// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>The tiles a room's own structure stands on, which nothing may walk over.</summary>
/// <remarks>
/// <para>CONFIRMED by the owner (2026-09-07, Q88): a portal's centre tile is not walkable and nor is
/// a dungeon heart's — the portal's centre is reserved for the arriving animation and for the Hand of Evil to throw creatures out of the dungeon (<see cref="HandRules"/>).</para>
/// <para>Two shapes. A portal blocks its single centre tile, the one <see cref="PortalRules"/>
/// already spawns a new creature on (ADR-0042). A heart blocks its centre 3x3: a keeper with no
/// treasury tops out at 16000 gold against a <c>MaxGoldPerDungeonHeartTile</c> of 1000, so 16 of the
/// heart's 25 tiles store gold and the other 9 are structure (Q52), and
/// <see cref="RoomInstance.IsCentrePlatform"/> is the same test the gold rules use, so the two cannot drift apart.</para>
/// <para>The original's mechanism is NOT this and is UNREAD (Q209): DK2 blocks these tiles with
/// objects, the object flags are mapped, and the routine that turns an object into an impassable
/// tile has not been found (structure-blocks.md §5). When it is, delete this type and teach the path grid about objects rather than rooms.</para></remarks>
[Dk2Behavior(Confidence.Confirmed, "docs/research/structure-blocks.md")]
public sealed class StructureBlocks
{
    private static readonly StructureBlocks Empty = new(new HashSet<int>());

    private readonly HashSet<int> _blocked;

    private StructureBlocks(HashSet<int> blocked) => _blocked = blocked;

    /// <summary>How many tiles are blocked. Zero on a map with no portal or heart.</summary>
    public int Count => _blocked.Count;

    /// <summary>Whether a room's structure stands on this tile index.</summary>
    public bool Blocks(int tileIndex) => _blocked.Contains(tileIndex);

    /// <summary>Works out which tiles a portal's or a heart's structure occupies. Scans for the two
    /// room terrains first and returns the shared empty instance when there are none — the common
    /// case for a test map — so the cost is one byte compare per tile plus a flood fill over portal
    /// and heart tiles only. Compute once a tick and share it: <see cref="WorldPathGrid"/> is built per creature per tick by <see cref="Systems.CreatureMovementSystem"/>.</summary>
    public static StructureBlocks For(WorldState world, GameDatabase database)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        if (database is null) throw new ArgumentNullException(nameof(database));

        // The two terrain ids up front, from the ROOMS records' own terrain column, so the per-tile
        // test is a byte compare and not a dictionary lookup.
        byte portalTerrain = 0;
        byte heartTerrain = 0;
        foreach (RoomDef room in database.Rooms.Values)
        {
            if (room.TypeId == RoomTypeId.Portal) portalTerrain = room.TerrainId;
            else if (room.TypeId == RoomTypeId.DungeonHeart) heartTerrain = room.TerrainId;
        }

        if (portalTerrain == 0 && heartTerrain == 0) return Empty;

        List<int>? seeds = null;
        for (int i = 0; i < world.TileCount; i++)
        {
            byte terrainId = world.TileAtIndex(i).TerrainId;
            if (terrainId != portalTerrain && terrainId != heartTerrain) continue;

            (seeds ??= []).Add(i);
        }

        if (seeds is null) return Empty;

        var blocked = new HashSet<int>();
        var seen = new HashSet<int>();

        foreach (int seed in seeds)
        {
            if (seen.Contains(seed)) continue;

            RoomInstance? room = RoomIndex.RoomAt(world, database, seed);
            if (room is null)
            {
                seen.Add(seed);
                continue;
            }

            foreach (int index in room.TileIndices) seen.Add(index);
            Add(world, room, blocked);
        }

        return blocked.Count == 0 ? Empty : new StructureBlocks(blocked);
    }

    private static void Add(WorldState world, RoomInstance room, HashSet<int> blocked)
    {
        switch (room.Definition.TypeId)
        {
            case RoomTypeId.Portal:
                // The single centre tile: the hole a creature arrives through, and the one a keeper
                // drops a creature into to be rid of it. A room with no middle has no structure —
                // the same guard RoomInstance.IsCentrePlatform makes for the heart: a portal
                // smaller than 3x3 does not exist in DK2, and blocking the only tile of a 1x1 test
                // portal would wall off the thing a deserter is walking to.
                if (room.Width >= 3 && room.Height >= 3)
                    blocked.Add(PortalRules.EntranceTileIndex(world, room));
                break;

            case RoomTypeId.DungeonHeart:
                // The middle nine, the raised platform Q52's 16000 pinned. IsCentrePlatform answers
                // false for a room narrower than 3, so a half-destroyed heart blocks nothing and
                // does not trap the creatures standing in it.
                foreach (int index in room.TileIndices)
                {
                    int x = index % world.Width;
                    int y = index / world.Width;
                    if (room.IsCentrePlatform(x, y)) blocked.Add(index);
                }
                break;
        }
    }
}
