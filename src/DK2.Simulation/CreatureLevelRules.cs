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
    /// Experience earned this second.
    ///
    /// <para><b>CONFIRMED by the owner</b> (Q124): <i>"imps get experience by
    /// working, while they do their usual things around the dungeon, digging,
    /// reinforcing walls, mining gold, etc."</i> and <i>"creatures also gain
    /// levels through experience, mostly training and fighting."</i></para>
    ///
    /// <para>So the rule is <b>a worker earns while it works</b>, at DK2's
    /// <c>ImpExperienceGainPerSecond</c> of 20 — 150 seconds to level 2 on the
    /// record's 3000 — and <b>everything else earns nothing here</b>, because
    /// training and fighting do not exist. That zero is the honest answer rather
    /// than a placeholder: a Goblin in this simulation has no way to earn
    /// experience, so it stays level 1, and pretending otherwise would invent a
    /// progression DK2 does not grant for standing around.</para>
    ///
    /// <para><b>Working, not merely alive.</b> An idle imp earns nothing, which
    /// is what the owner's <i>"while they do their usual things"</i> says and
    /// what makes experience a consequence of the player keeping their imps
    /// busy.</para>
    /// </summary>
    public static int ExperiencePerSecond(
        GameDatabase database, CreatureDef creature, CreatureState state)
    {
        if (state.Level >= MaxLevel) return 0;
        if (!creature.Flags.HasFlag(CreatureFlags.IsWorker)) return 0;
        if (state.Activity != CreatureActivity.Working) return 0;

        return database[VariableTypeId.ImpExperienceGainPerSecond];
    }

    /// <summary>
    /// Spends banked experience on as many levels as it buys, and reports
    /// whether any were gained.
    ///
    /// <para><b>The threshold is spent rather than accumulated</b>, so
    /// <see cref="CreatureState.ExperiencePoints"/> holds progress towards the
    /// <i>next</i> level and not a lifetime total. DK2 keeps a field its debug
    /// dump calls <c>ExperiencePoints</c> and nothing says which of the two it
    /// is (Q127). Spending is chosen because it makes the curve additive and
    /// keeps the number small; the alternative is one comparison away if the
    /// binary says otherwise.</para>
    ///
    /// <para>A loop rather than a single step, because a rate high enough to
    /// cross two thresholds in one second would otherwise silently discard the
    /// second one — and the loop is bounded by <see cref="MaxLevel"/>.</para>
    /// </summary>
    public static bool TryLevelUp(
        GameDatabase database, CreatureDef creature, CreatureState state)
    {
        bool gained = false;
        while (state.Level < MaxLevel)
        {
            int needed = ExperienceForNextLevel(database, creature, state.Level);
            if (needed <= 0 || state.ExperiencePoints < needed) break;

            state.ExperiencePoints -= needed;
            state.Level++;
            gained = true;
        }
        return gained;
    }

    /// <summary>
    /// The experience needed to reach the next level: the record's
    /// <c>ExpForNextLevel</c> scaled by the table.
    ///
    /// <para><b>Indexed by the creature's CURRENT level, and that is our reading
    /// of an ambiguity</b> (Q124). The table's rows run from level 2, so under
    /// this reading level 1 gets the implicit 100% and the cost of the first
    /// level is exactly the record's own 3000 — which is the natural meaning of
    /// a field called "experience for next level". Indexed by the <i>target</i>
    /// level instead, the first level would cost 3900 and the record's number
    /// would appear nowhere. Nothing reads this yet, so the choice costs nothing
    /// today; it is written down so that whoever implements levelling knows a
    /// choice was made.</para>
    /// </summary>
    public static int ExperienceForNextLevel(
        GameDatabase database, CreatureDef creature, int level) =>
        Scale(creature.ExpForNextLevel,
              Percent(database, CreatureStatId.ExperienceForNextLevel, level));
}
