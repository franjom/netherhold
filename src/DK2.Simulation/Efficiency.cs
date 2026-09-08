// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>How fast a worker works, as a percentage.</summary>
/// <remarks>
/// <para>CONFIRMED off DK2's own status bar, which prints the number, and
/// measured on a gold seam of 3000 gold over 660 health: 80% gives 272-273 gold
/// a swing, 90% 309-310, 100% 341 (owner, Q47, docs/research/tile-work.md).</para>
/// <para>Deviates: DK2 keeps two fixed-point fields its dump names
/// <c>WorkModifier</c> (base <c>0xccc</c>, set by the creature constructor and
/// never touched) and <c>WorkEnhancement</c> (the bonus, recomputed each update
/// from flags); this keeps whole percents, which the game displays and every
/// measurement landed on exactly (<see cref="Scale"/>). Trap: it is not the
/// creature's level — a level-1 imp reads what a level-3 one does.</para>
/// </remarks>
public static class EfficiencyRules
{
    /// <summary>
    /// What every creature works at with nothing helping it: <b>80%</b>.
    ///
    /// <para>DK2's <c>WorkModifier</c>, which its constructor sets to the
    /// fixed-point <c>0xccc</c> — 0.7998 rather than 0.8, and the difference is
    /// why this is a percent and not a 4096ths value. See <see cref="Scale"/>.</para>
    /// </summary>
    public const int BasePercent = 80;

    /// <summary>
    /// What each of the two bonuses is worth: <b>ten points</b>, and they stack
    /// to exactly 100%.
    ///
    /// <para>DK2 adds <c>0x199</c> = 409 = 0.0999 per bonus to
    /// <c>WorkEnhancement</c>, twice over from two independent flags.</para>
    /// </summary>
    public const int BonusPercent = 10;

    /// <summary>
    /// How long a slap's bonus lasts: <b>25 game seconds</b>. CONFIRMED by the
    /// owner off the status bar (Q159, 2026-09-06).
    /// <para>DK2's slap handler writes a hardcoded <c>100</c> into
    /// <c>WorkFasterTimer</c>, decremented once a tick; at variable 19's four
    /// ticks a game second that is 25 seconds, and the table's
    /// <c>SlapWorkFasterDurationSeconds</c> is exactly 25 — a second independent
    /// confirmation that variable 19 is ticks per game second. Trap: it is not
    /// <c>IncreasedWorkRateDurationFromSlappingSeconds</c> (60), which nothing
    /// reads.</para>
    /// </summary>
    public const int SlapBonusSeconds = 25;

    /// <summary>The slap bonus in our ticks. See <see cref="SlapBonusSeconds"/>.</summary>
    public const int SlapBonusTicks = SlapBonusSeconds * SimulationTiming.TicksPerSecond;

    /// <summary>
    /// This creature's Efficiency, as a whole percent: two independent bonuses of
    /// ten points each, as DK2 builds <c>WorkEnhancement</c> — a slap that has
    /// not run out, and <b>still doing the job it was dropped onto</b>.
    /// <para>Trap: the second is not "was dropped". The field DK2 reads is
    /// <c>ImpJobData.DroppedTask</c>, the <i>kind</i> of task the drop found
    /// (ADR-0069, <see cref="CreatureState.DroppedTask"/>).</para>
    /// <para>Not implemented, both identified: DK2's non-worker branch, which
    /// reads <c>DroppedToDoJobId</c> for a creature without
    /// <see cref="CreatureFlags.IsWorker"/>; and a third term of 0.05 per unit of
    /// a keeper-held counter, most likely the torture tally.</para>
    /// </summary>
    public static int Percent(CreatureState creature, CreatureDef definition)
    {
        int percent = BasePercent;

        if (creature.WorkFasterTicks > 0)
            percent += BonusPercent;

        if (definition.Flags.HasFlag(CreatureFlags.IsWorker)
            && creature.DroppedTask != ImpTaskKind.None)
            percent += BonusPercent;

        return percent;
    }

    /// <summary>
    /// Scales an amount of work from <see cref="BasePercent"/> to
    /// <paramref name="percent"/>, rounding halves away from zero. At
    /// <see cref="BasePercent"/> it returns its input untouched.
    /// <para>The rounding is the measurement: an imp at 90% takes 67.5 health
    /// off a gold seam and the game gives 309 gold every swing, which is 68
    /// health — truncating gives 304.5 and alternating gives 304/309. Which is
    /// also why this works in percents and not DK2's 4096ths, whose constants
    /// are approximations (<c>0xccc + 0x199</c> is 0.8997, and 300 x 0.8997 / 4
    /// is 67.47, rounding to 68 by no rule). The displayed percentages are
    /// exact, so this reproduces the numbers, not the arithmetic (ADR-0002).</para>
    /// </summary>
    public static int Scale(int baseAmount, int percent)
    {
        if (percent == BasePercent || baseAmount == 0) return baseAmount;

        int numerator = baseAmount * percent;
        int whole = numerator / BasePercent;
        int rest = Math.Abs(numerator % BasePercent);

        // Halves away from zero: 67.5 -> 68, and -67.5 -> -68 for the deltas
        // that run downward.
        if (rest * 2 >= BasePercent) whole += Math.Sign(numerator);

        return whole;
    }
}
