// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>A room's slab chain: the order DK2 links a room's tiles in, <c>FirstSlab</c> to
/// <c>LastSlab</c> (room-slab-chain.md). Order-sensitive room readers use it.</summary>
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
    public static void Changed(WorldState world, GameDatabase database, int tileIndex, byte terrainBefore, PlayerId ownerBefore)
    {
        ref TileState tile = ref world.TileAtIndex(tileIndex);
        if (tile.TerrainId == terrainBefore && tile.OwnerPlayerId == ownerBefore) return;
        tile.ChickensGenerated = 0;

        // The tile's part of the old room's deficit goes with the old room; whatever it joins, it
        // joins as a fresh tile (0x004EC5A0, 0x004EC650).
        int lost = tile.RoomHealthLost;
        tile.RoomHealthLost = 0;
        if (database.RoomByTerrain(terrainBefore) is { } before)
            Removed(world, database, tileIndex, terrainBefore, ownerBefore, before, lost);
        if (database.RoomByTerrain(tile.TerrainId) is { } after)
        {
            List<int> chain = Added(world, tileIndex);
            if (after.TypeId == RoomTypeId.Prison) Activate(world, chain);
        }
        else (tile.SlabOrder, tile.RoomSizeChangedTick, tile.RoomKickedTick) = (0, 0, 0);
        if (tile.TerrainId != terrainBefore) KickNeighbours(world, database, tileIndex);
    }

    /// <summary>A holder a neighbour's change picked: no room's stamp equals it.</summary>
    public const long KickedMark = -1;

    /// <summary><c>0x004E44E0</c>, which <c>Tile_SetTerrain</c> calls for each N/E/S/W neighbour that
    /// is a room tile (<c>0x00450214</c>), for a room whose type has <c>SpecialWalls</c>: a Library's
    /// holder to 62, a Training Room's to 113, a Workshop's to 70, a Torture Chamber's to 136 and a
    /// Casino's to 154; the Prison has the flag and no arm. ONE holder per call: the loop steps
    /// through the kicked creature's own link, which its release (<c>0x004E5E00</c>) has zeroed, so it
    /// takes the list's head, the latest claimant, and stops. Ours marks that holder, skipping one a
    /// change has already marked, and it takes the kick at its next turn (<see cref="RoomEvictRules"/>).
    /// Not modelled: a head whose kick the transition table refuses, after which DK2 tries the next.</summary>
    internal static void KickNeighbours(WorldState world, GameDatabase database, int tileIndex)
    {
        int x = tileIndex % world.Width, y = tileIndex / world.Width;
        for (int d = 0; d < Dx.Length; d++)
        {
            int nx = x + Dx[d], ny = y + Dy[d];
            if (nx < 0 || ny < 0 || nx >= world.Width || ny >= world.Height) continue;
            int n = (ny * world.Width) + nx;
            if (database.RoomByTerrain(world.TileAtIndex(n).TerrainId) is not { } definition
                || !definition.Flags.HasFlag(RoomFlags.SpecialWalls)
                || definition.TypeId is not (RoomTypeId.Library or RoomTypeId.TrainingRoom or RoomTypeId.Workshop
                    or RoomTypeId.TortureChamber or RoomTypeId.Casino)
                || RoomIndex.RoomAt(world, database, n) is not { } room)
                continue;

            CreatureState? head = null;
            foreach (CreatureState c in world.Creatures)
            {
                if (!c.HeldPlace.In(room) || c.HeldPlaceStamp == KickedMark) continue;
                if (head is null || c.HeldPlaceSerial > head.HeldPlaceSerial) head = c;
            }

            if (head is not null) head.HeldPlaceStamp = KickedMark;
        }
    }

    /// <summary>Stamps <paramref name="room"/>'s place-holders as kicked this turn
    /// (<see cref="TileState.RoomKickedTick"/>).</summary>
    internal static void Kick(WorldState world, RoomInstance room)
    {
        long now = world.CurrentTick.Value;
        foreach (int tile in room.TileIndices) world.TileAtIndex(tile).RoomKickedTick = now;
    }

    /// <summary>The later of the room's rebuild and its last kick: a holder that claimed its place
    /// before this has been kicked since (<see cref="RoomEvictRules"/>).</summary>
    public static long HoldersStamp(WorldState world, RoomInstance room)
    {
        long stamp = 0;
        foreach (int tile in room.TileIndices)
        {
            ref TileState state = ref world.TileAtIndex(tile);
            stamp = Math.Max(stamp, Math.Max(state.RoomSizeChangedTick, state.RoomKickedTick));
        }

        return stamp;
    }

    /// <summary><c>0x004ED320</c>, for a tile that has just become room terrain: it joins the room
    /// of its first N/E/S/W neighbour of the same terrain and owner, at the tail (<c>0x004EC650</c>),
    /// and every later neighbour's different room is appended after it (<c>0x004ECB70</c>). With no
    /// such neighbour it starts a room of its own (<c>0x004EC5A0</c>).</summary>
    /// <remarks>The room keeps the joined room's fields; an absorbed room's are dropped
    /// (<c>0x004ECD00</c> frees it). Of those fields ours keeps two sets on a tile, the room's lowest:
    /// the Prison's door byte (<see cref="JailBreakRules.DoorOf"/>) and the Casino's party
    /// (<see cref="CasinoJackpotRules.IsPartyOn"/>), so they move to the new lowest.</remarks>
    public static List<int> Added(WorldState world, int tileIndex)
    {
        ref TileState tile = ref world.TileAtIndex(tileIndex);
        byte terrain = tile.TerrainId, owner = tile.OwnerPlayerId;
        var taken = new HashSet<int> { tileIndex };
        List<int>? chain = null;
        byte door = 0, party = 0;
        long partyStart = 0;
        for (int d = 0; d < Dx.Length; d++)
        {
            int n = Neighbour(world, tileIndex, d);
            if (n < 0 || taken.Contains(n) || !Same(world, n, terrain, owner)) continue;
            List<int> room = Fill(world, n, terrain, owner, taken);
            Sort(world, room);
            if (chain is null)
            {
                foreach (int t in room)
                {
                    ref TileState kept = ref world.TileAtIndex(t);
                    door |= kept.PrisonDoor;
                    party |= kept.CasinoParty;
                    partyStart = Math.Max(partyStart, kept.CasinoPartyStartTick);
                }

                chain = room;
                chain.Add(tileIndex);
            }
            else chain.AddRange(room);
        }

        chain ??= [tileIndex];
        Stamp(world, chain);
        int lowest = KeepDoor(world, chain, door);
        ref TileState head = ref world.TileAtIndex(lowest);
        (head.CasinoParty, head.CasinoPartyStartTick) = (party, partyStart);
        return chain;
    }

    /// <summary>The Prison's slot +0x09 (<c>0x004E2B30</c>) sets <c>Activation</c> (<c>+0x59</c>) when
    /// the room is one tile (<c>+0xA7</c> = 1), so a Prison has it from its first tile on, whether
    /// built or loaded with the map, whose tiles are inserted one by one (<c>0x004C5C30</c>).</summary>
    private static void Activate(WorldState world, List<int> chain)
    {
        if (chain.Count != 1) return;
        world.TileAtIndex(chain[0]).PrisonDoor |= JailBreakRules.Activated;
    }

    /// <summary>Puts a room's door byte on its lowest tile and clears it and the Casino's party from
    /// every tile; returns the lowest.</summary>
    private static int KeepDoor(WorldState world, List<int> chain, byte door)
    {
        int lowest = int.MaxValue;
        foreach (int t in chain)
        {
            ref TileState tile = ref world.TileAtIndex(t);
            (tile.PrisonDoor, tile.CasinoParty, tile.CasinoPartyStartTick) = (0, 0, 0);
            lowest = Math.Min(lowest, t);
        }

        world.TileAtIndex(lowest).PrisonDoor = door;
        return lowest;
    }

    /// <summary><c>0x004ED7B0</c>, for a tile that has just stopped being a tile of the room with
    /// <paramref name="terrain"/> and <paramref name="owner"/>: the room's other tiles are taken in
    /// their old chain order and inserted again one by one by <see cref="Added"/>'s rule, among only
    /// those already re-inserted. A cut room comes back as several, and <c>0x004ED440</c> gives each
    /// the old room's share of health (<see cref="RoomHealthRules.SharePercent"/>);
    /// <paramref name="lost"/> is the leaving tile's part of the old deficit.</summary>
    public static void Removed(
        WorldState world, GameDatabase database, int tileIndex, byte terrain, PlayerId owner, RoomDef room, int lost)
    {
        var old = new HashSet<int>();
        for (int d = 0; d < Dx.Length; d++)
        {
            int n = Neighbour(world, tileIndex, d);
            if (n >= 0 && !old.Contains(n) && Same(world, n, terrain, owner)) Fill(world, n, terrain, owner, old);
        }

        // The share is taken of the room as it was, the leaving tile still counted (0x004ED54F).
        int size = old.Count + 1;
        foreach (int tile in old) lost += world.TileAtIndex(tile).RoomHealthLost;
        int health = (RoomHealthRules.StartPerTile(database, room) * size) - lost;
        int percent = RoomHealthRules.SharePercent(health, RoomHealthRules.MaxPerTile(database, room) * size);

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

        // 0x004ED440 restores the old room's saved words to each piece, the Prison's Activation
        // (0x004ED68B) among them, before the piece's slot +0x09 runs (0x004ED76C). A Casino's party
        // (+0x3D, +0x41, +0x45) is not among them: Room_Construct zeroes it, so every piece starts
        // without one, and the keeper's party count, which only the party's end lowers, stays raised.
        ref TileState leaving = ref world.TileAtIndex(tileIndex);
        byte door = leaving.PrisonDoor;
        (leaving.PrisonDoor, leaving.CasinoParty, leaving.CasinoPartyStartTick) = (0, 0, 0);
        foreach (int tile in old) door |= world.TileAtIndex(tile).PrisonDoor;

        foreach (List<int> chain in chains)
        {
            Stamp(world, chain);
            RoomHealthRules.SetPiece(world, database, room, chain, percent);
            KeepDoor(world, chain, door);
            if (room.TypeId == RoomTypeId.Prison) Activate(world, chain);
        }
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

    /// <summary>Relinks a rebuilt room and stamps its size change, as <c>Room_Construct</c> and the
    /// three tile rebuilds' <c>0x004E4480</c> write the game turn to <c>room+0x9F</c>.</summary>
    private static void Stamp(WorldState world, List<int> chain)
    {
        long now = world.CurrentTick.Value;
        foreach (int tile in chain)
        {
            ref TileState state = ref world.TileAtIndex(tile);
            state.SlabOrder = world.NextSlabOrder++;
            state.RoomSizeChangedTick = now;
        }
    }

    /// <summary>The room's <c>GameTickLastOnChangeSize</c> (<c>room+0x9F</c>); every tile of a room
    /// carries the stamp of its last rebuild.</summary>
    public static long SizeChangedTick(WorldState world, RoomInstance room)
    {
        long stamp = 0;
        foreach (int tile in room.TileIndices) stamp = Math.Max(stamp, world.TileAtIndex(tile).RoomSizeChangedTick);
        return stamp;
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

    private static bool Same(WorldState world, int tile, byte terrain, PlayerId owner)
    {
        ref TileState t = ref world.TileAtIndex(tile);
        return t.TerrainId == terrain && t.OwnerPlayerId == owner;
    }

    /// <summary>The tiles of one room, orthogonally connected, not entering <paramref name="taken"/>,
    /// which gains them.</summary>
    private static List<int> Fill(WorldState world, int start, byte terrain, PlayerId owner, HashSet<int> taken)
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
