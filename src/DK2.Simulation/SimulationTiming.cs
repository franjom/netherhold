// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

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

    /// <summary>TRAPS loader 0x004AA440: retain the low dword of the product, then shift logically.
    /// The authored value is seconds in 1/4096 units; overflow and negative inputs stay unsigned.</summary>
    [Dk2Behavior(Confidence.StronglyInferred, "docs/research/traps.md")]
    public static uint LoaderFixedSecondsToTicks(int secondsRaw) =>
        unchecked((uint)(secondsRaw * OriginalTicksPerGameSecond)) >> 12;

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

    /// <summary>A creature's turn a tick, <c>def+0x6A0</c>: the file's <see cref="CreatureDef.TurnRate"/>
    /// through <see cref="LoaderRatePerTick"/> (<c>0x00492320</c>), the rate every creature turn
    /// (<c>0x004B2AE0</c>) steps its heading by. Guard 256, Thief 512 and Dwarf 341 of the 2048 a
    /// circle, as DK2's Level 4 captures turn them (ADR-0602).</summary>
    [Dk2Behavior(Confidence.Confirmed, "docs/research/steering-behaviours.md#53-turning-comes-first")]
    public static int TurnRatePerTick(CreatureDef definition) => LoaderRatePerTick(definition.TurnRate);

    /// <summary>How many times a game second a worker applies work to a tile: four, once a tick,
    /// which is the creature update's own rate (Q1, Q47, ADR-0089). An imp mining a gold seam frees
    /// 273 gold a swing (owner), 60 health against <c>MineGoldHealth</c>'s −300 — exactly
    /// <c>delta × efficiency ÷ 4</c> at 80 % (<see cref="BaseApplicationDivisor"/> sizes an application, this schedules it).</summary>
    [Dk2Behavior(Confidence.StronglyInferred, "docs/research/open-questions.md#Q47")]
    public const int WorkApplicationsPerSecond = OriginalTicksPerGameSecond;

    /// <summary>One application of a tabled per-second work delta at <paramref name="efficiency"/>
    /// 4096ths, in DK2's two steps. The variable loader divides the value by variable 19 with
    /// <c>IDIV</c>, truncating toward zero (<c>0x0051CBA0</c>); each work action shifts the stored
    /// value into 12.12, multiplies it by the rate (<c>0x004363F0</c>) and shifts back with
    /// <c>SAR</c>, which floors (<c>0x00496F57</c>, <c>0x00497B66</c>; ADR-0133). One shift of
    /// fourteen agrees only for a value that four divides: <c>RepairWallHealth</c> 22 is stored as
    /// 5 and applies 3 at the base rate, not 4 (ADR-0387). Ours divides by the constant four,
    /// variable 19's shipped value; a level that overrides it would differ (combat-damage.md).</summary>
    [Dk2Behavior(Confidence.Confirmed, "docs/research/tile-work.md")]
    public static int WorkApplication(int perSecond, int efficiency)
    {
        int stored = perSecond / OriginalTicksPerGameSecond;
        return (int)(((long)stored * efficiency) >> 12);
    }

    /// <summary>How often a worker applies work to a tile, in ticks: one — work lands once per
    /// creature update and that is once a tick. Derived, so a finer <see cref="TicksPerSecond"/> carries it.</summary>
    public const int WorkApplicationPeriodTicks = TicksPerSecond / WorkApplicationsPerSecond;
}
