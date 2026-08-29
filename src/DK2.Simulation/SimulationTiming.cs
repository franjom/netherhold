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
    /// How often a per-second rate from the global variables table is applied.
    ///
    /// The VARIABLES health deltas (<c>DigRockHealth</c>, <c>ClaimTileHealth</c>,
    /// …) carry no unit in the data — unlike their neighbours, which spell out
    /// <c>…PerSecond</c> and <c>…Seconds</c> in their names. HYPOTHESIS: they
    /// are per second, applied once per second of work. See Q19.
    /// </summary>
    public const int WorkApplicationPeriodTicks = TicksPerSecond;
}
