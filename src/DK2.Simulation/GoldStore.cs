// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>
/// Where a keeper's gold physically is.
///
/// <para><b>A player has no gold counter.</b> In DK2 gold is always somewhere —
/// in a treasury, in the dungeon heart, in a pile on the floor, in a creature's
/// hands — and the number on the interface is the total of those places. That is
/// why a keeper with no treasury cannot keep any gold, why destroying a treasury
/// takes the gold with it, and why the balance table's three gold limits are all
/// <i>per tile</i>: <c>MaxGoldPerTreasuryTile</c>, <c>MaxGoldPerDungeonHeartTile</c>
/// and <c>MaxGoldPileOutsideTreasury</c>. Modelling gold as a player-level
/// integer would have made all three unenforceable, so it is not modelled that
/// way here (ADR-0015).</para>
///
/// <para><b>Three of the four places exist.</b> Treasury tiles and dungeon-heart
/// tiles are here, because a variable names each and a tile can carry the
/// amount; a creature's hands are on the creature
/// (<see cref="CreatureState.GoldHeld"/>), because that is where DK2 puts mined
/// gold before a treasury ever sees it (Q31). <b>Floor piles are not:</b> a pile
/// is an OBJECT and the OBJECTS record is not parsed until M6. So gold that will
/// not fit is <i>refused</i> here and reported by the caller, never silently
/// dropped and never silently kept. See docs/research/rooms-and-gold.md.</para>
///
/// <para>Which tile gets filled depends on who is asking.
/// <see cref="DepositInto"/> — the carrier's route — puts the load in the tile
/// the imp walked to, which is DK2's own answer to Q35 as far as it is known.
/// <see cref="Deposit"/> and <see cref="TryWithdraw"/> work in tile-index order,
/// which is <b>ours, not DK2's</b>, and is used only where no carrier is
/// involved: paying for a room, and refunds.</para>
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/rooms-and-gold.md")]
public static class GoldStore
{
    /// <summary>
    /// How much gold one tile can hold, <b>ignoring the shape of the room it is
    /// in</b>.
    ///
    /// Keyed on the room the tile's terrain belongs to, not on a terrain id: the
    /// two variables are named for the treasury and the dungeon heart, so the
    /// room type is the data's own vocabulary for this question. Every other
    /// tile holds nothing — including plain floor, whose
    /// <c>MaxGoldPileOutsideTreasury</c> would need gold-pile objects to mean
    /// anything.
    ///
    /// <para><b>Prefer the overload that takes a tile index.</b> This one cannot
    /// see that nine of a dungeon heart's tiles are the raised platform its
    /// structure stands on and hold nothing (Q52), so it over-reports a heart by
    /// 9000. It survives for the one caller that has a tile and no map — the
    /// spill calculation when a tile stops being a store — where the difference
    /// cannot bite, because a platform tile can never have had gold on it to
    /// spill.</para>
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
    ///
    /// <para><b>This is where a dungeon heart's 16000 comes from</b> (Q52,
    /// ADR-0027). A heart is 5x5 and the middle 3x3 is the platform the heart
    /// structure stands on, so sixteen tiles store gold at
    /// <c>MaxGoldPerDungeonHeartTile</c>'s 1000 and nine store nothing. The
    /// owner measured the total in the original: a keeper with no treasury tops
    /// out at 16000, and a screenshot shows the raised centre with gold piles
    /// ringing it.</para>
    ///
    /// <para>A treasury has no such platform, so every tile of one counts. The
    /// rule asks the room instance rather than the room type for the geometry,
    /// which is why a heart that has been partly destroyed answers sensibly
    /// instead of by a hardcoded 16.</para>
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
    /// The carrier's version of <see cref="Deposit"/>, and the one that is
    /// closest to what DK2 does: an imp walks to a tile and puts its load down
    /// there, so the tile is chosen by the walk rather than by an index. That
    /// makes which gold survives a treasury being partly destroyed a consequence
    /// of where imps walked, which is what the original looks like (Q35 —
    /// nearest tile, spillover to another, and the owner flagged it as belief).
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
