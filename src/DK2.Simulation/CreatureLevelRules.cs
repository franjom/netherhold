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
/// <para><b>What is deliberately NOT implemented: gaining a level.</b> Every
/// creature here is level 1 and stays there, because the unit of the experience
/// rate is ambiguous and guessing it would put an invented number inside a
/// determinism hash. The creature record's field this project calls
/// <c>ExpPerSecond</c> reads <b>1200 for an Imp</b> against a global
/// <c>ImpExperienceGainPerSecond</c> of <b>20</b> — and 20 x 60 = 1200, so one
/// of the two is per <i>minute</i> and the names cannot both be right. At 1200 a
/// second an imp would reach level 2 in two and a half seconds; at 20 a second,
/// in two and a half minutes. Q124, and the owner can settle it with a
/// stopwatch on a fresh imp.</para>
///
/// <para><b>Why implement the scaling before the levelling, then?</b> Because
/// the two are independent and the scaling is the half that is certain: the
/// percentages are in the file, unambiguous, and every stat read in this
/// project has silently been the level-1 value while pretending to be the
/// creature's. Making <see cref="CreatureState.Level"/> real means a level-2 imp
/// dropped into a scenario is <i>actually</i> faster, which is testable today
/// and is what the owner will be looking at tomorrow.</para>
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
