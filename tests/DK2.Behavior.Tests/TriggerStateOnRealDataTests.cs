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
            // it on the wrong one. The condition's index is byte 01 - which the
            // reader now calls `TimerId` - because that is the byte the handler
            // compares against 0x10 and then uses to index the bank
            // (0x005184EB, 0x0051851A). Byte 03, now `IndirectTimerId`, is the
            // indirect operand's timer and is 0 in all 303 shipped records
            // because no shipped level uses indirect TIMER mode. (Indirect
            // FLAG mode is used - exactly once, see below.)
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
    /// <b>A flag condition reads the flag named by byte 01, not byte 03.</b>
    /// This test exists because the reader had it on the wrong byte and every
    /// other test still passed: byte 03 is zero in 1374 of 1375 shipped
    /// records, so a sweep keyed on it saw one flag id and called it fine.
    ///
    /// <para>The handler at <c>0x0051845B</c> takes byte 01 into <c>DL</c> via
    /// <c>MOV DL,CH</c> and indexes the flag bank with it
    /// (<c>MOV BL,[EBP + ESI + 0x3F70]</c>). Byte 03 is the operand of the
    /// indirect form and is only read when byte 02's low bit is clear. The same
    /// shape as the timer condition, whose byte 01 was corrected earlier — this
    /// one was missed because only type 2 was re-checked.</para>
    ///
    /// <para>Written to <b>fail under the old reading</b>: byte 03 yields a
    /// single distinct value across the corpus, byte 01 yields dozens.</para>
    /// </summary>
    [InstallFact]
    public void AFlagConditionNamesItsFlagInByteOne()
    {
        string install = Install();

        var read = new SortedSet<long>();
        var indirect = new SortedSet<long>();
        int records = 0;

        foreach (TriggerRecord record in AllTriggers(install))
        {
            if (record.Tag != 213 || record.Type != 1) continue;
            records++;
            if (record.Parameters.TryGetValue("FlagId", out long f)) read.Add(f);
            if (record.Parameters.TryGetValue("IndirectFlagId", out long i)) indirect.Add(i);
        }

        Assert.Equal(1375, records);

        // The discriminating assertion. Byte 03 is 0 or 4 across the whole
        // campaign; byte 01 ranges over most of the bank.
        Assert.True(
            read.Count > 50,
            $"byte 01 yielded only {read.Count} distinct flag ids ({string.Join(",", read)}); "
            + "that is the signature of reading the indirect byte instead");
        Assert.Equal(91, read.Count);
        Assert.Equal(0, read.Min);

        // Including the special id, which the handler tests for explicitly
        // (CMP EBP,0x80) rather than indexing with.
        Assert.Equal(TriggerState.SpecialFlagId, read.Max);

        Assert.Equal(new long[] { 0, 4 }, indirect.ToArray());
    }

    /// <summary>
    /// <b>One shipped record compares a flag against another flag</b>, and it
    /// is the only one in the campaign — so indirect flag mode is a live
    /// mechanism with a population of one, not a dead branch.
    ///
    /// <para>Byte 02's low bit selects the operand: set means the immediate at
    /// <c>+4</c> (<c>MOV EAX,[EAX+0x4]</c>), clear means
    /// <c>flags[byte 03]</c> (<c>MOV AL,[ECX + ESI + 0x3F70]</c>). Exactly one
    /// record in 1375 has that bit clear, and it is the same record that has a
    /// non-zero byte 03. If the decode were wrong those two facts would have no
    /// reason to coincide.</para>
    ///
    /// <para><b>Consequence:</b> a condition evaluator that only supports
    /// comparison against an immediate cannot run level 3. Nothing evaluates
    /// conditions yet, so this is a requirement recorded rather than a bug.</para>
    /// </summary>
    [InstallFact]
    public void ExactlyOneShippedConditionComparesTwoFlags()
    {
        string install = Install();

        var indirectRecords = new List<(string Level, TriggerRecord Record)>();

        foreach (string level in LevelNames(install))
        {
            foreach (TriggerRecord record in
                     GameDatabaseLoader.LoadCampaignTriggers(install, level) ?? [])
            {
                if (record.Tag != 213 || record.Type != 1) continue;
                if (!record.Parameters.TryGetValue("Flag", out long mode)) continue;
                if ((mode & 1) == 0) indirectRecords.Add((level, record));
            }
        }

        (string Level, TriggerRecord Record) only = Assert.Single(indirectRecords);

        Assert.Equal("level3", only.Level);
        Assert.Equal(18, only.Record.TriggerId);

        // flag 3 > flag 4, repeating. Comparison 4 is signed '>' (0x00519980).
        Assert.Equal(4, only.Record.Parameters["Comparison"]);
        Assert.Equal(3, only.Record.Parameters["FlagId"]);
        Assert.Equal(4, only.Record.Parameters["IndirectFlagId"]);

        // And the immediate is unused in this mode, which is why it is zero.
        Assert.Equal(0, only.Record.Parameters["Value"]);
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

    [InstallFact]
    public void EveryShippedFlagAndOrdinaryTimerConditionEvaluates()
    {
        string install = Install();
        var state = new TriggerState();
        int flags = 0, timers = 0, specialTimers = 0;

        foreach (TriggerRecord record in AllTriggers(install))
        {
            if (record.Tag != 213 || record.Type is not (1 or 2)) continue;

            TriggerConditionResult result =
                TriggerConditionEvaluator.Evaluate(record, state, currentTick: 1);
            if (record.Type == 1)
            {
                flags++;
                Assert.NotEqual(TriggerConditionResult.NotEvaluated, result);
            }
            else if (record.Parameters["TimerId"] == TriggerState.SpecialTimerId)
            {
                specialTimers++;
                Assert.Equal(TriggerConditionResult.NotEvaluated, result);
            }
            else
            {
                timers++;
                Assert.NotEqual(TriggerConditionResult.NotEvaluated, result);
            }
        }

        Assert.Equal(1375, flags);
        Assert.Equal(298, timers);
        Assert.Equal(5, specialTimers);
    }

    [InstallFact]
    public void LevelThreeTrigger18EvaluatesFlagThreeAgainstFlagFour()
    {
        string install = Install();
        TriggerRecord record = (GameDatabaseLoader.LoadCampaignTriggers(install, "level3") ?? [])
            .Single(r => r.Tag == 213 && r.TriggerId == 18);
        var state = new TriggerState();
        state.Write(3, FlagWriteMode.Assign, 9);
        state.Write(4, FlagWriteMode.Assign, 5);

        Assert.Equal(1, record.Type);
        Assert.Equal(
            TriggerConditionResult.Satisfied,
            TriggerConditionEvaluator.Evaluate(record, state, currentTick: 0));

        state.Write(3, FlagWriteMode.Assign, 1);
        Assert.Equal(
            TriggerConditionResult.Unsatisfied,
            TriggerConditionEvaluator.Evaluate(record, state, currentTick: 0));
    }
}
