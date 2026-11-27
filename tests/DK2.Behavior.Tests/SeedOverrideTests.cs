// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Text.Json;
using DK2.Core;
using DK2.Harness;
using DK2.Simulation;
using DK2.Simulation.Players;

namespace DK2.Behavior.Tests;

/// <summary>The <c>--seed</c> override (campaign bot stage 3 plan, Task 0a): one scenario file runs on
/// any declared seed, the journal and the player-run manifest record the seed actually run, and a
/// replay refuses a journal whose seed is not the one it would run.</summary>
public sealed class SeedOverrideTests : IDisposable
{
    private static readonly PlayerRunId Run = new("seed-override-test");
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dk2-seed-{Guid.NewGuid():N}");
    private readonly string _scenarioPath;

    public SeedOverrideTests()
    {
        // Repository-shaped, so the manifest can hash a source tree beside the scenario.
        Directory.CreateDirectory(Path.Combine(_root, "src"));
        File.WriteAllText(Path.Combine(_root, "DK2.slnx"), "<Solution />");
        File.WriteAllText(Path.Combine(_root, "src", "A.cs"), "class A {}");
        _scenarioPath = Path.Combine(_root, "probe.yaml");
        File.WriteAllText(_scenarioPath, """
            id: seed-probe
            map: chamber_9x9
            seed: 7
            duration_ticks: 10
            """);
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void ASeedIsReadAndAMalformedOneIsRefused()
    {
        Assert.True(HarnessCommandLine.SeedOverride(["run", "x", "--seed", "12"], out ulong? twelve));
        Assert.Equal(12UL, twelve);
        Assert.True(HarnessCommandLine.SeedOverride(["run", "x"], out ulong? none));
        Assert.Null(none);
        foreach (string[] bad in new[] { new[] { "run", "x", "--seed", "x" }, ["run", "x", "--seed", "-1"], ["run", "x", "--seed"] })
        {
            Assert.False(HarnessCommandLine.SeedOverride(bad, out _));
        }
    }

    [Fact]
    public void WatchSeedsMakeOneJobPerScenarioAndSeed()
    {
        List<WatchJob> jobs = HarnessCommandLine.WatchJobs(["a.yaml", "b.yaml"], ["watch", "dir", "--seeds", "11-13"])!;
        Assert.Equal(6, jobs.Count);
        Assert.Equal(["a.yaml", "a.yaml", "a.yaml", "b.yaml", "b.yaml", "b.yaml"], jobs.Select(j => j.File));
        Assert.Equal(["seed 11", "seed 12", "seed 13", "seed 11", "seed 12", "seed 13"], jobs.Select(j => j.Label));
        Assert.Equal(["--seed", "12"], jobs[1].Args.Skip(jobs[1].Args.Count - 2));
    }

    [Fact]
    public void WatchPassesAPlainSeedToEveryRun()
    {
        List<WatchJob> jobs = HarnessCommandLine.WatchJobs(["a.yaml"], ["watch", "dir", "--seed", "5"])!;
        Assert.Equal(["--seed", "5"], Assert.Single(jobs).Args);
    }

    [Theory]
    [InlineData("--seeds", "13-11")]
    [InlineData("--seeds", "x")]
    [InlineData("--seeds", "11-")]
    [InlineData("--seed", "x")]
    public void AMalformedWatchSeedIsRefused(string flag, string value)
    {
        Assert.Null(HarnessCommandLine.WatchJobs(["a.yaml"], ["watch", "dir", flag, value]));
    }

    [Fact]
    public void WatchRefusesASeedAndASeedRangeTogether()
    {
        Assert.Null(HarnessCommandLine.WatchJobs(["a.yaml"], ["watch", "dir", "--seed", "5", "--seeds", "1-2"]));
    }

    [Fact]
    public void AReplayRefusesAJournalWhoseSeedIsNotTheOneItWouldRun()
    {
        var recorded = Scenario.Load(_scenarioPath);
        recorded.Seed = 12;
        var stream = new MemoryStream();
        using (var writer = new InputJournalWriter(stream, JournalHeader.For(recorded, [])))
        {
            writer.WriteEnd(10, "completed");
        }

        stream.Position = 0;
        var journal = InputJournal.Read(stream);

        var fromFile = Scenario.Load(_scenarioPath);
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() => JournalHeader.CheckReplayable(fromFile, journal));
        Assert.Contains("seed 12", refused.Message, StringComparison.Ordinal);
        Assert.Contains("--seed 12", refused.Message, StringComparison.Ordinal);

        fromFile.Seed = 12;
        JournalHeader.CheckReplayable(fromFile, journal);
    }

    [Fact]
    public void TheManifestRecordsTheSeedRunAndBlocksAnother()
    {
        string evidence = Path.Combine(_root, "evidence");
        var overridden = Scenario.Load(_scenarioPath);
        overridden.Seed = 12;
        Assert.Equal(12UL, PlayerRunManifest.Compute(overridden, null, Run).Seed);
        PlayerRunManifest.Write(evidence, overridden, null, Run);
        Assert.True(PlayerRunManifest.Verify(evidence, overridden, null, Run).MayReplay);

        var fromFile = Scenario.Load(_scenarioPath);
        Assert.Contains("seed differs: recorded 12, now 7", PlayerRunManifest.Verify(evidence, fromFile, null, Run).Blocking);
    }

