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

    /// <summary>
    /// A room a keeper never bought and cannot sell: a portal, a dungeon heart,
    /// a hero gate. The same <c>Buildable</c> flag that gates building gates
    /// this, so the two can never disagree.
    /// </summary>
    NotSellable = 4,
}

/// <summary>
/// Where a room may go, what it costs, and what it does to the tile it lands on.
///
/// Pure and static, like <see cref="TileWorkRules"/>: no world, no RNG, no time,
/// and every number read from <see cref="GameDatabase"/>. Evidence and
/// confidence per rule: docs/research/rooms-and-gold.md.
///
/// <para><b>What is data.</b> Which ground a room may sit on is the ROOMS
/// record's own <c>PlaceableOnLand</c> / <c>PlaceableOnWater</c> /
/// <c>PlaceableOnLava</c> flags — which is why the two bridges are the only
/// rooms that go on water and lava, and why nothing else does, without a line of
/// code naming a bridge. Whether a player may build it at all is the
/// <c>Buildable</c> flag. The terrain it lays down and what it costs are the
/// record's own <c>TerrainId</c> and <c>Cost</c>.</para>
///
/// <para><b>What is not implemented, and is not guessed at.</b> Room size and
/// shape rules, the walls a room grows around itself, the objects a room places
/// on its tiles, and any effect a room has beyond being terrain a player owns.
/// A treasury holds gold because a variable says how much; a library does
/// nothing, because nothing here knows what a library is.</para>
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/rooms-and-gold.md")]
public static class RoomRules
{
    /// <summary>The room a tile is part of, or null if the tile is not room terrain.</summary>
    public static RoomDef? RoomOn(GameDatabase database, in TileState tile) =>
        database.RoomByTerrain(tile.TerrainId);

    /// <summary>
    /// Whether this terrain is ground the room may sit on, ignoring ownership,
    /// cost and whether a player is allowed to build it.
    ///
    /// Reads the room's own placement flags against the terrain's own
    /// water/lava/solid flags. CONFIRMED at the level of the data: the flags
    /// exist for exactly this question, and they partition the shipped rooms
    /// into "the two bridges" and "everything else" without any help.
    /// </summary>
    public static bool IsSuitableGround(GameDatabase database, in TileState tile, RoomDef room)
    {
        TerrainDef terrain = database.TerrainById(tile.TerrainId);

        if (terrain.IsWater) return room.PlaceableOnWater;
        if (terrain.IsLava) return room.PlaceableOnLava;

        // Land. Solid rock has to be dug out first, and a tile that is already a
        // room is occupied rather than free ground.
        return room.PlaceableOnLand && !terrain.IsSolid && !terrain.IsRoom;
    }

    /// <summary>
    /// Whether <paramref name="playerId"/> may build <paramref name="room"/>
    /// here, and if not, why.
    ///
    /// <para>The land rule — <b>you build on ground you already own</b> — is
    /// STRONGLY INFERRED. It is how the original visibly plays: a keeper claims
    /// floor with imps and only then can put a room on it. What makes it more
    /// than an observation is that the data has no other candidate: nothing in
    /// the ROOMS record says anything about ownership, so the rule has to come
    /// from the tile, and <c>Ownable</c> plus an owner id is the only thing the
    /// tile carries. Water and lava are exempt because bridges are built out
    /// over ground nobody can own — the terrain is not <c>Ownable</c> at
    /// all.</para>
    ///
    /// <para><b>Not checked, and UNKNOWN (Q33):</b> whether the original also
    /// requires the new tile to touch something the player already has. Every
    /// tile it would reject is one this rule rejects anyway on owned-land
    /// grounds, except for bridges, so the gap is visible only there.</para>
    /// </summary>
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

    /// <summary>
    /// Gold this room costs per tile.
    ///
    /// CONFIRMED (Q32): the owner priced five rooms in the original and every one
    /// matched the record's <c>Cost</c> charged per tile. DK2 charges as the keeper
    /// paints, and a per-room reading would have made a 5x5 temple cost the same as
    /// a single tile of one.
    /// </summary>
    public static int CostPerTile(RoomDef room) => room.Cost;

    /// <summary>
    /// Turns a tile into part of <paramref name="room"/>, owned by
    /// <paramref name="playerId"/>.
    ///
    /// The tile takes the room's terrain and that terrain's starting health —
    /// the same conversion rule <see cref="TileWorkRules"/> uses, for the same
    /// reason: a tile's health belongs to what it now is, not to what it was.
    /// Dig markers are dropped, because room terrain is not taggable.
    /// </summary>
    public static void Place(
        GameDatabase database, ref TileState tile, RoomDef room, byte playerId)
    {
        TerrainDef covered = database.TerrainById(tile.TerrainId);
        TerrainDef terrain = database.TerrainById(room.TerrainId);

        // A bridge records the hazard it is being laid over, because that is
        // what it must revert to when it goes (Q64) and the terrain table's
        // destroyed-type field says Claimed Path for every room alike. The level
        // file carries this byte for bridges it shipped with; a bridge a player
        // builds has to write its own. See BridgeRules.
        if (BridgeRules.IsBridge(room) && (covered.IsWater || covered.IsLava))
            tile.Flags = BridgeRules.MarkerFor(covered);

        tile.TerrainId = room.TerrainId;
        tile.Health = terrain.StartingHealth;
        if (!terrain.IsTaggable) tile.TaggedBy = 0;
        if (terrain.IsOwnable) tile.OwnerPlayerId = playerId;
    }

