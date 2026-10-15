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

    /// <summary>A span over water or lava that touches none of the player's own floor
    /// (<see cref="OwnFloorRules"/>).</summary>
    NotTouchingOwnLand = 7,

    /// <summary>A terrain DK2's placement test refuses by id: a Mana Vault, claimed or not, or a
    /// Hero Gate (<see cref="RoomRules.RefusesByTerrainId"/>).</summary>
    ReservedTerrain = 8,
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
    /// owner id is all the tile carries. Water and lava belong to the neutral player (ADR-0362); a span over them
    /// must instead touch the player's own floor, <paramref name="touchesOwnLand"/>
    /// (<c>0x0044F630</c>, Q33). Not implemented: its refusal of a tile an enemy creature,
    /// door or trap stands on (<c>0x0044F800</c>, Q501).</summary>
    public static RoomPlacement CanBuild(
        GameDatabase database, in TileState tile, RoomDef room, PlayerId playerId, bool touchesOwnLand)
    {
        if (!room.IsBuildable) return RoomPlacement.NotBuildable;

        TerrainDef terrain = database.TerrainById(tile.TerrainId);
        if (RefusesByTerrainId(terrain.TerrainId)) return RoomPlacement.ReservedTerrain;
        if (terrain.IsRoom) return RoomPlacement.Occupied;
        if (!IsSuitableGround(database, tile, room)) return RoomPlacement.WrongGround;

        if (terrain.IsWater || terrain.IsLava)
            return touchesOwnLand ? RoomPlacement.Allowed : RoomPlacement.NotTouchingOwnLand;

        return terrain.IsOwnable && tile.OwnerPlayerId == playerId
            ? RoomPlacement.Allowed
            : RoomPlacement.NotYourLand;
    }

    /// <summary>0x0044F630 refuses terrain ids 31–34 and 37 before anything else; DK2's code names the
    /// ids, which on shipped data are Mana Vault, Claimed Vault and the three Hero Gates
    /// (<c>RoomsOnRealDataTests</c> guards them). The gates are rooms anyway; the vaults are not.</summary>
    public static bool RefusesByTerrainId(byte terrainId) => terrainId is >= 31 and <= 34 or 37;

    /// <summary><see cref="CanBuild(GameDatabase, in TileState, RoomDef, byte, bool)"/> for a world
    /// tile, asking its neighbours for the adjacency; the authoritative placement test.</summary>
    public static RoomPlacement CanBuild(
        GameDatabase database, WorldState world, TilePos at, RoomDef room, PlayerId playerId)
    {
        if (!world.Contains(at)) return RoomPlacement.OffMap;
        ref TileState tile = ref world.Tile(at);
        TerrainDef terrain = database.TerrainById(tile.TerrainId);
        bool touches = (terrain.IsWater || terrain.IsLava) && OwnFloorRules.Touches(database, world, at, playerId);
        return CanBuild(database, tile, room, playerId, touches);
    }

    /// <summary>The water or lava tiles of one bridge order that may be spanned: those passing
    /// <see cref="CanBuild(GameDatabase, WorldState, TilePos, RoomDef, byte)"/>, then every hazard tile of
    /// the order orthogonally joined to one through hazard tiles of the order. DK2 marks a dragged
    /// rectangle this way before its wave lays it (<c>0x004C57F0</c>), so a span grows from the
    /// shore whatever corner the wave starts at; one tile is the plain test (rooms-and-gold.md).</summary>
    public static HashSet<TilePos> SpannableTiles(
        GameDatabase database, WorldState world, IReadOnlyList<TilePos> order, RoomDef room, PlayerId playerId)
    {
        var inOrder = new HashSet<TilePos>();
        foreach (TilePos p in order)
            if (world.Contains(p) && IsSpannable(database, world.Tile(p), room)) inOrder.Add(p);

        var marked = new HashSet<TilePos>();
        var frontier = new Queue<TilePos>();
        foreach (TilePos p in inOrder)
            if (CanBuild(database, world, p, room, playerId) == RoomPlacement.Allowed && marked.Add(p)) frontier.Enqueue(p);

        ReadOnlySpan<int> dx = OwnFloorRules.NeighbourDx, dy = OwnFloorRules.NeighbourDy;
        while (frontier.Count > 0)
        {
            TilePos p = frontier.Dequeue();
            for (int i = 0; i < dx.Length; i++)
            {
                var n = new TilePos(p.X + dx[i], p.Y + dy[i]);
                if (inOrder.Contains(n) && marked.Add(n)) frontier.Enqueue(n);
            }
        }
        return marked;
    }

    /// <summary>Water the room may span, or lava it may span (0x004C57F0's tile test).</summary>
    private static bool IsSpannable(GameDatabase database, in TileState tile, RoomDef room)
    {
        TerrainDef terrain = database.TerrainById(tile.TerrainId);
        return (terrain.IsWater && room.PlaceableOnWater) || (terrain.IsLava && room.PlaceableOnLava);
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
        GameDatabase database, ref TileState tile, RoomDef room, PlayerId playerId)
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

        // Tile_SetTerrain's mana adjustment (0x00455B90) runs only when neither terrain is a
        // Room, so the floor's gain stays on the builder's rate word under the new room. A room
        // over another room keeps what that one kept; a room over ground the builder did not
        // own brings nothing. The bridge's own +1 lands on the tile's tag before the owner is
        // set - the neutral player, over water or lava - so it is not the builder's and is not
        // built (ADR-0334, Q521).
        byte kept = 0;
        if (!covered.IsRoom && tile.OwnerPlayerId == playerId) kept = (byte)Math.Min(byte.MaxValue, covered.ManaGain);
        else if (covered.IsRoom && tile.OwnerPlayerId == playerId) kept = tile.KeptManaGain;
        tile.KeptManaGain = kept;

        tile.TerrainId = room.TerrainId;
        tile.Health = terrain.StartingHealth;
        if (!terrain.IsTaggable) tile.TaggedBy = PlayerMask.None;
        if (terrain.IsOwnable) tile.OwnerPlayerId = playerId.Value;
        tile.SetExploredBy(playerId);

        // A tile on its own is a one-tile room, whose Prison slot +0x09 turns Activation on
        // (0x004E2B30). Joined to a room, it takes that room's door byte (SlabChain.Added).
        if (room.TypeId == RoomTypeId.Prison) tile.PrisonDoor |= JailBreakRules.Activated;
    }

    /// <summary>Whether <paramref name="playerId"/> may sell the room on this tile,
    /// and if not, why. Sellable means <c>Buildable</c>, so what a keeper may put down
    /// and take up can never disagree and nothing here names a portal or a heart: on
    /// shipped data that admits the 17 rooms a keeper builds and refuses the portals,
    /// hearts and hero gates. A bridge stands on ground nobody but the neutral player
    /// holds, so ownership is not asked.</summary>
    public static RoomSale CanSell(
        GameDatabase database, in TileState tile, PlayerId playerId, out RoomDef? room)
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
        byte oldTerrain = tile.TerrainId;
        TerrainDef next = database.TerrainById(revertsTo);

        tile.TerrainId = revertsTo;
        tile.Health = next.StartingHealth;
        tile.BurnTicksRemaining = 0;
        if (!next.IsTaggable) tile.TaggedBy = PlayerMask.None;

        // Unownable ground belongs to the neutral player, as Tile_SetTerrain gives it
        // (0x0044FDBF, Q514); keeping a stale byte was a four-month defect (Q95, ADR-0045).
        // The bridge marker returns to its default: the bridge that recorded the hazard is gone.
        if (!next.IsOwnable) tile.OwnerPlayerId = PlayerIds.Neutral;
        DiscoveryRules.TerrainChanged(database, ref tile, oldTerrain);
        tile.Flags = BridgeRules.SpansWater;

        int displaced = Math.Max(0, tile.GoldHeld - GoldStore.Capacity(database, tile));
        tile.GoldHeld -= displaced;
        return displaced;
    }
}
