// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Harness;
using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.Behavior.Tests;

/// <summary>
/// Journalled runs and their replay (ADR-0552 Stage B): a run recorded with a controller replays
/// without it, hash for hash; a changed hash, scenario file or build is caught; a failing run
/// leaves a journal that says how it ended. Synthetic database, no installation needed.
/// </summary>
public class JournalReplayTests
{
    private static readonly PlayerId Keeper = new(3);
    private const string Scripted = "  - { at_tick: 2, player: 3, tag: [[6, 5]] }";

    [Fact]
    public void AControlledRun_ReplaysWithoutItsController()
    {
        using var scenario = new TempScenario(Yaml(Scripted));
        var stream = new MemoryStream();
        ScenarioResult recorded = Runner().Run(scenario.Load(), [TagOnce()], stream);

        InputJournal journal = Read(stream);
        ScenarioResult replayed = Runner().Replay(scenario.Load(), journal);

        Assert.Equal(recorded.HashPerTick, replayed.HashPerTick);
        Assert.NotNull(replayed.Replay);
        Assert.True(replayed.Replay.Matches);
        Assert.Equal(11, replayed.Replay.ComparedTicks);
        Assert.Equal("completed", journal.EndReason);
        Assert.Equal([1L, 2L], journal.FrameTicks);
        Assert.Empty(replayed.ControllerCommandLog);
    }

    [Fact]
    public void AChangedHash_IsReportedAtItsTick()
    {
        using var scenario = new TempScenario(Yaml(Scripted));
        var stream = new MemoryStream();
        Runner().Run(scenario.Load(), [TagOnce()], stream);
        InputJournal original = Read(stream);

        var tampered = new MemoryStream();
        using (var writer = new InputJournalWriter(tampered, original.Header))
        {
            foreach (long tick in original.FrameTicks) writer.WriteFrame(tick, original.FrameAt(tick));
            foreach ((long tick, ulong hash) in original.Hashes)
                writer.WriteHash(tick, tick == 5 ? hash ^ 1 : hash);
            writer.WriteEnd(original.TicksRun!.Value, "completed");
        }

        ReplayReport report = Runner().Replay(scenario.Load(), Read(tampered)).Replay!;

        Assert.False(report.Matches);
        Assert.Equal(5, report.FirstDivergentTick);
        Assert.Equal(report.Replayed ^ 1, report.Recorded);
    }

    [Fact]
    public void AnEditedScenarioFile_IsRefused()
    {
        using var scenario = new TempScenario(Yaml(Scripted));
        var stream = new MemoryStream();
        Runner().Run(scenario.Load(), [], stream);
        File.AppendAllText(scenario.Path, "\n# edited\n");

        var thrown = Assert.Throws<InvalidOperationException>(() => Runner().Replay(scenario.Load(), Read(stream)));
        Assert.Contains("SHA-256", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFailingRun_EndsItsJournalWithTheFailure()
    {
        using var scenario = new TempScenario(Yaml());
        var stream = new MemoryStream();
        var failing = new ScenarioController("failing", Keeper,
            view => view.Tick.Value == 4 ? throw new InvalidOperationException("boom") : []);

        Assert.Throws<InvalidOperationException>(() => Runner().Run(scenario.Load(), [failing], stream));

        InputJournal journal = Read(stream);
        Assert.Equal(4, journal.TicksRun);
        Assert.Contains("boom", journal.EndReason, StringComparison.Ordinal);
        Assert.Equal(4, journal.LastHashedTick);
    }

    [Fact]
    public void TheHeader_NamesTheScenarioAndTheBuild()
    {
        using var scenario = new TempScenario(Yaml());
        var stream = new MemoryStream();
        Runner().Run(scenario.Load(), [TagOnce()], stream);

        InputJournal journal = Read(stream);
        Assert.Equal(GameSimulation.CurrentHashVersion, journal.HashVersion);
        Assert.Equal("journal-probe", journal.Header["scenario.id"]);
        Assert.Equal("tag-once:3:1", journal.Header["controllers"]);
        Assert.Equal(64, journal.Header[JournalHeader.ScenarioSha256].Length);
        Assert.Contains(journal.Header[JournalHeader.SourceDirty], new[] { "true", "false", "unknown" });
    }

    private static ScenarioRunner Runner() => new(ScenarioTests.SyntheticDatabase(), null);

    private static InputJournal Read(MemoryStream stream) => InputJournal.Read(new MemoryStream(stream.ToArray()));

    private static ScenarioController TagOnce()
    {
        bool done = false;
        return new ScenarioController("tag-once", Keeper, view =>
        {
            if (done) return [];
            done = true;
            return [TagTilesCommand.Tag(view.PlayerId, new TilePos(6, 4))];
        });
    }

    private static string Yaml(string? commands = null) => $"""
        id: journal-probe
        map: chamber_9x9
        seed: 7
        duration_ticks: 10
        setup:
          players:
            - {"{"} id: 3, gold: 0, mana: 0 {"}"}
        {(commands is null ? "" : "commands:\n" + commands)}
        """;

    private sealed class TempScenario : IDisposable
    {
        public TempScenario(string yaml)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"dk2-scenario-{Guid.NewGuid():N}.yaml");
            File.WriteAllText(Path, yaml);
        }

        public string Path { get; }

        public Scenario Load() => Scenario.Load(Path);

        public void Dispose() => File.Delete(Path);
    }
}
