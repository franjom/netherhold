// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation;

/// <summary>
/// The one place where simulation ticks are related to seconds.
///
/// <b>Q1 is ANSWERED: a tick is 250 ms, and there are four of them in a
/// second.</b> Settled 2026-09-07 from the binary, after a year of placeholder.
/// Three independent readings meet:
///
/// <list type="number">
/// <item><b>Four ticks a game second.</b> Variable 19 is the engine's
/// seconds-to-ticks multiplier and the level loader copies it out of the LEVEL
/// chunk; <b>all 66 shipped levels carry 4</b>.</item>
/// <item><b>A game second is a real second.</b> CONFIRMED by the owner's portal
/// stopwatch — seven consecutive arrival gaps against
/// <c>(n² + base) × variable 19</c> ticks, mean absolute error 1.0 s over a
/// series running to 59 s. A game second that were 0.8 or 1.25 real seconds
/// would have shown as a growing error and did not.</item>
/// <item><b>The creature update runs exactly variable-19 times a game
/// second.</b> Read out of <c>FUN_00488930</c>, which is the creature's own
/// per-tick method: it initialises two effect timers to
/// <c>variable 19 × 10</c> and decrements each <b>once per call</b> while the
/// effect plays. A counter set to <c>4 × 10</c> and decremented once per call
/// is ten game seconds if and only if the call happens four times a game
/// second. Independently, the same function gates work on
/// <c>tick % variable 19 == 0</c> and on <c>(tick + id × 2) &amp; 7</c>, both of
/// which are only correct at one call per tick value.</item>
/// </list>
///
/// Nothing in the simulation may write a tick count inline. A number of ticks
/// that means "one second" must come from here.
///
/// <para><b>What this cost and what it bought.</b> The placeholder was 20, and
/// 20 was not chosen to be wrong — it divides by both 4 and 5, and Q47 had read
/// work as landing five times a second. That reading is now corrected (see
/// <see cref="WorkApplicationsPerSecond"/>): the fifth was the <i>size</i> of
/// an application and never its cadence. With the cadence read rather than
/// inferred, nothing needs a period finer than a tick and the rate is DK2's
/// own.</para>
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/open-questions.md#Q1")]
public static class SimulationTiming
{
    /// <summary>
    /// Simulation ticks per second of game time: <b>four</b>, which is DK2's
    /// own rate and no longer a placeholder (Q1 — see the type's remarks).
    ///
    /// <para>One tick is <b>250 ms</b> of wall clock at normal speed, because a
    /// game second is a real second. That makes this the same number as
    /// <see cref="OriginalTicksPerGameSecond"/>, and the two are kept separate
    /// on purpose: this one is <i>our</i> step rate, which the simulation is
    /// free to refine if some mechanic is ever found to need sub-tick
    /// resolution, and that one is a constant of the original in the same class
    /// as a tabled balance variable. They are equal today because nothing
    /// needs a finer step, not because they are the same idea.</para>
    /// </summary>
    public const int TicksPerSecond = 4;

    /// <summary>Ticks in <paramref name="seconds"/> seconds of game time, rounded down.</summary>
    public static int TicksFromSeconds(int seconds) => seconds * TicksPerSecond;

    /// <summary>
    /// <b>DK2's own</b> ticks per game second: <b>four</b>. Not ours, and not a
    /// placeholder.
    ///
    /// <para><b>Read, not chosen</b> (Q1/Q132). The original's variable 19 is
    /// the seconds-to-ticks multiplier its whole engine divides and multiplies
    /// by, the level loader copies it out of the LEVEL chunk field OpenKeeper
    /// names <c>ticksPerSec</c>, and <b>all 66 shipped level files carry
    /// 4</b>.</para>
    ///
    /// <para><b>What it is for, and it is not <see cref="TicksPerSecond"/>.</b>
    /// Some quantities are not stored in the shipped data in the unit the
    /// mechanic uses them in: DK2's creature loader divides the four
    /// <c>angerNo*</c> fields by this number as it loads them, so what the
    /// engine actually adds is <i>anger per DK2 tick</i>, and a routine that
    /// applies one such unit per game second applies a <b>quarter</b> of the
    /// tabled figure. Reproducing that needs the original's rate, whatever ours
    /// is — the two are equal today, and writing those sites against
    /// <see cref="TicksPerSecond"/> would make a fact about DK2 depend on a
    /// choice of ours that is free to change.</para>
    ///
    /// <para>So: <see cref="TicksPerSecond"/> is how often <i>we</i> step;
    /// this is a constant of the original, in the same class as a tabled
    /// balance variable.</para>
    /// </summary>
    [Dk2Behavior(Confidence.StronglyInferred, "docs/research/open-questions.md#Q132")]
    public const int OriginalTicksPerGameSecond = 4;

