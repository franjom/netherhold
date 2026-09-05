// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>
/// Where imps come from, and what they cost.
///
/// Two routes, both CONFIRMED by the owner from the original (2026-08-30), and
/// both of which the shipped data names:
///
/// <list type="bullet">
/// <item><b>Free, up to a floor.</b> A keeper always has at least
/// <c>MinimumImpThreshold</c> imps (4). Kill them, or drop them into the dungeon
/// heart, and they come back one every
/// <c>TimeBeforeFreeImpGeneratedSeconds</c> (5). The two variables say exactly
/// that in their own names and carry exactly the observed numbers, which is as
/// close to confirmation as a balance table gets.</item>
/// <item><b>Bought, without limit but not cheaply.</b> The Create Imp keeper
/// spell costs 1500 mana for the first, 3000 for the second, 4500 for the
/// third, and so on — so a keeper cannot spam imps however rich they are. The
/// 1500 is DK2's own: it is the mana cost on the <c>Create Imp</c> record in
/// <c>KeeperSpells.kwd</c>.</item>
/// </list>
///
/// <para><b>What imps cost to keep is NOT here</b>, and it should be. The
/// community figure is 7 mana a second each, and the owner's screenshot agrees —
/// a red drain of about 28 against the four starting imps. But 7 is in neither
/// the balance table nor the CREATURES record (searched: no offset holds the
/// Imp's 7 against the Horned Reaper's reported 2000), so implementing it would
/// mean hardcoding a balance constant, which this project treats as a bug. See
/// Q53 and docs/research/imps-and-mana.md.</para>
/// </summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/imps-and-mana.md")]
public static class ImpSupplyRules
{
    /// <summary>
    /// The number of imps a keeper is guaranteed. <c>MinimumImpThreshold</c> — 4
    /// in the shipped data, and 4 is what a keeper starts a level with.
    /// </summary>
    public static int MinimumImps(GameDatabase database) =>
        database[VariableTypeId.MinimumImpThreshold];

    /// <summary>
    /// How long between free imps. <c>TimeBeforeFreeImpGeneratedSeconds</c> — 5,
    /// which is the interval the owner measured by dropping every imp into a
    /// dungeon heart and watching them come back.
    /// </summary>
    public static int FreeImpIntervalTicks(GameDatabase database) =>
        SimulationTiming.TicksFromSeconds(
            database[VariableTypeId.TimeBeforeFreeImpGeneratedSeconds]);

    /// <summary>
    /// How many imps this player has right now.
    ///
    /// <para><b>A creature held in the Hand of Evil still counts, and this is
    /// the line that will be got wrong.</b> CONFIRMED by the owner
    /// (2026-09-05): holding one of exactly four imps produces <i>no</i> fifth
    /// imp. The original agrees for a readable reason — the tally its free
    /// supply consults is moved in exactly two places, when a thing gains an
    /// owner and when it loses one, and picking a creature up does not change
    /// whose it is.</para>
    ///
    /// <para><b>DK2 keeps two counts of a keeper's imps</b>: this one, and the
    /// imp portrait's figure, which visibly <i>excludes</i> held imps. When the
    /// Hand of Evil is built, the natural thing to do is reuse the count the
    /// interface shows — and that would let a player hold one imp indefinitely
    /// and farm a fifth out of the dungeon heart. Nothing enforces this today
    /// because nothing can be picked up, which is exactly the shape of claim
    /// this project has been bitten by: true because of a fact about the
    /// features that exist, not because anything checks it. <b>Q148.</b></para>
    /// </summary>
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

    /// <summary>
    /// What the next Create Imp would cost this keeper.
    ///
    /// <b>CONFIRMED</b> (Q54), in two halves and from two messages. The base is
    /// DK2's own — 1500, the mana cost on the Create Imp record. The rise is the
    /// owner's: "1500, then 3000, then 4500 … that way the game ensures you
    /// can't spam infinite number of imps".
    ///
    /// <para>The formula is <c>base × (imps − minimum + 1)</c>, and what pins it
    /// down rather than merely fitting it is the owner's second statement:
    /// <i>"four free (starting) imps don't raise mana cost, they are free, base,
    /// so if you drop down to 4 (something killed your imps), cost also drops to
    /// base 1500"</i>. That settles both open ends at once — the free four do not
    /// count towards the price, and <b>the price falls back when imps die</b>,
    /// which rules out the obvious alternative of counting how many a keeper has
    /// ever bought. It is a function of the imps a keeper has right now, so it
    /// needs no counter and cannot drift from the roster.</para>
    ///
    /// <para>A pleasant consequence: losing imps is expensive in work and free in
    /// mana. A keeper whose workforce is wiped out gets four back for nothing and
    /// rebuilds the rest at the cheap end of the curve.</para>
    /// </summary>
    public static int CreateImpCost(WorldState world, GameDatabase database, byte playerId)
    {
        int baseCost = CreateImpBaseCost(database);
        int over = ImpCount(world, database, playerId) - MinimumImps(database);
        return baseCost * (Math.Max(0, over) + 1);
    }

    /// <summary>
    /// The Create Imp spell's own mana cost: <b>1500</b> in the shipped data,
    /// and read out of it rather than written here.
    ///
    /// <para><b>This used to be the project's only hardcoded balance constant,
    /// and it is not one any more.</b> It was a named exception for one session,
    /// on the grounds that <c>KeeperSpells.kwd</c> was unparsed and 1500 could
    /// only be had by reading the file by hand. Chasing the imp mana upkeep
    /// (Q53) into the same record forced a parser to exist, so the exception
    /// paid for itself and then closed: <see cref="KwdKeeperSpells"/> now reads
    /// the cost at offset 0x11C, where all thirteen spells hold a plausible
    /// price that ranks the way the game plays, and Create Imp's 1500 is exactly
    /// what the owner measured in the original.</para>
    /// </summary>
    public static int CreateImpBaseCost(GameDatabase database) =>
        database.RequireKeeperSpell(KeeperSpellName.CreateImp).ManaCost;

    /// <summary>
    /// Where a new imp appears: a walkable dungeon-heart tile belonging to this
    /// keeper, or none.
    ///
    /// STRONGLY INFERRED. In the original imps are created at the dungeon heart
    /// and a keeper without one is a keeper who has lost (Q40), so "no heart, no
    /// imps" needs no extra rule. Which of the heart's tiles is UNKNOWN and is
    /// the same question as Q52, where only 16 of a 5×5 heart's 25 tiles turn
    /// out to be ordinary floor; the first in tile order is ours.
    /// </summary>
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
