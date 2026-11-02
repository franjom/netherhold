// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>Imp supply and buying costs. Free casts replenish toward MinimumImpThreshold after
/// the Heart opening; they use the native deadline rather than guaranteeing an initial roster.
/// Create Imp's price and upkeep come from its shipped keeper-spell record.
/// See docs/research/imps-and-mana.md.</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/imps-and-mana.md")]
public static class ImpSupplyRules
{
    /// <summary>Native unsigned turn 0xFFFFFFFF: supply is disabled until the Heart opening arms it.</summary>
    public static readonly GameTick DisabledFreeImpDeadline = new(uint.MaxValue);

    /// <summary>The replenishment floor: MinimumImpThreshold, four in the shipped data.
    /// Starting rosters belong to the level's THINGS records.</summary>
    public static int MinimumImps(GameDatabase database) =>
        database[VariableTypeId.MinimumImpThreshold];

    /// <summary>How long between free imps: <c>TimeBeforeFreeImpGeneratedSeconds</c>, 5 — the
    /// interval the owner measured by dropping every imp into a dungeon heart.</summary>
    public static int FreeImpIntervalTicks(GameDatabase database) =>
        SimulationTiming.TicksFromSeconds(
            database[VariableTypeId.TimeBeforeFreeImpGeneratedSeconds]);

    /// <summary>How many imps this player has right now. An imp held in the Hand of Evil still
    /// counts: CONFIRMED (owner, 2026-09-05) — holding one of exactly four produces no fifth, and
    /// the original's tally moves only when a thing gains or loses an owner. DK2 keeps two counts,
    /// this one and the imp portrait's, which visibly excludes held imps; reusing the interface
    /// count would let a player hold one imp and farm a fifth, and nothing here enforces it (Q148).</summary>
    public static int ImpCount(WorldState world, GameDatabase database, PlayerId playerId)
    {
        int count = 0;
        IReadOnlyList<CreatureState> creatures = world.Creatures;
        for (int i = 0; i < creatures.Count; i++)
        {
            CreatureState creature = creatures[i];
            if (creature.OwnerPlayerId != playerId) continue;
            if (database.CreatureById(creature.CreatureTypeId).Flags.HasFlag(CreatureFlags.IsWorker))
                count++;
        }
        return count;
    }

    /// <summary>What the next Create Imp would cost this keeper: <c>base × (imps − minimum + 1)</c>.
    /// CONFIRMED (Q54) in two halves — the base is DK2's own 1500, and the owner's "1500, then 3000,
    /// then 4500" gives the rise. The four free imps do not raise the cost and the price falls back
    /// when imps die, which rules out counting how many a keeper has ever bought: a function of the
    /// current roster needs no counter and cannot drift from one.</summary>
    public static int CreateImpCost(WorldState world, GameDatabase database, PlayerId playerId)
    {
        int baseCost = CreateImpBaseCost(database);
        int over = ImpCount(world, database, playerId) - MinimumImps(database);
        return baseCost * (Math.Max(0, over) + 1);
    }

    /// <summary>The Create Imp spell's own mana cost: 1500 in the shipped data, read out of it
    /// rather than written here. <c>KwdKeeperSpells</c> (DK2.Data, not referenced from here) reads
    /// the cost at offset <c>0x11C</c>, where all thirteen spells hold a price that ranks the way
    /// the game plays, and Create Imp's 1500 is what the owner measured.</summary>
    public static int CreateImpBaseCost(GameDatabase database) =>
        database.RequireKeeperSpell(KeeperSpellName.CreateImp).ManaCost;

    /// <summary>Finds this keeper's first Heart tile in storage order, or none.
    /// Used for a <see cref="CreateImpCommand"/> without a tile and by callers that only need
    /// to know whether a Heart exists; free supply aims from the Heart's position instead
    /// (ADR-0630, docs/research/imps-and-mana.md).</summary>
    public static int HeartTileOf(WorldState world, GameDatabase database, PlayerId playerId)
    {
        byte? heartTerrain = ManaRules.DungeonHeartTerrainId(database);
        if (heartTerrain is null) return -1;

        for (int i = 0; i < world.TileCount; i++)
        {
            ref TileState tile = ref world.TileAtIndex(i);
            if (tile.OwnerPlayerId == playerId && tile.TerrainId == heartTerrain.Value)
                return i;
        }
        return -1;
    }
}