    [Fact]
    public void ARunOnAnOverriddenSeedRecordsItAndReplaysOnIt()
    {
        var runner = new ScenarioRunner(ScenarioTests.SyntheticDatabase(), null);
        var scenario = Scenario.Load(_scenarioPath);
        scenario.OverrideSeed(12);
        var stream = new MemoryStream();
        ScenarioResult run = runner.Run(scenario, new RunOptions { Journal = stream });
        stream.Position = 0;
        var journal = InputJournal.Read(stream);
        Assert.Equal("12", journal.Header["seed"]);

        ScenarioResult replay = runner.Replay(scenario, journal);
        Assert.Null(replay.Replay!.FirstDivergentTick);
        Assert.Equal(run.TicksRun, replay.TicksRun);
        Assert.Throws<InvalidOperationException>(() => runner.Replay(Scenario.Load(_scenarioPath), journal));
    }

    [Fact]
    public void AJournalWithNoSeedRecordStillReplays()
    {
        var scenario = Scenario.Load(_scenarioPath);
        var header = new Dictionary<string, string>(JournalHeader.For(scenario, []), StringComparer.Ordinal);
        header.Remove("seed");
        var stream = new MemoryStream();
        using (var writer = new InputJournalWriter(stream, header))
        {
            writer.WriteEnd(10, "completed");
        }

        stream.Position = 0;
        JournalHeader.CheckReplayable(scenario, InputJournal.Read(stream));
    }

    [Fact]
    public void AnOverrideKeepsTheFilesOwnRuleForADk2Seed()
    {
        string dk2 = Path.Combine(_root, "dk2.yaml");
        File.WriteAllText(dk2, "id: dk2-probe\nmap: chamber_9x9\nseed: 7\nrandom: dk2\nduration_ticks: 10\n");
        var scenario = Scenario.Load(dk2);
        Assert.Throws<InvalidDataException>(() => scenario.OverrideSeed((ulong)uint.MaxValue + 1));
        Assert.Equal(7UL, scenario.Seed);
        scenario.OverrideSeed(uint.MaxValue);
        Assert.Equal((ulong)uint.MaxValue, scenario.Seed);

        var modern = Scenario.Load(_scenarioPath);
        modern.OverrideSeed(ulong.MaxValue);
        Assert.Equal(ulong.MaxValue, modern.Seed);
    }

    [Fact]
    public void WatchSeedsCarryAFuzzSpecAndRefuseFuzzSeedsAndSeedZero()
    {
        List<WatchJob> jobs = HarnessCommandLine.WatchJobs(["a.yaml"], ["watch", "dir", "--fuzz", "3", "--seeds", "1-2"])!;
        Assert.Equal(["--fuzz", "3", "--seed", "2"], jobs[1].Args.Skip(jobs[1].Args.Count - 4));
        Assert.Null(HarnessCommandLine.WatchJobs(["a.yaml"], ["watch", "dir", "--fuzz", "3", "--fuzz-seeds", "2", "--seeds", "1-2"]));
        Assert.Null(HarnessCommandLine.WatchJobs(["a.yaml"], ["watch", "dir", "--seeds", "0-3"]));
        Assert.Equal("seed 5", Assert.Single(HarnessCommandLine.WatchJobs(["a.yaml"], ["watch", "dir", "--seed", "5"])!).Label);
    }

    [Fact]
    public void AFailureReportReproducesOnTheJournalsSeed()
    {
        var scenario = Scenario.Load(_scenarioPath);
        scenario.OverrideSeed(12);
        var stream = new MemoryStream();
        using (var writer = new InputJournalWriter(stream, JournalHeader.For(scenario, [])))
        {
            writer.WriteEnd(3, "exception: probe");
        }

        stream.Position = 0;
        string report = Watchdog.FailureReport(_scenarioPath, "seed-probe", "failed", 1, TimeSpan.FromSeconds(60),
            "journal", InputJournal.Read(stream), "");
        Assert.Contains($"replay \"{_scenarioPath}\" \"journal\" --seed 12", report, StringComparison.Ordinal);
    }

    [Fact]
    public void ASchemaOneManifestStillVerifiesOnTheFilesOwnSeed()
    {
        string evidence = Path.Combine(_root, "evidence-v1");
        var scenario = Scenario.Load(_scenarioPath);
        PlayerRunManifest.Write(evidence, scenario, null, Run);
        string path = Path.Combine(evidence, PlayerRunManifest.FileName);
        // A schema-1 manifest, as stored evidence has it: no seed field.
        Dictionary<string, JsonElement> v1 = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(path))!;
        v1.Remove("Seed");
        v1["Schema"] = JsonSerializer.SerializeToElement(1);
        File.WriteAllText(path, JsonSerializer.Serialize(v1));

        Assert.True(PlayerRunManifest.Verify(evidence, scenario, null, Run).MayReplay);
        scenario.Seed = 12;
        Assert.Contains("seed differs: recorded 7, now 12", PlayerRunManifest.Verify(evidence, scenario, null, Run).Blocking);
    }
}
