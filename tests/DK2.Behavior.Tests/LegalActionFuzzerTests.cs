// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Harness;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

/// <summary>
/// The legal-action fuzzer and the run's observers (ADR-0552 Stage C). The fuzzer runs on Level 1's
/// shipped data: the synthetic database has no hand capacity and no terrain a sold room reverts
/// to, which the fuzzer reached within a few hundred ticks.
/// </summary>
public class LegalActionFuzzerTests
{
    private static readonly PlayerId Keeper = new(PlayerIds.FirstKeeper);

    [InstallFact]
    public void TheFuzzer_IssuesOnlyHumanCommandsForItsPlayer()
    {
        ScenarioResult result = Level1(Fuzzed(seed: 1));

        Assert.True(result.ControllerCommandLog.Select(e => e.Command.GetType()).Distinct().Count() >= 4,
            "the fuzzer should reach several kinds of command on Level 1");
        Assert.All(result.ControllerCommandLog, e =>
        {
            Assert.True(HumanCommandPolicy.AllowsInPlay(e.Command), e.Command.GetType().Name);
            Assert.Equal(Keeper, e.Command.PlayerId);
        });
    }

    [InstallFact]
    public void OneSeed_PlaysTheSameGame_AndAnotherDoesNot()
    {
        ScenarioResult first = Level1(Fuzzed(seed: 1));
        ScenarioResult again = Level1(Fuzzed(seed: 1));
        ScenarioResult other = Level1(Fuzzed(seed: 2));

        Assert.Equal(first.HashPerTick, again.HashPerTick);
        Assert.NotEqual(Commands(first), Commands(other));
    }

    [InstallFact]
    public void TheObservers_DoNotChangeTheGame()
    {
        ScenarioResult plain = Level1(Fuzzed(seed: 1));
        ScenarioResult watched = Level1(Fuzzed(seed: 1) with
        {
            Coverage = true, ProgressEvery = 40, SnapshotCheckEvery = 100, SnapshotCheckTicks = 40,
        });

        Assert.Equal(plain.HashPerTick, watched.HashPerTick);
        Assert.Equal(3, watched.SnapshotChecks); // ticks 100, 200, 300; none starts on the last
        Assert.Empty(watched.InvariantFailures);
        Assert.NotNull(watched.Coverage);
        Assert.True(watched.Coverage.TriggerRecordsReached > 0, "Level 1's triggers should be reached");
        Assert.NotEmpty(watched.Coverage.CreatureTicksByState);
        Assert.Contains(watched.Progress, p => p.Player == Keeper.Value);
    }

    [Fact]
    public void AnIdleController_IsAStallCandidate_NotAFailure()
    {
        var idle = new ScenarioController("idle", Keeper, _ => []);
        string path = Path.Combine(Path.GetTempPath(), $"dk2-scenario-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, "id: idle-probe\nmap: chamber_9x9\nseed: 5\nduration_ticks: 200\nsetup:\n  players:\n    - { id: 3, gold: 0, mana: 0 }\n");
        try
        {
            ScenarioResult result = new ScenarioRunner(ScenarioTests.SyntheticDatabase(), null).Run(
                Scenario.Load(path), new RunOptions { Controllers = [idle], ProgressEvery = 10, StallWindowTicks = 100 });

            string stall = Assert.Single(result.StallCandidates);
            Assert.Contains("controller idle", stall, StringComparison.Ordinal);
            Assert.Empty(result.InvariantFailures);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AFuzzerForTheHeroes_IsRefused()
    {
        Assert.Throws<ArgumentException>(() => LegalActionFuzzer.Create(new PlayerId(PlayerIds.Good), 1));
    }

    private static RunOptions Fuzzed(ulong seed) => new()
    {
        Controllers = [LegalActionFuzzer.Create(Keeper, seed, intervalTicks: 2)],
    };

    private static string[] Commands(ScenarioResult result) =>
        result.ControllerCommandLog.Select(e => $"{e.InputTick}:{e.Command}").ToArray();

    private static ScenarioResult Level1(RunOptions options)
    {
        string path = Path.Combine(Path.GetTempPath(), $"dk2-scenario-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, "id: fuzz-level1-probe\nmap: campaign:level1\nseed: 1\nduration_ticks: 400\nsetup:\n  placed_things: true\n");
        try
        {
            return new ScenarioRunner(ScenarioCorpus.Database, Dk2InstallGate.Path).Run(Scenario.Load(path), options);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
