// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>Where imps come from, and what they cost: two routes, both CONFIRMED by the
/// owner (2026-08-30) and both named by the shipped data (docs/research/imps-and-mana.md).
/// <b>Free, up to a floor</b> — a keeper always has at least <c>MinimumImpThreshold</c>
/// imps (4) and a missing one comes back every
/// <c>TimeBeforeFreeImpGeneratedSeconds</c> (5). <b>Bought, without limit but not
/// cheaply</b> — Create Imp costs 1500 mana for the first, 3000 for the second, 4500 for
/// the third, so a keeper cannot spam imps however rich they are. Upkeep lives in
/// <see cref="ManaRules.ImpUpkeepPerSecond"/>, off the same spell record (Q53).</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/imps-and-mana.md")]
public static class ImpSupplyRules
{
    /// <summary>The number of imps a keeper is guaranteed:
    /// <c>MinimumImpThreshold</c>, <b>4</b> in the shipped data, which is also what a
    /// keeper starts a level with.</summary>
    public static int MinimumImps(GameDatabase database) =>
        database[VariableTypeId.MinimumImpThreshold];

    /// <summary>How long between free imps: <c>TimeBeforeFreeImpGeneratedSeconds</c>,
    /// <b>5</b>, the interval the owner measured by dropping every imp into a dungeon
    /// heart and watching them come back.</summary>
    public static int FreeImpIntervalTicks(GameDatabase database) =>
        SimulationTiming.TicksFromSeconds(
            database[VariableTypeId.TimeBeforeFreeImpGeneratedSeconds]);

    /// <summary>How many imps this player has right now. <b>An imp held in the Hand of
    /// Evil still counts</b>: CONFIRMED (owner, 2026-09-05), holding one of exactly four
    /// produces no fifth, and the original's tally moves only when a thing gains or
    /// loses an owner, which picking it up does not.
    /// <para>DK2 keeps <i>two</i> counts — this one and the imp portrait's, which
    /// visibly excludes held imps. Reusing the interface count when the Hand of Evil is
    /// built would let a player hold one imp and farm a fifth out of the dungeon heart,
    /// and nothing here enforces the difference (Q148).</para></summary>
    public static int ImpCount(WorldState world, GameDatabase database, byte playerId)
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

    /// <summary>What the next Create Imp would cost this keeper:
    /// <c>base × (imps − minimum + 1)</c>. CONFIRMED (Q54) in two halves — the base is
    /// DK2's own 1500, the rise is the owner's "1500, then 3000, then 4500 … that way
    /// the game ensures you can't spam infinite number of imps".
    /// <para>What pins the formula rather than merely fitting it is the owner's second
    /// statement, that the four free imps do not raise the cost and that <b>the price
    /// falls back when imps die</b> — which rules out counting how many a keeper has
    /// ever bought. Being a function of the current roster, it needs no counter and
    /// cannot drift from it, and losing imps is therefore expensive in work and free in
    /// mana.</para></summary>
    public static int CreateImpCost(WorldState world, GameDatabase database, byte playerId)
    {
        int baseCost = CreateImpBaseCost(database);
        int over = ImpCount(world, database, playerId) - MinimumImps(database);
        return baseCost * (Math.Max(0, over) + 1);
    }

    /// <summary>The Create Imp spell's own mana cost: <b>1500</b> in the shipped data,
    /// read out of it rather than written here. <see cref="KwdKeeperSpells"/> reads the
    /// cost at offset <c>0x11C</c>, where all thirteen spells hold a plausible price
    /// that ranks the way the game plays, and Create Imp's 1500 is what the owner
    /// measured in the original.</summary>
    public static int CreateImpBaseCost(GameDatabase database) =>
        database.RequireKeeperSpell(KeeperSpellName.CreateImp).ManaCost;

    /// <summary>Where a new imp appears: a dungeon-heart tile belonging to this keeper,
    /// or none. STRONGLY INFERRED — imps are created at the heart in the original, and a
    /// keeper without one has lost (Q40), so "no heart, no imps" needs no extra rule.
    /// <b>Which</b> heart tile is UNKNOWN (Q52, where only 16 of a 5×5 heart's 25 tiles
    /// are ordinary floor); the first in tile order is ours.</summary>
    public static int HeartTileOf(WorldState world, GameDatabase database, byte playerId)
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