    /// <summary>
    /// Whether <paramref name="playerId"/> may sell the room on this tile, and
    /// if not, why.
    ///
    /// <para>Sellable means <c>Buildable</c> — the same flag that decides what a
    /// keeper may put down decides what they may take up, so the two can never
    /// disagree and nothing here names a portal or a dungeon heart. In the
    /// shipped data that admits the 17 rooms a keeper builds and refuses the
    /// portals, the two hearts and the hero gates.</para>
    ///
    /// <para>A room on unownable ground — a bridge over water or lava — carries
    /// no owner, so ownership cannot be asked of it and is not.</para>
    /// </summary>
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

    /// <summary>
    /// Gold returned for selling one tile of <paramref name="room"/>: <b>half
    /// what it cost</b>.
    ///
    /// <para><b>CONFIRMED, and entirely from the data</b> (Q34). The owner
    /// observed a 50% refund per tile in the original, and
    /// <c>RoomSellValuePercentageOfCost</c> is <b>50</b>. This project spent four
    /// sessions recording that variable as 100 and concluding the refund was "in
    /// no field we read"; our own extracted dump said 50 throughout. There is no
    /// override here and none is needed.</para>
    ///
    /// <para><b><c>RoomDef.ReturnPercentage</c> is not a rival reading of the
    /// same number</b> — it is a per-room gate on top of the global rate: 100 for
    /// every buildable room and <b>0 for exactly the hero gates</b>, which a
    /// keeper cannot sell. That the zero rows are precisely the unsellable rooms
    /// is what makes the reading more than a guess. Whether the two multiply or
    /// the per-room value overrides the global is <b>indistinguishable on the
    /// shipped data</b>, because no room carries anything but 100 or 0; they are
    /// multiplied here, which is the reading that would still behave sensibly if
    /// some other data set used a middle value.</para>
    ///
    /// <para>Integer arithmetic, and the truncation order is fixed: the per-room
    /// gate is applied first, then the global rate. Every shipped room divides
    /// exactly either way — a treasury refunds 100 of its 200, a temple 1500 of
    /// its 3000 — so the order is a decision about data we do not have rather
    /// than about DK2.</para>
    /// </summary>
    public static int RefundPerTile(GameDatabase database, RoomDef room)
    {
        int standardRate = database[VariableTypeId.RoomSellValuePercentageOfCost];
        int afterRoomGate = CostPerTile(room) * room.ReturnPercentage / 100;
        return afterRoomGate * standardRate / 100;
    }

    /// <summary>
    /// Takes a room tile back to what it was, and says how much stored gold the
    /// tile can no longer hold.
    ///
    /// <para>Where it goes is the terrain's own
    /// <see cref="TerrainDef.DestroyedTypeTerrainId"/> — for every room terrain
    /// in the shipped data that is Claimed Path, so a keeper who sells a treasury
    /// keeps the floor it stood on. Ownership is deliberately not cleared for the
    /// same reason <see cref="TileWorkRules"/> never clears it: selling your own
    /// room does not hand the ground to nobody.</para>
    ///
    /// <para><b>Known wrong for bridges, and it is the data that says so</b>
    /// (Q64). A wooden bridge's destroyed type is Claimed Path like everything
    /// else, so selling a bridge over water leaves walkable floor where there
    /// should be water. The tile does not record what was underneath it — the MAP
    /// record's byte 3 is bridge-related and unparsed (Q16) — so there is nothing
    /// to revert to yet. Reproduced as the data has it rather than special-cased,
    /// and the event says when a bridge was involved.</para>
    ///
    /// <para><b>The displaced gold is returned rather than kept, and where it
    /// should go is CONFIRMED</b> (owner, 2026-09-03): it drops onto this very
    /// tile as a pile and comes off the keeper's spendable total until an imp
    /// fetches it. The total falling out is therefore correct; the pile is not
    /// here, because a pile is an OBJECT (M6, Q31), so the caller reports the
    /// amount the same way a destroyed treasury already does.</para>
    /// </summary>
    public static int Unmake(GameDatabase database, ref TileState tile)
    {
        byte revertsTo = BridgeRules.RevertsTo(database, tile);
        TerrainDef next = database.TerrainById(revertsTo);

        tile.TerrainId = revertsTo;
        tile.Health = next.StartingHealth;
        tile.UpwardWork = 0;
        tile.WorkRemainder = 0;
        if (!next.IsTaggable) tile.TaggedBy = 0;

        // The owner byte is deliberately left alone even when the tile reverts to
        // water, for the same reason TileWorkRules never clears it: which owner
        // id means "nobody" is UNKNOWN (Q17), and every rule that cares asks
        // TerrainDef.IsOwnable first, so a stale byte on unownable terrain is
        // inert. Inventing a neutral id here would be the riskier choice.
        //
        // The bridge marker does go back to its default, because the bridge that
        // recorded which hazard this was no longer exists.
        tile.Flags = BridgeRules.SpansWater;

        int displaced = Math.Max(0, tile.GoldHeld - GoldStore.Capacity(database, tile));
        tile.GoldHeld -= displaced;
        return displaced;
    }
}
