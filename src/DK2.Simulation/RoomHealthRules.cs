// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>What one room-health application did.</summary>
public enum RoomHealthOutcome
{
    /// <summary>The health moved, or sat at its cap.</summary>
    Progressed = 0,

    /// <summary>The health fell below 1: the room changed hands whole and was reset.</summary>
    TookOver = 1,
}

/// <summary>A room's one health word, DK2's room <c>+0x35</c>, and the applier <c>0x004E5300</c>
/// every changer goes through (room-health.md, Q500).</summary>
/// <remarks>Rooms here are derived from tiles (<see cref="RoomInstance"/>), so the health is kept
/// on them as a deficit, <c>StartingHealth × Size − Health</c>, summed over the room's
/// <see cref="TileState.RoomHealthLost"/>. An untouched room reads full however it was laid down,
/// and a tile added or a merge needs no write: DK2's clamped sums equal the summed deficit while
/// StartingHealth ≤ MaxHealth, as every shipped room terrain has (<c>RoomHealthOnRealDataTests</c>).
/// A removal, a takeover and an application write it (ADR-0336).</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/room-health.md")]
public static class RoomHealthRules
{
    /// <summary>The terrain's StartingHealth for one tile (terrain <c>+0x1AB</c>, reached through
    /// the room definition's terrain, def <c>+0x44E</c>).</summary>
    public static int StartPerTile(GameDatabase database, RoomDef room) =>
        database.TerrainById(room.TerrainId).StartingHealth;

    /// <summary>The terrain's MaxHealth for one tile (terrain <c>+0x1AD</c>).</summary>
    public static int MaxPerTile(GameDatabase database, RoomDef room) =>
        database.TerrainById(room.TerrainId).MaxHealth;

    /// <summary>MaxHealth × Size, the positive arm's clamp (<c>0x004E6C60</c>).</summary>
    public static int Cap(GameDatabase database, RoomInstance room) =>
        MaxPerTile(database, room.Definition) * room.TileCount;

    /// <summary>The room's health now.</summary>
    public static int Health(WorldState world, GameDatabase database, RoomInstance room)
    {
        int lost = 0;
        foreach (int index in room.TileIndices) lost += world.TileAtIndex(index).RoomHealthLost;
        return (StartPerTile(database, room.Definition) * room.TileCount) - lost;
    }

    /// <summary>Writes the room's health outright (<c>Room_SetHealth</c>, <c>0x004E53A0</c>): the
    /// whole deficit on the room's first tile, none on the rest.</summary>
    public static void Set(WorldState world, GameDatabase database, RoomInstance room, int health) =>
        Set(world, room.TileIndices, StartPerTile(database, room.Definition), health);

    private static void Set(WorldState world, IReadOnlyList<int> tiles, int startPerTile, int health)
    {
        for (int i = 0; i < tiles.Count; i++)
            world.TileAtIndex(tiles[i]).RoomHealthLost = i == 0 ? (startPerTile * tiles.Count) - health : 0;
    }

    /// <summary>
    /// <c>Room_ApplyHealthDelta</c> (<c>0x004E5300</c>). A negative delta subtracts; below 1 the room
    /// passes whole to <paramref name="playerId"/> and is reset to StartingHealth × Size. A
    /// non-negative one adds and clamps at <see cref="Cap"/>. CONFIRMED against room <c>+0x35</c> in
    /// the Level 1 and 2 full-state captures (growth, the fall, the takeover and its reset).
    /// </summary>
    [Dk2Behavior(Confidence.Confirmed, "docs/research/room-health.md")]
    public static RoomHealthOutcome Apply(
        WorldState world, GameDatabase database, RoomInstance room, int delta, PlayerId playerId,
        SimulationContext? context = null)
    {
        int health = Health(world, database, room);
        if (delta >= 0)
        {
            Set(world, database, room, Math.Min(health + delta, Cap(database, room)));
            return RoomHealthOutcome.Progressed;
        }

        health += delta;
        if (health > 0)
        {
            Set(world, database, room, health);
            return RoomHealthOutcome.Progressed;
        }

        TransferOwnership(world, database, room, playerId, context);
        Set(world, database, room, StartPerTile(database, room.Definition) * room.TileCount);
        return RoomHealthOutcome.TookOver;
    }

