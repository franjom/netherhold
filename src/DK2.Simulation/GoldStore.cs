// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>Where a keeper's gold physically is.</summary>
/// <remarks>
/// <para>Rule: a player has no gold counter. Gold is always somewhere — a
/// treasury tile, a dungeon-heart tile, a floor pile (<see cref="FloorGold"/>)
/// or a creature's hands (<see cref="CreatureState.GoldHeld"/>, Q31) — so a
/// keeper with no treasury cannot keep any, and the table's three limits are
/// <i>per tile</i> (ADR-0015, docs/research/rooms-and-gold.md).</para>
/// <para>Deviates: a treasury tile's gold is object type 3 in the original;
/// ours keeps it in the tile. <see cref="Deposit"/> and
/// <see cref="TryWithdraw"/> work in tile-index order, <b>ours not DK2's</b>;
/// the carrier's target is chosen by <see cref="DepositTileAlongChain"/>.</para>
/// </remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/rooms-and-gold.md")]
public static class GoldStore
{
    /// <summary>
    /// How much gold one tile can hold, <b>ignoring the shape of the room it is
    /// in</b>. Keyed on the room the tile's terrain belongs to, not on a terrain
    /// id: the two variables are named for the treasury and the dungeon heart, so
    /// the room type is the data's own vocabulary. Every other tile holds nothing.
    /// <para>Prefer the overload taking a tile index. This one cannot see that
    /// nine of a dungeon heart's tiles are the raised platform and hold nothing
    /// (Q52), so it over-reports a heart by 9000. It survives for the one caller
    /// with a tile and no map — the spill when a tile stops being a store —
    /// where a platform tile can never have had gold to spill.</para>
    /// </summary>
    public static int Capacity(GameDatabase database, in TileState tile)
    {
        RoomDef? room = RoomRules.RoomOn(database, tile);
        return room?.TypeId switch
        {
            RoomTypeId.Treasury => database[VariableTypeId.MaxGoldPerTreasuryTile],
            RoomTypeId.DungeonHeart => database[VariableTypeId.MaxGoldPerDungeonHeartTile],
            _ => 0,
        };
    }

    /// <summary>
    /// How much gold the tile at <paramref name="tileIndex"/> can hold, given
    /// the room it is part of.
    /// <para>Where a dungeon heart's 16000 comes from (Q52, ADR-0027): a heart
    /// is 5x5 and its middle 3x3 is the platform the structure stands on, so
    /// sixteen tiles store <c>MaxGoldPerDungeonHeartTile</c>'s 1000 and nine
    /// store nothing. CONFIRMED by the owner, who measured a treasury-less keeper
    /// topping out at 16000. A treasury has no platform, so every tile counts;
    /// the geometry is asked of the room <i>instance</i>, so a partly destroyed
    /// heart answers sensibly rather than by a hardcoded 16.</para>
    /// </summary>
    public static int Capacity(WorldState world, GameDatabase database, int tileIndex) =>
        Capacity(world, database, tileIndex, RoomIndex.RoomAt(world, database, tileIndex));

    /// <summary>
    /// The same, for a caller that has already worked out which room the tile is
    /// in — a loop over every tile should build the index once rather than
    /// flood-filling per tile.
    /// </summary>
    public static int Capacity(
        WorldState world, GameDatabase database, int tileIndex, RoomInstance? room)
    {
        if (room is null) return 0;

        // A ruined heart holds nothing: DK2's gold sum skips a heart whose +0x9A bit 0
        // is set, and that bit is the ruin (Q263, win-and-defeat.md §4).
        if (room.Definition.TypeId == RoomTypeId.DungeonHeart
            && world.HasPlayer(room.OwnerPlayerId)
            && world.Player(room.OwnerPlayerId).HeartRuined)
            return 0;

        int perTile = room.Definition.TypeId switch
        {
            RoomTypeId.Treasury => database[VariableTypeId.MaxGoldPerTreasuryTile],
            RoomTypeId.DungeonHeart => database[VariableTypeId.MaxGoldPerDungeonHeartTile],
            _ => 0,
        };

        if (perTile == 0) return 0;
        if (room.Definition.TypeId != RoomTypeId.DungeonHeart) return perTile;

        return room.IsCentrePlatform(tileIndex % world.Width, tileIndex / world.Width)
            ? 0
            : perTile;
    }

