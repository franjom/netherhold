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
/// <para><b>What is not here.</b> Nothing spends mana yet, because no spell
/// exists. The dungeon heart contributes nothing, because
/// <c>DungeonHeartManaGenerationIncreasePerSecond</c> (30) is per
/// <i>heart</i> and a heart is a room instance, which the simulation does not
/// have — rooms are terrain here. And <see cref="TerrainDef.MaxManaGain"/> is
/// read by nothing at all: it is non-zero on exactly one terrain, which is not
/// enough to tell what it means (Q37). See docs/research/mana.md
/// §"What is not implemented, and is not guessed at".</para>
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
    /// The level at which mana stops accruing and starts draining back.
    ///
    /// A soft ceiling, not a clamp: <c>ExcessManaDecreaseRatePerSecond</c> only
    /// means something if mana can be above this, which lump sums such as
    /// <c>SpecialIncreaseManaAmount</c> (50000) can do. STRONGLY INFERRED from
    /// those two variables existing together.
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
        }

        int cap = MaxGainPerSecond(database);
        for (int i = 0; i < gain.Length; i++)
            gain[i] = Math.Min(gain[i], cap);

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
