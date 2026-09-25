// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>How fast a worker works, as a whole percent: 80 % base plus two ten-point bonuses.</summary>
/// <remarks>CONFIRMED off DK2's status bar and by mining a 3000-gold/660-health seam — 272-273 gold
/// a swing at 80 %, 309-310 at 90 %, 341 at 100 % (owner, Q47; tile-work.md "Efficiency", "The slap
/// confirms the whole model"). Deviates: DK2 carries fixed-point <c>WorkModifier</c> and
/// <c>WorkEnhancement</c>; whole percents are what the game displays and what every measurement landed on (<see cref="Scale"/>). Trap: it is not the creature's level (same doc, "does NOT change with level").</remarks>
public static class EfficiencyRules
{
    /// <summary>What every creature works at with nothing helping it, as the status bar shows it:
    /// 80 %. The arithmetic uses <see cref="BaseModifier"/>, which is not quite the same number.</summary>
    public const int BasePercent = 80;

    /// <summary>What DK2's constructor writes into <c>WorkModifier</c>: <c>0xccc</c> = 0.7998, not
    /// 0.8. Run 15 read four work rates off the running game at once and only the 4096ths reproduce
    /// all four — <c>× 80 ÷ 100</c> gives −27 for a dig the game does at −26 (ADR-0133).</summary>
    [Dk2Behavior(Confidence.Confirmed, "docs/research/tile-work.md")]
    public const int BaseModifier = 0xCCC;

    /// <summary>What each bonus adds to <c>WorkEnhancement</c>: <c>0x199</c> = 409. Two of them reach
    /// <c>0xffe</c>, three short of 4096, which is why "100 %" is a display value.</summary>
    public const int BonusModifier = 0x199;

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
    /// found, not merely "was dropped" — ADR-0069), or for a non-worker the job of the room it was
    /// dropped on (<see cref="CreatureState.DroppedToDoJob"/>, ADR-0215). Not implemented: a third
    /// term of 0.05 per keeper-held counter unit, most likely the torture tally.</summary>
    public static int Percent(CreatureState creature, CreatureDef definition) =>
        BasePercent + (BonusPercent * Bonuses(creature, definition));

    /// <summary>The same creature's multiplier in 4096ths, which is what work arithmetic uses:
    /// <see cref="BaseModifier"/> plus <see cref="BonusModifier"/> a bonus.</summary>
    public static int Modifier(CreatureState creature, CreatureDef definition) =>
        BaseModifier + (BonusModifier * Bonuses(creature, definition));

    /// <summary>How many of the two bonuses this creature has, so the percent the status bar shows
    /// and the 4096ths the arithmetic uses can never drift apart.</summary>
    private static int Bonuses(CreatureState creature, CreatureDef definition)
    {
        int bonuses = 0;

        if (creature.WorkFasterTicks > 0) bonuses++;

        if (definition.Flags.HasFlag(CreatureFlags.IsWorker)
            ? creature.DroppedTask != ImpTaskKind.None
            : creature.DroppedToDoJob != JobType.None)
            bonuses++;

        return bonuses;
    }

}
