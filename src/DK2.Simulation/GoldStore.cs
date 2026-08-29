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
/// <para><b>Two of the four places exist.</b> Treasury tiles and dungeon-heart
/// tiles are implemented, because a variable names each and a tile can carry the
/// amount. Floor piles and creatures' hands are not: a pile is an OBJECT, and
/// the OBJECTS record is not parsed until M6. So gold that will not fit is
/// <i>refused</i> here and reported by the caller, never silently dropped and
/// never silently kept. See docs/research/rooms-and-gold.md and Q31.</para>
///
/// <para>Tiles are filled and drained in tile-index order. That order is
/// behaviour — which tile a deposit lands in decides which gold survives a
/// treasury being destroyed — and it is <b>ours, not DK2's</b>: the original
/// plausibly fills whichever tile the carrier reached first, which nothing here
/// can know until a carrier exists (Q35).</para>
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/rooms-and-gold.md")]
public static class GoldStore
{
    /// <summary>
    /// How much gold one tile can hold.
    ///
    /// Keyed on the room the tile's terrain belongs to, not on a terrain id: the
    /// two variables are named for the treasury and the dungeon heart, so the
    /// room type is the data's own vocabulary for this question. Every other
    /// tile holds nothing — including plain floor, whose
    /// <c>MaxGoldPileOutsideTreasury</c> would need gold-pile objects to mean
    /// anything.
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

    /// <summary>Room left in one tile.</summary>
    public static int SpaceIn(GameDatabase database, in TileState tile) =>
        Math.Max(0, Capacity(database, tile) - tile.GoldHeld);

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

    /// <summary>How much more this player could store, across every tile they own.</summary>
    public static int SpaceFor(WorldState world, GameDatabase database, byte playerId)
    {
        long space = 0;
        for (int i = 0; i < world.TileCount; i++)
        {
            ref TileState tile = ref world.TileAtIndex(i);
            if (tile.OwnerPlayerId == playerId) space += SpaceIn(database, tile);
        }
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
        for (int i = 0; i < world.TileCount && remaining > 0; i++)
        {
            ref TileState tile = ref world.TileAtIndex(i);
            if (tile.OwnerPlayerId != playerId) continue;

            int space = SpaceIn(database, tile);
            if (space <= 0) continue;

            int put = Math.Min(space, remaining);
            tile.GoldHeld += put;
            remaining -= put;
        }

        return amount - remaining;
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
