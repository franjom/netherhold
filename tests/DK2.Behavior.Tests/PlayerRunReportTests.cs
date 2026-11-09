// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Text.Json;
using DK2.AI.Players;
using DK2.Core;
using DK2.Data.Model;
using DK2.Harness;
using DK2.Simulation;
using DK2.Simulation.Players;

namespace DK2.Behavior.Tests;

/// <summary>The player run's command line, verdicts and evidence (campaign player plan, task 6).</summary>
public class PlayerRunReportTests
{
    private sealed class Waits : IPlayerController
    {
        public PlayerDecision Decide(PlayerObservation observation) => PlayerDecision.Wait(4, "wait");
    }

    private sealed class GivesUp : IPlayerController
    {
        public PlayerDecision Decide(PlayerObservation observation) => PlayerDecision.Limited("stuck");
    }

    /// <summary>A coordinator after one tick of a world in the given state.</summary>
    private static PlayerRunCoordinator After(byte status = PlayerStatus.Playing, IPlayerController? controller = null, Func<string?>? stop = null)
    {
        var (database, world) = PlayerObservationTests.World();
        var simulation = new GameSimulation(database, world, 1, []);
        var coordinator = new PlayerRunCoordinator(database,
            new PlayerRunOptions(controller ?? new Waits(), PlayerObservationTests.Viewer, PlayerObservationTests.Run) { FinaleTicks = 0, StopRequested = stop });
        world.Player(PlayerObservationTests.Viewer).Status = status;
        simulation.Tick(InputFrame.Empty);
        coordinator.AfterTick(world, GameTick.Zero);
        coordinator.Finish();
        return coordinator;
    }

    [Fact]
    public void EachEndHasItsOwnOutcomeAndOnlyAWinExitsZero()
    {
        var outcomes = new[]
        {
            PlayerRunReport.Outcome(After(PlayerStatus.Won), evidenceComplete: true),
            PlayerRunReport.Outcome(After(PlayerStatus.Lost), evidenceComplete: true),
            PlayerRunReport.Outcome(After(controller: new GivesUp()), evidenceComplete: true),
            PlayerRunReport.Outcome(After(), evidenceComplete: true),                       // ran out of ticks: no result
            PlayerRunReport.Outcome(After(stop: () => "wall-time limit"), evidenceComplete: true),
            PlayerRunReport.Outcome(After(PlayerStatus.Won), evidenceComplete: false),
        };
        Assert.Equal([PlayerRunOutcome.Completed, PlayerRunOutcome.Defeated, PlayerRunOutcome.ControllerLimited,
            PlayerRunOutcome.ControllerLimited, PlayerRunOutcome.IncompleteEvidence, PlayerRunOutcome.IncompleteEvidence], outcomes);
        Assert.Equal([0, 1, 1, 1, 2, 2], outcomes.Select(PlayerRunReport.ExitCode));
        Assert.Equal(1, PlayerRunReport.ExitCode(PlayerRunOutcome.TechnicalFailure));
        Assert.Equal("wall-time limit", After(stop: () => "wall-time limit").Limitation);
    }

    [Theory]
    [InlineData("--player-interval-ticks", "41")]
    [InlineData("--player-interval-ticks", "0")]
    [InlineData("--player-max-ticks", "-1")]
    [InlineData("--player-max-ticks", "99999999999")]
    [InlineData("--player-finale-ticks", "-1")]
    [InlineData("--player-output-mib", "1")]
    [InlineData("--player-retries", "x")]
    public void OutOfRangePlayerLimitsAreRefused(string flag, string value)
    {
        Assert.Null(HarnessCommandLine.RunOptions(["run", "x", "--campaign-player", "3", flag, value]));
    }