    /// <summary>
    /// How many of <i>our</i> ticks make one of the original's — <b>one</b>, now
    /// that the rate is read rather than guessed.
    ///
    /// <para><b>Why anything needs this.</b> Most per-second work can be done
    /// once a game second and nothing can tell the difference. Some cannot:
    /// where DK2 acts four times a second <i>and</i> the action is not additive,
    /// collapsing it into one application a second changes the outcome rather
    /// than the schedule. Training experience is the case that forced this
    /// constant — DK2 promotes a creature and <b>zeroes</b> the remainder
    /// (<see cref="CreatureLevelRules.Grant"/>), so four awards of seven and one
    /// award of twenty-eight leave a different number behind.</para>
    ///
    /// <para><b>It is 1 today and it still earns its keep.</b> Every call site
    /// that reaches for it is saying "this must happen on the original's
    /// cadence, not on a second boundary", and that statement stays true and
    /// stays checkable if <see cref="TicksPerSecond"/> is ever refined. Writing
    /// those sites against a bare tick would lose the reason.</para>
    ///
    /// <para>Prefer once a game second wherever the mechanic is additive. Reach
    /// for this only when the original's own cadence is load-bearing, and say
    /// why at the call site.</para>
    /// </summary>
    public const int OriginalTickPeriodTicks = TicksPerSecond / OriginalTicksPerGameSecond;

    /// <summary>
    /// Whether this tick is one of the original's ticks.
    ///
    /// <para>Phased on the <i>last</i> of each group, like every per-second
    /// system here, so that the fourth original tick of a game second and that
    /// second's own boundary are the same tick. A system that does per-tick and
    /// per-second work therefore does both in one pass at the end of a second,
    /// in the order the original's dispatcher uses.</para>
    /// </summary>
    public static bool OnOriginalTick(long tick) =>
        (tick + 1) % OriginalTickPeriodTicks == 0;

    /// <summary>
    /// The original's own load-time conversion of a per-second rate in the
    /// shipped data into the per-tick rate its engine actually adds:
    /// <b>integer divide by <see cref="OriginalTicksPerGameSecond"/>, floored at
    /// one whenever the file value was non-zero</b>.
    ///
    /// <para><b>This is a fact about DK2's creature loader, not a convenience.</b>
    /// Ten record fields are known to go through it — the four <c>angerNo*</c>
    /// fields, <c>TimeAwake</c> and <c>TimeSleep</c> (multiplied by the same
    /// register), and the four rates <c>ExpPerSecond</c>,
    /// <c>ExpPerSecondTraining</c>, <c>ResearchPerSecond</c> and
    /// <c>ManufacturePerSecond</c>, whose stores sit in one run in
    /// <c>FUN_00492320</c> reading five consecutive frame slots. Its immediate
    /// neighbour <c>ExpForNextLevel</c> is stored <b>raw</b>, and that is the
    /// tell that the divide means something: a threshold is not a rate.</para>
    ///
    /// <para><b>The floor is not rounding hygiene, it is reachable, and it runs
    /// in BOTH directions.</b> The Imp, the Dwarf and Horny carry an
    /// <c>angerNo*</c> of 1 and keep it; without the floor a creature that can
    /// be angered at all would silently stop being angerable. The truncation on
    /// the other side is reachable too and is <i>not</i> corrected: an
    /// <c>ExpPerSecondTraining</c> of 30 becomes 7 a tick and therefore
    /// <b>28 a game second, not 30</b>. Every rate that is not a multiple of
    /// four loses up to three of itself a second, and that is reproduced rather
    /// than rounded away.</para>
    ///
    /// <para><b>And a rate below four does not shrink — it GROWS.</b> Found
    /// 2026-09-07 while building the Library (<see cref="ResearchRules"/>).
    /// The <b>Skeleton's</b> <c>ResearchPerSecond</c> is <b>1</b>, which the
    /// floor turns into 1 a tick and therefore <b>4 a game second — four times
    /// what its record says</b>; the Monk's and the Dark Angel's 3 become 4 the
    /// same way. So this line inflates three creatures as surely as it
    /// truncates twenty-six others, and it is load-bearing in a direction
    /// nobody had looked.</para>
    ///
    /// <para>Rates that are added once a game second should be multiplied back
    /// up by <see cref="OriginalTicksPerGameSecond"/> so the loss survives;
    /// rates whose cadence matters should be added on
    /// <see cref="OnOriginalTick"/>.</para>
    ///
    /// <para><b>The floor is DK2's literal <c>1</c>, sign and all</b>, so a
    /// field of −1 to −3 would come back as <c>+1</c> and change direction. No
    /// shipped field is negative in this range — the two negative anger fields
    /// (−5000 slap, −250 in hand) are stored raw by a different part of the
    /// loader — so the sign flip cannot occur, and the line is left alone.</para>
    ///
    /// <para><b>The reason first given for that was false and is worth keeping
    /// visible.</b> It read: <i>"no shipped field reaches it: every one of the
    /// ten is either zero or at least four"</i>. Three creatures carry 1 or 3
    /// in <c>ResearchPerSecond</c>, so fields <i>do</i> reach the floor — they
    /// simply reach it from the positive side, where the consequence is
    /// inflation rather than a sign flip. The conclusion survives; the evidence
    /// offered for it did not, and nothing checked it for a month
    /// (<c>ResearchOnRealDataTests</c> checks it now).</para>
    /// </summary>
    [Dk2Behavior(Confidence.Confirmed, "docs/research/creature-levels.md")]
    public static int LoaderRatePerTick(int fileValue)
    {
        if (fileValue == 0) return 0;

        int perTick = fileValue / OriginalTicksPerGameSecond;
        return perTick != 0 ? perTick : 1;
    }