    /// <summary>Room left in the tile at <paramref name="tileIndex"/>.</summary>
    public static int SpaceIn(WorldState world, GameDatabase database, int tileIndex) =>
        Math.Max(0, Capacity(world, database, tileIndex) - world.TileAtIndex(tileIndex).GoldHeld);

    private static int SpaceIn(
        WorldState world, GameDatabase database, int tileIndex, RoomInstance? room) =>
        Math.Max(0, Capacity(world, database, tileIndex, room)
                    - world.TileAtIndex(tileIndex).GoldHeld);

    /// <summary>Room left in one tile, ignoring room shape. See the note on <see cref="Capacity(GameDatabase, in TileState)"/>.</summary>
    public static int SpaceIn(GameDatabase database, in TileState tile) =>
        Math.Max(0, Capacity(database, tile) - tile.GoldHeld);


    /// <summary>
    /// Puts as much of <paramref name="amount"/> into <i>this one tile</i> as it
    /// will take, and returns how much went in.
    ///
    /// <para>The carrier's version of <see cref="Deposit"/>, and the closer to
    /// DK2: an imp walks to a tile and puts its load there, so the tile is
    /// chosen by the walk. Which gold survives a treasury being partly destroyed
    /// is therefore a consequence of where imps walked. HYPOTHESIS (Q35):
    /// nearest tile with spillover to another, which the owner flagged as
    /// belief.</para>
    /// </summary>
    public static int DepositInto(
        WorldState world, GameDatabase database, int tileIndex, int amount)
    {
        if (amount <= 0) return 0;

        int put = Math.Min(SpaceIn(world, database, tileIndex), amount);
        world.TileAtIndex(tileIndex).GoldHeld += put;
        return put;
    }

    /// <summary>Everything <paramref name="playerId"/> can spend.</summary>
    public static int Total(WorldState world, byte playerId)
    {
        long total = 0;
        for (int i = 0; i < world.TileCount; i++)
        {
            ref TileState tile = ref world.TileAtIndex(i);
            if (tile.OwnerPlayerId == playerId) total += tile.GoldHeld;
        }
        return (int)Math.Min(total, int.MaxValue);
    }

    /// <summary>
    /// How much more this player could store, across every tile they own.
    ///
    /// Builds the room index once rather than flood-filling per tile, which is
    /// the whole reason <see cref="RoomIndex.Build"/> exists alongside
    /// <see cref="RoomIndex.RoomAt"/>.
    /// </summary>
    public static int SpaceFor(WorldState world, GameDatabase database, byte playerId)
    {
        long space = 0;
        foreach ((int index, RoomInstance room) in StoreTilesOf(world, database, playerId))
            space += SpaceIn(world, database, index, room);

        return (int)Math.Min(space, int.MaxValue);
    }

    /// <summary>The carrier's tile search after it picks a room (<c>0x00464400</c>):
    /// start at a random chain tile and follow the links, wrapping once.</summary>
    public static int DepositTileAlongChain(WorldState world, GameDatabase database,
        RoomInstance room, int startTile)
    {
        int[] chain = SlabChain.Order(world, room);
        int start = Array.IndexOf(chain, startTile);
        if (start < 0) return -1;
        for (int step = 0; step < chain.Length; step++)
        {
            int tile = chain[(start + step) % chain.Length];
            if (SpaceIn(world, database, tile, room) > 0) return tile;
        }
        return -1;
    }