    /// <summary>The part of <c>Room_TransferOwnership</c> (<c>0x004E5650</c>) built here: every tile
    /// to the new owner, a portal's clock restarted (<see cref="PortalRules.OnOwnerChanged"/>) and
    /// both keepers told (<see cref="PostTransfer"/>). Gold stays where it is, a treasury's with the
    /// treasury (Q40). It never writes the health.</summary>
    private static void TransferOwnership(WorldState world, GameDatabase database, RoomInstance room, PlayerId playerId,
        SimulationContext? context)
    {
        PostTransfer(world, room, room.OwnerPlayerId, playerId);
        LevelStatisticsRules.CreditRoomTakeover(world, room.Definition, room.TileCount, playerId);
        RoomListPosition listPosition = RoomLists.Next(world);
        RoomLists.Set(world, room.TileIndices, listPosition);
        foreach (int index in SlabChain.Order(world, room))
        {
            ref TileState tile = ref world.TileAtIndex(index);
            tile.SetExploredBy(playerId);
            if (tile.OwnerPlayerId == playerId) continue;
            tile.OwnerPlayerId = playerId.Value;
            context?.RequestEffect("RoomHealth", ViewEffectRequest.AtTileCentre(new EffectId(8), playerId,
                new TilePos(index % world.Width, index / world.Width), new Fp32(0)));
            tile.KeptManaGain = 0; // Tile_SetOwner moves the room terrain's 0, not the floor's 1 (ADR-0334)
            if (PortalRules.IsPortalTerrain(database, tile.TerrainId)) PortalRules.OnOwnerChanged(ref tile);
            WallSupportRules.ReleaseUnsupported(world, database, new TilePos(index % world.Width, index / world.Width));
        }

        // The type's slot +0x0D kicks every place-holder (room-return-states.md §1, ADR-0406).
        SlabChain.Kick(world, room);
        if (RoomIndex.RoomAt(world, database, room.TileIndices[0]) is { } merged)
            RoomLists.Set(world, merged.TileIndices, listPosition);
    }

    /// <summary>The transfer's messages: 35 to the old owner (<c>0x004E58F4</c>), then 32 to the new,
    /// or 33 when the old owner is the neutral player (<c>0x004E5931</c>); Data1 the room type and
    /// Data2 the room, which DK2 gives as its thing tag and ours as its first slab's tile.</summary>
    internal static void PostTransfer(WorldState world, RoomInstance room, PlayerId oldOwner, PlayerId newOwner)
    {
        int tag = SlabChain.FirstSlab(world, room);
        var type = (RoomTypeId)room.Definition.RoomId;
        if (world.HasPlayer(oldOwner))
            world.Messages.PostOwnership(world, oldOwner.Value, PlayerMessageKind.RoomLost, type, tag);
        if (world.HasPlayer(newOwner))
            world.Messages.PostOwnership(world, newOwner.Value,
                oldOwner == PlayerIds.Neutral ? PlayerMessageKind.NeutralRoomCaptured : PlayerMessageKind.RoomCaptured, type, tag);
    }

    /// <summary>The variable action 10 (<c>0x00497BF0</c>) reads for a tile with this owner: a
    /// neutral tile <c>ConvertNeutralRoomHealth</c> (136, <c>0x006F257C</c>), anyone else's
    /// <c>ConvertRoomHealth</c> (14).</summary>
    public static VariableTypeId ConvertVariable(PlayerId tileOwner) =>
        tileOwner == PlayerIds.Neutral ? VariableTypeId.ConvertNeutralRoomHealth : VariableTypeId.ConvertRoomHealth;

    /// <summary>One conversion application at <paramref name="efficiency"/> 4096ths: the tabled
    /// per-second value over variable 19, times the rate, floored (−5000 × 0xCCC ≫ 12 = −4000, the
    /// captures' step).</summary>
    [Dk2Behavior(Confidence.Confirmed, "docs/research/room-health.md")]
    public static int ConvertPerApplication(GameDatabase database, PlayerId tileOwner, int efficiency) =>
        SimulationTiming.WorkApplication(database[ConvertVariable(tileOwner)], efficiency);

