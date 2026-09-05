// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>
/// Efficiency: how fast a worker works, as a percentage, and the two things a
/// keeper can do to raise it.
///
/// <para><b>CONFIRMED by the owner (2026-09-05), off DK2's own status bar</b> —
/// <i>"Imp. Name: Snipe. Job: idle. Efficiency: 80%. Mood: Happy."</i> The game
/// prints the number, which is what turned a constant this project had fitted
/// to one gold measurement into a creature stat with a name.</para>
///
/// <para>DK2 keeps it as two fixed-point fields, and its own debug dump names
/// them: <c>WorkModifier</c> (the base, set to <c>0xccc</c> = 0.8 by the
/// creature constructor and never touched again) and <c>WorkEnhancement</c>
/// (the bonus, recomputed from scratch every update out of a handful of flags).
/// This project keeps whole percents instead, because whole percents are what
/// the game displays and what every measurement landed on exactly — see
/// <see cref="Scale"/> for why that distinction has teeth.</para>
///
/// <para>Measured, all three, on a gold seam (3000 gold over 660 health, so
/// 4.545 gold a health point):</para>
///
/// <list type="table">
///   <item><description><b>80%</b> — 300 x 0.80 / 4 = 60 health, 272.7 gold; owner read <b>272-273</b></description></item>
///   <item><description><b>90%</b> — 67.5 -&gt; 68 health, 309.1 gold; owner read <b>309-310</b></description></item>
///   <item><description><b>100%</b> — 75 health, 340.9 gold; owner read <b>341</b></description></item>
/// </list>
///
/// <para><b>Not the creature's level.</b> A freshly created level-1 imp reads
/// the same number as a level-3 one, and the day's apparent level effects were
/// all this bonus coming and going (Q47, docs/research/tile-work.md).</para>
/// </summary>
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
    /// How long a slap's bonus lasts: <b>25 game seconds</b>.
    ///
    /// <para>DK2's slap handler writes a hardcoded <c>100</c> into
    /// <c>WorkFasterTimer</c>, which the creature update decrements by one every
    /// tick. At variable 19's four ticks a game second that is 25 seconds, and
    /// the balance table has a <c>SlapWorkFasterDurationSeconds</c> of exactly
    /// <b>25</b> — so the hardcoded 100 and the named variable agree, and the
    /// pair is a second independent confirmation that variable 19 is ticks per
    /// game second.</para>
    ///
    /// <para><b>This contradicts what the owner measured</b>, who watched a
    /// slapped imp revert <i>"after a minute"</i> — and the table also carries
    /// an <c>IncreasedWorkRateDurationFromSlappingSeconds</c> of 60, which is
    /// where that minute would come from. The binary is unambiguous and the
    /// stopwatch was loose, so 25 is what is built here; Q159 records the
    /// conflict and names the test that settles it, which is now a cheap one
    /// because the status bar shows the percentage directly.</para>
    /// </summary>
    public const int SlapBonusSeconds = 25;

    /// <summary>The slap bonus in our ticks. See <see cref="SlapBonusSeconds"/>.</summary>
    public const int SlapBonusTicks = SlapBonusSeconds * SimulationTiming.TicksPerSecond;

    /// <summary>
    /// This creature's Efficiency, as a whole percent.
    ///
    /// <para>Two independent bonuses, ten points each, exactly as DK2 builds
    /// <c>WorkEnhancement</c>: a slap that has not yet run out, and having been
    /// dropped onto work by the keeper.</para>
    ///
    /// <para><b>Two of DK2's contributions are deliberately absent</b> and both
    /// are named in docs/research/hand-of-evil.md: the bonus a <i>non-worker</i>
    /// gets (DK2 reads a different field for creatures without
    /// <see cref="CreatureFlags.IsWorker"/>, and nothing in this project makes a
    /// non-worker do tile work yet), and a third term of 0.05 per unit of a
    /// counter held on the keeper — the torture tally, most likely, which needs
    /// a Torture Chamber to be non-zero.</para>
    /// </summary>
    public static int Percent(CreatureState creature, CreatureDef definition)
    {
        int percent = BasePercent;

        if (creature.WorkFasterTicks > 0)
            percent += BonusPercent;

        // DK2 branches here on IsWorker and reads a different field for
        // non-workers. Only workers do tile work in this project, so only the
        // worker's field exists; the branch is recorded rather than guessed.
        if (definition.Flags.HasFlag(CreatureFlags.IsWorker) && creature.JustDropped)
            percent += BonusPercent;

        return percent;
    }

    /// <summary>
    /// Scales an amount of work from <see cref="BasePercent"/> to
    /// <paramref name="percent"/>, rounding halves away from zero.
    ///
    /// <para><b>Rounding is not a detail here, it is the measurement.</b> An
    /// imp at 90% takes 67.5 health off a gold seam, and the owner read
    /// <b>309-310</b> gold a swing — which is 68 health, not 67. Truncating
    /// would give 304.5 and alternating would give 304/309; the game gives 309
    /// every swing, so the half rounds up.</para>
    ///
    /// <para>That is also why this works in percents rather than in DK2's
    /// 4096ths. Its own constants are approximations — <c>0xccc</c> is 0.7998
    /// and <c>0xccc + 0x199</c> is 0.8997 — and 300 x 0.8997 / 4 is 67.47,
    /// which does not round to 68 by any rule. The displayed percentages are
    /// exact and the fixed-point constants are the game's approximation of
    /// them, so this reproduces the numbers rather than the arithmetic
    /// (ADR-0002: behaviour, not implementation).</para>
    ///
    /// <para>At <see cref="BasePercent"/> it returns its input untouched, which
    /// is what keeps every existing measurement, test and hash exactly as it
    /// was: the 80% case is the one this project already had right.</para>
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