    /// <summary>Fills one room's tiles in chain order from its head, and returns what did not fit:
    /// the room deposit <c>0x004F4090</c>, which walks the chain once and, in a heart, skips the
    /// platform (<c>0x004DA1B0</c>).</summary>
    public static int FillAlongChain(WorldState world, GameDatabase database, RoomInstance room, int amount)
    {
        int left = amount;
        foreach (int tile in SlabChain.Order(world, room))
        {
            if (left <= 0) break;
            int put = Math.Min(SpaceIn(world, database, tile, room), left);
            world.TileAtIndex(tile).GoldHeld += put;
            left -= put;
        }
        return Math.Max(0, left);
    }

    /// <summary>The same walk from a given tile of the room, wrapping once, and what did not fit:
    /// the deposit <c>0x004F3C60</c> the store sweep hands a loose pile to, from the pile's tile
    /// (gold-on-the-floor.md §11).</summary>
    public static int FillAlongChainFrom(
        WorldState world, GameDatabase database, RoomInstance room, int startTile, int amount)
    {
        int[] chain = SlabChain.Order(world, room);
        int start = Math.Max(0, Array.IndexOf(chain, startTile));
        int left = amount;
        for (int step = 0; step < chain.Length && left > 0; step++)
        {
            int tile = chain[(start + step) % chain.Length];
            int put = Math.Min(SpaceIn(world, database, tile, room), left);
            world.TileAtIndex(tile).GoldHeld += put;
            left -= put;
        }
        return Math.Max(0, left);
    }

    /// <summary>
    /// Stores as much of <paramref name="amount"/> as this player's tiles will
    /// take, and returns how much was stored.
    ///
    /// A shortfall is the caller's problem to report. Nothing here invents a
    /// destination for gold that has nowhere to go.
    /// </summary>
    public static int Deposit(WorldState world, GameDatabase database, byte playerId, int amount)
    {
        if (amount <= 0) return 0;

        int remaining = amount;
        foreach ((int index, RoomInstance room) in StoreTilesOf(world, database, playerId))
        {
            if (remaining <= 0) break;

            int space = SpaceIn(world, database, index, room);
            if (space <= 0) continue;

            int put = Math.Min(space, remaining);
            world.TileAtIndex(index).GoldHeld += put;
            remaining -= put;
        }

        return amount - remaining;
    }

    /// <summary>
    /// Every tile this player owns that is part of a room, paired with the room,
    /// in ascending tile order.
    ///
    /// One sweep of the map, and the tile order is what makes
    /// <see cref="Deposit"/> and <see cref="TryWithdraw"/> reproducible. That
    /// order is <b>ours, not DK2's</b> — see the note on the class.
    /// </summary>
    private static List<(int Index, RoomInstance Room)> StoreTilesOf(
        WorldState world, GameDatabase database, byte playerId)
    {
        var result = new List<(int, RoomInstance)>();

        foreach (RoomInstance room in RoomIndex.Build(world, database))
        {
            if (room.OwnerPlayerId != playerId) continue;
            foreach (int index in room.TileIndices) result.Add((index, room));
        }

        result.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return result;
    }


    /// <summary>
    /// Takes exactly <paramref name="amount"/> from this player's tiles, or
    /// takes nothing and returns false.
    ///
    /// All-or-nothing on purpose: a room that half charges for itself would be a
    /// worse bug than one that refuses to be built.
    /// </summary>
    public static bool TryWithdraw(WorldState world, byte playerId, int amount)
    {
        if (amount <= 0) return true;
        if (Total(world, playerId) < amount) return false;

        int remaining = amount;
        for (int i = 0; i < world.TileCount && remaining > 0; i++)
        {
            ref TileState tile = ref world.TileAtIndex(i);
            if (tile.OwnerPlayerId != playerId || tile.GoldHeld <= 0) continue;

            int take = Math.Min(tile.GoldHeld, remaining);
            tile.GoldHeld -= take;
            remaining -= take;
        }

        return true;
    }
}
