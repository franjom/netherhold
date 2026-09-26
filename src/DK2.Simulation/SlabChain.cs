// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>A room's slab chain: the order DK2 links a room's tiles in, <c>FirstSlab</c> to
/// <c>LastSlab</c> (room-slab-chain.md). Every "walk the room" routine follows it, so it decides
/// the Prison's door, the pit finder's distance and a random room tile.</summary>
/// <remarks>Rooms here are still found from the map (<see cref="RoomIndex"/>); only the order is
/// state, as <see cref="TileState.SlabOrder"/>. Tiles with equal orders, the map's own at 0, come
/// in tile-index order, which is the row-major order DK2 loads a level's rooms in (<c>0x0056D2D0</c>).</remarks>
[Dk2Behavior(Confidence.Confirmed, "docs/research/room-slab-chain.md")]
public static class SlabChain
{
    /// <summary><c>0x006BC350</c>: N, E, S, W. The first neighbour in this order names the room a
    /// new tile joins; each later one of a different room is merged in behind it.</summary>
    private static readonly int[] Dx = [0, 1, 0, -1];
    private static readonly int[] Dy = [-1, 0, 1, 0];

    /// <summary>The room's tiles in chain order.</summary>
    public static int[] Order(WorldState world, RoomInstance room)
    {
        int[] tiles = [.. room.TileIndices];
        Sort(world, tiles);
        return tiles;
    }

    /// <summary><c>FirstSlab</c> (<c>room+0x2D</c>): the head of the chain.</summary>
    public static int FirstSlab(WorldState world, RoomInstance room)
    {
        int best = -1;
        foreach (int tile in room.TileIndices)
            if (best < 0 || Before(world, tile, best)) best = tile;
        return best;
    }

    /// <summary>Call after <paramref name="tileIndex"/> changed terrain or owner, with what it was
    /// before: it leaves the room it was in and joins the one it is now in, if either is a room.</summary>
    public static void Changed(WorldState world, GameDatabase database, int tileIndex, byte terrainBefore, byte ownerBefore)
    {
        ref TileState tile = ref world.TileAtIndex(tileIndex);
        if (tile.TerrainId == terrainBefore && tile.OwnerPlayerId == ownerBefore) return;
        if (database.RoomByTerrain(terrainBefore) is not null) Removed(world, tileIndex, terrainBefore, ownerBefore);
        if (database.RoomByTerrain(tile.TerrainId) is not null) Added(world, tileIndex);
        else tile.SlabOrder = 0;
    }

    /// <summary><c>0x004ED320</c>, for a tile that has just become room terrain: it joins the room
    /// of its first N/E/S/W neighbour of the same terrain and owner, at the tail (<c>0x004EC650</c>),
    /// and every later neighbour's different room is appended after it (<c>0x004ECB70</c>). With no
    /// such neighbour it starts a room of its own (<c>0x004EC5A0</c>).</summary>
    public static void Added(WorldState world, int tileIndex)
    {
        ref TileState tile = ref world.TileAtIndex(tileIndex);
        byte terrain = tile.TerrainId, owner = tile.OwnerPlayerId;
        var taken = new HashSet<int> { tileIndex };
        List<int>? chain = null;
        for (int d = 0; d < Dx.Length; d++)
        {
            int n = Neighbour(world, tileIndex, d);
            if (n < 0 || taken.Contains(n) || !Same(world, n, terrain, owner)) continue;
            List<int> room = Fill(world, n, terrain, owner, taken);
            Sort(world, room);
            if (chain is null)
            {
                chain = room;
                chain.Add(tileIndex);
            }
            else chain.AddRange(room);
        }

        Stamp(world, chain ?? [tileIndex]);
    }

