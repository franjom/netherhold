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
/// <para><b>What is not implemented, and is not guessed at.</b> Selling a room
/// (two candidate refund numbers disagree — see Q34), room size and shape rules,
/// the walls a room grows around itself, the objects a room places on its tiles,
/// and any effect a room has beyond being terrain a player owns. A treasury
/// holds gold because a variable says how much; a library does nothing, because
/// nothing here knows what a library is.</para>
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
    /// STRONGLY INFERRED that <c>Cost</c> is per tile rather than per room: DK2
    /// charges as the keeper paints, and a per-room reading would make a 5x5
    /// temple cost the same as a single tile of one. It has not been measured
    /// against the original (Q32), and it is one multiplication away from being
    /// changed if it turns out to be wrong.
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
        TerrainDef terrain = database.TerrainById(room.TerrainId);

        tile.TerrainId = room.TerrainId;
        tile.Health = terrain.StartingHealth;
        if (!terrain.IsTaggable) tile.TaggedBy = 0;
        if (terrain.IsOwnable) tile.OwnerPlayerId = playerId;
    }
}
