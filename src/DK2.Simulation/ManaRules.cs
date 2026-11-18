// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>Mana income uses per-player running terrain gain and cap words (mana.md, Q521).
/// Terrain and owner writes preserve room and bridge residuals; current tiles alone
/// cannot reconstruct the words. A Heart adds 30 before the income cap; upkeep is
/// deducted afterwards. The stored-mana ceiling is separate (Q37).</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/mana.md")]
public static class ManaRules
{
    // 0x455C2A tests these exact terrain ids, not room-placement flags. Mods with
    // bridge-like flags on other ids do not earn this bonus (00455B90.md).
    private static bool HasBridgeBonus(byte terrainId) => terrainId is 0x11 or 0x1A;

    /// <summary>Terrain-write half of player +0x10E2; must run before any owner change.</summary>
    public static void SetTerrain(WorldState world, GameDatabase database, ref TileState tile, byte terrainId)
    {
        RecordTerrainChange(world, database, tile, terrainId);
        tile.TerrainId = terrainId;
    }

    internal static void RecordTerrainChange(WorldState world, GameDatabase database, in TileState tile, byte terrainId)
    {
        if (tile.TerrainId == terrainId || !world.HasPlayer(tile.OwnerPlayerId)) return;
        TerrainDef old = database.TerrainById(tile.TerrainId);
        TerrainDef next = database.TerrainById(terrainId);
        ref PlayerState player = ref world.Player(tile.OwnerPlayerId);
        if (!old.IsRoom && !next.IsRoom)
        {
            if (tile.OwnerPlayerId != PlayerIds.Unowned)
                player.TerrainManaGainPerSecond += (short)next.ManaGain
                    - (tile.TerrainId == 0 ? 0 : (short)old.ManaGain);
        }
        else if (HasBridgeBonus(terrainId)) player.TerrainManaGainPerSecond++;
        else if (HasBridgeBonus(tile.TerrainId)) player.TerrainManaGainPerSecond--;
    }

    /// <summary>Owner-write half: transfers only the current definition's words, leaving room and bridge leftovers.</summary>
    public static void SetOwner(WorldState world, GameDatabase database, ref TileState tile, PlayerId owner)
    {
        if (tile.OwnerPlayerId == owner) return;
        TerrainDef terrain = database.TerrainById(tile.TerrainId);
        if (tile.OwnerPlayerId != PlayerIds.Unowned && world.HasPlayer(tile.OwnerPlayerId))
        {
            ref PlayerState old = ref world.Player(tile.OwnerPlayerId);
            old.TerrainManaGainPerSecond -= (short)terrain.ManaGain;
            old.TerrainManaGainCapBonus -= (short)terrain.MaxManaGain;
        }
        if (world.HasPlayer(owner))
        {
            ref PlayerState next = ref world.Player(owner);
            next.TerrainManaGainPerSecond += (short)terrain.ManaGain;
            next.TerrainManaGainCapBonus += (short)terrain.MaxManaGain;
        }
        tile.OwnerPlayerId = owner.Value;
    }

    internal static void RecordTileChange(WorldState world, GameDatabase database, in TileState before, in TileState after)
    {
        TileState accounting = before;
        SetTerrain(world, database, ref accounting, after.TerrainId);
        SetOwner(world, database, ref accounting, after.OwnerPlayerId);
    }

    internal static void InitializeTerrainRates(WorldState world, GameDatabase database)
    {
        for (int i = 0; i < world.TileCount; i++)
        {
            ref TileState tile = ref world.TileAtIndex(i);
            TerrainDef terrain = database.TerrainById(tile.TerrainId);
            if (world.HasPlayer(tile.OwnerPlayerId))
            {
                ref PlayerState owner = ref world.Player(tile.OwnerPlayerId);
                if (tile.OwnerPlayerId != PlayerIds.Unowned)
                {
                    owner.TerrainManaGainPerSecond += (short)terrain.ManaGain;
                    owner.TerrainManaGainCapBonus += (short)terrain.MaxManaGain;
                }
                // Map_LoadTileRecord lays the hazard, then builds the room for its owner;
                // Map_SetRoomTile therefore credits map bridges just like newly built ones.
                if (terrain.IsRoom && HasBridgeBonus(tile.TerrainId)) owner.TerrainManaGainPerSecond++;
            }
        }
    }
    /// <summary>Standalone tile estimate retained for tile-only callers. World income cannot
    /// be reconstructed from current tiles: it uses the per-player running rate (ADR-0334).
    /// This estimate excludes bridge bonuses and income left behind by ownership changes.</summary>
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

    /// <summary>Running terrain rate plus the heart, capped per keeper; indexed like <see cref="WorldState.PlayerAtIndex"/>.</summary>
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
            indexOfOwner[world.PlayerAtIndex(i).PlayerId.Value] = i;

        for (int i = 0; i < world.TileCount; i++)
        {
            ref TileState tile = ref world.TileAtIndex(i);
            int player = indexOfOwner[tile.OwnerPlayerId];
            if (player < 0) continue;

            // A flag rather than a count: however many tiles a heart covers it is one
            // heart, and a keeper never has two.
            if (tile.TerrainId == heartTerrain) hasHeart[player] = true;
        }

        int fromHeart = hasHeart.Contains(true) ? HeartGainPerSecond(database) : 0;
        int cap = MaxGainPerSecond(database);
        for (int i = 0; i < gain.Length; i++)
        {
            ref PlayerState player = ref world.PlayerAtIndex(i);
            gain[i] = Math.Min(player.TerrainManaGainPerSecond + (hasHeart[i] ? fromHeart : 0),
                cap + player.TerrainManaGainCapBonus);
        }

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

        EntityList<CreatureState> creatures = world.Creatures;
        for (int i = 0; i < creatures.Count; i++)
        {
            CreatureState creature = creatures[i];
            int player = indexOfOwner[creature.OwnerPlayerId];
            if (player < 0) continue;
            if (((database.CreatureById(creature.CreatureTypeId).Flags & CreatureFlags.IsWorker) == CreatureFlags.IsWorker))
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
