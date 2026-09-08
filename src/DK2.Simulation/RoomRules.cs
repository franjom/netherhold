// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>Why a room tile could not be placed. Reported, never silently swallowed.</summary>
public enum RoomPlacement
{
    /// <summary>The tile can take this room.</summary>
    Allowed = 0,

    /// <summary>Off the edge of the map.</summary>
    OffMap = 1,

    /// <summary>The room type is not one a player may build (portals, hearts, hero gates).</summary>
    NotBuildable = 2,

    /// <summary>The terrain is not ground this room may sit on.</summary>
    WrongGround = 3,

    /// <summary>The ground is right but it is not this player's land.</summary>
    NotYourLand = 4,

    /// <summary>There is already a room here.</summary>
    Occupied = 5,

    /// <summary>The player cannot pay for it.</summary>
    NotEnoughGold = 6,
}

/// <summary>Why a room tile could not be sold. Reported, never silently swallowed.</summary>
public enum RoomSale
{
    /// <summary>The tile can be sold.</summary>
    Allowed = 0,

    /// <summary>Off the edge of the map.</summary>
    OffMap = 1,

    /// <summary>There is no room on this tile.</summary>
    NoRoomHere = 2,

    /// <summary>The room is somebody else's.</summary>
    NotYourRoom = 3,

    /// <summary>A room a keeper never bought and cannot sell: a portal, a dungeon
    /// heart, a hero gate. Gated by the same <c>Buildable</c> flag as building.</summary>
    NotSellable = 4,
}

/// <summary>Where a room may go, what it costs, and what it does to the tile it
/// lands on. Pure and static like <see cref="TileWorkRules"/> — no world, no RNG, no
/// time, every number from <see cref="GameDatabase"/> (docs/research/rooms-and-gold.md).
/// <para>All from the ROOMS record: <c>PlaceableOnLand</c>/<c>OnWater</c>/<c>OnLava</c>
/// decide the ground, which is why the two bridges are the only rooms over water and
/// lava with no code naming a bridge; <c>Buildable</c> decides what a player may put
/// down; <c>TerrainId</c> and <c>Cost</c> the rest. Not implemented, and not guessed
/// at: room size and shape rules, the walls a room grows, the objects it places, and
/// any effect beyond being owned terrain — a library does nothing.</para></summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/rooms-and-gold.md")]
public static class RoomRules
{
    /// <summary>The room a tile is part of, or null if the tile is not room terrain.</summary>
    public static RoomDef? RoomOn(GameDatabase database, in TileState tile) =>
        database.RoomByTerrain(tile.TerrainId);

    /// <summary>Whether this terrain is ground the room may sit on, ignoring
    /// ownership, cost and buildability: the room's placement flags against the
    /// terrain's water/lava/solid flags. CONFIRMED at the level of the data — the flags
    /// exist for this question and partition the shipped rooms unaided.</summary>
    public static bool IsSuitableGround(GameDatabase database, in TileState tile, RoomDef room)
    {
        TerrainDef terrain = database.TerrainById(tile.TerrainId);

        if (terrain.IsWater) return room.PlaceableOnWater;
        if (terrain.IsLava) return room.PlaceableOnLava;

        // Land. Solid rock has to be dug out first, and a tile that is already a
        // room is occupied rather than free ground.
        return room.PlaceableOnLand && !terrain.IsSolid && !terrain.IsRoom;
    }

    /// <summary>Whether <paramref name="playerId"/> may build
    /// <paramref name="room"/> here, and if not, why. The land rule — <b>you build on
    /// ground you already own</b> — is STRONGLY INFERRED: nothing in the ROOMS record
    /// mentions ownership, so it has to come from the tile, and <c>Ownable</c> plus an
    /// owner id is all the tile carries. Water and lava are exempt because a bridge is
    /// built out over ground nobody can own.
    /// <para>Not implemented: the adjacency rule, that a new room tile must touch land
    /// the keeper already holds (Q33, ANSWERED yes — owner, 2026-08-29). Every tile it
    /// would reject is rejected here anyway except a bridge.</para></summary>
    public static RoomPlacement CanBuild(
        GameDatabase database, in TileState tile, RoomDef room, byte playerId)
    {
        if (!room.IsBuildable) return RoomPlacement.NotBuildable;

        TerrainDef terrain = database.TerrainById(tile.TerrainId);
        if (terrain.IsRoom) return RoomPlacement.Occupied;
        if (!IsSuitableGround(database, tile, room)) return RoomPlacement.WrongGround;

        // Water and lava carry no owner, so a bridge cannot be asked for one.
        if (terrain.IsWater || terrain.IsLava) return RoomPlacement.Allowed;

        return terrain.IsOwnable && tile.OwnerPlayerId == playerId
            ? RoomPlacement.Allowed
            : RoomPlacement.NotYourLand;
    }

    /// <summary>Gold this room costs per tile. CONFIRMED (Q32): the owner priced five
    /// rooms in the original and each matched the record's <c>Cost</c> charged per
    /// tile — DK2 charges as the keeper paints. Pinned by
    /// <c>RoomsOnRealDataTests.ARoomCostsWhatTheOriginalChargesForIt_PerTile</c>.</summary>
    public static int CostPerTile(RoomDef room) => room.Cost;

