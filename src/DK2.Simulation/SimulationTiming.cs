// Copyright (c) 2026 Franjo Misetic
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

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