    /// <summary>
    /// One application of action 10 (<c>0x00497BF0</c>) by <paramref name="playerId"/>'s worker on
    /// the tile at <paramref name="at"/>: when the tile gate passes (<c>0x00454040</c>,
    /// <see cref="TileWorkRules.Applies(WorldState, GameDatabase, in TileState, TileWorkKind, byte)"/>),
    /// the rate for the tile's owner goes to the room under it. No tile's terrain or health changes.
    /// </summary>
    public static TileWorkResult Convert(
        WorldState world, GameDatabase database, TilePos at, PlayerId playerId, int efficiency = EfficiencyRules.BaseModifier,
        SimulationContext? context = null)
    {
        int tileIndex = world.TileIndex(at);
        ref TileState tile = ref world.TileAtIndex(tileIndex);
        if (!TileWorkRules.Applies(world, database, tile, TileWorkKind.ConvertRoom, playerId)
            || RoomIndex.RoomAt(world, database, tileIndex) is not { } room)
            return new TileWorkResult(TileWorkOutcome.Rejected, 0, 0, 0, tile.TerrainId);

        int delta = ConvertPerApplication(database, tile.OwnerPlayerId, efficiency);
        TileWorkOutcome outcome = Apply(world, database, room, delta, playerId, context) == RoomHealthOutcome.TookOver
            ? TileWorkOutcome.Transformed
            : delta == 0 ? TileWorkOutcome.NoChange : TileWorkOutcome.Progressed;
        return new TileWorkResult(outcome, delta, 0, 0, tile.TerrainId);
    }

    /// <summary>One application of action 12 (<c>0x00498120</c>) by <paramref name="playerId"/>'s
    /// worker: while the room under the tile is the player's and below its cap
    /// (<see cref="NeedsRepair"/>), <see cref="RepairPerApplication"/> into its health, clamped.</summary>
    public static TileWorkResult Repair(
        WorldState world, GameDatabase database, int tileIndex, PlayerId playerId, int efficiency = EfficiencyRules.BaseModifier)
    {
        ref TileState tile = ref world.TileAtIndex(tileIndex);
        if (!TileWorkRules.Applies(database, tile, TileWorkKind.RepairRoom, playerId)
            || RoomIndex.RoomAt(world, database, tileIndex) is not { } room)
            return new TileWorkResult(TileWorkOutcome.Rejected, 0, 0, 0, tile.TerrainId);
        if (!NeedsRepair(world, database, room, playerId))
            return new TileWorkResult(TileWorkOutcome.NoChange, 0, 0, 0, tile.TerrainId);

        int before = Health(world, database, room);
        Apply(world, database, room, RepairPerApplication(database, efficiency), playerId);
        return new TileWorkResult(TileWorkOutcome.Progressed, Health(world, database, room) - before, 0, 0, tile.TerrainId);
    }

    /// <summary>One repair application (action 12, <c>0x00498120</c>): <c>RepairRoomHealth</c> the
    /// same way.</summary>
    public static int RepairPerApplication(GameDatabase database, int efficiency) =>
        SimulationTiming.WorkApplication(database[VariableTypeId.RepairRoomHealth], efficiency);

    /// <summary><c>Room_NeedsRepair</c> (<c>0x004E6C00</c>): the room is the player's and below its
    /// cap. Action 12's gate; nothing in v1.70 starts action 12 (Q500).</summary>
    public static bool NeedsRepair(WorldState world, GameDatabase database, RoomInstance room, PlayerId playerId) =>
        room.OwnerPlayerId == playerId && Health(world, database, room) < Cap(database, room);

    /// <summary>
    /// The removal rebuild's share (<c>0x004ED440</c>): the old room's health as whole percent of
    /// its cap, through a 12.12 divide then × 100 ≫ 12; each piece left then gets
    /// <c>max(1, MaxHealth × its Size × percent / 100)</c>.
    /// </summary>
    public static int SharePercent(int health, int cap)
    {
        if (cap <= 0) return 0;
        int fraction = (int)(((long)health << 12) / cap);
        return (fraction * 100) >> 12;
    }

    /// <summary>A piece's health after a removal, from <see cref="SharePercent"/>.</summary>
    public static int PieceHealth(int maxPerTile, int size, int percent) =>
        Math.Max(1, (int)((uint)(maxPerTile * size * percent) / 100u));

    /// <summary>Sets a piece of a rebuilt room (tiles in any order) to its share.</summary>
    internal static void SetPiece(WorldState world, GameDatabase database, RoomDef room, IReadOnlyList<int> tiles, int percent) =>
        Set(world, tiles, StartPerTile(database, room), PieceHealth(MaxPerTile(database, room), tiles.Count, percent));

    /// <summary>
    /// The room tick's health step (<c>0x004E4B00</c>): under its cap a room gains its definition's
    /// <c>HealthGain</c> (Rooms.kwd <c>0x41D</c>). 0 for all 26 shipped rooms, so live and inert.
    /// </summary>
    public static void Regenerate(WorldState world, GameDatabase database, RoomInstance room)
    {
        int gain = room.Definition.HealthGain;
        if (gain == 0 || Health(world, database, room) >= Cap(database, room)) return;
        Apply(world, database, room, gain, room.OwnerPlayerId);
    }
}