    [Fact]
    public void ThePlayerOptionsParseAndRefuseConflicts()
    {
        RunTemplate template = HarnessCommandLine.RunOptions(["run", "x", "--campaign-player", "3", "--player-finale-ticks", "0",
            "--player-interval-ticks", "40", "--player-output-mib", "2"])!;
        Assert.Equal(new CampaignPlayerSpec(new PlayerId(3), IntervalTicks: 40, FinaleTicks: 0, OutputMiB: 2), template.Player);
        Assert.Equal(new CampaignPlayerSpec(new PlayerId(3)), HarnessCommandLine.RunOptions(["run", "x", "--campaign-player", "3"])!.Player);
        Assert.Null(HarnessCommandLine.RunOptions(["run", "x", "--campaign-player", "3", "--fuzz", "3"]));
        Assert.Null(HarnessCommandLine.RunOptions(["run", "x", "--campaign-player", "1"]));        // heroes are no keeper
        Assert.Null(HarnessCommandLine.RunOptions(["run", "x", "--player-max-ticks", "10"]));      // a limit with no player
        Assert.NotNull(HarnessCommandLine.RunOptions(["run", "x", "--campaign-player", "3", "--fuzz", "4"]));
    }

    [Fact]
    public void WatchPassesThePlayerOptionsToEachRun()
    {
        List<WatchJob> jobs = HarnessCommandLine.WatchJobs(["a.yaml", "b.yaml"],
            ["watch", "x", "--campaign-player", "3", "--player-max-ticks", "100", "--player-finale-ticks", "0"])!;
        // Under the default 600 s timeout the wall limit defaults to 570 s, so the run stops itself first.
        Assert.All(jobs, job => Assert.Equal(["--campaign-player", "3", "--player-max-ticks", "100", "--player-finale-ticks", "0",
            "--player-wall-seconds", "570"], job.Args));
        Assert.Null(HarnessCommandLine.WatchJobs(["a.yaml"], ["watch", "x", "--campaign-player", "3", "--player-interval-ticks", "99"]));
        Assert.Null(HarnessCommandLine.WatchJobs(["a.yaml"], ["watch", "x", "--campaign-player", "3", "--player-run-id", "one"]));
        Assert.Null(HarnessCommandLine.WatchJobs(["a.yaml"], ["watch", "x", "--campaign-player", "3", "--player-wall-seconds", "600"], 600));
        Assert.NotNull(HarnessCommandLine.WatchJobs(["a.yaml"], ["watch", "x", "--campaign-player", "3", "--player-wall-seconds", "599"], 600));
    }

    [Fact]
    public void AMisspeltPlayerLimitIsRefusedNotIgnored()
    {
        Assert.Null(HarnessCommandLine.RunOptions(["run", "x", "--campaign-player", "3", "--player-max-tick", "5"]));
        Assert.NotNull(HarnessCommandLine.RunOptions(["run", "x", "--campaign-player", "3", "--player-run-id", "one"]));
    }

