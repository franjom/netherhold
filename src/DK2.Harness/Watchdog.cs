// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Diagnostics;
using System.Globalization;
using System.Text;
using DK2.Simulation;

namespace DK2.Harness;

/// <summary>Limits for <see cref="Watchdog"/> (ADR-0552 §6): one run at a time, always.</summary>
public sealed record WatchLimits(TimeSpan PerRun, TimeSpan Total, int MaxRuns)
{
    public static readonly WatchLimits Default = new(TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), 20);
}

/// <summary>What a watch did: runs started, runs that did not pass, scenarios left unrun.</summary>
public sealed record WatchSummary(string Root, int Ran, int NotPassed, int Unrun);

/// <summary>
/// Runs scenarios one at a time, each in its own harness process, and kills a run that outlives
/// its limit: a hang inside <c>GameSimulation.Tick()</c> cannot be cancelled from inside. Every
/// watch writes under a new folder and every run under its own numbered one, so no evidence is
/// deleted or mistaken for an earlier run's. The child writes its report and journal; for a run
/// that did not pass, the watchdog adds <c>failure.md</c> and keeps the child's output.
/// </summary>
/// <param name="launch">Starts one run from a scenario file and an output folder. Defaults to this
/// harness's <c>run</c> verb; tests substitute another process.</param>
public sealed class Watchdog(string outRoot, WatchLimits limits, Func<string, string, ProcessStartInfo>? launch = null)
{
    private volatile bool _cancelled;
    private Process? _current;

    /// <summary>Stops launching runs and kills the current one (Ctrl+C).</summary>
    public void Cancel()
    {
        _cancelled = true;
        Kill(_current);
    }

    public WatchSummary Watch(IReadOnlyList<string> scenarioFiles, TextWriter log)
    {
        string root = Path.Combine(outRoot, "watch-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(root);
        log.WriteLine($"watch output: {root}");
        string summaryPath = Path.Combine(root, "watch-summary.tsv");
        File.WriteAllText(summaryPath, "scenario\tverdict\texit\tseconds\tjournal_ticks\tjournal_end\n");
        var total = Stopwatch.StartNew();
        int notPassed = 0;
        int run = 0;
        foreach (string file in scenarioFiles)
        {
            TimeSpan left = limits.Total - total.Elapsed;
            if (_cancelled || run >= limits.MaxRuns || left <= TimeSpan.Zero)
            {
                log.WriteLine($"stopping before {file}: {(_cancelled ? "cancelled" : run >= limits.MaxRuns ? "run limit" : "time budget")} reached");
                break;
            }
            run++;
            Outcome outcome = RunOne(file, Path.Combine(root, run.ToString("D3", CultureInfo.InvariantCulture)),
                left < limits.PerRun ? left : limits.PerRun);
            if (outcome.Verdict != "passed") notPassed++;
            File.AppendAllText(summaryPath, string.Join('\t',
                outcome.Id, outcome.Verdict, outcome.ExitCode.ToString(CultureInfo.InvariantCulture),
                outcome.Seconds.ToString("F1", CultureInfo.InvariantCulture),
                outcome.Journal?.LastHashedTick.ToString(CultureInfo.InvariantCulture) ?? "",
                outcome.Journal?.EndReason ?? (outcome.Journal is null ? "no journal" : "cut short")) + "\n");
            log.WriteLine($"{outcome.Id,-36} {outcome.Verdict,-8} {outcome.Seconds,7:F1} s");
        }
        return new WatchSummary(root, run, notPassed, scenarioFiles.Count - run);
    }

    private sealed record Outcome(string Id, string Verdict, int ExitCode, double Seconds, InputJournal? Journal);

    private Outcome RunOne(string file, string runOut, TimeSpan limit)
    {
        Directory.CreateDirectory(runOut);
        string id;
        try { id = Scenario.Load(file).Id; }
        catch (Exception ex)
        {
            // Any load failure, YAML syntax included, is this scenario's verdict, not the watch's end.
            File.WriteAllText(Path.Combine(runOut, "invalid.txt"), $"{file}\n{ex.GetType().FullName}: {ex.Message}\n");
            return new Outcome(Path.GetFileName(file), "invalid", -1, 0, null);
        }

        string dir = Path.Combine(runOut, id);
        Directory.CreateDirectory(dir);
        string journalPath = Path.Combine(dir, "input.journal");
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var clock = Stopwatch.StartNew();
        ProcessStartInfo start = (launch ?? SelfRun)(file, runOut);
        start.RedirectStandardOutput = start.RedirectStandardError = true;
        start.UseShellExecute = false;
        using var child = new Process { StartInfo = start };
        child.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (stdout) stdout.AppendLine(e.Data); };
        child.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (stderr) stderr.AppendLine(e.Data); };
        child.Start();
        _current = child;
        if (_cancelled) Kill(child);
        child.BeginOutputReadLine();
        child.BeginErrorReadLine();
        bool exited = child.WaitForExit(limit);
        if (!exited) Kill(child);
        // A descendant that survives the kill may hold the output pipes; do not wait on it forever.
        child.WaitForExit(30_000);
        _current = null;

