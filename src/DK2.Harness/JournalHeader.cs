// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using DK2.Simulation;

namespace DK2.Harness;

/// <summary>
/// What a harness journal records before tick 0 (ADR-0552 §7): the build, whether <c>src/</c> had
/// uncommitted changes, the scenario file's SHA-256, and the run's inputs. A run from a dirty tree
/// is recorded but is not called reproducible: the commit does not name the code that ran.
/// </summary>
public static class JournalHeader
{
    public const string ScenarioSha256 = "scenario.sha256";
    public const string SourceDirty = "src.dirty";

    public static IReadOnlyDictionary<string, string> For(Scenario scenario, IReadOnlyList<ScenarioController> controllers)
    {
        (string commit, string dirty) = GitState(scenario.SourcePath);
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["commit"] = commit,
            [SourceDirty] = dirty,
            ["scenario.id"] = scenario.Id,
            ["scenario.path"] = scenario.SourcePath,
            [ScenarioSha256] = Sha256(scenario.SourcePath),
            ["map"] = scenario.Map,
            ["seed"] = scenario.Seed.ToString(CultureInfo.InvariantCulture),
            ["duration_ticks"] = scenario.DurationTicks.ToString(CultureInfo.InvariantCulture),
            ["computer_keepers"] = string.Join(",", scenario.ComputerKeepers),
            ["controllers"] = string.Join(";", controllers.Select(c => $"{c.Name}:{c.PlayerId.Value}:{c.IntervalTicks}")),
            ["started_utc"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
        };
    }

    /// <summary>Throws unless this build and this scenario file are the ones the journal recorded.</summary>
    public static void CheckReplayable(Scenario scenario, InputJournal journal)
    {
        if (journal.HashVersion != GameSimulation.CurrentHashVersion)
            throw new InvalidOperationException(
                $"The journal was written at HashVersion {journal.HashVersion}; this build is " +
                $"{GameSimulation.CurrentHashVersion}. Replay it at its own commit ({Value(journal, "commit")}).");
        string recorded = Value(journal, ScenarioSha256);
        string current = Sha256(scenario.SourcePath);
        if (recorded is "none" or "unknown")
            throw new InvalidOperationException("The journal names no scenario file, so nothing ties it to this one.");
        if (recorded != current)
            throw new InvalidOperationException(
                $"'{scenario.SourcePath}' is not the scenario file the journal recorded " +
                $"(SHA-256 {recorded}, this file {current}).");
    }

    private static string Value(InputJournal journal, string key) =>
        journal.Header.TryGetValue(key, out string? value) ? value : "unknown";

    private static string Sha256(string path) =>
        File.Exists(path) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant() : "none";

    /// <summary>The commit and whether <c>src/</c> is dirty, from the repository holding the scenario
    /// (or the working directory). "unknown" when git is missing or the file is outside a repository.</summary>
    /// <remarks>Read once per directory per process: a <c>run</c> over the corpus would otherwise
    /// start three git processes per scenario. It names the tree, not the binary: a stale build of
    /// an older commit still records the tree's.</remarks>
    private static (string Commit, string Dirty) GitState(string scenarioPath)
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(scenarioPath.Length > 0 ? scenarioPath : ".")) ?? ".";
        lock (GitStates)
        {
            if (GitStates.TryGetValue(dir, out (string, string) known)) return known;
            string? commit = Git(dir, "rev-parse", "HEAD");
            string? top = Git(dir, "rev-parse", "--show-toplevel");
            string? status = top is null ? null : Git(top, "status", "--porcelain", "--", "src");
            return GitStates[dir] = (commit ?? "unknown", status is null ? "unknown" : status.Length == 0 ? "false" : "true");
        }
    }

    private static readonly Dictionary<string, (string, string)> GitStates = new(StringComparer.OrdinalIgnoreCase);

    private static string? Git(string workingDirectory, params string[] args)
    {
        try
        {
            var start = new ProcessStartInfo("git")
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (string arg in args) start.ArgumentList.Add(arg);
            using Process? git = Process.Start(start);
            if (git is null) return null;
            string output = git.StandardOutput.ReadToEnd().Trim();
            git.StandardError.ReadToEnd();
            git.WaitForExit();
            return git.ExitCode == 0 ? output : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}

/// <summary>A replay's verdict: how many recorded hashes it checked and the first that differed.</summary>
public sealed record ReplayReport(int ComparedTicks, long? FirstDivergentTick, ulong Recorded, ulong Replayed, bool JournalTruncated)
{
    public bool Matches => FirstDivergentTick is null;
}

/// <summary>Compares a replay's hashes with its journal's as the run goes; inert for an ordinary run.</summary>
internal sealed class ReplayComparison(InputJournal? journal)
{
    private int _compared;
    private long? _first;
    private ulong _recorded;
    private ulong _replayed;

    public void Compare(long tick, ulong hash)
    {
        if (journal is null || !journal.Hashes.TryGetValue(tick, out ulong recorded)) return;
        _compared++;
        if (_first is not null || recorded == hash) return;
        _first = tick;
        _recorded = recorded;
        _replayed = hash;
    }

    public ReplayReport? Report() =>
        journal is null ? null : new ReplayReport(_compared, _first, _recorded, _replayed, journal.Truncated);
}
