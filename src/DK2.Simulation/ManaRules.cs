// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

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
/// <para><b>Mana also drains.</b> Every imp alive costs its keeper
/// <see cref="ImpUpkeepPerSecond"/> — 7 a second, the upkeep on DK2's own Create
/// Imp spell record — which is what the red figure under the mana orb is made
/// of. CONFIRMED by the owner from two screenshots: four imps drain 28 and five
/// drain 35 (Q53). Income and upkeep are kept apart rather than netted, because
/// the original shows them apart and because <c>MaximumManaGainPerSecond</c>
/// caps only the income half.</para>
///
/// <para><b>What is not here.</b> Only Create Imp's upkeep, of the five spells
/// that carry one: a summoned Horned Reaper costs 2000 a second and Call To Arms
/// 90, but neither spell exists yet (M7). Traps draw mana too — reported by the
/// owner, not investigated. And <see cref="TerrainDef.MaxManaGain"/> is read by nothing at all,
/// nor is <c>DungeonHeartMaxManaIncrease</c>: both look like contributions to a
/// keeper's mana ceiling, and neither can be one, because the ceiling is 200000
/// from the first second and never moves (CONFIRMED — Q37/Q37a). <b>Neither may
/// be used to justify a mana rule.</b> See docs/research/mana.md §"What is not implemented, and is
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
    /// What one imp costs its keeper per second, for as long as it is alive.
    ///
    /// <b>CONFIRMED (2026-08-30, owner, Q53): 7.</b> The owner photographed the
    /// mana orb with four imps and again with five; the red figure read 28 and
    /// then 35. Both divide by 7 exactly, which fixes the rate and confirms the
    /// unit is per second at the same time.
    ///
    /// <para><b>It is DK2's number, off the Create Imp spell record</b>, not a
    /// constant written here — which is the whole reason this rule exists now
    /// and did not before. The field was hunted for a long time in the wrong
    /// place: an upkeep for a creature ought to be on the CREATURES record and
    /// is not, and a scan of all 5449 bytes for the Imp's 7 turned up only an
    /// ArtResource frame count, an array-length header and uninitialised
    /// padding. It lives on the <i>spell</i>, and the giveaway is that the same
    /// column holds 2000 for Summon Horny — the figure the community reports for
    /// a Horned Reaper's drain, arrived at independently.</para>
    /// </summary>
    public static int ImpUpkeepPerSecond(GameDatabase database) =>
        database.RequireKeeperSpell(KeeperSpellName.CreateImp).ManaUpkeepPerSecond;

    /// <summary>
    /// What every player pays per second to keep the imps they have, indexed to
    /// match <see cref="WorldState.PlayerAtIndex"/>.
    ///
    /// <para>Charged for every imp, including the free ones a keeper never paid
    /// for: the owner's four starting imps drain 28, so the floor is free to
    /// fill and not free to keep. That is what stops a keeper buying imps until
    /// the map is full, and it is the pressure the whole imp economy is built
    /// around.</para>
    /// </summary>
    public static int[] UpkeepPerSecondByPlayerIndex(WorldState world, GameDatabase database)
    {
        var upkeep = new int[world.PlayerCount];
        int perImp = ImpUpkeepPerSecond(database);
        if (perImp == 0) return upkeep;

        Span<int> indexOfOwner = stackalloc int[256];
        indexOfOwner.Fill(-1);
        for (int i = 0; i < world.PlayerCount; i++)
            indexOfOwner[world.PlayerAtIndex(i).PlayerId] = i;

        IReadOnlyList<CreatureState> creatures = world.Creatures;
        for (int i = 0; i < creatures.Count; i++)
        {
            CreatureState creature = creatures[i];
            int player = indexOfOwner[creature.OwnerPlayerId];
            if (player < 0) continue;
            if (database.CreatureById(creature.CreatureTypeId).Flags.HasFlag(CreatureFlags.IsWorker))
                upkeep[player] += perImp;
        }
        return upkeep;
    }

    /// <summary>
    /// One second of mana for one player: what their land earned, less what
    /// their imps cost.
    ///
    /// <para><b>The two halves are separate for the income cap and netted for
    /// the ceiling</b>, and both halves of that are observed rather than
    /// chosen. <see cref="MaxGainPerSecond"/> caps the green figure alone, so
    /// upkeep must not be subtracted before it — otherwise a big enough
    /// workforce would raise a keeper's income cap. But the <i>ceiling</i> sees
    /// the net: the owner's screenshot of a keeper at full mana reads exactly
    /// 200000 with +400 and −297 on the orb, so income beating upkeep pins a
    /// keeper at the top rather than parking them 297 short of it.</para>
    ///
    /// <para><b>Mana floors at zero</b>, and what the original does when a
    /// keeper cannot pay is UNKNOWN. <c>InsufficientManaImpEvaluationPeriodSeconds</c>
    /// (10) says something happens on a ten-second clock — the obvious guess is
    /// that imps start dying — but guessing it would be inventing behaviour, so
    /// a keeper here simply sits at zero. See Q57.</para>
    ///
    /// Returns the change actually applied, which is what the caller reports.
    /// </summary>
    public static int ApplyOneSecond(
        GameDatabase database, ref PlayerState player, int gainPerSecond, int upkeepPerSecond = 0)
    {
        int threshold = Threshold(database);
        int before = player.Mana;
        long mana;

        if (before > threshold)
        {
            // Above the ceiling nothing accrues, whatever the land is worth;
            // the excess bleeds off at its own rate and upkeep comes out too.
            mana = Math.Max(threshold, (long)before - ExcessDrainPerSecond(database))
                   - upkeepPerSecond;
        }
        else
        {
            // Netted, THEN clamped — in that order, and the order is observed
            // rather than chosen. The owner's screenshot of a full treasury of
            // mana reads exactly 200000 while the orb shows +400 and −297: a
            // keeper whose income beats their upkeep sits pinned at the
            // ceiling. Clamping the income first and taking upkeep afterwards
            // would park them 297 below it for ever, which is a different and
            // wrong-looking number.
            mana = Math.Min(threshold, (long)before + gainPerSecond - upkeepPerSecond);
        }

        player.Mana = (int)Math.Max(0, mana);
        return player.Mana - before;
    }
}
