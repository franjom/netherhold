// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>Where a keeper's mana comes from, and what stops it growing (mana.md).
/// <para><b>Mana is land</b>: the three terrains with a non-zero <see cref="TerrainDef.ManaGain"/>
/// — Claimed Path 1, Reinforced Wall 1, Claimed Vault 100 — are exactly the non-room
/// terrains a keeper holds, so income is the size of the territory (CONFIRMED from the
/// data). The unit is per second (STRONGLY INFERRED): <c>MaximumManaGainPerSecond</c>
/// (500) caps the total and a cap shares its unit. A heart adds a flat 30, once per
/// keeper — exact, because <b>a keeper has at most one heart, ever</b>. Every imp drains
/// <see cref="ImpUpkeepPerSecond"/>, kept apart from income because the cap covers only
/// the income half (Q53).</para>
/// <para>Not implemented: the four other spell upkeeps (Summon Horny 2000 a second, Call To Arms 90 — M7) and trap drain, owner-reported and uninvestigated.
/// <b><see cref="TerrainDef.MaxManaGain"/> and <c>DungeonHeartMaxManaIncrease</c> may not justify a mana rule</b>: both read like a ceiling contribution and neither can be
/// one, the ceiling being 200000 from the first second and never moving (CONFIRMED, Q37).</para></summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/mana.md")]
public static class ManaRules
{
    /// <summary>Mana per second this tile generates for whoever owns it. Read from the
    /// terrain, never from a terrain id, so a new claimable floor type would generate
    /// mana without a line of code changing.</summary>
    public static int GainPerSecond(GameDatabase database, in TileState tile) =>
        database.TerrainById(tile.TerrainId).ManaGain;

    /// <summary>The most mana any one keeper may gain in a second, whatever they own.
    /// HYPOTHESIS that the cap is per player rather than level-wide (Q38): nothing in
    /// the name says which, and a shared cap would make one keeper's expansion throttle
    /// another's, which is not how the original plays.</summary>
    public static int MaxGainPerSecond(GameDatabase database) =>
        database[VariableTypeId.MaximumManaGainPerSecond];

    /// <summary>What a keeper's dungeon heart generates per second, on top of their
    /// land. Counted once per keeper, not per heart tile, so a 5×5 heart is worth 30 a
    /// second and not 750.</summary>
    public static int HeartGainPerSecond(GameDatabase database) =>
        database[VariableTypeId.DungeonHeartManaGenerationIncreasePerSecond];

    /// <summary>The terrain a dungeon heart is made of, or null if this database has
    /// none (only a synthetic one). Names the room <i>type</i>, as
    /// <see cref="GoldStore.Capacity"/> does — the variable is itself named for the dungeon heart — but never a terrain id: that is read out of the room record.</summary>
    public static byte? DungeonHeartTerrainId(GameDatabase database)
    {
        foreach (RoomDef room in database.Rooms.Values)
            if (room.TypeId == RoomTypeId.DungeonHeart) return room.TerrainId;
        return null;
    }

    /// <summary>The level at which mana stops accruing and starts draining back.
    /// <b>200000 for every keeper</b>, CONFIRMED by observation, and it does not grow
    /// with the dungeon. A soft ceiling rather than a clamp:
    /// <c>ExcessManaDecreaseRatePerSecond</c> only means something if mana can be above it, which a lump sum such as <c>SpecialIncreaseManaAmount</c> (50000) can do.</summary>
    public static int Threshold(GameDatabase database) =>
        database[VariableTypeId.MaximumManaThreshold];

    /// <summary>How fast mana above <see cref="Threshold"/> drains away.</summary>
    public static int ExcessDrainPerSecond(GameDatabase database) =>
        database[VariableTypeId.ExcessManaDecreaseRatePerSecond];

