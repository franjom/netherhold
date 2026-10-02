// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Harness;
using DK2.TestSupport;
using Xunit.Abstractions;

namespace DK2.Behavior.Tests;

/// <summary>The repository's <c>scenarios/</c> tree, for the tests that load and run it, and the
/// one place a run's timings are written to a test's output.</summary>
internal static class ScenarioCorpus
{
    /// <summary>Scenarios a test of their own runs and checks for expectation failures, so the
    /// corpus theory does not run them a second time. Every test that loads one of these asserts
    /// <see cref="ScenarioResult.ExpectationFailures"/> is empty; <c>ScenarioTests</c> checks
    /// each entry names a file that exists.</summary>
    public static readonly IReadOnlySet<string> RunByTheirOwnTest = new HashSet<string>(StringComparer.Ordinal)
    {
        "keepers/level11a_drako_attack.yaml",
        "keepers/level17_harkan_call_to_arms.yaml",
        "keepers/level17_player5_retreat.yaml",
        "keepers/level17_harkan_training.yaml",
        "keepers/level17_harkan_unassisted.yaml",
        "keepers/level18_nemesis_attack.yaml",
        "keepers/level18_nemesis_heal.yaml",
    };

    private static readonly Lazy<GameDatabase> InstallDatabase = new(() => GameDatabaseLoader.Load(Dk2InstallGate.Path));

    /// <summary>The shipped database, loaded once for the whole assembly: a run never mutates it,
    /// and a level's own variables are layered on per run by <see cref="ScenarioRunner"/>.</summary>
    public static GameDatabase Database => InstallDatabase.Value;

    /// <summary>Every scenario file, as a path relative to <c>scenarios/</c> with forward slashes.</summary>
    public static List<string> Files() =>
        [.. Directory.EnumerateFiles(Root(), "*.yaml", SearchOption.AllDirectories)
             .Select(f => Path.GetRelativePath(Root(), f).Replace('\\', '/'))
             .OrderBy(f => f, StringComparer.Ordinal)];

    public static string PathOf(string relative) => Path.Combine(Root(), relative);

    public static Scenario Load(string relative) => Scenario.Load(PathOf(relative));

    /// <summary>Runs a scenario against the shipped install and writes where its time went.</summary>
    public static ScenarioResult Run(Scenario scenario, ITestOutputHelper output)
    {
        ScenarioResult result = new ScenarioRunner(Database, Dk2InstallGate.Path).Run(scenario);
        Report(scenario, result, output);
        return result;
    }

    public static void Report(Scenario scenario, ScenarioResult result, ITestOutputHelper output)
    {
        if (result.Timings is { } timings) output.WriteLine($"{scenario.Id}: {timings.Summarise()}");
    }

    public static string RepositoryRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DK2.slnx")))
            dir = dir.Parent;

        return dir?.FullName ?? throw new DirectoryNotFoundException(
            "Could not locate the repository root (no DK2.slnx above the test binaries).");
    }

    private static string Root() => Path.Combine(RepositoryRoot(), "scenarios");
}
