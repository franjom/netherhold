// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Harness;
using DK2.Simulation;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Behavior.Tests;

/// <summary>
/// The run monitor's verdicts can fail as well as pass (ADR-0552 Stage C), and the command line
/// plans watch jobs as documented. Synthetic database, no installation needed.
/// </summary>
public class RunMonitorTests
{
    private static readonly PlayerId Keeper = new(PlayerIds.FirstKeeper);

    [Fact]
    public void ASnapshotCopyFedOtherInput_IsAnInvariantFailure()
    {
        // The negative control: the shadow is told the live tick had no command when it had one.
        GameDatabase database = ScenarioTests.SyntheticDatabase();
        LevelMap map = new ScenarioWorldBuilder(database, null).ResolveMap("rock_9x9");
        var live = new GameSimulation(database, WorldState.FromMap(database, map, [Keeper.Value]), 1, SimulationSystems.Default());
        var monitor = new RunMonitor(new RunOptions { SnapshotCheckEvery = 1, SnapshotCheckTicks = 5 },
            database, () => null, [], durationTicks: 10);

        live.Tick(InputFrame.Empty);
        monitor.AfterTick(live, 0, InputFrame.Empty, live.StateHash());
        live.Tick(InputFrame.Of(TagTilesCommand.Tag(Keeper, new TilePos(4, 4))));
        monitor.AfterTick(live, 1, InputFrame.Empty, live.StateHash());

        // The failed check ends; the next tick starts another.
        Assert.Equal(2, monitor.SnapshotChecks);
        string failure = Assert.Single(monitor.InvariantFailures);
        Assert.Contains("diverged from the live run after tick 2", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void NoCheckStarts_OnTheLastTick()
    {
        GameDatabase database = ScenarioTests.SyntheticDatabase();
        LevelMap map = new ScenarioWorldBuilder(database, null).ResolveMap("rock_9x9");
        var live = new GameSimulation(database, WorldState.FromMap(database, map, [Keeper.Value]), 1, SimulationSystems.Default());
        var monitor = new RunMonitor(new RunOptions { SnapshotCheckEvery = 1 }, database, () => null, [], durationTicks: 1);

        live.Tick(InputFrame.Empty);
        monitor.AfterTick(live, 0, InputFrame.Empty, live.StateHash());

        Assert.Equal(0, monitor.SnapshotChecks);
    }

    [Fact]
    public void CommandsWithoutProgress_AreAStallCandidate()
    {
        // Untagging an untagged tile every decision: commands issued, nothing moves.
        var futile = new ScenarioController("futile", Keeper,
            _ => [TagTilesCommand.Rect(Keeper, 4, 4, 4, 4, tagged: false)]) { IntervalTicks = 10 };
        string path = Path.Combine(Path.GetTempPath(), $"dk2-scenario-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, "id: futile-probe\nmap: chamber_9x9\nseed: 5\nduration_ticks: 200\nsetup:\n  players:\n    - { id: 3, gold: 0, mana: 0 }\n");
        try
        {
            ScenarioResult result = new ScenarioRunner(ScenarioTests.SyntheticDatabase(), null).Run(
                Scenario.Load(path), new RunOptions { Controllers = [futile], ProgressEvery = 10, StallWindowTicks = 100 });

            string stall = Assert.Single(result.StallCandidates);
            Assert.Contains("commands issued", stall, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(new[] { "watch", "x", "--fuzz", "3:7" }, new[] { "--fuzz 3:7" })]
    [InlineData(new[] { "watch", "x", "--fuzz", "3" }, new[] { "--fuzz 3" })]
    [InlineData(new[] { "watch", "x", "--fuzz", "3:7:2", "--fuzz-seeds", "2" }, new[] { "--fuzz 3:1:2", "--fuzz 3:2:2" })]
    [InlineData(new[] { "watch", "x", "--coverage", "--snapshot-check", "100" }, new[] { "--coverage --snapshot-check 100" })]
    public void WatchJobs_PassTheFuzzSeedThroughUnlessSeedsAreAsked(string[] args, string[] expected)
    {
        List<WatchJob> jobs = HarnessCommandLine.WatchJobs(["a.yaml"], args)!;

        Assert.Equal(expected, jobs.Select(j => string.Join(' ', j.Args)));
    }

    [Theory]
    [InlineData("--fuzz-seeds", "3")]
    [InlineData("--fuzz", "x:1")]
    [InlineData("--fuzz", "3:1:0")]
    [InlineData("--snapshot-check", "0")]
    public void MalformedOptions_AreRefused(string flag, string value)
    {
        Assert.Null(HarnessCommandLine.WatchJobs(["a.yaml"], ["watch", "x", flag, value]));
    }
}
