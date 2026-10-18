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
/// <para>Normal Horny upkeep is <see cref="ReaperRules.UpkeepPerSecond"/>; the one-shot variant is excluded by the binary. Possession is <see cref="PossessionRules.UpkeepPerSecond"/>. Call To Arms is <see cref="CallToArmsRules.UpkeepPerSecond"/>, and a completed trap's is <see cref="TrapUpkeepPerSecond"/>.
/// <b><see cref="TerrainDef.MaxManaGain"/> and <c>DungeonHeartMaxManaIncrease</c> may not justify a mana rule</b>: both read like a ceiling contribution and neither can be
/// one, the ceiling being 200000 from the first second and never moving (CONFIRMED, Q37).</para></summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/mana.md")]
public static class ManaRules
{
    /// <summary>Mana per second this tile generates for whoever owns it: the terrain's
    /// <c>ManaGain</c>, plus, on a room tile, the floor's gain it kept when the room was built
    /// over it (<see cref="TileState.KeptManaGain"/>). DK2 keeps the rate as a running word
    /// (<c>player+0x10E2</c>): <c>Tile_SetOwner</c> (<c>0x004559D0</c>) moves the current
    /// terrain's gain between owners, and <c>Tile_SetTerrain</c>'s adjustment
    /// (<c>0x00455B90</c>) runs only when neither the old nor the new terrain is a Room, so a
    /// room laid on claimed floor keeps the floor's 1 and a room owned from the start pays
    /// nothing (ADR-0334). Read from the terrain, never from a terrain id.</summary>
    public static int GainPerSecond(GameDatabase database, in TileState tile)
    {
        TerrainDef terrain = database.TerrainById(tile.TerrainId);
        return terrain.ManaGain + (terrain.IsRoom ? tile.KeptManaGain : 0);
    }

    /// <summary>The most mana any one keeper may gain in a second, whatever they own. The cap is
    /// per keeper, not level-wide: one keeper's dungeon does not throttle another's.
    /// CONFIRMED (owner, 2026-08-29, Q38).</summary>
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

    /// <summary>The mana ceiling, player <c>+0x10EA</c>: <b>200000 for every keeper</b>,
    /// CONFIRMED by observation, and it does not grow with the dungeon. A clamp, not a soft
    /// ceiling: a lump sum such as <c>SpecialIncreaseManaAmount</c> (50000) can lift mana above
    /// it, and the next second whose net is not negative cuts it back in one step
    /// (<see cref="ApplyOneSecond"/>, Q545). <c>ExcessManaDecreaseRatePerSecond</c> has no
    /// absolute-address reader but its loader; a table-relative one is not excluded.</summary>
    public static int Threshold(GameDatabase database) =>
        database[VariableTypeId.MaximumManaThreshold];

    /// <summary>What every player in the world gains per second, indexed to match
    /// <see cref="WorldState.PlayerAtIndex"/>. One pass over the map rather than one per
    /// player: the map is the big thing.</summary>
    public static int[] GainPerSecondByPlayerIndex(WorldState world, GameDatabase database)
    {
        var gain = new int[world.PlayerCount];
        // Tile_SetOwner moves the terrain's MaxManaGain into the owner's cap word (+0x10DE)
        // beside its gain: a Claimed Vault raises a keeper's cap by 100 a tile (ADR-0334).
        var capBonus = new int[world.PlayerCount];
        var hasHeart = new bool[world.PlayerCount];
        byte? heartTerrain = DungeonHeartTerrainId(database);

        // owner id -> index into gain, or -1. Tile ownership is a byte, so 256 slots
        // cover every value a tile can hold without a search per tile.
        Span<int> indexOfOwner = stackalloc int[256];
        indexOfOwner.Fill(-1);
        for (int i = 0; i < world.PlayerCount; i++)
            indexOfOwner[world.PlayerAtIndex(i).PlayerId.Value] = i;

        for (int i = 0; i < world.TileCount; i++)
        {
            ref TileState tile = ref world.TileAtIndex(i);
            int player = indexOfOwner[tile.OwnerPlayerId];
            if (player < 0) continue;

            int perTile = GainPerSecond(database, tile);
            if (perTile != 0) gain[player] += perTile;
            capBonus[player] += database.TerrainById(tile.TerrainId).MaxManaGain;

            // A flag rather than a count: however many tiles a heart covers it is one
            // heart, and a keeper never has two.
            if (tile.TerrainId == heartTerrain) hasHeart[player] = true;
        }

        int fromHeart = hasHeart.Contains(true) ? HeartGainPerSecond(database) : 0;
        int cap = MaxGainPerSecond(database);
        for (int i = 0; i < gain.Length; i++)
            gain[i] = Math.Min(gain[i] + (hasHeart[i] ? fromHeart : 0), cap + capBonus[i]);

        return gain;
    }

