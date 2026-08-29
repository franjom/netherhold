using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>
/// Where a keeper's mana comes from, and what stops it growing.
///
/// <para><b>Mana is land.</b> Three terrain types in the whole table carry a
/// non-zero <see cref="TerrainDef.ManaGain"/> — Claimed Path (1), Reinforced
/// Wall (1) and Claimed Vault (100) — and all three are exactly the terrains a
/// keeper holds that are not a room. So a keeper's mana income is the size of
/// their territory, which is what the original visibly does: mana climbs as the
/// dungeon is claimed and falls when land is lost. CONFIRMED at the level of the
/// data; see docs/research/mana.md.</para>
///
/// <para><b>The unit is per second</b> (STRONGLY INFERRED). The terrain field
/// itself carries no unit, but the balance table caps the total with
/// <c>MaximumManaGainPerSecond</c> (500), and a cap and the thing it caps have
/// to be in the same unit. 500 against one per claimed tile also puts the cap
/// where a large dungeon would actually meet it, which a per-tick reading would
/// not.</para>
///
/// <para><b>A dungeon heart pays too</b>, a flat
/// <c>DungeonHeartManaGenerationIncreasePerSecond</c> (30) on top of the land.
/// It is counted once per keeper rather than once per heart tile, and that is
/// exact rather than a simplification: <b>a keeper has at most one dungeon
/// heart, ever</b> — CONFIRMED by observation of the original, where losing it
/// is what defeat <i>is</i>. So "per heart" and "per keeper who has one" are the
/// same quantity, and no room-instance concept is needed to count it.</para>
///
/// <para><b>What is not here.</b> Nothing spends mana yet, because no spell
/// exists. And <see cref="TerrainDef.MaxManaGain"/> is read by nothing at all,
/// nor is <c>DungeonHeartMaxManaIncrease</c>: both look like contributions to a
/// keeper's mana ceiling, and neither can be one, because the ceiling is 200000
/// from the first second and never moves (CONFIRMED — Q37/Q37a). What they are
/// instead has no candidate explanation left. <b>Neither may be used to justify
/// a mana rule.</b> See docs/research/mana.md §"What is not implemented, and is
/// not guessed at".</para>
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/mana.md")]
public static class ManaRules
{
    /// <summary>
    /// Mana per second this tile generates for whoever owns it.
    ///
    /// Read from the terrain, never from a terrain id: a mod that made a new
    /// claimable floor type would generate mana here without a line of code
    /// changing, which is the same rule the tile and room mechanics follow.
    /// </summary>
    public static int GainPerSecond(GameDatabase database, in TileState tile) =>
        database.TerrainById(tile.TerrainId).ManaGain;

    /// <summary>
    /// The most mana any one keeper may gain in a second, whatever they own.
    ///
    /// HYPOTHESIS that the cap is per player rather than shared across the
    /// level: nothing in the name says which, and a shared cap would make one
    /// keeper's expansion throttle another's, which is not how the original
    /// plays. See Q38.
    /// </summary>
    public static int MaxGainPerSecond(GameDatabase database) =>
        database[VariableTypeId.MaximumManaGainPerSecond];

    /// <summary>
    /// What a keeper's dungeon heart generates per second, on top of their land.
    ///
    /// Counted once per keeper, not once per heart tile: a keeper has at most
    /// one heart (CONFIRMED — observed in the original, where losing it is
    /// defeat), so a 5×5 heart is worth 30 a second and not 750.
    /// </summary>
    public static int HeartGainPerSecond(GameDatabase database) =>
        database[VariableTypeId.DungeonHeartManaGenerationIncreasePerSecond];

    /// <summary>
    /// The terrain a dungeon heart is made of, or null if this database has no
    /// dungeon heart at all (which only a synthetic one does not).
    ///
    /// Names the room <i>type</i>, like <see cref="GoldStore.Capacity"/> and for
    /// the same reason: the variable is itself named for the dungeon heart, so
    /// the room type is the data's own vocabulary for this question. It is still
    /// not a terrain id — the id is read out of the room record.
    /// </summary>
    public static byte? DungeonHeartTerrainId(GameDatabase database)
    {
        foreach (RoomDef room in database.Rooms.Values)
            if (room.TypeId == RoomTypeId.DungeonHeart) return room.TerrainId;
        return null;
    }

    /// <summary>
    /// The level at which mana stops accruing and starts draining back.
    ///
    /// <b>200000 is the ceiling every keeper has</b> — CONFIRMED by observation
    /// of the original — and it does not grow with the dungeon. A soft ceiling
    /// rather than a clamp: <c>ExcessManaDecreaseRatePerSecond</c> only means
    /// something if mana can be above this, which lump sums such as
    /// <c>SpecialIncreaseManaAmount</c> (50000) can do.
    /// </summary>
    public static int Threshold(GameDatabase database) =>
        database[VariableTypeId.MaximumManaThreshold];

    /// <summary>How fast mana above <see cref="Threshold"/> drains away.</summary>
    public static int ExcessDrainPerSecond(GameDatabase database) =>
        database[VariableTypeId.ExcessManaDecreaseRatePerSecond];

    /// <summary>
    /// What every player in the world gains per second, indexed to match
    /// <see cref="WorldState.PlayerAtIndex"/>.
    ///
    /// One pass over the map rather than one per player: the map is the big
    /// thing and the player list is not.
    /// </summary>
    public static int[] GainPerSecondByPlayerIndex(WorldState world, GameDatabase database)
    {
        var gain = new int[world.PlayerCount];
        var hasHeart = new bool[world.PlayerCount];
        byte? heartTerrain = DungeonHeartTerrainId(database);

        // owner id -> index into gain, or -1. Tile ownership is a byte, so this
        // covers every value a tile can hold without a search per tile.
        Span<int> indexOfOwner = stackalloc int[256];
        indexOfOwner.Fill(-1);
        for (int i = 0; i < world.PlayerCount; i++)
            indexOfOwner[world.PlayerAtIndex(i).PlayerId] = i;

        for (int i = 0; i < world.TileCount; i++)
        {
            ref TileState tile = ref world.TileAtIndex(i);
            int player = indexOfOwner[tile.OwnerPlayerId];
            if (player < 0) continue;

            int perTile = GainPerSecond(database, tile);
            if (perTile != 0) gain[player] += perTile;

            // A flag rather than a count: however many tiles a heart covers,
            // it is one heart, and a keeper never has two.
            if (tile.TerrainId == heartTerrain) hasHeart[player] = true;
        }

        int fromHeart = hasHeart.Contains(true) ? HeartGainPerSecond(database) : 0;
        int cap = MaxGainPerSecond(database);
        for (int i = 0; i < gain.Length; i++)
            gain[i] = Math.Min(gain[i] + (hasHeart[i] ? fromHeart : 0), cap);

        return gain;
    }

    /// <summary>
    /// One second of mana for one player: accrual up to the threshold, or drain
    /// back down to it, and never both.
    ///
    /// Returns the change actually applied, which is what the caller reports.
    /// </summary>
    public static int ApplyOneSecond(GameDatabase database, ref PlayerState player, int gainPerSecond)
    {
        int threshold = Threshold(database);
        int before = player.Mana;

        if (player.Mana > threshold)
        {
            // Above the ceiling nothing accrues, whatever the land is worth.
            player.Mana = Math.Max(threshold, player.Mana - ExcessDrainPerSecond(database));
        }
        else if (gainPerSecond > 0)
        {
            player.Mana = (int)Math.Min(threshold, (long)player.Mana + gainPerSecond);
        }

        return player.Mana - before;
    }
}