    /// <summary>
    /// How many times a second a worker applies work to a tile: <b>four</b>,
    /// once a tick, which is the creature update's own rate (Q47, Q1).
    ///
    /// <para><b>This was 5, and 5 was a cadence inferred from an amount.</b>
    /// The measurement behind it stands and is the sharpest number in the
    /// project: an imp mining a gold seam frees <b>273 gold a swing</b> (owner,
    /// 2026-08-30), which is 60 health against <c>MineGoldHealth</c>'s −300 —
    /// exactly a fifth. Q47 read the fifth as "work lands five times a second".
    /// It is not. The fifth is the <i>size</i> of one application:
    /// <c>delta × efficiency ÷ 4</c>, at the default 80% efficiency, and the
    /// binary computes it that way — see <see cref="BaseApplicationDivisor"/>
    /// and <see cref="EfficiencyRules"/>. The slap corroborates the split
    /// rather than the rate: 341 gold slapped over 273 unslapped is 1.25
    /// exactly, which is 1.0 ÷ 0.8.</para>
    ///
    /// <para><b>What made the wrong cadence look right for a week.</b> Under
    /// ADR-0024's rule that an upward job costs the terrain's whole
    /// <c>MaxHealth</c>, reinforcing Rock was 66 applications, and 66 in the
    /// owner's stopwatched 13.3–13.4 s is 4.9 a second. Both halves were wrong
    /// and they cancelled: DK2 charges the <i>deficit</i> (660 − 165 = 495, so
    /// 50 applications), and 50 applications at four a second is 12.5 s against
    /// a hand-timed 13.35 — an over-read of 0.85 s, which is what a hand timer
    /// does. The slapped run agrees: 45 applications, 11.25 s predicted against
    /// 11.5–11.9 observed. <b>Two compensating errors are why no test caught
    /// either.</b></para>
    /// </summary>
    [Dk2Behavior(Confidence.StronglyInferred, "docs/research/open-questions.md#Q47")]
    public const int WorkApplicationsPerSecond = OriginalTicksPerGameSecond;

    /// <summary>
    /// What a tabled per-second work delta is divided by to give one
    /// application at the default Efficiency: <b>five</b>.
    ///
    /// <para>Derived, not chosen — <c>4 ÷ 0.8</c>. DK2's work handlers read the
    /// rate global <i>already divided</i> by <see cref="OriginalTicksPerGameSecond"/>
    /// at load time, then multiply by the worker's Efficiency in 4096ths
    /// (<c>0xccc</c> = 0.8 at spawn). So one application is
    /// <c>tabled ÷ 4 × 0.8</c> = <c>tabled ÷ 5</c>, and the 273 gold a swing
    /// that Q47 measured is the arithmetic, not a fit.</para>
    ///
    /// <para>Kept separate from <see cref="WorkApplicationsPerSecond"/> because
    /// they are different quantities that happened to be conflated: this one
    /// sizes an application, that one schedules it. Confusing the two is the
    /// mistake this constant exists to prevent from recurring.</para>
    /// </summary>
    [Dk2Behavior(Confidence.StronglyInferred, "docs/research/tile-work.md")]
    public const int BaseApplicationDivisor =
        OriginalTicksPerGameSecond * 100 / EfficiencyRules.BasePercent;

    /// <summary>
    /// How often a worker applies work to a tile, in ticks: <b>one</b>.
    ///
    /// Work lands once per creature update and the creature update is once a
    /// tick, so this is 1 by construction rather than by choice. It is still
    /// derived, so that a finer <see cref="TicksPerSecond"/> would carry it.
    /// </summary>
    public const int WorkApplicationPeriodTicks = TicksPerSecond / WorkApplicationsPerSecond;
}
