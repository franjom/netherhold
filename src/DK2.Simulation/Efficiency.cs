// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>How fast a worker works, as a whole percent: 80 % base plus two ten-point bonuses.</summary>
/// <remarks>CONFIRMED off DK2's status bar and by mining a 3000-gold/660-health seam — 272-273 gold
/// a swing at 80 %, 309-310 at 90 %, 341 at 100 % (owner, Q47; tile-work.md "Efficiency", "The slap
/// confirms the whole model"). Deviates: DK2 carries fixed-point <c>WorkModifier</c> and
/// <c>WorkEnhancement</c>; whole percents are what the game displays and what every measurement landed on (<see cref="Scale"/>). Trap: it is not the creature's level (same doc, "does NOT change with level").</remarks>
public static class EfficiencyRules
{
    /// <summary>What every creature works at with nothing helping it: 80 %. DK2's constructor sets
    /// <c>WorkModifier</c> to <c>0xccc</c> = 0.7998, not 0.8 — why this is a percent, not 4096ths.</summary>
    public const int BasePercent = 80;

    /// <summary>What each of the two bonuses is worth: ten points, stacking to exactly 100 %. DK2
    /// adds a hardcoded <c>0x199</c> = 409 = 0.0999 to <c>WorkEnhancement</c> per bonus.</summary>
    public const int BonusPercent = 10;

    /// <summary>How long a slap's bonus lasts: 25 game seconds. CONFIRMED by the owner off the
    /// status bar (Q159). DK2 writes a hardcoded <c>100</c> into <c>WorkFasterTimer</c>, decremented
    /// once a tick — 25 s at variable 19's four ticks a game second, matching <c>SlapWorkFasterDurationSeconds</c>.
    /// Trap: not <c>IncreasedWorkRateDurationFromSlappingSeconds</c> (60), for which no reader was found.</summary>
    public const int SlapBonusSeconds = 25;

    /// <summary>The slap bonus in our ticks. See <see cref="SlapBonusSeconds"/>.</summary>
    public const int SlapBonusTicks = SlapBonusSeconds * SimulationTiming.TicksPerSecond;

    /// <summary>This creature's Efficiency as a whole percent: two independent ten-point bonuses,
    /// as DK2 builds <c>WorkEnhancement</c> — a slap that has not run out, and still doing the job
    /// it was dropped onto (<see cref="CreatureState.DroppedTask"/> holds the kind of task the drop
    /// found, not merely "was dropped" — ADR-0069). Not implemented: DK2's non-worker branch
    /// (<c>DroppedToDoJobId</c> without <see cref="CreatureFlags.IsWorker"/>) and a third term of 0.05 per keeper-held counter unit, most likely the torture tally.</summary>
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

    /// <summary>Scales work from <see cref="BasePercent"/> to <paramref name="percent"/>, rounding
    /// halves away from zero; at <see cref="BasePercent"/> it returns its input untouched. The
    /// rounding is the measurement: 67.5 health at 90 % pays 309 gold a swing (68), where
    /// truncating gives 304.5. Percents, not DK2's 4096ths, whose constants are approximations (<c>0xccc + 0x199</c> = 0.8997): reproduce the numbers, not the arithmetic (AGENTS.md, Fidelity).</summary>
    public static int Scale(int baseAmount, int percent)
    {
        if (percent == BasePercent || baseAmount == 0) return baseAmount;

        int numerator = baseAmount * percent;
        int whole = numerator / BasePercent;
        int rest = Math.Abs(numerator % BasePercent);

        // Halves away from zero: 67.5 -> 68, and -67.5 -> -68 for downward deltas.
        if (rest * 2 >= BasePercent) whole += Math.Sign(numerator);

        return whole;
    }
}
