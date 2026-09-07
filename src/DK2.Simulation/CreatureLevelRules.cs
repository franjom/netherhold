// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>
/// What a level does to a creature: it multiplies its stats, by percentages the
/// shipped data states outright.
///
/// <para><b>CONFIRMED by the owner</b> (2026-09-04): <i>"imps gain speed as they
/// gain more levels"</i> — said in passing while trying to time one through
/// water, and it sent this project looking for something it had been carrying
/// unread the whole time. <c>GlobalVariables.kwd</c> holds a <b>32-stat by
/// 9-level table of percentages</b> against the level-1 value, 320 rows, parsed
/// into <c>GlobalVariables.CreatureStats</c> since variables were first read and
/// looked at by nothing until now (Q119).</para>
///
/// <para>The curve, for the stats that matter today:</para>
///
/// <list type="bullet">
/// <item><b>Health</b> 125% at level 2, then 150 / 175 / 200 / 225 / 250 / 300 /
/// 400 / <b>600%</b> at level 10. So a level-10 Goblin has 9000 health against
/// its record's 1500.</item>
/// <item><b>Speed</b> and <b>run speed</b> 105% per level to level 5, then 125 /
/// 150 / 175 / 200 / <b>250%</b>. An imp at level 2 walks at 1.93 tiles a second
/// against 1.84 at level 1 — visible, which is what the owner saw.</item>
/// <item><b>Experience for the next level</b> 130% rising to <b>1000%</b>, so
/// the last level costs ten times what the first one did.</item>
/// <item>And <b>ten of the 32 stats never scale at all</b>, own-land
/// regeneration among them — an Imp heals at 67 a second for ever.</item>
/// </list>
///
/// <para><b>Gaining a level is implemented for workers only</b>, and the owner
/// settled the rule (Q124): <i>"imps get experience by working, while they do
/// their usual things around the dungeon, digging, reinforcing walls, mining
/// gold, etc. they get exp and passively their level raises. creatures also gain
/// levels through experience, mostly training and fighting. creatures can get up
/// to level 4 using the training room, up to level 8 using the combat pit, and
/// to get to level 10 they need to fight actual fights."</i></para>
///
/// <para><b>Two of those caps are shipped variables to the number</b> —
/// <c>TrainingRoomMaxExperienceLevel</c> 4 and
/// <c>CombatPitMaxExperienceLevel</c> 8 — which is the third time an owner
/// statement has landed on a variable name outright. So an imp works and levels
/// here; nothing else earns anything, because training and fighting do not
/// exist. See <see cref="Systems.CreatureExperienceSystem"/>.</para>
///
/// <para><b>And the owner's answer resolved a unit that looked unresolvable.</b>
/// The record's field this project calls <c>ExpPerSecond</c> reads <b>1200</b>
/// for an Imp, which is absurd per second — level 2 in two and a half seconds.
/// But <b>1200 / 60 = 20</b>, exactly the global
/// <c>ImpExperienceGainPerSecond</c>, so the record's field is <b>per
/// minute</b>: two independent fields agreeing on one creature's rate. That
/// makes a Goblin's 200 into 3.33 a second, or fifteen minutes a level —
/// plausible, and unused here because nothing makes a Goblin fight.</para>
///
/// <para>Integer arithmetic throughout, on the same 100-based percentages the
/// file uses. A percentage is applied to the <i>record's</i> value rather than
/// compounded per level, so there is no rounding drift up the curve and no
/// dependence on the order levels were gained in.</para>
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/creature-levels.md")]
public static class CreatureLevelRules
{
    /// <summary>The level a creature arrives at, and the only one it can be today.</summary>
    public const int FirstLevel = 1;

    /// <summary>Ten, because the shipped table's rows stop there.</summary>
    public const int MaxLevel = GameDatabase.MaxCreatureLevel;

    /// <summary>
    /// Clamps a level into the range the data can describe. A creature restored
    /// from a snapshot written by a different build, or placed by a scenario
    /// that asked for level 12, gets something the table can answer for.
    /// </summary>
    public static int Clamp(int level) => Math.Clamp(level, FirstLevel, MaxLevel);

    /// <summary>
    /// Applies one of the table's percentages to a value from the creature
    /// record.
    ///
    /// <para>64-bit intermediate because a stat at 1000% of a five-figure value
    /// overflows an <c>int</c> in the multiply while the result still fits, and
    /// the one field that reaches 1000% is the experience threshold.</para>
    /// </summary>
    public static int Scale(int recordValue, int percent) =>
        (int)Math.Clamp((long)recordValue * percent / 100, int.MinValue, int.MaxValue);

