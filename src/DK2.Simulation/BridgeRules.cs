using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>
/// What a bridge is built over, and therefore what is left when it goes.
///
/// <para><b>The problem this solves.</b> Every room terrain in the shipped data
/// has the same <see cref="TerrainDef.DestroyedTypeTerrainId"/> — Claimed Path —
/// the two bridges included. Taken at face value that means selling a bridge over
/// a lava lake leaves a keeper standing on solid floor in the middle of it. The
/// owner confirmed it does not (2026-09-03): <b>a sold bridge reverts to the
/// water or lava it spanned</b>. So the hazard has to be recorded per tile
/// somewhere, and the destroyed-type field is not it.</para>
///
/// <para><b>It is recorded, in a byte we were already carrying.</b> The MAP
/// record gives four bytes a tile: terrain, owner, and two nobody here had a
/// meaning for (Q16). <see cref="TileState.Flags"/> is the third of them, kept
/// verbatim since M2 with the comment "bridge-related; exact meaning UNKNOWN".
/// It is <b>1 on every tile in every shipped level except bridge tiles over
/// lava, where it is 2</b>:</para>
///
/// <list type="bullet">
/// <item>swept all <b>66</b> shipped level maps: the value is never anything but
/// 1 or 2, and <b>2 occurs only on bridge terrain</b> — never on any of the other
/// 38 terrain types;</item>
/// <item>every <c>2</c> bridge tile has a <b>lava</b> tile orthogonally beside
/// it, and every <c>1</c> bridge tile that touches a hazard at all touches
/// <b>water</b>. Across the whole corpus and all three bridge types the two sets
/// never cross;</item>
/// <item>the reading survives the trap that nearly refuted it. A level's
/// remaining water count is <i>not</i> evidence about what its bridges span,
/// because a fully bridged pond leaves no water to count — Furnace looks like it
/// has no water at all and its bridges are over lava. Asking what each tile
/// <i>borders</i> is what makes the partition visible.</item>
/// </list>
///
/// <para><b>Independently corroborated, by somebody who did not know what it
/// meant.</b> A 2013 KeeperKlan reverse-engineering thread — the author says they
/// had been "upside down inside of this executable for the past year and a half"
/// — documents the on-disk map element as
/// <c>{BYTE idTerrain; BYTE idPlayer; BYTE flags; BYTE unused;}</c> and notes of
/// the third byte that <i>"only a '2' bit here is interpreted to do anything
/// special at load, 1 may indicate 'valid'"</i>, and that the 2 bit <i>"assigns an
/// as yet unknown flag in the runtime map element"</i>. That confirms three things
/// from a source with no stake in this reading: the byte is a flags field, the
/// only value that means anything is 2, and the fourth byte is unused. What the
/// 2 is <b>for</b> was open there and is what the 66-map sweep above supplies.
/// Facts quoted with provenance; no code taken. See
/// docs/research/external-resources.md.</para>
///
/// <para><b>STRONGLY INFERRED rather than CONFIRMED.</b> The behaviour is
/// confirmed; that this byte is where the original keeps it is an inference from
/// a clean partition over 66 maps. It could be a rendering hint that merely
/// happens to correlate. Nothing depends on being right about the original's
/// storage, only on the revert being correct — and per the project's brief the
/// internal representation is explicitly not required to match.</para>
///
/// <para><b>A bridge cannot be built on land</b>, so for a bridge tile the byte
/// is a straight binary: 2 means lava and anything else means water. That is not
/// an assumption about the byte, it is the ROOMS record — neither bridge carries
/// <c>PlaceableOnLand</c>.</para>
///
/// <para><b>What this unlocked beyond selling.</b>
/// <c>WoodBridgeLifeOnLavaSeconds</c> is 30: a wooden bridge over lava burns
/// away, and that rule needs to know which bridges are over lava. Nothing here
/// could tell before. It is implemented now as <see cref="LifetimeTicks"/> and
/// <see cref="Systems.BridgeBurnSystem"/> — the <i>tile</i> half of it. What
/// happens to whatever was standing on the bridge needs creature health, which
/// does not exist yet. Q67.</para>
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/rooms-and-gold.md")]
public static class BridgeRules
{
    /// <summary>
    /// The value <see cref="TileState.Flags"/> takes on a bridge tile spanning
    /// lava. Every other tile in every shipped level carries 1.
    /// </summary>
    public const byte SpansLava = 2;

