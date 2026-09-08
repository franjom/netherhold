// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

/// <summary>
/// <see cref="TriggerState"/>'s bank sizes against the shipped campaign. See
/// docs/research/trigger-state.md and ADR-0102.
///
/// <para>The bank widths come from the executable's layout — flags at
/// <c>+0x3F70</c>, timers 0x80 bytes later at <c>+0x3FF0</c>. These are what
/// check that the shipped levels actually live inside them.</para>
/// </summary>
public class TriggerStateOnRealDataTests
{
    private static string Install() => Dk2InstallGate.Path;

    private static IEnumerable<string> LevelNames(string install)
    {
        string maps = Path.Combine(install, "Data", "editor", "maps");
        Assert.True(Directory.Exists(maps), $"'{maps}' is missing from the installation.");

        foreach (string file in Directory.EnumerateFiles(maps, "*Triggers.kld").OrderBy(f => f))
            yield return Path.GetFileName(file)[..^"Triggers.kld".Length];
    }

    private static IEnumerable<TriggerRecord> AllTriggers(string install)
    {
        foreach (string level in LevelNames(install))
            foreach (TriggerRecord record in GameDatabaseLoader.LoadCampaignTriggers(install, level) ?? [])
                yield return record;
    }

    /// <summary>
    /// <b>No shipped level names a flag outside the bank.</b> The bank is 128
    /// bytes because the timer bank starts 0x80 bytes after it, and id 128 is
    /// special-cased by both dispatchers rather than running off the end.
    ///
    /// <para>What this rules out is the alarming case: an id above 128, which
    /// in the original would write <b>into the timer bank</b> because the write
    /// path masks the id with 0xFF and bounds it no further. If a level did
    /// that, faithfulness and sanity would be in conflict and it would need
    /// deciding rather than assuming. None does.</para>
    /// </summary>
    [InstallFact]
    public void NoShippedLevelNamesAFlagOrTimerOutsideItsBank()
    {
        string install = Install();

        var flagIds = new SortedSet<long>();
        var startedTimers = new SortedSet<long>();
        var testedTimers = new SortedSet<long>();
        var type23Timers = new SortedSet<long>();

        foreach (TriggerRecord record in AllTriggers(install))
        {
            if (record.Parameters.TryGetValue("FlagId", out long flag)) flagIds.Add(flag);

            // WHICH BYTE IS THE TIMER INDEX MATTERS, and the external names had
            // it on the wrong one. The condition's index is byte 01 - the field
            // the reader still calls `TargetId` - because that is the byte the
            // handler compares against 0x10 and then uses to index the bank
            // (0x005184EB, 0x0051851A). Byte 03, which the reader calls
            // `TimerId`, is the INDIRECT operand's timer and is 0 in all 303
            // shipped records because no shipped level uses indirect mode.
            if (record.Tag == 213 && record.Type == 2
                && record.Parameters.TryGetValue("TimerId", out long tested))
                testedTimers.Add(tested);

            if (record.Tag == 214 && record.Type == 8
                && record.Parameters.TryGetValue("TimerId", out long started))
                startedTimers.Add(started);

            if (record.Tag == 214 && record.Type == 23
                && record.Parameters.TryGetValue("TimerId", out long set))
                type23Timers.Add(set);
        }

        Assert.NotEmpty(flagIds);
        Assert.NotEmpty(startedTimers);
        Assert.NotEmpty(testedTimers);

        Assert.True(
            flagIds.Max <= TriggerState.SpecialFlagId,
            $"flag id {flagIds.Max} is past the bank and past the special target; "
            + $"ids seen: {string.Join(",", flagIds)}");

        // Started timers all live inside the sixteen-slot bank.
        Assert.True(
            startedTimers.Max < TriggerState.TimerCount,
            $"timer id {startedTimers.Max} is past the {TriggerState.TimerCount}-slot bank; "
            + $"ids seen: {string.Join(",", startedTimers)}");

        // Tested timers reach the sentinel and never go past it.
        Assert.True(
            testedTimers.Max <= TriggerState.SpecialTimerId,
            $"tested timer id {testedTimers.Max} is past the special target; "
            + $"ids seen: {string.Join(",", testedTimers)}");

        // And EVERY action type 23 record names the sentinel, which is what
        // makes it a live mechanism rather than a curiosity (Q232).
        Assert.Equal(new[] { (long)TriggerState.SpecialTimerId }, type23Timers.ToArray());
    }

    /// <summary>
    /// <b>Flags and timers are the trigger system's dominant mechanism</b>, and
    /// that is the reason they were built before anything else a trigger can
    /// do. Pinned so that a later reading which quietly stops recognising them
    /// shows up as a count rather than as a level that no longer finishes.
    /// </summary>
    [InstallFact]
    public void FlagsAndTimersAreMostOfWhatTheCampaignsTriggersDo()
    {
        string install = Install();

        int total = 0, flagOrTimer = 0;
        foreach (TriggerRecord record in AllTriggers(install))
        {
            total++;
            if (record.Parameters.ContainsKey("FlagId") || record.Parameters.ContainsKey("TimerId"))
                flagOrTimer++;
        }

        Assert.Equal(9161, total);
        Assert.Equal(3472, flagOrTimer);
        Assert.True(flagOrTimer * 100 / total >= 37);
    }

    /// <summary>
    /// <b>Every comparison code the campaign uses is one DK2 implements.</b>
    /// The routine at <c>0x00519980</c> defines 1 to 6 and nothing else, so a
    /// seventh value in the data would mean the reading is wrong about which
    /// byte the comparison is.
    /// </summary>
    [InstallFact]
    public void EveryComparisonCodeInTheCampaignIsOneOfTheSix()
    {
        string install = Install();

        var codes = new SortedSet<long>();
        foreach (TriggerRecord record in AllTriggers(install))
            if (record.Parameters.TryGetValue("Comparison", out long code))
                codes.Add(code);

        Assert.NotEmpty(codes);
        foreach (long code in codes)
            Assert.InRange(code, 1, 6);

        // Every code the campaign actually uses, exercised against the mapping
        // rather than only range-checked. `Compare(code, 1, 2) || true` was
        // here first and could not fail, which is worse than no assertion.
        var expected = new Dictionary<long, bool>
        {
            [1] = true,   // 1 <  2
            [2] = true,   // 1 <= 2
            [3] = false,  // 1 == 2
            [4] = false,  // 1 >  2
            [5] = false,  // 1 >= 2
            [6] = true,   // 1 != 2
        };

        foreach (long code in codes)
            Assert.Equal(expected[code], TriggerState.Compare((byte)code, 1, 2));
    }
}
