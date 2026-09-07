// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>
/// The tiles a room's own structure stands on, which nothing may walk over.
///
/// <para><b>CONFIRMED by the owner, 2026-09-07 (Q88)</b>, asked whether a
/// portal's centre tile is walkable: <i>"no it is not walkable, dungeon heart's
/// center is also not walkable. portal's center is reserved for creature's
/// 'arriving' animation and for hand of evil to throw creatures in, removing
/// them from dungeon"</i>. Two rooms, and the portal's centre has two jobs —
/// the second of which is a keeper power (<see cref="HandRules"/>).</para>
///
/// <para><b>Two tiles, two different shapes, and each rests on something already
/// established rather than on a guess:</b></para>
///
/// <list type="bullet">
/// <item><b>A portal blocks its single centre tile.</b> That is the tile
/// <see cref="PortalRules"/> already spawns a new creature on (ADR-0042), which
/// the owner's answer identifies as the arriving animation — so the same tile
/// was already special and is now also a hole.</item>
/// <item><b>A dungeon heart blocks its centre 3x3.</b> Not one tile: the middle
/// nine are the raised platform the heart's structure stands on, and this
/// project knows they are nine rather than one from arithmetic that has nothing
/// to do with walking — a keeper with no treasury tops out at <b>16000</b> gold
/// against a <c>MaxGoldPerDungeonHeartTile</c> of 1000, so 16 of the heart's 25
/// tiles store gold and the other 9 are structure (Q52, CONFIRMED by the owner
/// off the gold counter). A tile that cannot hold gold because the heart is
/// standing on it is a tile a creature cannot stand on either.
/// <see cref="RoomInstance.IsCentrePlatform"/> is the same test the gold rules
/// use, so the two cannot drift apart.</item>
/// </list>
///
/// <para><b>What the original does is NOT what this does, and the mechanism is
/// UNREAD (Q209).</b> The dig went a fair way and stopped short. DK2 does not
/// have a room-level walkability override: it has an <b>obstacle</b> system
/// driven by the object table. Object <b>100 is literally named "Dungeon Heart
/// Obstacle"</b>, carries <c>SolidObstacle</c>, and is one of fifteen objects
/// that do — all of them room furniture (training dummy, anvil, work bench,
/// book case, four tombstones, roulette wheel and table, bar table, barrel,
/// steel bucket, guard post table). <c>FUN_004D12B0</c> enumerates them over a
/// tile rectangle and reports each to a callback as
/// <c>{x, y, size, flags}</c>, where <c>objDef+0xf4</c> is the flags word and
/// <c>objDef+0xf8</c> a size in 4096 fixed point (file offset <c>0x32C</c>,
/// found from the loader's frame slot at <c>EBP+0x3AC</c> in
/// <c>FUN_0049DD60</c>). <b>But that routine is not tile pathing</b>: its gate
/// is bit 14 <c>Obstacle</c>, which <b>58</b> objects carry including every
/// creature bed and every gold pile, and creatures demonstrably stand on both.
/// It also consults <c>FirstPersonObstacle</c> only when a global is set, which
/// says what it is for. So the honest position is: <b>DK2 blocks these tiles
/// with objects, the object flags are mapped, and the routine that turns an
/// object into an impassable tile has not been found.</b> The Portal Crystal
/// (object 99) carries <c>Obstacle</c> without <c>SolidObstacle</c>, so not even
/// the fifteen are the answer on their own.</para>
///
/// <para><b>Why this is built the shallow way anyway.</b> The behaviour is
/// confirmed by the oracle and the mechanism is not, and inventing an
/// object-radius rule would have been worse than a narrow one: the bed data
/// already refutes the obvious candidate. When the real predicate is found this
/// type should be deleted and the path grid should learn about <i>objects</i>
/// — which is a correction to what Q88 used to say, that it would have to learn
/// about <i>rooms</i>.</para>
///
/// <para><b>Cost.</b> One byte compare per tile, plus a flood fill over portal
/// and heart tiles only. <see cref="WorldPathGrid"/> is constructed per creature
/// per tick by <see cref="Systems.CreatureMovementSystem"/>, so the mask is
/// computed once and shared rather than once per grid.</para>
/// </summary>
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

    /// <summary>
    /// Works out which tiles a portal's or a heart's structure occupies.
    ///
    /// <para>Scans for the two room terrains first and returns the shared empty
    /// instance when there are none, which is the common case for a test map and
    /// costs one pass of byte compares.</para>
    /// </summary>
    public static StructureBlocks For(WorldState world, GameDatabase database)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        if (database is null) throw new ArgumentNullException(nameof(database));

        // The two terrain ids up front, from the ROOMS records' own terrain
        // column, so the per-tile test is a byte compare and not a dictionary
        // lookup. A database with neither room answers immediately.
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
                // The single centre tile: the hole a creature arrives through,
                // and the one a keeper drops a creature into to be rid of it.
                //
                // A ROOM WITH NO MIDDLE HAS NO STRUCTURE, which is the same
                // guard RoomInstance.IsCentrePlatform makes for the heart and is
                // here for the same reason: a portal smaller than 3x3 does not
                // exist in DK2, and blocking the only tile of a 1x1 test portal
                // would wall off the thing a deserter is walking to. A portal
                // dug down to a 2-wide strip is a broken portal, not a hole.
                if (room.Width >= 3 && room.Height >= 3)
                    blocked.Add(PortalRules.EntranceTileIndex(world, room));
                break;

            case RoomTypeId.DungeonHeart:
                // The middle nine, which is the raised platform Q52's 16000
                // pinned. IsCentrePlatform answers false for a room narrower
                // than 3, so a half-destroyed heart blocks nothing and does not
                // trap the creatures standing in it.
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
