// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>Research: what a creature standing in a Library earns for its keeper.
/// STRONGLY INFERRED from DK2's per-tick hook for state category 4 (<c>sub_0047b000</c>)
/// and the keeper-side adder it calls (docs/research/library-and-research.md). Scope is
/// the <b>accrual, not the tree</b> — the original credits points whether or not a target
/// is selected, so the accrual is a complete mechanism on its own.
/// <para>The rate is per <b>tick</b>: <c>ResearchPerSecond</c> is one of the ten record
/// fields the loader divides by variable 19 (<see cref="SimulationTiming.LoaderRatePerTick"/>)
/// and the hook credits the divided figure every tick, so a rate that is not a multiple of four loses up to three of itself a second — the Wizard's 10 gives <b>8, not 10</b> — while a rate below four <i>grows</i>.</para></summary>
public static class ResearchRules
{
    /// <summary>What one tick of research is worth, before any bonus: the record's
    /// <c>ResearchPerSecond</c> made per-tick by the loader, scaled by the level table's
    /// <see cref="CreatureStatId.ResearchPerSecond"/> row — 125% at level 2 to <b>700%</b>
    /// at level 10, the steepest curve outside the experience threshold, indexed by the
    /// hook at <c>+0x201</c>. <b>Level 1 bypasses the table</b> rather than multiplying by
    /// 100%, as every one of these hooks does; the floor at zero and the ceiling at <see cref="MaxCredit"/> are the original's, neither reachable on shipped data.</summary>
    [Dk2Behavior(Confidence.StronglyInferred, "docs/research/library-and-research.md")]
    public static int PerOriginalTick(
        GameDatabase database, CreatureDef creature, CreatureState state)
    {
        int perTick = SimulationTiming.LoaderRatePerTick(creature.ResearchPerSecond);
        if (perTick == 0) return 0;

        int level = CreatureLevelRules.Clamp(state.Level);
        if (level == CreatureLevelRules.FirstLevel) return perTick;

        int scaled = CreatureLevelRules.Scale(
            perTick, CreatureLevelRules.Percent(database, CreatureStatId.ResearchPerSecond, level));

        if (scaled < 0) return 0;
        return scaled > MaxCredit ? MaxCredit : scaled;
    }

    /// <summary>The original's ceiling on one credit, <c>0xFFFE</c>: its own constant,
    /// compared with <c>JGE</c> in the hook, unreachable on shipped data.</summary>
    public const int MaxCredit = 0xFFFE;

    /// <summary>How long a slap makes a creature research faster: <b>60 game seconds</b>,
    /// from <c>IncreasedWorkRateDurationFromSlappingSeconds</c>. A slap does <b>two</b>
    /// unrelated things and only this one is the research bonus, gated on
    /// <see cref="CreatureState.LastSlapTick"/>; the other is Efficiency +10 for 25 game
    /// seconds from a hardcoded 100 ticks (<see cref="EfficiencyRules.SlapBonusSeconds"/>, Q159). The owner's "after a minute" was this effect, not a mis-timing of that one.</summary>
    [Dk2Behavior(Confidence.StronglyInferred, "docs/research/library-and-research.md")]
    public static int SlapBonusSeconds(GameDatabase database) =>
        database[VariableTypeId.IncreasedWorkRateDurationFromSlappingSeconds];

    /// <summary>The bonus a recently slapped researcher earns, added <b>once a game second</b>
    /// on top of the per-tick base, or zero. The arithmetic is the original's and is not what the
    /// variable says: it multiplies by variable 19 <i>twice</i>, so <c>4 × 50 × 4 / 100</c> is
    /// <b>8 times the per-tick amount</b> — an effective <b>+200%</b> where
    /// <c>IncreasedWorkRatePecentageFromSlapping</c> is named 50%. Reproduced, not corrected
    /// (ADR-0002): the extra factor is in the shipped v1.70 instructions, and its two unbuilt
    /// siblings are wrong in two further ways (library-and-research.md §3). UNKNOWN whether a
    /// player notices — a Warlock goes from 4 a second to 12 (Q216).</summary>
    [Dk2Behavior(Confidence.StronglyInferred, "docs/research/library-and-research.md")]
    public static int SlapBonus(
        GameDatabase database, CreatureState state, int perTickAmount, long now)
    {
        if (perTickAmount <= 0 || state.LastSlapTick == 0) return 0;

        long window = (long)SimulationTiming.OriginalTicksPerGameSecond * SlapBonusSeconds(database);
        if (window <= 0 || now - state.LastSlapTick >= window) return 0;

        return SimulationTiming.OriginalTicksPerGameSecond
             * database[VariableTypeId.IncreasedWorkRatePecentageFromSlapping]
             * SimulationTiming.OriginalTicksPerGameSecond
             * perTickAmount / 100;
    }

}