    /// <summary>The default, and what a bridge over water carries.</summary>
    public const byte SpansWater = 1;

    /// <summary>
    /// Whether this room is one that goes over a hazard — asked of the room's own
    /// placement flags, so nothing here knows what a bridge is called.
    /// </summary>
    public static bool IsBridge(RoomDef room) =>
        room.PlaceableOnWater || room.PlaceableOnLava;

    /// <summary>
    /// The terrain a bridge tile is standing over, or null if this tile is not a
    /// bridge.
    /// </summary>
    public static TerrainDef? SpannedTerrain(GameDatabase database, in TileState tile)
    {
        RoomDef? room = database.RoomByTerrain(tile.TerrainId);
        if (room is null || !IsBridge(room)) return null;

        return tile.Flags == SpansLava ? database.LavaTerrain : database.WaterTerrain;
    }

    /// <summary>
    /// The marker to record on a tile about to be bridged, given the terrain
    /// being covered.
    ///
    /// Used when a <i>player</i> builds a bridge: the level file's byte only
    /// covers bridges the level shipped with, and a bridge laid mid-game has to
    /// record its own hazard the same way or selling it would revert it to the
    /// wrong thing.
    /// </summary>
    public static byte MarkerFor(TerrainDef covered) =>
        covered.IsLava ? SpansLava : SpansWater;

    /// <summary>
    /// How long a bridge laid on this terrain lasts, in ticks, or 0 if it lasts
    /// for ever.
    ///
    /// <para><b>A wooden bridge over lava burns away</b>
    /// (<c>WoodBridgeLifeOnLavaSeconds</c> = 30). CONFIRMED by the owner
    /// (2026-09-03), and with it the one detail that decides the shape: <b>the
    /// clock starts when the bridge is built.</b> Not when a creature steps on
    /// it, and not as damage to the tile's health — which rules out the two
    /// readings that would otherwise have fitted the variable's name equally
    /// well.</para>
    ///
    /// <para><b>This is the one rule here that names a room type</b>, and the
    /// reason is that DK2's own variable names it: <c>WoodBridge…</c>. The
    /// terrain table cannot help — Wooden Bridge and Stone Bridge carry
    /// <i>identical</i> flags (0x30000034) and identical health, and neither
    /// carries <see cref="TerrainFlags.Decay"/>. So the distinction exists only
    /// as the room's identity, and reading it here is following the data rather
    /// than working around it.</para>
    ///
    /// <para>A stone bridge over lava lasts for ever, which is what makes the
    /// wooden one a real decision in the original: cheap and temporary against
    /// 500 a tile and permanent.</para>
    /// </summary>
    public static int LifetimeTicks(GameDatabase database, RoomDef room, TerrainDef covered)
    {
        if (!covered.IsLava) return 0;
        if (room.TypeId != RoomTypeId.WoodenBridge) return 0;

        return SimulationTiming.TicksFromSeconds(
            database[VariableTypeId.WoodBridgeLifeOnLavaSeconds]);
    }

    /// <summary>
    /// What a tile becomes when whatever is on it is taken away — a bridge back
    /// to its hazard, anything else to its terrain's own destroyed type.
    ///
    /// <para><b>The one place that decision is made</b>, so that selling a bridge
    /// and beating one down cannot disagree. Both used to go through the
    /// destroyed-type field alone and both were wrong in the same way.</para>
    /// </summary>
    public static byte RevertsTo(GameDatabase database, in TileState tile)
    {
        TerrainDef? spanned = SpannedTerrain(database, tile);
        return spanned?.TerrainId ?? database.TerrainById(tile.TerrainId).DestroyedTypeTerrainId;
    }
}