    /// <summary>What every player in the world gains per second, indexed to match
    /// <see cref="WorldState.PlayerAtIndex"/>. One pass over the map rather than one per
    /// player: the map is the big thing.</summary>
    public static int[] GainPerSecondByPlayerIndex(WorldState world, GameDatabase database)
    {
        var gain = new int[world.PlayerCount];
        var hasHeart = new bool[world.PlayerCount];
        byte? heartTerrain = DungeonHeartTerrainId(database);

        // owner id -> index into gain, or -1. Tile ownership is a byte, so 256 slots
        // cover every value a tile can hold without a search per tile.
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

            // A flag rather than a count: however many tiles a heart covers it is one
            // heart, and a keeper never has two.
            if (tile.TerrainId == heartTerrain) hasHeart[player] = true;
        }

        int fromHeart = hasHeart.Contains(true) ? HeartGainPerSecond(database) : 0;
        int cap = MaxGainPerSecond(database);
        for (int i = 0; i < gain.Length; i++)
            gain[i] = Math.Min(gain[i] + (hasHeart[i] ? fromHeart : 0), cap);

        return gain;
    }

    /// <summary>What one imp costs its keeper per second, for as long as it is alive:
    /// <b>7</b>, CONFIRMED (owner, 2026-08-30, Q53) from the mana orb reading 28 with
    /// four imps and 35 with five, which fixes the rate and the per-second unit at once.
    /// Read from the <b>Create Imp spell record</b> rather than written here — the upkeep
    /// is not on the CREATURES record at all, and the giveaway is that the same column holds 2000 for Summon Horny, the figure the community reports for a Horned Reaper.</summary>
    public static int ImpUpkeepPerSecond(GameDatabase database) =>
        database.RequireKeeperSpell(KeeperSpellName.CreateImp).ManaUpkeepPerSecond;

    /// <summary>What every player pays per second to keep the imps they have, indexed
    /// to match <see cref="WorldState.PlayerAtIndex"/>. Charged for <b>every</b> imp,
    /// including the free ones a keeper never paid for — the owner's four starting imps
    /// drain 28 — so the floor is free to fill and not free to keep, which is the
    /// pressure the imp economy is built around.</summary>
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

    /// <summary>One second of mana for one player: what their land earned, less what
    /// their imps cost. Returns the change actually applied.
    /// <para><b>The two halves are separate for the income cap and netted for the
    /// ceiling</b>, and both are observed: <see cref="MaxGainPerSecond"/> caps the green
    /// figure alone, so subtracting upkeep first would let a big workforce raise a
    /// keeper's income cap; but the owner's screenshot of a full keeper reads exactly
    /// 200000 with +400 and −297 on the orb, so income beating upkeep pins them at the
    /// top rather than 297 short.</para>
    /// <para><b>Mana floors at zero</b>, and what the original does when a keeper cannot
    /// pay is UNKNOWN (Q57): <c>InsufficientManaImpEvaluationPeriodSeconds</c> (10) says
    /// something happens on a ten-second clock, and guessing it would be inventing
    /// behaviour, so a keeper here simply sits at zero.</para></summary>
    public static int ApplyOneSecond(
        GameDatabase database, ref PlayerState player, int gainPerSecond, int upkeepPerSecond = 0)
    {
        int threshold = Threshold(database);
        int before = player.Mana;
        long mana;

        if (before > threshold)
        {
            // Above the ceiling nothing accrues, whatever the land is worth; the excess
            // bleeds off at its own rate and upkeep comes out too.
            mana = Math.Max(threshold, (long)before - ExcessDrainPerSecond(database))
                   - upkeepPerSecond;
        }
        else
        {
            // Netted, THEN clamped, in that order: clamping first and taking upkeep
            // afterwards would park a full keeper 297 below the ceiling for ever
            // instead of pinned to it. See the doc comment for the observation.
            mana = Math.Min(threshold, (long)before + gainPerSecond - upkeepPerSecond);
        }

        player.Mana = (int)Math.Max(0, mana);
        return player.Mana - before;
    }
}
