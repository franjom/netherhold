// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>
/// Research: what a creature standing in a Library earns for its keeper.
///
/// <para><b>Read from DK2's own per-tick hook for state category 4</b>
/// (<c>sub_0047b000</c>, installed at <c>+0x14</c> of the category record at
/// <c>0x00699788</c>), and from the keeper-side adder it calls
/// (<c>FUN_004c2dc0</c>). The hook is eleven lines long and does exactly one
/// thing: take the researcher's <c>ResearchPerSecond</c>, scale it by the
/// creature's level, and hand it to the keeper.</para>
///
/// <para><b>Scope: the accrual, not the tree.</b> DK2 keeps two fields on the
/// keeper — the accumulated points and the <i>current target</i> — and this
/// class reproduces only the first. Nothing here decides what is being
/// researched, what it costs or what happens when it completes. That matters
/// less than it sounds: the original credits the points **whether or not there
/// is a target**, so a keeper with nothing selected still banks them, and the
/// accrual is a complete mechanism on its own.</para>
///
/// <para><b>The rate is per tick, not per second, and it is the data that says
/// so.</b> <c>ResearchPerSecond</c> is one of the ten record fields DK2's
/// creature loader divides by variable 19 as it loads them
/// (<see cref="SimulationTiming.LoaderRatePerTick"/>), and the hook credits the
/// divided figure **every tick**. So the tabled figure is delivered once a game
/// second in four instalments, and every rate that is not a multiple of four
/// loses up to three of itself a second to the loader's truncation. The
/// <b>Wizard</b> ships at 10, which becomes 2 a tick and therefore <b>8 a
/// second, not 10</b>; the Warlock's 5 becomes 1 a tick and delivers <b>4</b>.
/// And a rate below four does not shrink but <i>grows</i> — see
/// <see cref="SimulationTiming.LoaderRatePerTick"/>, where the Skeleton's
/// tabled 1 becomes 4 a second.</para>
/// </summary>
public static class ResearchRules
{
    /// <summary>
    /// What one tick of research is worth, before any bonus: the record's
    /// <c>ResearchPerSecond</c> divided by the original's ticks per game
    /// second, then scaled by the level table's Research row.
    ///
    /// <para><b>The level curve is the steepest in the game outside the
    /// experience threshold</b> — 125% at level 2 rising to <b>700%</b> at
    /// level 10 — so a Warlock's level is the single biggest lever a keeper has
    /// over how fast they research. <see cref="CreatureStatId.ResearchPerSecond"/>
    /// records how that column was pinned; today's reading of the hook
    /// **confirms it from the other side**, because the hook indexes the row at
    /// <c>+0x201</c>, which is exactly the column that argument predicted.</para>
    ///
    /// <para><b>Level 1 bypasses the table rather than multiplying by 100%</b>,
    /// which is the shape every one of these hooks uses and is reproduced
    /// literally: the two differ if the shipped row is ever not 100 at level 1.
    /// The floor at zero and the ceiling at <c>0xFFFE</c> are the original's
    /// too. Neither is reachable with shipped data — the largest product is a
    /// Warlock's 2 a tick at 700%, which is 14 — and they are here because they
    /// are what the instructions do, not because anything needs them.</para>
    /// </summary>
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

    /// <summary>
    /// The original's ceiling on one credit, <c>0xFFFE</c>. Its own constant,
    /// compared with <c>JGE</c> in the hook, and unreachable with shipped data.
    /// </summary>
    public const int MaxCredit = 0xFFFE;

    /// <summary>
    /// How long a slap makes a creature research faster: <b>60 game
    /// seconds</b>, from <c>IncreasedWorkRateDurationFromSlappingSeconds</c>.
    ///
    /// <para><b>This variable was recorded across this project's research as
    /// one that nothing reads</b> — see the correction in
    /// <c>docs/research/tile-work.md</c>. It is read, here, and the reason it
    /// looked dead is that it was being looked for in the wrong mechanism. A
    /// slap does <i>two</i> unrelated things, and only one of them is the work
    /// rate:</para>
    ///
    /// <list type="bullet">
    /// <item><b>Efficiency +10 points for 25 game seconds</b>, from a
    /// <b>hardcoded</b> 100 ticks in the slap handler
    /// (<see cref="EfficiencyRules.SlapBonusSeconds"/>, Q159, CONFIRMED by the
    /// owner off the status bar). The <c>...FromSlappingSeconds</c> variable of
    /// 60 has nothing to do with it, which is why Q159 closed in the binary's
    /// favour against a hand-timed "after a minute".</item>
    /// <item><b>A research bonus for 60 game seconds</b>, this one, measured
    /// from <see cref="CreatureState.LastSlapTick"/> and gated in the keeper's
    /// research adder.</item>
    /// </list>
    ///
    /// <para>So the owner's original <i>"after a minute"</i> was not a
    /// mis-timing of the efficiency bonus — <b>there really is a minute-long
    /// slap effect</b>, and it is this one. Two mechanisms wearing one
    /// variable's name.</para>
    /// </summary>
    [Dk2Behavior(Confidence.StronglyInferred, "docs/research/library-and-research.md")]
    public static int SlapBonusSeconds(GameDatabase database) =>
        database[VariableTypeId.IncreasedWorkRateDurationFromSlappingSeconds];

    /// <summary>
    /// The bonus a recently slapped researcher earns, added <b>once a game
    /// second</b> on top of the per-tick base, or zero.
    ///
    /// <para><b>The arithmetic is the original's and it is not what the
    /// variable says.</b> DK2 computes</para>
    ///
    /// <code>
    /// bonus = variable19 * IncreasedWorkRatePercentageFromSlapping
    ///                    * variable19 * perTickAmount / 100
    /// </code>
    ///
    /// <para>— read off <c>IMUL ECX,EAX ; IMUL ECX,EBX ; IMUL ECX,EDI</c>, with
    /// the divide by 100 as the compiler's <c>0x51EB851F</c> reciprocal. That is
    /// <c>4 x 50 x 4 / 100</c> = <b>8 times the per-tick amount</b>, added once
    /// a second against a base of four per-tick amounts a second. So the
    /// effective bonus is <b>+200%</b> where the variable is named 50%: a
    /// slapped researcher works at <b>three times</b> the rate, not one and a
    /// half.</para>
    ///
    /// <para><b>Why this is reproduced rather than corrected.</b> The extra
    /// <c>variable19</c> is almost certainly a slip — the bonus reads as
    /// "a percentage of the per-second rate", which would need one factor and
    /// not two — but ADR-0002 is explicit that a quirk is reproduced, and this
    /// one is in the shipped v1.70 instructions rather than in a shipped
    /// number. Its two sibling bonuses in the same function are wrong in
    /// <b>two different ways</b> — the casino line repeats the same doubled
    /// <c>variable19</c>, while the tortured-type line <i>adds</i> where these
    /// multiply, turning a tabled 25 into an effective 29 — which is what a
    /// hand-edited run of three lines looks like, and is the strongest argument
    /// that none of it is intentional. Neither sibling is built (no Casino, no
    /// Torture Chamber); both formulas are in
    /// <c>docs/research/library-and-research.md</c> section 3 rather than in
    /// unused constants here.</para>
    ///
    /// <para><b>UNKNOWN: whether a player would notice.</b> A Warlock earns 2 a
    /// tick, so slapping takes it from 8 a second to 24 — a threefold change in
    /// research speed, which is very visible if research completion is ever
    /// built. Worth watching in the original once the tree exists (Q216).</para>
    /// </summary>
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