    /// <summary>The percentage this stat carries at this level. 100 at level 1.</summary>
    public static int Percent(GameDatabase database, CreatureStatId stat, int level) =>
        database.StatPercent(stat, Clamp(level));

    /// <summary>
    /// A creature's maximum health at its level: the record's <c>Hp</c> scaled
    /// by the Health row.
    ///
    /// <para>This is the method that used to be <c>CreatureHealthRules.MaxHealth</c>
    /// and returned <c>creature.Hp</c> flat — which was right for the world as
    /// it was, since every creature was level 1, and would have quietly stayed
    /// wrong the moment anything set a level.</para>
    /// </summary>
    public static int MaxHealth(GameDatabase database, CreatureDef creature, int level) =>
        Scale(creature.Hp, Percent(database, CreatureStatId.Health, level));

    /// <summary>
    /// Walking speed in fixed-point raw units at this level — the record's
    /// <c>Speed</c> scaled by the Speed row.
    ///
    /// <para>Scaled in raw units on purpose: <see cref="Fp32"/>'s denominator is
    /// 4096 and the percentage is integer, so scaling the raw value keeps the
    /// arithmetic exact where converting to and from a decimal would not.</para>
    /// </summary>
    public static int SpeedRaw(GameDatabase database, CreatureDef creature, int level) =>
        Scale(creature.Speed.Raw, Percent(database, CreatureStatId.Speed, level));

    /// <summary>Running speed, which scales by its own row — identical to walking's in the shipped table.</summary>
    public static int RunSpeedRaw(GameDatabase database, CreatureDef creature, int level) =>
        Scale(creature.RunSpeed.Raw, Percent(database, CreatureStatId.RunSpeed, level));

    /// <summary>
    /// What a worker earns for one of the original's ticks of work — the raw
    /// <c>ImpExperienceGainPerSecond</c> of <b>20</b>, undivided.
    ///
    /// <para><b>CONFIRMED by the owner</b> (Q124): <i>"imps get experience by
    /// working, while they do their usual things around the dungeon, digging,
    /// reinforcing walls, mining gold, etc."</i> — and the binary says which
    /// activities and how often (Q79, 2026-09-07).</para>
    ///
    /// <para><b>Per tick of work, not per job, and not per second.</b> DK2 reads
    /// this variable at <b>nine</b> sites, every one of them in the imp's task
    /// unit, and every one has the identical shape: a predicate on the target
    /// tile, then this award, then one tick's worth of work applied to the tile.
    /// The three distinct predicates were read and they are all tile
    /// <i>properties</i> — "is this claimable", "is this a diggable wall", "is
    /// this reinforceable" — so the guard means <b>"is this still a valid
    /// target"</b> and not "the job just finished". The award sits on the same
    /// cadence as the work accrual beside it, which
    /// <c>docs/research/tile-work.md</c> established is once an original tick.
    /// The owner's <i>"per job done"</i> named exactly the right activities and
    /// the granularity turned out finer.</para>
    ///
    /// <para><b>The one rate in the family that is NOT divided by four.</b> Its
    /// four siblings on the creature record go through
    /// <see cref="SimulationTiming.LoaderRatePerTick"/> as the loader stores
    /// them; this one is a global, is read raw, and is added raw. So its name is
    /// wrong by a factor of four in the opposite direction to everything around
    /// it: <b>20 a tick is 80 a game second</b>, and an imp reaches level 2 in
    /// about <b>37 seconds</b> of continuous work rather than the 150 this
    /// project believed for three weeks.</para>
    ///
    /// <para><b>Working, not merely alive.</b> An idle imp earns nothing, which
    /// is what the owner's <i>"while they do their usual things"</i> says and
    /// what makes experience a consequence of the player keeping their imps
    /// busy.</para>
    /// </summary>
    [Dk2Behavior(Confidence.StronglyInferred, "docs/research/creature-levels.md")]
    public static int WorkExperiencePerOriginalTick(
        GameDatabase database, CreatureDef creature, CreatureState state)
    {
        if (state.Level >= MaxLevel) return 0;
        if (!creature.Flags.HasFlag(CreatureFlags.IsWorker)) return 0;
        if (state.Activity != CreatureActivity.Working) return 0;

        return database[VariableTypeId.ImpExperienceGainPerSecond];
    }

