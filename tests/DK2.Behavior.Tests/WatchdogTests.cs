// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Diagnostics;
using DK2.Harness;

namespace DK2.Behavior.Tests;

/// <summary>
/// The watchdog's bookkeeping (ADR-0552 Stage B), with <c>dotnet --version</c> standing in for a
/// harness run. Killing a hung run was checked by hand: a 4-second limit on a 3000-tick keeper
/// scenario left a failure report and a journal readable to tick 240.
/// </summary>
public class WatchdogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dk2-watch-{Guid.NewGuid():N}");
    private readonly string _good;
    private readonly string _broken;

    public WatchdogTests()
    {
        Directory.CreateDirectory(_root);
        _good = Path.Combine(_root, "good.yaml");
        File.WriteAllText(_good, "id: watch-probe\nmap: flat_8x8\nseed: 1\nduration_ticks: 1\n");
        _broken = Path.Combine(_root, "broken.yaml");
        File.WriteAllText(_broken, "id: [unclosed\n");
    }

    [Theory]
    [InlineData(true, 0, false, "passed")]
    [InlineData(true, 1, false, "failed")]
    [InlineData(true, 2, false, "setup")]
    [InlineData(true, 3, false, "setup")]
    [InlineData(true, -532462766, false, "crashed")]
    [InlineData(false, -1, false, "timeout")]
    [InlineData(true, 0, true, "cancelled")]
    [InlineData(false, -1, true, "cancelled")]
    public void AnEndedProcess_IsClassified(bool exited, int exitCode, bool cancelled, string verdict)
    {
        Assert.Equal(verdict, Watchdog.Classify(exited, exitCode, cancelled));
    }

    [Fact]
    public void AMalformedScenario_IsItsOwnVerdictAndTheWatchGoesOn()
    {
        WatchSummary summary = Watch(WatchLimits.Default, _broken, _good);

        Assert.Equal(2, summary.Ran);
        Assert.Equal(1, summary.NotPassed);
        string[] rows = File.ReadAllLines(Path.Combine(summary.Root, "watch-summary.tsv"));
        Assert.StartsWith("broken.yaml\tinvalid", rows[1], StringComparison.Ordinal);
        Assert.StartsWith("watch-probe\tpassed", rows[2], StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(summary.Root, "001", "invalid.txt")));
    }

    [Fact]
    public void TheRunLimit_LeavesTheRestUnrunAndSaysSo()
    {
        WatchSummary summary = Watch(WatchLimits.Default with { MaxRuns = 1 }, _good, _good, _good);

        Assert.Equal(1, summary.Ran);
        Assert.Equal(2, summary.Unrun);
    }

    [Fact]
    public void EveryWatch_WritesANewFolder()
    {
        WatchSummary first = Watch(WatchLimits.Default, _good);
        Thread.Sleep(5);
        WatchSummary second = Watch(WatchLimits.Default, _good);

        Assert.NotEqual(first.Root, second.Root);
        Assert.True(File.Exists(Path.Combine(first.Root, "001", "watch-probe", "stdout.log")));
    }

    private WatchSummary Watch(WatchLimits limits, params string[] files) =>
        new Watchdog(Path.Combine(_root, "out"), limits, (_, _) => new ProcessStartInfo("dotnet", "--version"))
            .Watch(files, TextWriter.Null);

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
