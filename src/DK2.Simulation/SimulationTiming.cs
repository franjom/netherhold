// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation;

/// <summary>
/// The one place where simulation ticks are related to seconds.
///
/// <b>Q1 is UNKNOWN.</b> DK2 v1.70 runs a fixed integer tick (STRONGLY INFERRED
/// — Flame's <c>limit_tps</c> patch and its tick-stamped struct fields), but the
/// rate has never been measured. Every value here is therefore a placeholder,
/// deliberately gathered into a single type so that measuring Q1 is a one-line
/// change rather than an archaeology exercise.
///
/// Nothing in the simulation may write a tick count inline. A number of ticks
/// that means "one second" must come from here.
///
/// <para>
/// What the placeholder does and does not affect: the <i>order</i> of simulated
/// events, and therefore every determinism test and every state hash, is
/// independent of <see cref="TicksPerSecond"/>, because per-second work is
/// scheduled as "one application every <see cref="TicksPerSecond"/> ticks". What
/// it does affect is the mapping from tick counts to wall-clock seconds, which
/// is exactly the claim we are not yet entitled to make.
/// </para>
/// </summary>
[Dk2Behavior(Confidence.Unknown, "docs/research/open-questions.md#Q1")]
public static class SimulationTiming
{
    /// <summary>
    /// Simulation ticks per second of game time. <b>PLACEHOLDER — UNKNOWN (Q1).</b>
    ///
    /// 20 is not a measurement and not a claim about DK2. It was chosen only
    /// because it divides evenly and keeps per-second work periods small. When
    /// Q1 is answered this constant changes and the pinned determinism hash is
    /// expected to change with it only if some system's schedule depends on it.
    /// </summary>
    public const int TicksPerSecond = 20;

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
    /// turns out to be — using <see cref="TicksPerSecond"/> there would make the
    /// number an artefact of our placeholder instead of a fact about DK2.</para>
    ///
    /// <para>So: <see cref="TicksPerSecond"/> is how often <i>we</i> step;
    /// this is a constant of the original, in the same class as a tabled
    /// balance variable.</para>
    /// </summary>
    [Dk2Behavior(Confidence.StronglyInferred, "docs/research/open-questions.md#Q132")]
    public const int OriginalTicksPerGameSecond = 4;

    /// <summary>
    /// How many of <i>our</i> ticks make one of the original's — five, at the
    /// placeholder rate.
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
    /// <para><b>The floor is not rounding hygiene, it is reachable.</b> The Imp,
    /// the Dwarf and Horny carry an <c>angerNo*</c> of 1 and keep it; without
    /// the floor a creature that can be angered at all would silently stop being
    /// angerable. The truncation on the other side is reachable too and is
    /// <i>not</i> corrected: an <c>ExpPerSecondTraining</c> of 30 becomes 7 a
    /// tick and therefore <b>28 a game second, not 30</b>. Every rate that is
    /// not a multiple of four loses up to three of itself a second, and that is
    /// reproduced rather than rounded away.</para>
    ///
    /// <para>Rates that are added once a game second should be multiplied back
    /// up by <see cref="OriginalTicksPerGameSecond"/> so the loss survives;
    /// rates whose cadence matters should be added on
    /// <see cref="OnOriginalTick"/>.</para>
    ///
    /// <para><b>The floor is DK2's literal <c>1</c>, sign and all</b>, so a
    /// field of −1 to −3 would come back as <c>+1</c> and change direction. That
    /// is what the original's store does and it is left alone because <b>no
    /// shipped field reaches it</b>: every one of the ten is either zero or at
    /// least four, and the two negative anger fields (−5000 slap, −250 in hand)
    /// are stored raw by a different part of the loader. If a field ever does
    /// land there, this is the line to look at first.</para>
    /// </summary>
    [Dk2Behavior(Confidence.Confirmed, "docs/research/creature-levels.md")]
    public static int LoaderRatePerTick(int fileValue)
    {
        if (fileValue == 0) return 0;

        int perTick = fileValue / OriginalTicksPerGameSecond;
        return perTick != 0 ? perTick : 1;
    }

    /// <summary>
    /// How many times a second a worker applies work to a tile. <b>Five —
    /// STRONGLY INFERRED, and measured rather than chosen</b> (Q47).
    ///
    /// <para>The measurement: an imp mining a gold seam frees <b>273 gold per
    /// swing</b> (owner observation of the original, 2026-08-30). A seam is 3000
    /// gold over 660 health, so 273 gold is 273 × 660 / 3000 = <b>60 health</b>,
    /// and a seam therefore takes 660 / 60 = <b>11 swings</b>. Against
    /// <c>MineGoldHealth</c> of −300 that is exactly five applications to the
    /// tabled value.</para>
    ///
    /// <para>Two things corroborate it, neither of which was looked for:</para>
    /// <list type="bullet">
    /// <item><b>Eight of the ten tabled deltas divide by five exactly</b> — 825,
    /// −140, 125, −300, −5000, −130, 5, 50 — where you would expect two by
    /// chance. No other small rate comes close: 825 is odd, which alone rules
    /// out the "twice a second" reading this replaces, and rules out four and
    /// ten as well.</item>
    /// <item>Claiming lands where the owner said it does. Dirt Path is 660
    /// health against <c>ClaimTileHealth</c> 825, so at five a second that is
    /// 825 / 5 = 165 a swing and <b>four swings</b> — the "2-3-4 jumps MAX" of
    /// Q46.</item>
    /// </list>
    ///
    /// <para>What this is <i>not</i> is a measurement of the tick rate. It fixes
    /// the ratio between an application and a tabled per-second delta, which is
    /// what decides how much a swing does and how many swings a tile takes.
    /// How long five applications take in wall-clock terms still rides on Q1.</para>
    ///
    /// <para><b>Amended 2026-09-05: the fifth is Efficiency, and DK2 shows
    /// it.</b> The game prints the stat beside a creature's name —
    /// <i>"Efficiency: 80%"</i> — and 80% is the fixed-point multiplier the work
    /// handlers read. An application is <c>delta x efficiency / 4</c>, the 4
    /// being ticks a game second, so at the default 80% it is a fifth of the
    /// delta applied <b>once a tick</b> rather than five times a second. This
    /// constant is kept because it reproduces the default case exactly; when the
    /// hand and the slap exist it becomes a per-creature Efficiency (a slap or a
    /// drop onto marked work is +10 points each, 100% with both). See
    /// <c>docs/research/tile-work.md</c>, "Efficiency".</para>
    /// </summary>
    [Dk2Behavior(Confidence.StronglyInferred, "docs/research/open-questions.md#Q47")]
    public const int WorkApplicationsPerSecond = 5;

    /// <summary>
    /// How often a worker applies work to a tile, in ticks.
    ///
    /// The per-second rate is <see cref="WorkApplicationsPerSecond"/>; this is
    /// only that rate expressed in the placeholder tick rate. Nothing may depend
    /// on the number of ticks — the *count* of applications a tile takes is the
    /// measured quantity, and it is independent of Q1.
    /// </summary>
    public const int WorkApplicationPeriodTicks = TicksPerSecond / WorkApplicationsPerSecond;
}