        int exit = exited ? child.ExitCode : -1;
        string verdict = Classify(exited, exit, _cancelled);
        InputJournal? journal = ReadJournal(journalPath);
        File.WriteAllText(Path.Combine(dir, "stdout.log"), stdout.ToString());
        File.WriteAllText(Path.Combine(dir, "stderr.log"), stderr.ToString());
        if (verdict != "passed")
            File.WriteAllText(Path.Combine(dir, "failure.md"),
                FailureReport(file, id, verdict, exit, limit, journalPath, journal, stderr.ToString()));
        return new Outcome(id, verdict, exit, clock.Elapsed.TotalSeconds, journal);
    }

    /// <summary>A run's verdict from how its process ended. Exit 1 is the harness's own failure (an
    /// unmet expectation or a refused scenario); 2 and 3 are usage and a missing installation. A
    /// Ctrl+C reaches the child too, so a cancelled watch names its last run cancelled however it ended.</summary>
    public static string Classify(bool exited, int exitCode, bool cancelled) =>
        cancelled ? "cancelled"
        : !exited ? "timeout"
        : exitCode switch { 0 => "passed", 1 => "failed", 2 or 3 => "setup", _ => "crashed" };

    private static void Kill(Process? process)
    {
        try { process?.Kill(entireProcessTree: true); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }

    private static InputJournal? ReadJournal(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using FileStream stream = File.OpenRead(path);
            return InputJournal.Read(stream);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or EndOfStreamException)
        {
            return null;
        }
    }

    private static string FailureReport(
        string file, string id, string verdict, int exit, TimeSpan limit, string journalPath,
        InputJournal? journal, string stderr)
    {
        var text = new StringBuilder();
        text.AppendLine($"# {verdict}: {id}");
        text.AppendLine();
        text.AppendLine($"- scenario: {file}");
        text.AppendLine(verdict == "timeout" ? $"- killed after {limit.TotalSeconds:F0} s" : $"- exit code: {exit}");
        if (journal is null)
            text.AppendLine("- journal: none readable (the run failed before tick 0)");
        else
        {
            text.AppendLine($"- journal: {journalPath}");
            text.AppendLine($"- last hashed tick: {journal.LastHashedTick} (hashes are flushed every {InputJournalWriter.FlushEveryTicks} ticks, every command at once)");
            text.AppendLine($"- journal end: {journal.EndReason ?? "none - the process was killed or crashed"}");
            text.AppendLine($"- commit: {Header(journal, "commit")}, src dirty: {Header(journal, JournalHeader.SourceDirty)}");
            text.AppendLine();
            text.AppendLine("Reproduce at that commit:");
            text.AppendLine();
            text.AppendLine($"    dotnet run --project src/DK2.Harness -- replay \"{file}\" \"{journalPath}\"");
        }
        string tail = string.Join('\n', stderr.Split('\n').TakeLast(40));
        if (tail.Trim().Length > 0)
        {
            text.AppendLine();
            text.AppendLine("## stderr (last 40 lines)");
            text.AppendLine();
            text.AppendLine("```");
            text.AppendLine(tail.TrimEnd());
            text.AppendLine("```");
        }
        return text.ToString();
    }

    private static string Header(InputJournal journal, string key) =>
        journal.Header.TryGetValue(key, out string? value) ? value : "unknown";

    private static ProcessStartInfo SelfRun(string file, string runOut) => SelfStart("run", file, "--out", runOut);

    /// <summary>This harness again, whether it was started as an apphost or through <c>dotnet</c>.</summary>
    private static ProcessStartInfo SelfStart(params string[] args)
    {
        string host = Environment.ProcessPath ?? throw new InvalidOperationException("No process path.");
        var start = new ProcessStartInfo(host)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (Path.GetFileNameWithoutExtension(host).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(Watchdog).Assembly.Location);
        foreach (string arg in args) start.ArgumentList.Add(arg);
        return start;
    }
}
