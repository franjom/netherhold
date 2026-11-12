// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Diagnostics;
using DK2.Harness;

namespace DK2.Behavior.Tests;

/// <summary>
/// The watchdog's bounds, ordered outcomes and cancellation (ADR-0552), using real helper
/// processes and synthetic scenarios without a game installation. Sleeping children expose
/// overlap, budget and cancellation errors without depending on simulation speed.
/// </summary>
public class WatchdogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dk2-watch-{Guid.NewGuid():N}");
    private readonly string _good;
    private readonly string _broken;

    // A cold PowerShell on a busy Windows runner can take well over 15 s to write its PID, so the
    // tests wait this long for a child to start; the 30 s sleep after the write keeps it alive meanwhile.
    private static readonly TimeSpan ChildStart = TimeSpan.FromSeconds(60);

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
        Assert.StartsWith("broken.yaml\t\tinvalid", rows[1], StringComparison.Ordinal);
        Assert.StartsWith("watch-probe\t\tpassed", rows[2], StringComparison.Ordinal);
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

    /// <summary>Overlap is read from the children's own markers, not from elapsed time, so a loaded
    /// machine cannot fail it: with two jobs each child sees the other start before it ends; with one,
    /// the second starts only after the first is done.</summary>
    [Fact]
    public void WithTwoJobsTwoRunsOverlap()
    {
        string serial = Run(1, waitSeconds: 1), parallel = Run(2, waitSeconds: 60);
        Assert.True(File.Exists(Path.Combine(parallel, "a.saw-other")) && File.Exists(Path.Combine(parallel, "b.saw-other")),
            "with two jobs both children run at once");
        Assert.True(File.Exists(Path.Combine(serial, "b.after-other")) && !File.Exists(Path.Combine(serial, "a.saw-other")),
            "with one job the second child starts after the first is done");

        string Run(int jobs, int waitSeconds)
        {
            string markers = Path.Combine(_root, $"markers{jobs}");
            Directory.CreateDirectory(markers);
            var limits = new WatchLimits(TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5), 20, Jobs: jobs);
            new Watchdog(Path.Combine(_root, $"out{jobs}"), limits,
                    (job, _) => Rendezvous(markers, job.Label, job.Label == "a" ? "b" : "a", waitSeconds))
                .Watch([new WatchJob(_good, [], "a"), new WatchJob(_good, [], "b")], TextWriter.Null);
            return markers;
        }
    }

    /// <summary>A child that marks its start, notes whether the other child had already finished, waits
    /// up to <paramref name="waitSeconds"/> for the other to start, notes whether it did, and marks its end.</summary>
    private static ProcessStartInfo Rendezvous(string dir, string me, string other, int waitSeconds)
    {
        var start = new ProcessStartInfo(OperatingSystem.IsWindows() ? "powershell" : "sh") { CreateNoWindow = true };
        if (OperatingSystem.IsWindows())
        {
            string Q(string name) => "'" + Path.Combine(dir, name).Replace("'", "''") + "'";
            foreach (string arg in new[] { "-NoProfile", "-NonInteractive", "-Command",
                $"Set-Content {Q(me + ".started")} ''; if (Test-Path {Q(other + ".done")}) {{ Set-Content {Q(me + ".after-other")} '' }}; " +
                $"$t=[DateTime]::UtcNow.AddSeconds({waitSeconds}); while (-not (Test-Path {Q(other + ".started")}) -and [DateTime]::UtcNow -lt $t) {{ Start-Sleep -Milliseconds 50 }}; " +
                $"if (Test-Path {Q(other + ".started")}) {{ Set-Content {Q(me + ".saw-other")} '' }}; Set-Content {Q(me + ".done")} ''" })
                start.ArgumentList.Add(arg);
        }
        else
        {
            const string script = "d=$1; : > \"$d/$2.started\"; [ -e \"$d/$3.done\" ] && : > \"$d/$2.after-other\"; i=0; " +
                "while [ ! -e \"$d/$3.started\" ] && [ $i -lt $(($4*20)) ]; do sleep 0.05; i=$((i+1)); done; " +
                "[ -e \"$d/$3.started\" ] && : > \"$d/$2.saw-other\"; : > \"$d/$2.done\"";
            foreach (string arg in new[] { "-c", script, "dk2-watch", dir, me, other, waitSeconds.ToString() })
                start.ArgumentList.Add(arg);
        }
        return start;
    }

    [Fact]
    public void SummaryAndConsoleKeepJobOrderWhenTheSecondFinishesFirst()
    {
        var limits = WatchLimits.Default with { Jobs = 2 };
        using var log = new StringWriter();
        WatchSummary summary = new Watchdog(Path.Combine(_root, "ordered"), limits,
            (job, _) => Sleep(job.Label == "first" ? 2 : 0))
            .Watch([new WatchJob(_good, [], "first"), new WatchJob(_good, [], "second")], log);

        string[] rows = File.ReadAllLines(Path.Combine(summary.Root, "watch-summary.tsv")).Skip(1).ToArray();
        Assert.Equal(["first", "second"], rows.Select(r => r.Split('\t')[1]));
        Assert.True(log.ToString().IndexOf("first", StringComparison.Ordinal) < log.ToString().IndexOf("second", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CancelKillsEveryRunningChild()
    {
        var watchdog = new Watchdog(Path.Combine(_root, "cancel"), WatchLimits.Default with { Jobs = 2 },
            (job, _) => Sleep(30, Path.Combine(_root, job.Label + ".pid")));
        Task<WatchSummary> watching = Task.Run(() => watchdog.Watch(
            [new WatchJob(_good, [], "first"), new WatchJob(_good, [], "second"), new WatchJob(_good, [], "unrun")], TextWriter.Null));
        try
        {
            Assert.True(SpinWait.SpinUntil(() => HasPid(Path.Combine(_root, "first.pid"))
                && HasPid(Path.Combine(_root, "second.pid")), ChildStart), "both children must start");
            watchdog.Cancel();
            WatchSummary summary = await watching.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(2, summary.Ran);
            Assert.Equal(1, summary.Unrun);
            Assert.Equal(["cancelled", "cancelled"], File.ReadAllLines(Path.Combine(summary.Root, "watch-summary.tsv"))
                .Skip(1).Select(row => row.Split('\t')[2]));
            foreach (string name in new[] { "first", "second" })
            {
                int pid = int.Parse(File.ReadAllText(Path.Combine(_root, name + ".pid")));
                try { using Process process = Process.GetProcessById(pid); Assert.True(process.HasExited); }
                catch (ArgumentException) { /* The exited process has already left the process table. */ }
            }
        }
        finally
        {
            watchdog.Cancel();
            await watching.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void JobsBelowOneAreRefused(int jobs)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WatchLimits(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1), 1, Jobs: jobs));
        Assert.Throws<ArgumentOutOfRangeException>(() => WatchLimits.Default with { Jobs = jobs });
    }

    [Fact]
    public void ParallelJobsStillRespectTheRunLimit()
    {
        WatchSummary summary = Watch(WatchLimits.Default with { Jobs = 2, MaxRuns = 1 }, _good, _good, _good);
        Assert.Equal((1, 2), (summary.Ran, summary.Unrun));
    }

    private static ProcessStartInfo Sleep(int seconds, string? pidPath = null)
    {
        var start = new ProcessStartInfo(OperatingSystem.IsWindows() ? "powershell" : "sh") { CreateNoWindow = true };
        if (OperatingSystem.IsWindows())
        {
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-NonInteractive");
            start.ArgumentList.Add("-Command");
            string pid = pidPath is null ? "" : $"[IO.File]::WriteAllText('{pidPath.Replace("'", "''")}', [string]$PID); ";
            start.ArgumentList.Add($"{pid}Start-Sleep -Seconds {seconds}");
        }
        else
        {
            foreach (string arg in new[] { "-c", "echo $$ > \"$1\"; exec sleep \"$2\"", "dk2-watch", pidPath ?? "/dev/null", seconds.ToString() })
                start.ArgumentList.Add(arg);
        }
        return start;
    }

    [Fact]
    public void TheBudgetIsRecheckedAfterWaitingForAWorkerSlot()
    {
        var limits = new WatchLimits(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1), 20, Jobs: 2);
        WatchSummary summary = new Watchdog(Path.Combine(_root, "budget"), limits, (_, _) => Sleep(3))
            .Watch([_good, _good, _good], TextWriter.Null);
        Assert.Equal((2, 1), (summary.Ran, summary.Unrun));
        Assert.Equal(["timeout", "timeout"], File.ReadAllLines(Path.Combine(summary.Root, "watch-summary.tsv"))
            .Skip(1).Select(row => row.Split('\t')[2]));
    }

    [Fact]
    public async Task ALaunchFailureCancelsAndJoinsTheOtherWorkers()
    {
        string pid = Path.Combine(_root, "survivor.pid");
        var watchdog = new Watchdog(Path.Combine(_root, "launch-failure"), WatchLimits.Default with { Jobs = 2 }, (job, _) =>
        {
            if (job.Label == "first") return Sleep(30, pid);
            if (!SpinWait.SpinUntil(() => HasPid(pid), ChildStart)) throw new TimeoutException("first child did not start");
            throw new InvalidOperationException("launch failed");
        });
        Task<WatchSummary> watching = Task.Run(() => watchdog.Watch(
            [new WatchJob(_good, [], "first"), new WatchJob(_good, [], "second")], TextWriter.Null));
        try
        {
            InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => watching.WaitAsync(ChildStart + TimeSpan.FromSeconds(10)));
            Assert.Equal("launch failed", error.Message);
            try { using Process child = Process.GetProcessById(int.Parse(File.ReadAllText(pid))); Assert.True(child.HasExited); }
            catch (ArgumentException) { }
        }
        finally
        {
            watchdog.Cancel();
            try { await watching.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (InvalidOperationException) { }
        }
    }

    private static bool HasPid(string path)
    {
        try { return int.TryParse(File.ReadAllText(path), out _); }
        catch (IOException) { return false; }
    }

    [Theory]
    [InlineData("--jobs")]
    [InlineData("--max-runs")]
    public void AnExplicitIntegerOptionRequiresAValue(string flag)
    {
        Assert.Null(HarnessCommandLine.Int(["watch", _good, flag], flag, 1));
        Assert.Equal(1, HarnessCommandLine.Int(["watch", _good], flag, 1));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