    [Fact]
    public void WatchFindsAPlayerRunsJournalOneFolderDown()
    {
        string root = Path.Combine(Path.GetTempPath(), $"dk2-player-watch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string scenario = Path.Combine(root, "probe.yaml");
        File.WriteAllText(scenario, Yaml(4));
        try
        {
            var watchdog = new Watchdog(Path.Combine(root, "out"), WatchLimits.Default, (job, runOut) =>
            {
                string folder = Path.Combine(runOut, "player-report-probe", "a-run");
                Directory.CreateDirectory(folder);
                using (var stream = File.Create(Path.Combine(folder, "input.journal")))
                using (var writer = new InputJournalWriter(stream, new Dictionary<string, string>()))
                {
                    for (long tick = 0; tick <= 3; tick++) writer.WriteHash(tick, 1);
                    writer.WriteEnd(3, "completed");
                }
                return new System.Diagnostics.ProcessStartInfo("dotnet", "--version");
            });
            WatchSummary summary = watchdog.Watch([new WatchJob(scenario, [], "")], TextWriter.Null);
            string row = File.ReadAllLines(Path.Combine(summary.Root, "watch-summary.tsv"))[1];
            Assert.True(row.EndsWith("\t3\tcompleted", StringComparison.Ordinal), row);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TheBudgetedStreamStopsBeforeItsReserveAndRefusesToPassItsBudget()
    {
        using var stream = new BudgetedStream(new MemoryStream(), budgetBytes: 100, reserveBytes: 10);
        stream.Write(new byte[89]);
        Assert.False(stream.Exhausted);
        stream.Write(new byte[1]);
        Assert.True(stream.Exhausted);
        stream.Write(new byte[10]);                                     // the reserve is for the final records
        Assert.Throws<EvidenceWriteException>(() => stream.Write(new byte[1]));
        Assert.Equal(100, stream.Written);
    }

    private static string Yaml(int duration) => $"""
        id: player-report-probe
        map: chamber_9x9
        seed: 7
        duration_ticks: {duration}
        setup:
          players:
            - {"{"} id: 3, gold: 0, mana: 0 {"}"}
        """;

    private static GameDatabase Database()
    {
        GameDatabase database = ScenarioTests.SyntheticDatabase();
        database.Variables.Misc[VariableTypeId.MaxNumberOfThingsInHand] = new MiscVariable(VariableTypeId.MaxNumberOfThingsInHand, 8, 0, 0);
        database.Variables.Misc[VariableTypeId.RubberBandAreaLimit] = new MiscVariable(VariableTypeId.RubberBandAreaLimit, 5, 0, 0);
        return database;
    }

    private static byte[] LegacyJournal()
    {
        using var stream = new MemoryStream();
        using (var writer = new InputJournalWriter(stream, new Dictionary<string, string>())) writer.WriteEnd(0, "completed");
        return stream.ToArray();
    }

    [Fact]
    public void ARunWritesItsEvidenceAndNeverOverwritesIt()
    {
        string root = Path.Combine(Path.GetTempPath(), $"dk2-player-report-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string scenarioPath = Path.Combine(root, "probe.yaml");
        File.WriteAllText(scenarioPath, Yaml(40));
        try
        {
            GameDatabase database = Database();
            var runner = new ScenarioRunner(database, null);
            Scenario scenario = Scenario.Load(scenarioPath);
            RunTemplate template = HarnessCommandLine.RunOptions(["run", "x", "--campaign-player", "3", "--player-finale-ticks", "0"])!;
            var output = new StringWriter();
            var error = new StringWriter();

            // No result in 40 ticks is a non-completed attempt: exit 1, never 0.
            Assert.Equal(1, PlayerRunCommand.Run(runner, database, null, scenario, template, Path.Combine(root, "out"), "first", output, error));
            string folder = Path.Combine(root, "out", "player-report-probe", "first");
            foreach (string file in new[] { "input.journal", PlayerRunManifest.FileName, PlayerRunReport.RunFile, PlayerRunReport.DecisionsFile, PlayerRunReport.ActionsFile })
                Assert.True(File.Exists(Path.Combine(folder, file)), file);
            using (JsonDocument report = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, PlayerRunReport.RunFile))))
            {
                Assert.Equal("ControllerLimited", report.RootElement.GetProperty("outcome").GetString());
                Assert.Equal("NoResult", report.RootElement.GetProperty("stop").GetString());
                Assert.Equal(PlayerRunReport.AcceptanceNotClaimed, report.RootElement.GetProperty("acceptance").GetString());
                Assert.Equal(600, report.RootElement.GetProperty("limits").GetProperty("wall_seconds").GetInt32());
                Assert.Equal("no result within 40 ticks", report.RootElement.GetProperty("note").GetString());
                Assert.Equal(40, report.RootElement.GetProperty("ticks_run").GetInt32());
                Assert.NotEmpty(report.RootElement.GetProperty("untested").EnumerateArray());
            }
            Assert.Equal(11, File.ReadAllLines(Path.Combine(folder, PlayerRunReport.DecisionsFile)).Length);   // header + decisions after 0,4..36

            // The same run id again: refused, and the first evidence is untouched.
            DateTime written = File.GetLastWriteTimeUtc(Path.Combine(folder, "input.journal"));
            Assert.Equal(2, PlayerRunCommand.Run(runner, database, null, scenario, template, Path.Combine(root, "out"), "first", output, error));
            Assert.Equal(written, File.GetLastWriteTimeUtc(Path.Combine(folder, "input.journal")));
            Assert.Contains("never overwritten", error.ToString(), StringComparison.Ordinal);

            // A failing host leaves a readable technical failure: journal, manifest and report.
            Assert.Equal(1, PlayerRunCommand.Run(runner, database, null, scenario, template, Path.Combine(root, "out"), "broken", output, error,
                o => o with { StopRequested = () => throw new InvalidOperationException("host broke") }));
            string broken = Path.Combine(root, "out", "player-report-probe", "broken");
            Assert.True(File.Exists(Path.Combine(broken, "input.journal")));
            Assert.True(File.Exists(Path.Combine(broken, PlayerRunManifest.FileName)));
            using (JsonDocument failure = JsonDocument.Parse(File.ReadAllText(Path.Combine(broken, PlayerRunReport.RunFile))))
                Assert.Equal("TechnicalFailure", failure.RootElement.GetProperty("outcome").GetString());

            // A refusal before any tick exits 2 with a Refused verdict, not a technical failure.
            RunTemplate absent = HarnessCommandLine.RunOptions(["run", "x", "--campaign-player", "5"])!;
            Assert.Equal(2, PlayerRunCommand.Run(runner, database, null, scenario, absent, Path.Combine(root, "out"), "absent", output, error));
            using (JsonDocument refused = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "out", "player-report-probe", "absent", PlayerRunReport.RunFile))))
                Assert.Equal("Refused", refused.RootElement.GetProperty("outcome").GetString());