    /// <summary>Turns a tile into part of <paramref name="room"/>, owned by
    /// <paramref name="playerId"/>. The tile takes the room's terrain and that
    /// terrain's starting health, the conversion rule <see cref="TileWorkRules"/> uses:
    /// a tile's health belongs to what it now is. Dig markers go, room terrain not
    /// being taggable.</summary>
    public static void Place(
        GameDatabase database, ref TileState tile, RoomDef room, byte playerId)
    {
        TerrainDef covered = database.TerrainById(tile.TerrainId);
        TerrainDef terrain = database.TerrainById(room.TerrainId);

        // A bridge records the hazard it is laid over, because that is what it
        // reverts to when it goes (Q64, ADR-0031) and the terrain table says Claimed
        // Path for every room alike. The level file carries this byte for shipped
        // bridges; a bridge a player builds writes its own. See BridgeRules.
        if (BridgeRules.IsBridge(room) && (covered.IsWater || covered.IsLava))
        {
            tile.Flags = BridgeRules.MarkerFor(covered);

            // And a wooden one over lava starts burning the moment it is laid
            // (Q67, CONFIRMED: the clock starts at build).
            tile.BurnTicksRemaining = BridgeRules.LifetimeTicks(database, room, covered);
        }

        tile.TerrainId = room.TerrainId;
        tile.Health = terrain.StartingHealth;
        if (!terrain.IsTaggable) tile.TaggedBy = 0;
        if (terrain.IsOwnable) tile.OwnerPlayerId = playerId;
    }

    /// <summary>Whether <paramref name="playerId"/> may sell the room on this tile,
    /// and if not, why. Sellable means <c>Buildable</c>, so what a keeper may put down
    /// and take up can never disagree and nothing here names a portal or a heart: on
    /// shipped data that admits the 17 rooms a keeper builds and refuses the portals,
    /// hearts and hero gates. A bridge over water or lava carries no owner, so
    /// ownership is not asked.</summary>
    public static RoomSale CanSell(
        GameDatabase database, in TileState tile, byte playerId, out RoomDef? room)
    {
        room = RoomOn(database, tile);
        if (room is null) return RoomSale.NoRoomHere;
        if (!room.IsBuildable) return RoomSale.NotSellable;

        TerrainDef terrain = database.TerrainById(tile.TerrainId);
        if (!terrain.IsOwnable) return RoomSale.Allowed;

        return tile.OwnerPlayerId == playerId ? RoomSale.Allowed : RoomSale.NotYourRoom;
    }

    /// <summary>Gold returned for selling one tile of <paramref name="room"/>:
    /// <b>half what it cost</b>. CONFIRMED from the data and the owner (Q34, ADR-0029)
    /// — <c>RoomSellValuePercentageOfCost</c> is <b>50</b>, matching an observed 50%
    /// refund per tile. <c>RoomDef.ReturnPercentage</c> is a per-room gate on top of
    /// that rate, not a rival reading of it: 100 for every buildable room and <b>0 for
    /// exactly the hero gates</b>, which a keeper cannot sell. Whether they multiply or
    /// the per-room value overrides is indistinguishable on shipped data, where no room
    /// carries anything but 100 or 0; multiplied here, per-room gate first, and every
    /// shipped room divides exactly either way.</summary>
    public static int RefundPerTile(GameDatabase database, RoomDef room)
    {
        int standardRate = database[VariableTypeId.RoomSellValuePercentageOfCost];
        int afterRoomGate = CostPerTile(room) * room.ReturnPercentage / 100;
        return afterRoomGate * standardRate / 100;
    }

    /// <summary>Takes a room tile back to what it was, and says how much stored gold
    /// the tile can no longer hold. The destination comes from
    /// <c>BridgeRules.RevertsTo</c>, not straight from
    /// <see cref="TerrainDef.DestroyedTypeTerrainId"/>: every room terrain in the
    /// shipped data claims Claimed Path, but a sold bridge reverts to the water or lava
    /// it spanned (Q64, ADR-0031).
    /// <para>Displaced gold is returned rather than kept, for the caller to drop on
    /// this tile as a pile (<see cref="FloorGold"/>) — CONFIRMED (owner 2026-09-03;
    /// binary 2026-09-05), the original remaking each gold object as a floor pile in
    /// place and taking the amount off the keeper's total.</para></summary>
    public static int Unmake(GameDatabase database, ref TileState tile)
    {
        byte revertsTo = BridgeRules.RevertsTo(database, tile);
        TerrainDef next = database.TerrainById(revertsTo);

        tile.TerrainId = revertsTo;
        tile.Health = next.StartingHealth;
        tile.WorkRemainder = 0;
        tile.BurnTicksRemaining = 0;
        if (!next.IsTaggable) tile.TaggedBy = 0;

        // Unownable ground carries no owner: 0 is unowned (Q17, ADR-0043), and
        // keeping a stale byte was a four-month defect (Q95, ADR-0045 —
        // TileWorkRules.Convert). The bridge marker returns to its default, because
        // the bridge that recorded which hazard this was no longer exists.
        if (!next.IsOwnable) tile.OwnerPlayerId = PlayerIds.Unowned;
        tile.Flags = BridgeRules.SpansWater;

        int displaced = Math.Max(0, tile.GoldHeld - GoldStore.Capacity(database, tile));
        tile.GoldHeld -= displaced;
        return displaced;
    }
}
