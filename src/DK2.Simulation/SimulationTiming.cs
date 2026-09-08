// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation;

/// <summary>The one place where ticks are related to seconds: four ticks a game
/// second, so a tick is 250 ms of wall clock at normal speed. Nothing else in the
/// simulation may write a tick count that means a number of seconds.</summary>
/// <remarks>Source: STRONGLY INFERRED (Q1, ADR-0089,
/// docs/research/balance-variables-in-the-binary.md) from three independent
/// readings that meet: variable 19, the seconds-to-ticks multiplier the level
/// loader copies out of the LEVEL chunk, is 4 in all 66 shipped levels; the
/// owner's portal stopwatch fitted <c>(n² + base) × variable 19</c> ticks over
/// seven consecutive arrivals to a mean absolute error of 1.0 s on a series
/// running to 59 s; and <c>FUN_00488930</c>, the creature's per-tick method, sets
/// two effect timers to <c>variable 19 × 10</c> and decrements each once per
/// call.</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/open-questions.md#Q1")]
public static class SimulationTiming
{
    /// <summary>
    /// Simulation ticks per second of game time: four (Q1); one tick is 250 ms of
    /// wall clock. Equal to <see cref="OriginalTicksPerGameSecond"/> and kept
    /// separate on purpose — this is <i>our</i> step rate, free to refine if a
    /// mechanic is ever found to need sub-tick resolution; that one is a constant
    /// of the original, in the same class as a tabled balance variable.
    /// </summary>
    public const int TicksPerSecond = 4;

    /// <summary>Ticks in <paramref name="seconds"/> seconds of game time, rounded down.</summary>
    public static int TicksFromSeconds(int seconds) => seconds * TicksPerSecond;

    /// <summary>
    /// DK2's own ticks per game second: four (Q1, Q132). Read, not chosen —
    /// variable 19 is the seconds-to-ticks multiplier the whole engine divides and
    /// multiplies by, the level loader copies it out of the LEVEL chunk field
    /// OpenKeeper names <c>ticksPerSec</c>, and all 66 shipped levels carry 4.
    ///
    /// <para>Distinct from <see cref="TicksPerSecond"/> because the shipped data
    /// does not always store a quantity in the unit the mechanic uses it in: the
    /// creature loader divides the four <c>angerNo*</c> fields by this, so a
    /// routine applying one such unit a game second applies a quarter of the
    /// tabled figure. A fact about DK2 must not depend on our step rate.</para>
    /// </summary>
    [Dk2Behavior(Confidence.StronglyInferred, "docs/research/open-questions.md#Q132")]
    public const int OriginalTicksPerGameSecond = 4;

    /// <summary>
    /// How many of our ticks make one of the original's: one, today.
    /// <para>Needed where DK2 acts four times a game second <i>and</i> the action
    /// is not additive, so collapsing it into one application a second changes the
    /// outcome rather than the schedule. Training experience forced it: DK2
    /// promotes a creature and zeroes the remainder
    /// (<see cref="CreatureLevelRules.Grant"/>), so four awards of seven and one
    /// award of twenty-eight leave different amounts behind.</para>
    /// <para>Prefer once a game second wherever the mechanic is additive; reach
    /// for this only when the original's own cadence is load-bearing, and say why
    /// at the call site.</para>
    /// </summary>
    public const int OriginalTickPeriodTicks = TicksPerSecond / OriginalTicksPerGameSecond;

    /// <summary>
    /// Whether this tick is one of the original's ticks. Phased on the <i>last</i>
    /// of each group, like every per-second system here, so the fourth original
    /// tick of a game second and that second's own boundary are the same tick: a
    /// system doing per-tick and per-second work does both in one pass, in the
    /// order the original's dispatcher uses.
    /// </summary>
    public static bool OnOriginalTick(long tick) =>
        (tick + 1) % OriginalTickPeriodTicks == 0;

    /// <summary>
    /// DK2's own load-time conversion of a per-second rate in the shipped data
    /// into the per-tick rate its engine adds: integer divide by
    /// <see cref="OriginalTicksPerGameSecond"/>, floored at one whenever the file
    /// value was non-zero. Ten creature fields go through it; the neighbouring
    /// threshold <c>ExpForNextLevel</c> is stored raw (docs/research/training.md §4).
    /// <para>Both directions are reproduced, not rounded away: an
    /// <c>ExpPerSecondTraining</c> of 30 becomes 7 a tick and so 28 a game second,
    /// while the Skeleton's <c>ResearchPerSecond</c> of 1 and the Monk's and Dark
    /// Angel's 3 all become 4 (<c>ResearchOnRealDataTests</c>). Multiply back up
    /// by that constant before adding once a game second; where the cadence
    /// matters, add on <see cref="OnOriginalTick"/>.</para></summary>
    [Dk2Behavior(Confidence.Confirmed, "docs/research/creature-levels.md")]
    public static int LoaderRatePerTick(int fileValue)
    {
        if (fileValue == 0) return 0;

        int perTick = fileValue / OriginalTicksPerGameSecond;
        return perTick != 0 ? perTick : 1;
    }

    /// <summary>
    /// How many times a game second a worker applies work to a tile: four, once a
    /// tick, which is the creature update's own rate (Q1, Q47, ADR-0089).
    /// <para>The measurement behind the earlier 5 stands but sized an application
    /// rather than its cadence: an imp mining a gold seam frees 273 gold a swing
    /// (owner, 2026-08-30), which is 60 health against <c>MineGoldHealth</c>'s
    /// −300 — exactly <c>delta × efficiency ÷ 4</c> at the default 80% efficiency
    /// (<see cref="BaseApplicationDivisor"/>, <see cref="EfficiencyRules"/>). The
    /// slap corroborates the split and not the rate: 341 gold slapped over 273
    /// unslapped is 1.25, which is 1.0 ÷ 0.8.</para>
    /// </summary>
    [Dk2Behavior(Confidence.StronglyInferred, "docs/research/open-questions.md#Q47")]
    public const int WorkApplicationsPerSecond = OriginalTicksPerGameSecond;

    /// <summary>
    /// What a tabled per-second work delta is divided by to give one application
    /// at the default Efficiency: five, derived as <c>4 ÷ 0.8</c>. DK2's work
    /// handlers read the rate global <i>already divided</i> by
    /// <see cref="OriginalTicksPerGameSecond"/> at load time, then multiply by the
    /// worker's Efficiency in 4096ths (<c>0xccc</c> = 0.8 at spawn), so one
    /// application is <c>tabled ÷ 4 × 0.8</c>.
    /// <para>Kept separate from <see cref="WorkApplicationsPerSecond"/>: this
    /// sizes an application, that one schedules it.</para>
    /// </summary>
    [Dk2Behavior(Confidence.StronglyInferred, "docs/research/tile-work.md")]
    public const int BaseApplicationDivisor =
        OriginalTicksPerGameSecond * 100 / EfficiencyRules.BasePercent;

    /// <summary>
    /// How often a worker applies work to a tile, in ticks: one. Work lands once
    /// per creature update and the creature update is once a tick, so this is 1 by
    /// construction; still derived, so a finer <see cref="TicksPerSecond"/> carries
    /// it.
    /// </summary>
    public const int WorkApplicationPeriodTicks = TicksPerSecond / WorkApplicationsPerSecond;
}