            // The output budget and the wall clock stop a real run, as incomplete evidence (exit 2).
            var tight = new RunTemplate(null, false, 0, 0) { Player = new CampaignPlayerSpec(new PlayerId(3), FinaleTicks: 0, OutputMiB: 1) };
            Assert.Equal(2, PlayerRunCommand.Run(runner, database, null, scenario, tight, Path.Combine(root, "out"), "budget", output, error));
            Assert.Contains("output budget", File.ReadAllText(Path.Combine(root, "out", "player-report-probe", "budget", PlayerRunReport.RunFile)), StringComparison.Ordinal);
            var hasty = new RunTemplate(null, false, 0, 0) { Player = new CampaignPlayerSpec(new PlayerId(3), FinaleTicks: 0, WallSeconds: 0) };
            Assert.Equal(2, PlayerRunCommand.Run(runner, database, null, scenario, hasty, Path.Combine(root, "out"), "wall", output, error));
            Assert.Contains("wall-time limit", File.ReadAllText(Path.Combine(root, "out", "player-report-probe", "wall", PlayerRunReport.RunFile)), StringComparison.Ordinal);

            // An output root that is a file cannot hold evidence: incomplete, exit 2.
            string blocked = Path.Combine(root, "blocked");
            File.WriteAllText(blocked, "a file, not a folder");
            Assert.Equal(2, PlayerRunCommand.Run(runner, database, null, scenario, template, blocked, "second", output, error));

            // Its journal replays only while its manifest still matches.
            InputJournal journal;
            using (FileStream read = File.OpenRead(Path.Combine(folder, "input.journal"))) journal = InputJournal.Read(read);
            string journalPath = Path.Combine(folder, "input.journal");
            Assert.Null(PlayerRunCommand.VerifyBeforeReplay(journalPath, scenario, null, journal, output, error));
            // A legacy journal needs no manifest; a player journal without one is refused.
            InputJournal legacy = InputJournal.Read(new MemoryStream(LegacyJournal()));
            Assert.Null(PlayerRunCommand.VerifyBeforeReplay(Path.Combine(root, "elsewhere", "input.journal"), scenario, null, legacy, output, error));
            Assert.Equal(2, PlayerRunCommand.VerifyBeforeReplay(Path.Combine(root, "elsewhere", "input.journal"), scenario, null, journal, output, error));
            File.AppendAllText(scenarioPath, "# edited" + Environment.NewLine);
            Assert.Equal(2, PlayerRunCommand.VerifyBeforeReplay(journalPath, Scenario.Load(scenarioPath), null, journal, output, error));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
