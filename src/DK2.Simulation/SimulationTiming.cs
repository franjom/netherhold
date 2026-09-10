// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation;

/// <summary>The one place where ticks are related to seconds: four ticks a game second, a nominal
/// 250 ms a tick (measured 254-263, docs/EXPERIMENTS.md). Nothing else may write a tick count that
/// means a number of seconds. STRONGLY INFERRED from three independent readings that meet — variable
/// 19, the portal stopwatch fit and <c>FUN_00488930</c>'s effect timers (Q1, ADR-0089,
/// docs/research/balance-variables-in-the-binary.md).</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/open-questions.md#Q1")]
public static class SimulationTiming
{
    /// <summary>Our step rate: four ticks a second of game time (Q1); one tick is 250 ms of wall
    /// clock. Equal to <see cref="OriginalTicksPerGameSecond"/> and kept separate — ours is free to
    /// refine if a mechanic ever needs sub-tick resolution, that one is a constant of the original.</summary>
    public const int TicksPerSecond = 4;

    /// <summary>Ticks in <paramref name="seconds"/> seconds of game time, rounded down.</summary>
    public static int TicksFromSeconds(int seconds) => seconds * TicksPerSecond;

    /// <summary>DK2's own ticks per game second: four (Q1, Q132). Read, not chosen — variable 19,
    /// the seconds-to-ticks multiplier the engine divides and multiplies by, is copied out of the
    /// LEVEL chunk and reads 4 in all 66 shipped levels. Distinct from <see cref="TicksPerSecond"/>
    /// because the data does not always store a quantity in the unit the mechanic uses it in (the
    /// creature loader divides the four <c>angerNo*</c> fields by this): a DK2 fact must not depend on our step rate.</summary>
    [Dk2Behavior(Confidence.StronglyInferred, "docs/research/open-questions.md#Q132")]
    public const int OriginalTicksPerGameSecond = 4;

    /// <summary>How many of our ticks make one of the original's: one, today. Needed only where DK2
    /// acts four times a game second and the action is not additive, so collapsing it changes the
    /// outcome rather than the schedule — training experience forced it: DK2 promotes and zeroes the
    /// remainder (<see cref="CreatureLevelRules.Grant"/>), so four awards of seven and one of
    /// twenty-eight differ. Otherwise prefer once a game second, and say why at the call site.</summary>
    public const int OriginalTickPeriodTicks = TicksPerSecond / OriginalTicksPerGameSecond;

    /// <summary>Whether this tick is one of the original's. Phased on the last of each group, like
    /// every per-second system here, so an original tick and a game-second boundary coincide and one
    /// pass does both kinds of work, in the order the original's dispatcher uses.</summary>
    public static bool OnOriginalTick(long tick) =>
        (tick + 1) % OriginalTickPeriodTicks == 0;

    /// <summary>DK2's load-time conversion of a tabled per-second rate into the per-tick rate its
    /// engine adds: integer divide by <see cref="OriginalTicksPerGameSecond"/>, floored at one for a
    /// non-zero file value. Ten creature fields go through it; neighbouring <c>ExpForNextLevel</c> is
    /// stored raw (training.md §4). Both directions are reproduced: <c>ExpPerSecondTraining</c> 30
    /// becomes 7 a tick and so 28 a game second, while <c>ResearchPerSecond</c> 1 and 3 both become 4
    /// (<c>ResearchOnRealDataTests</c>). Multiply back up before adding once a game second; where the cadence matters, add on <see cref="OnOriginalTick"/>.</summary>
    [Dk2Behavior(Confidence.Confirmed, "docs/research/creature-levels.md")]
    public static int LoaderRatePerTick(int fileValue)
    {
        if (fileValue == 0) return 0;

        int perTick = fileValue / OriginalTicksPerGameSecond;
        return perTick != 0 ? perTick : 1;
    }

    /// <summary>How many times a game second a worker applies work to a tile: four, once a tick,
    /// which is the creature update's own rate (Q1, Q47, ADR-0089). An imp mining a gold seam frees
    /// 273 gold a swing (owner), 60 health against <c>MineGoldHealth</c>'s −300 — exactly
    /// <c>delta × efficiency ÷ 4</c> at 80 % (<see cref="BaseApplicationDivisor"/> sizes an application, this schedules it).</summary>
    [Dk2Behavior(Confidence.StronglyInferred, "docs/research/open-questions.md#Q47")]
    public const int WorkApplicationsPerSecond = OriginalTicksPerGameSecond;

    /// <summary>The arithmetic shift a tabled per-second work delta takes to become one application:
    /// fourteen — twelve for Efficiency's 4096ths, two for the four ticks of a game second. A signed
    /// shift <b>floors</b>, where the <c>÷ 5</c> this replaced truncated toward zero, and that one
    /// unit is the whole of ADR-0133 (Q292, run 15 read four rates at once).</summary>
    [Dk2Behavior(Confidence.Confirmed, "docs/research/tile-work.md")]
    public const int WorkApplicationShift = 14;

    /// <summary>How often a worker applies work to a tile, in ticks: one — work lands once per
    /// creature update and that is once a tick. Derived, so a finer <see cref="TicksPerSecond"/> carries it.</summary>
    public const int WorkApplicationPeriodTicks = TicksPerSecond / WorkApplicationsPerSecond;
}