    /// <summary>
    /// Adds experience and promotes at most one level, reproducing DK2's adder
    /// (<c>FUN_00457880</c>) clause by clause.
    ///
    /// <para><b>This replaces a reading that was wrong in both halves</b> (Q127).
    /// The old code spent the threshold and <i>carried</i> the remainder, and
    /// looped so that a large award could buy two levels. DK2 does neither.</para>
    ///
    /// <list type="number">
    /// <item><b>A threshold of zero refuses everything.</b> The original
    /// computes the level's threshold first and returns without touching the
    /// balance if it is zero — so a creature whose <c>ExpForNextLevel</c> is
    /// zero can never gain <i>or lose</i> experience, whatever is handed to
    /// it.</item>
    /// <item><b>A loss bigger than the balance is a no-op, not a clamp.</b> The
    /// original subtracts only when the magnitude fits, so experience floors at
    /// zero by refusing the subtraction outright. A creature on 10 points handed
    /// −50 keeps its 10.</item>
    /// <item><b>Reaching the threshold zeroes the balance.</b> Not
    /// <c>-= needed</c>. The overshoot is <b>discarded</b>, so a single enormous
    /// award buys exactly one level and throws the rest away.</item>
    /// <item><b>One level per call.</b> No loop. Two levels in one award is not
    /// possible in the original, and making it possible here would let any
    /// future lump payout outrun DK2.</item>
    /// </list>
    ///
    /// <para><b>Why the discard is load-bearing rather than a curiosity:</b> it
    /// makes the <i>cadence</i> of an award observable. Four awards of seven and
    /// one award of twenty-eight leave different balances behind the moment one
    /// of them crosses a threshold, which is why training pays on
    /// <see cref="SimulationTiming.OnOriginalTick"/> instead of once a game
    /// second.</para>
    ///
    /// <para><b>What is not reproduced:</b> the original also refuses the whole
    /// call when a creature flag (<c>0x8000</c> on the runtime creature) is set
    /// and the level has reached <c>CombatPitMaxExperienceLevel</c>. That flag
    /// is unmapped and nothing here sets it, so the clause would be dead code
    /// carrying a guess.</para>
    ///
    /// <para>Returns true if a level was gained.</para>
    /// </summary>
    [Dk2Behavior(Confidence.Confirmed, "docs/research/creature-levels.md")]
    public static bool Grant(
        GameDatabase database, CreatureDef creature, CreatureState state, int amount)
    {
        int needed = ExperienceForNextLevel(database, creature, state.Level);
        if (needed <= 0) return false;

        if (amount < 0)
        {
            // Refused, not clamped: the original only subtracts when it fits.
            if (-amount <= state.ExperiencePoints) state.ExperiencePoints += amount;
            return false;
        }

        if (amount == 0) return false;

        state.ExperiencePoints += amount;
        if (state.ExperiencePoints < needed) return false;

        // The remainder is thrown away, and only one level is bought.
        state.ExperiencePoints = 0;
        if (state.Level >= MaxLevel) return false;

        state.Level++;
        return true;
    }

    /// <summary>
    /// The experience needed to reach the next level: the record's
    /// <c>ExpForNextLevel</c> scaled by the table.
    ///
    /// <para><b>Indexed by the creature's CURRENT level, and the binary
    /// CONFIRMS the reading this project chose</b> (Q124, settled 2026-09-07).
    /// DK2's adder indexes the <c>ExperienceForNextLevel</c> column with the
    /// creature's <i>current</i> level byte and uses the record's raw value
    /// when that level is 1 — so level 1 does get the implicit 100% and the
    /// first level costs exactly the record's own 3000. The alternative reading,
    /// indexing by the target level, would have made the first level cost 3900
    /// and the record's number appear nowhere.</para>
    ///
    /// <para>The original also clamps the scaled threshold to <c>0xFFFE</c>.
    /// Not reproduced, because it cannot bite: the largest shipped product is
    /// 3000 at 1000% = 30000, less than half the clamp.</para>
    /// </summary>
    public static int ExperienceForNextLevel(
        GameDatabase database, CreatureDef creature, int level) =>
        Scale(creature.ExpForNextLevel,
              Percent(database, CreatureStatId.ExperienceForNextLevel, level));
}