    /// <summary>What one imp costs its keeper per second, for as long as it is alive:
    /// <b>7</b>, CONFIRMED (owner, 2026-08-30, Q53) from the mana orb reading 28 with
    /// four imps and 35 with five, which fixes the rate and the per-second unit at once.
    /// Read from the <b>Create Imp spell record</b> rather than written here — the upkeep
    /// is not on the CREATURES record at all, and the giveaway is that the same column holds 2000 for Summon Horny, the figure the community reports for a Horned Reaper.</summary>
    public static int ImpUpkeepPerSecond(GameDatabase database) =>
        database.RequireKeeperSpell(KeeperSpellName.CreateImp).ManaUpkeepPerSecond;

    /// <summary>What one completed trap costs its keeper per second: definition <c>+0x96</c>
    /// (<c>Traps.kwd</c> <c>0x202</c>, a signed word), added to the player's <c>+0x10D6</c> at
    /// completion (<c>0x004C1B60</c>) and taken off at its cleanup (<c>0x004C1B90</c>), so the
    /// sum over the completed traps standing. Owner-measured (Q59): Spike and Sentry 5,
    /// Fireburst and Freeze 10, Lightning 25.</summary>
    public static int TrapUpkeepPerSecond(TrapDef definition) => (short)definition.ManaUpkeepPerSecond;

    /// <summary>What every player pays per second to keep the imps and traps they have, indexed
    /// to match <see cref="WorldState.PlayerAtIndex"/>. Charged for <b>every</b> imp,
    /// including the free ones a keeper never paid for — the owner's four starting imps
    /// drain 28 — so the floor is free to fill and not free to keep, which is the
    /// pressure the imp economy is built around.</summary>
    public static int[] UpkeepPerSecondByPlayerIndex(WorldState world, GameDatabase database) =>
        UpkeepPerSecondByPlayerIndex(world, database, world.CurrentTick.Next);

    /// <summary>Upkeep evaluated at a specific turn, including the prepaid Call to Arms boundary.</summary>
    public static int[] UpkeepPerSecondByPlayerIndex(WorldState world, GameDatabase database, GameTick atTick)
    {
        int[] upkeep = ImpAndTrapUpkeepPerSecondByPlayerIndex(world, database);
        uint turn = unchecked((uint)atTick.Value);
        for (int i = 0; i < world.PlayerCount; i++)
        {
            upkeep[i] += PossessionRules.UpkeepPerSecond(world, database, world.PlayerAtIndex(i));
            upkeep[i] += CallToArmsRules.UpkeepPerSecond(world, database, world.PlayerAtIndex(i), turn);
            upkeep[i] += ReaperRules.UpkeepPerSecond(world, database, world.PlayerAtIndex(i));
        }
        return upkeep;
    }

    /// <summary>The imp (<c>+0x10D2</c>) and completed-trap (<c>+0x10D6</c>) part of the drain,
    /// which <c>0x004C10E0</c> also holds back from the mana a possession must be paid from.</summary>
    public static int[] ImpAndTrapUpkeepPerSecondByPlayerIndex(WorldState world, GameDatabase database)
    {
        var upkeep = new int[world.PlayerCount];
        Span<int> indexOfOwner = stackalloc int[256];
        indexOfOwner.Fill(-1);
        for (int i = 0; i < world.PlayerCount; i++)
            indexOfOwner[world.PlayerAtIndex(i).PlayerId.Value] = i;

        // +0x10D6: the completed traps' upkeep, summed as Mana_UpkeepPerSecond (0x004C17A0) sums it.
        // DK2 registers it for any owner but pays keepers alone (0x004B9EC0); this loop pays every
        // player, so a hero's traps are left out rather than drain what DK2 never charges.
        IReadOnlyList<TrapState> traps = world.Traps;
        for (int i = 0; i < traps.Count; i++)
        {
            TrapState trap = traps[i];
            int owner = indexOfOwner[trap.PlayerId];
            if (owner < 0 || !PlayerIds.IsKeeper(trap.PlayerId) || trap.UnderConstruction || !database.Traps.TryGetValue(trap.TrapId, out TrapDef? definition)) continue;
            upkeep[owner] += TrapUpkeepPerSecond(definition);
        }

        int perImp = ImpUpkeepPerSecond(database);
        if (perImp == 0) return upkeep;

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
    /// <para>The net is added, then <c>0x004C10E0</c> (<c>0x004C11C1</c>–<c>0x004C11E7</c>)
    /// clamps it: a net that is not negative cuts mana to the ceiling in one step, however far
    /// above it a lump sum left it; a negative net only floors it at zero, so mana above the
    /// ceiling falls by the upkeep alone (Q545).</para></summary>
    public static int ApplyOneSecond(
        GameDatabase database, ref PlayerState player, int gainPerSecond, int upkeepPerSecond = 0)
    {
        int before = player.Mana;
        long net = (long)gainPerSecond - upkeepPerSecond;
        long mana = before + net;
        if (net >= 0) mana = Math.Min(mana, Threshold(database));

        player.Mana = (int)Math.Max(0, mana);
        return player.Mana - before;
    }
}