    /// <summary><c>0x004ED7B0</c>, for a tile that has just stopped being a tile of the room with
    /// <paramref name="terrain"/> and <paramref name="owner"/>: the room's other tiles are taken in
    /// their old chain order and inserted again one by one by <see cref="Added"/>'s rule, among only
    /// those already re-inserted. A cut room comes back as several.</summary>
    public static void Removed(WorldState world, int tileIndex, byte terrain, byte owner)
    {
        var old = new HashSet<int>();
        for (int d = 0; d < Dx.Length; d++)
        {
            int n = Neighbour(world, tileIndex, d);
            if (n >= 0 && !old.Contains(n) && Same(world, n, terrain, owner)) Fill(world, n, terrain, owner, old);
        }

        int[] order = [.. old];
        Sort(world, order);

        var chainOf = new Dictionary<int, List<int>>(order.Length);
        var chains = new List<List<int>>();
        foreach (int tile in order)
        {
            List<int>? joined = null;
            for (int d = 0; d < Dx.Length; d++)
            {
                int n = Neighbour(world, tile, d);
                if (n < 0 || !chainOf.TryGetValue(n, out List<int>? other)) continue;
                if (joined is null)
                {
                    joined = other;
                    joined.Add(tile);
                    chainOf[tile] = joined;
                }
                else if (!ReferenceEquals(other, joined))
                {
                    foreach (int moved in other)
                    {
                        joined.Add(moved);
                        chainOf[moved] = joined;
                    }

                    chains.Remove(other);
                }
            }

            if (joined is null)
            {
                var alone = new List<int> { tile };
                chainOf[tile] = alone;
                chains.Add(alone);
            }
        }

        foreach (List<int> chain in chains) Stamp(world, chain);
    }

    /// <summary>What a saved game's load does to the chains (<c>0x0044F4C0</c>, <c>0x00452BD0</c>):
    /// every room tile is re-added row by row to the room it was saved in, with no merge, so each
    /// chain becomes its tiles in row-major order — order 0 here.</summary>
    public static void AsLoaded(WorldState world)
    {
        for (int i = 0; i < world.TileCount; i++) world.TileAtIndex(i).SlabOrder = 0;
    }

    /// <summary><c>0x004C7380</c>'s wave over a dragged rectangle: from the second corner, one
    /// diagonal at a time (<c>|x − x2| + |y − y2|</c>), and along each diagonal from x2 toward x1.
    /// It ends at the first corner. Selling (<c>0x004C7160</c>) sweeps the same way.</summary>
    public static List<TilePos> Wave(int x1, int y1, int x2, int y2)
    {
        int dx = Math.Abs(x1 - x2), dy = Math.Abs(y1 - y2);
        int sx = x2 <= x1 ? 1 : -1, sy = y2 <= y1 ? 1 : -1;
        var tiles = new List<TilePos>((dx + 1) * (dy + 1));
        for (int e = 0; e <= dx + dy; e++)
            for (int i = Math.Max(0, e - dy); i <= Math.Min(e, dx); i++)
                tiles.Add(new TilePos(x2 + (i * sx), y2 + ((e - i) * sy)));
        return tiles;
    }

    private static void Stamp(WorldState world, List<int> chain)
    {
        foreach (int tile in chain) world.TileAtIndex(tile).SlabOrder = world.NextSlabOrder++;
    }

    private static bool Before(WorldState world, int a, int b)
    {
        int oa = world.TileAtIndex(a).SlabOrder, ob = world.TileAtIndex(b).SlabOrder;
        return oa != ob ? oa < ob : a < b;
    }

    private static void Sort(WorldState world, int[] tiles) =>
        Array.Sort(tiles, (a, b) => Before(world, a, b) ? -1 : a == b ? 0 : 1);

    private static void Sort(WorldState world, List<int> tiles) =>
        tiles.Sort((a, b) => Before(world, a, b) ? -1 : a == b ? 0 : 1);

    private static int Neighbour(WorldState world, int tile, int d)
    {
        int x = (tile % world.Width) + Dx[d], y = (tile / world.Width) + Dy[d];
        return world.Contains(x, y) ? (y * world.Width) + x : -1;
    }

    private static bool Same(WorldState world, int tile, byte terrain, byte owner)
    {
        ref TileState t = ref world.TileAtIndex(tile);
        return t.TerrainId == terrain && t.OwnerPlayerId == owner;
    }

    /// <summary>The tiles of one room, orthogonally connected, not entering <paramref name="taken"/>,
    /// which gains them.</summary>
    private static List<int> Fill(WorldState world, int start, byte terrain, byte owner, HashSet<int> taken)
    {
        var tiles = new List<int> { start };
        taken.Add(start);
        for (int i = 0; i < tiles.Count; i++)
            for (int d = 0; d < Dx.Length; d++)
            {
                int n = Neighbour(world, tiles[i], d);
                if (n >= 0 && !taken.Contains(n) && Same(world, n, terrain, owner))
                {
                    taken.Add(n);
                    tiles.Add(n);
                }
            }

        return tiles;
    }
}
