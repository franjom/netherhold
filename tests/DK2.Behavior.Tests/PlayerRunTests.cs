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

/// <summary>The controlled player inside the scenario loop (campaign player plan, task 4).</summary>
public class PlayerRunTests
{
    private static readonly PlayerId Keeper = new(3);
    private static readonly TilePos Rock = new(6, 4);
    private static readonly PlayerRunId Run = new("player-run-test");

    /// <summary>A scripted controller: a fixed action list at each decision, a fixed advance.</summary>
    private sealed class Scripted(int advance, Func<PlayerObservation, IEnumerable<PlayerAction>>? act = null) : IPlayerController
    {
        public List<PlayerObservation> Seen { get; } = [];

        public PlayerDecision Decide(PlayerObservation observation)
        {
            Seen.Add(observation);
            return new PlayerDecision(act?.Invoke(observation) ?? [], advance, "scripted");
        }
    }

    private static string Yaml(int duration = 10, string? commands = null) => $"""
        id: player-run-probe
        map: chamber_9x9
        seed: 7
        duration_ticks: {duration}
        setup:
          players:
            - {"{"} id: 3, gold: 0, mana: 0 {"}"}
        {(commands is null ? "" : "commands:\n" + commands)}
        """;

    private static T WithScenario<T>(string yaml, Func<Scenario, T> use)
    {
        string path = Path.Combine(Path.GetTempPath(), $"dk2-player-run-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, yaml);
        try { return use(Scenario.Load(path)); }
        finally { File.Delete(path); }
    }

    /// <summary>The scenario database plus the two hand and drag variables shipped data always has.</summary>
    private static ScenarioRunner Runner()
    {
        GameDatabase database = ScenarioTests.SyntheticDatabase();
        database.Variables.Misc[VariableTypeId.MaxNumberOfThingsInHand] = new MiscVariable(VariableTypeId.MaxNumberOfThingsInHand, 8, 0, 0);
        database.Variables.Misc[VariableTypeId.RubberBandAreaLimit] = new MiscVariable(VariableTypeId.RubberBandAreaLimit, 5, 0, 0);
        return new ScenarioRunner(database, null);
    }

    private static ScenarioResult RunPlayer(string yaml, IPlayerController controller, Stream? journal = null,
        Func<PlayerRunOptions, PlayerRunOptions>? tune = null) =>
        WithScenario(yaml, scenario => Runner().Run(scenario, new RunOptions
        {
            Journal = journal,
            PlayerRun = (tune ?? (o => o))(new PlayerRunOptions(controller, Keeper, Run)),
        }));

    private static IEnumerable<PlayerAction> TagRockOnce(PlayerObservation o) =>
        o.Stamp.Revision.Value == 1 ? [new PlayerAction.TagArea(Rock, Rock)] : [];

    [Fact]
    public void AControlledActionIsTheSameInputAsTheScriptedCommandOneTickLater()
    {
        ScenarioResult controlled = RunPlayer(Yaml(), new Scripted(4, TagRockOnce));
        ScenarioResult scripted = WithScenario(Yaml(commands: "  - { at_tick: 1, player: 3, tag: [[6, 4]] }"), s => Runner().Run(s));

        Assert.Equal(scripted.HashPerTick, controlled.HashPerTick);
        ControllerCommandEmission emission = Assert.Single(controlled.ControllerCommandLog);
        Assert.Equal(1, emission.InputTick);
        PlayerDecisionRecord first = controlled.PlayerRun!.Decisions[0];
        Assert.Equal(new GameTick(1), Assert.Single(first.Receipts).ScheduledTick);
    }

    [Fact]
    public void DecisionsAfterTicksZeroFourEightObserveStampsOneFiveNine()
    {
        var controller = new Scripted(4);
        ScenarioResult result = RunPlayer(Yaml(), controller);

        Assert.Equal([1L, 5L, 9L], controller.Seen.Select(o => o.Stamp.Tick.Value));
        Assert.Equal([1L, 2L, 3L], controller.Seen.Select(o => o.Stamp.Revision.Value));
        Assert.Equal(10, result.TicksRun);
        Assert.Equal(PlayerRunStop.None, result.PlayerRun!.Stop);
    }

    [Fact]
    public void ObservingAndDecidingLeaveTheHashSeriesAsAnUncontrolledRunHasIt()
    {
        ScenarioResult observed = RunPlayer(Yaml(), new Scripted(1));
        ScenarioResult plain = WithScenario(Yaml(), s => Runner().Run(s));

        Assert.Equal(plain.HashPerTick, observed.HashPerTick);
        Assert.Equal(9, observed.PlayerRun!.Decisions.Count); // after ticks 0..8; none after the last
    }

    [Fact]
    public void TheMonitorOnOrOffLeavesTheHashSeriesAndItsRandomStateAlone()
    {
        // The state hash folds in the generator's state, so equal series mean equal RNG too.
        ScenarioResult Monitored(bool monitored) => WithScenario(Yaml(duration: 40), s => Runner().Run(s, new RunOptions
        {
            Coverage = monitored,
            ProgressEvery = monitored ? 4 : 0,
            PlayerRun = new PlayerRunOptions(new Scripted(4, TagRockOnce), Keeper, Run),
        }));

        ScenarioResult on = Monitored(true), off = Monitored(false);

        Assert.Equal(off.HashPerTick, on.HashPerTick);
        Assert.NotEmpty(on.Progress);
        Assert.Empty(off.Progress);
    }

    [Fact]
    public void APlayerOwnedByAKeeperOrAControllerIsRefused()
    {
        var other = new ScenarioController("other", Keeper, _ => []);
        Assert.Throws<ArgumentException>(() => WithScenario(Yaml(), s => Runner().Run(s, new RunOptions
        {
            Controllers = [other],
            PlayerRun = new PlayerRunOptions(new Scripted(1), Keeper, Run),
        })));
        Assert.Throws<ArgumentException>(() => WithScenario(Yaml(), s => Runner().Run(s, new RunOptions
        {
            PlayerRun = new PlayerRunOptions(new Scripted(1), new PlayerId(5), Run),
        })));
        var scripted = Assert.Throws<ArgumentException>(() => RunPlayer(Yaml(commands: "  - { at_tick: 1, player: 3, tag: [[6, 4]] }"), new Scripted(1)));
        Assert.Contains("one command source", scripted.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AControllerThatThrowsOrAnswersNothingIsAControllerLimitationNotAnEngineFailure()
    {
        using var journal = new MemoryStream();
        ScenarioResult threw = RunPlayer(Yaml(), new Scripted(1, _ => throw new FormatException("bad reply")), journal);
        Assert.Equal(PlayerRunStop.ControllerLimited, threw.PlayerRun!.Stop);
        Assert.Contains("FormatException", threw.PlayerRun.Limitation, StringComparison.Ordinal);
        Assert.StartsWith("player:controller-limited", InputJournal.Read(new MemoryStream(journal.ToArray())).EndReason, StringComparison.Ordinal);

        ScenarioResult silent = RunPlayer(Yaml(), new Silent());
        Assert.Equal(PlayerRunStop.ControllerLimited, silent.PlayerRun!.Stop);
    }

    [Fact]
    public void ActionsAreNumberedOneUpwardInDecisionOrderAcrossDecisions()
    {
        // Controllers read receipts by this numbering (IPlayerController's contract).
        var controller = new Scripted(1, o => o.Stamp.Revision.Value <= 2
            ? [new PlayerAction.TagArea(Rock, Rock), new PlayerAction.TagArea(new TilePos(5, 4), new TilePos(5, 4))] : []);
        RunPlayer(Yaml(), controller);
        Assert.Equal([1L, 2L, 3L, 4L], controller.Seen.SelectMany(o => o.Receipts)
            .Where(r => r.Status == PlayerReceiptStatus.Queued).Select(r => r.Id.Value));
    }

    [Fact]
    public void AStrategysOwnLimitationEndsTheRunAsOne()
    {
        ScenarioResult limited = RunPlayer(Yaml(), new GivesUp());
        Assert.Equal((PlayerRunStop.ControllerLimited, "stuck"), (limited.PlayerRun!.Stop, limited.PlayerRun.Limitation));
        Assert.Equal(1, limited.TicksRun);
    }

    private sealed class GivesUp : IPlayerController
    {
        public PlayerDecision Decide(PlayerObservation observation) => PlayerDecision.Limited("stuck");
    }

    private sealed class Silent : IPlayerController
    {
        public PlayerDecision Decide(PlayerObservation observation) => null!;
    }

    [Fact]
    public void ADecisionCostsAFewKilobytesAndTheCadenceIsTheAdvance()
    {
        // Cadence: 400 ticks at an advance of 4 is 100 decisions. Allocation: what the player adds
        // over the same run without it, per decision; Level 1 measured about 8 KB (2026-11-09).
        long Allocated(bool player, out ScenarioResult result)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            result = player ? RunPlayer(Yaml(duration: 400), new Scripted(4)) : WithScenario(Yaml(duration: 400), s => Runner().Run(s));
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
        Allocated(true, out _);
        long plain = Allocated(false, out _), withPlayer = Allocated(true, out ScenarioResult observed);

        Assert.Equal(100, observed.PlayerRun!.Decisions.Count);
        long perDecision = (withPlayer - plain) / 100;
        Assert.True(perDecision < 16 * 1024, $"{perDecision} bytes a decision");
    }

    [Theory]
    [InlineData(41)]
    [InlineData(100)]
    public void AnAdvanceBeyondTheLimitStopsTheRunAsAControllerLimitation(int advance)
    {
        using var journal = new MemoryStream();
        ScenarioResult result = RunPlayer(Yaml(), new Scripted(advance), journal);

        Assert.Equal(PlayerRunStop.ControllerLimited, result.PlayerRun!.Stop);
        Assert.Equal(1, result.TicksRun);
        Assert.Equal(2, result.HashPerTick.Count);
        InputJournal read = InputJournal.Read(new MemoryStream(journal.ToArray()));
        Assert.Equal(1, read.TicksRun);
        Assert.StartsWith("player:controller-limited", read.EndReason, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTickLimitBoundsTheRunAndIsNotCalledCompleted()
    {
        using var journal = new MemoryStream();
        ScenarioResult result = RunPlayer(Yaml(duration: 20), new Scripted(4), journal, o => o with { MaxTicks = 6 });

        Assert.Equal(6, result.TicksRun);
        InputJournal read = InputJournal.Read(new MemoryStream(journal.ToArray()));
        Assert.Equal("1", read.Header[JournalHeader.PlayerRunSchema]);
        Assert.Equal(6, read.TicksRun);
        Assert.NotEqual("completed", read.EndReason);
    }

    [Fact]
    public void APlayerRunJournalReplaysItsRecordedTicksWithoutTheController()
    {
        using var journal = new MemoryStream();
        ScenarioResult recorded = RunPlayer(Yaml(duration: 20), new Scripted(2, TagRockOnce), journal, o => o with { MaxTicks = 7 });
        InputJournal read = InputJournal.Read(new MemoryStream(journal.ToArray()));

        ScenarioResult replayed = WithScenario(Yaml(duration: 20), s =>
        {
            // The scenario file is rewritten per call, so pin the replay to this file's hash.
            var header = new Dictionary<string, string>(read.Header) { [JournalHeader.ScenarioSha256] = Sha(s.SourcePath) };
            return Runner().Replay(s, Rehead(read, header));
        });

        Assert.Equal(7, replayed.TicksRun);
        Assert.True(replayed.Replay!.Matches);
        Assert.Equal(recorded.HashPerTick, replayed.HashPerTick);
        Assert.Null(replayed.PlayerRun);
    }

    [Fact]
    public void ATruncatedPlayerRunJournalIsRefused()
    {
        using var journal = new MemoryStream();
        RunPlayer(Yaml(duration: 20), new Scripted(2), journal, o => o with { MaxTicks = 7 });
        byte[] bytes = journal.ToArray();
        InputJournal read = InputJournal.Read(new MemoryStream(bytes));
        InputJournal cut = InputJournal.Read(new MemoryStream(bytes[..^16]));
        Assert.True(cut.Truncated);

        WithScenario(Yaml(duration: 20), s =>
        {
            Assert.Equal(7, JournalHeader.ReplayTicks(s, read));
            Assert.Throws<InvalidOperationException>(() => JournalHeader.ReplayTicks(s, cut));
            return 0;
        });
        WithScenario(Yaml(duration: 5), s => Assert.Throws<InvalidOperationException>(() => JournalHeader.ReplayTicks(s, read)));

        // A run that threw replays the tick that threw as well, so the failure recurs.
        InputJournal crashed = Rehead(read, read.Header, ticksRun: 4, endReason: "exception after 4 ticks: System.Exception: boom");
        WithScenario(Yaml(duration: 20), s =>
        {
            Assert.Equal(5, JournalHeader.ReplayTicks(s, crashed));
            return 0;
        });
        WithScenario(Yaml(duration: 4), s =>
        {
            Assert.Equal(4, JournalHeader.ReplayTicks(s, crashed));
            return 0;
        });
    }

    // ---- the coordinator on its own, for results no synthetic scenario reaches ----

    private static (WorldState World, GameSimulation Simulation, PlayerRunCoordinator Coordinator, Scripted Controller) Coordinated(int finaleTicks = 5)
    {
        var (database, world) = PlayerObservationTests.World();
        var simulation = new GameSimulation(database, world, 1, []);
        var controller = new Scripted(1, o => [new PlayerAction.TagArea(new TilePos(0, 0), new TilePos(0, 0))]);
        var coordinator = new PlayerRunCoordinator(database,
            new PlayerRunOptions(controller, PlayerObservationTests.Viewer, PlayerObservationTests.Run) { FinaleTicks = finaleTicks });
        return (world, simulation, coordinator, controller);
    }

    /// <summary>Marks the viewer as having won on a level whose finale has a gem to fetch.</summary>
    private static void WinWithGem(WorldState world)
    {
        world.Player(PlayerObservationTests.Viewer).Status = PlayerStatus.Won;
        world.Player(PlayerObservationTests.Viewer).Finale.GemObjectId = 1;
    }

    private static IReadOnlyList<PlayerCommand> Step(GameSimulation simulation, PlayerRunCoordinator coordinator, IReadOnlyList<PlayerCommand> input)
    {
        long t = simulation.World.CurrentTick.Value;
        simulation.Tick(new InputFrame(input));
        return coordinator.AfterTick(simulation.World, new GameTick(t));
    }

    [Fact]
    public void AfterAWinNoDecisionIsAskedAndTheFinaleWindowEndsAtThePickup()
    {
        var (world, simulation, coordinator, controller) = Coordinated(finaleTicks: 10);
        IReadOnlyList<PlayerCommand> next = Step(simulation, coordinator, []);
        Assert.Single(next);
        WinWithGem(world);

        next = Step(simulation, coordinator, next);
        int asked = controller.Seen.Count;
        Assert.Empty(next);
        Assert.Equal(new GameTick(1), coordinator.ResultTick);
        Assert.False(coordinator.Stopped);
        Assert.True(coordinator.Session.IsCancelled);
        Step(simulation, coordinator, []);
        world.Player(PlayerObservationTests.Viewer).Finale.PickupTick = 3;
        Step(simulation, coordinator, []);

        Assert.Equal(asked, controller.Seen.Count);
        Assert.Equal((PlayerRunStop.Won, PlayerFinaleCoverage.ReachedPickup, (GameTick?)new GameTick(3)),
            (coordinator.Stop, coordinator.Finale, coordinator.FinalePickupTick));
        Assert.Equal("completed", coordinator.EndReason(4));
        Assert.Empty(Step(simulation, coordinator, []));
    }

    [Fact]
    public void AFinaleThatNeverReachesTheGemStopsAtTheWindowAndIsReportedAsSuch()
    {
        var (world, simulation, coordinator, _) = Coordinated(finaleTicks: 3);
        WinWithGem(world);
        for (int i = 0; i < 3; i++) Step(simulation, coordinator, []);
        Assert.False(coordinator.Stopped);
        Step(simulation, coordinator, []);

        Assert.Equal((PlayerRunStop.Won, PlayerFinaleCoverage.NotReached), (coordinator.Stop, coordinator.Finale));
    }

    [Fact]
    public void AFinaleWithNoGemIsNotApplicableAtOnceAndAZeroWindowSkipsIt()
    {
        // No gem: FinaleRules.Begin records none and sets no flag; the first post-win tick says so.
        var (world, simulation, coordinator, _) = Coordinated();
        world.Player(PlayerObservationTests.Viewer).Status = PlayerStatus.Won;
        Step(simulation, coordinator, []);
        Assert.Equal((PlayerRunStop.Won, PlayerFinaleCoverage.NotApplicable), (coordinator.Stop, coordinator.Finale));

        var (world2, simulation2, skipped, _) = Coordinated(finaleTicks: 0);
        WinWithGem(world2);
        Step(simulation2, skipped, []);
        Assert.Equal((PlayerRunStop.Won, PlayerFinaleCoverage.Unchecked), (skipped.Stop, skipped.Finale));
    }

    [Theory]
    [InlineData(PlayerStatus.Lost)]
    [InlineData(PlayerStatus.Conquered)]
    public void ADefeatStopsAtOnceWithoutAFinale(byte status)
    {
        var (world, simulation, coordinator, _) = Coordinated();
        world.Player(PlayerObservationTests.Viewer).Status = status;
        Step(simulation, coordinator, []);

        Assert.Equal((PlayerRunStop.Defeated, PlayerFinaleCoverage.Unchecked), (coordinator.Stop, coordinator.Finale));
        Assert.Equal("player:defeated", coordinator.EndReason(1));
    }

    [Fact]
    public void AWinOnTheLastTickOrACutWindowStillEndsWon()
    {
        var (world, simulation, coordinator, controller) = Coordinated();
        WinWithGem(world);
        simulation.Tick(InputFrame.Empty);
        Assert.Empty(coordinator.AfterTick(world, new GameTick(0), anotherTick: false));
        coordinator.Finish();
        Assert.Equal((PlayerRunStop.Won, PlayerFinaleCoverage.CutShort, "completed"),
            (coordinator.Stop, coordinator.Finale, coordinator.EndReason(1)));
        Assert.Empty(controller.Seen);

        var (world2, simulation2, cancelled, _) = Coordinated();
        WinWithGem(world2);
        Step(simulation2, cancelled, []);
        cancelled.Cancel();
        Assert.Equal((PlayerRunStop.Won, PlayerFinaleCoverage.CutShort), (cancelled.Stop, cancelled.Finale));

        var (_, simulation3, plain, _) = Coordinated();
        Step(simulation3, plain, []);
        plain.Cancel();
        Assert.Equal(PlayerRunStop.Cancelled, plain.Stop);
        Assert.Equal("player:cancelled", plain.EndReason(1));
    }

    [Fact]
    public void TheLastTickRecordsAResultButMakesNoDecision()
    {
        var (_, simulation, coordinator, controller) = Coordinated();
        simulation.Tick(InputFrame.Empty);
        Assert.Empty(coordinator.AfterTick(simulation.World, new GameTick(0), anotherTick: false));
        Assert.Empty(controller.Seen);
    }

    [Fact]
    public void TheManifestVerifiesItsRunAndNamesEveryDifference()
    {
        // A repository-shaped root, so the source tree is hashed: DK2.slnx and src/ beside the scenario.
        string root = Path.Combine(Path.GetTempPath(), $"dk2-manifest-{Guid.NewGuid():N}");
        string install = Path.Combine(root, "install"), evidence = Path.Combine(root, "evidence");
        string editor = Path.Combine(install, "Data", "editor");
        Directory.CreateDirectory(editor);
        Directory.CreateDirectory(Path.Combine(root, "src"));
        File.WriteAllText(Path.Combine(root, "DK2.slnx"), "<Solution />");
        File.WriteAllText(Path.Combine(root, "src", "A.cs"), "class A {}");
        File.WriteAllText(Path.Combine(editor, "Terrain.kwd"), "terrain");
        File.WriteAllText(Path.Combine(editor, "Level1.KLD"), "level");
        File.WriteAllText(Path.Combine(install, "Data", "Paths.WAD"), "paths");
        string scenarioPath = Path.Combine(root, "probe.yaml");
        File.WriteAllText(scenarioPath, Yaml());
        var other = new PlayerRunId("another-run");
        try
        {
            Scenario scenario = Scenario.Load(scenarioPath);
            Assert.StartsWith("incomplete evidence", Assert.Single(PlayerRunManifest.Verify(evidence, scenario, install, Run).Blocking), StringComparison.Ordinal);

            PlayerRunManifest.Write(evidence, scenario, install, Run);
            PlayerRunManifest.Verification clean = PlayerRunManifest.Verify(evidence, scenario, install, Run);
            Assert.True(clean.MayReplay);
            Assert.Empty(clean.Diagnostic);
            Assert.Throws<IOException>(() => PlayerRunManifest.Write(evidence, scenario, install, Run));
            PlayerRunManifest.Identity identity = PlayerRunManifest.Compute(scenario, install, Run);
            Assert.Equal(3, identity.DataFiles);                          // two KWD/KLD, any case, and Paths.WAD
            Assert.Equal(2, identity.SourceFiles);                        // src/A.cs and DK2.slnx
            Assert.Superset(new HashSet<string> { "DK2.Harness", "DK2.Simulation", "DK2.Presentation", "DK2.AI" },
                new HashSet<string>(identity.Assemblies.Keys));
            Assert.DoesNotContain(identity.Assemblies.Keys, k => k.EndsWith(".Tests", StringComparison.Ordinal));
            Assert.Contains("the manifest is run 'player-run-test', not 'another-run'", PlayerRunManifest.Verify(evidence, scenario, install, other).Blocking);

            // A source-only change is diagnostic: the replay may proceed.
            File.AppendAllText(Path.Combine(root, "src", "A.cs"), "// edited");
            PlayerRunManifest.Verification sourceOnly = PlayerRunManifest.Verify(evidence, scenario, install, Run);
            Assert.True(sourceOnly.MayReplay);
            Assert.Single(sourceOnly.Diagnostic);

            File.AppendAllText(Path.Combine(install, "Data", "Paths.WAD"), "!");
            File.AppendAllText(scenarioPath, "# changed" + Environment.NewLine);
            PlayerRunManifest.Verification changed = PlayerRunManifest.Verify(evidence, Scenario.Load(scenarioPath), install, Run);
            Assert.False(changed.MayReplay);
            Assert.Contains(changed.Blocking, p => p.StartsWith("scenario differs", StringComparison.Ordinal));
            Assert.Contains(changed.Blocking, p => p.StartsWith("installed data differs", StringComparison.Ordinal));

            string manifest = Path.Combine(evidence, PlayerRunManifest.FileName);
            File.WriteAllText(manifest, File.ReadAllText(manifest).Replace(identity.Assemblies["DK2.Harness"], new string('0', 64), StringComparison.Ordinal));
            Assert.Contains("assembly DK2.Harness differs from the recorded run", PlayerRunManifest.Verify(evidence, Scenario.Load(scenarioPath), install, Run).Blocking);
            File.WriteAllText(manifest, "{ not json");
            Assert.StartsWith("incomplete evidence", Assert.Single(PlayerRunManifest.Verify(evidence, scenario, install, Run).Blocking), StringComparison.Ordinal);
            Assert.Equal(("none", 0), (PlayerRunManifest.Compute(scenario, null, Run).DataSha256, PlayerRunManifest.Compute(scenario, null, Run).DataFiles));
            Assert.Throws<DirectoryNotFoundException>(() => PlayerRunManifest.Compute(scenario, Path.Combine(root, "nowhere"), Run));

            // A manifest naming no harness or simulation assembly identifies no build.
            string bare = JsonSerializer.Serialize(identity with { Assemblies = new Dictionary<string, string>() });
            File.WriteAllText(manifest, bare);
            Assert.Contains("incomplete evidence: the manifest names no DK2.Simulation", PlayerRunManifest.Verify(evidence, scenario, install, Run).Blocking);
            Assert.StartsWith("incomplete evidence", Assert.Single(PlayerRunManifest.Verify(evidence, scenario, Path.Combine(root, "nowhere"), Run).Blocking), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string Sha(string path) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    /// <summary>The same journal under another header: the test scenario file is rewritten per
    /// call, so its recorded hash names a deleted file.</summary>
    private static InputJournal Rehead(InputJournal journal, IReadOnlyDictionary<string, string> header,
        long? ticksRun = null, string? endReason = null)
    {
        using var stream = new MemoryStream();
        using (var writer = new InputJournalWriter(stream, header))
        {
            foreach (long tick in journal.FrameTicks) writer.WriteFrame(tick, journal.FrameAt(tick));
            foreach (KeyValuePair<long, ulong> hash in journal.Hashes.OrderBy(h => h.Key)) writer.WriteHash(hash.Key, hash.Value);
            writer.WriteEnd(ticksRun ?? journal.TicksRun ?? -1, endReason ?? journal.EndReason ?? "");
        }
        return InputJournal.Read(new MemoryStream(stream.ToArray()));
    }
}
