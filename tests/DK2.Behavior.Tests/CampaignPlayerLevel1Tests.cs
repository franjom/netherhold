// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Security.Cryptography;
using System.Text.Json;
using DK2.Data.Model;
using DK2.Harness;
using DK2.Simulation;
using DK2.Simulation.Players;
using DK2.TestSupport;
using Xunit.Abstractions;

namespace DK2.Behavior.Tests;

/// <summary>
/// The campaign player on shipped Level 1 (campaign player plan, task 7): seed 1 from the unmodified
/// level start, the controller built exactly as <c>run --campaign-player 3</c> builds it, the win read
/// from the simulation's own player status and the Horned Reaper finale
/// (campaign-win-sequence.md §2-§3) followed to the Portal Gem pickup. The tick numbers are this
/// remake's output for this controller, not DK2 facts, so the tests assert relations between them.
/// </summary>
public class CampaignPlayerLevel1Tests(ITestOutputHelper output)
{
    private const string ScenarioFile = "campaign-player/level1.yaml";
    private static readonly PlayerId Keeper = new(3);

    /// <summary>One seed-1 attempt shared by every test here: a full win takes seconds, not milliseconds.</summary>
    private static readonly Lazy<Attempt> Seed1 = new(() => Attempt.Play(ScenarioCorpus.Load(ScenarioFile)));

    private sealed record Attempt(
        ScenarioResult Result, PlayerRunCoordinator Player, InputJournal Journal,
        long? NativeWinTick, IReadOnlyList<PlayerId> OthersWon, int GemAtWin, (int X, int Y) GemTileAtWin, (int X, int Y)? GemPointAtWin)
    {
        public static Attempt Play(Scenario scenario)
        {
            long? won = null;
            int gem = 0;
            (int X, int Y) gemTile = default;
            (int X, int Y)? gemPoint = null;
            var othersWon = new List<PlayerId>();
            var spec = new CampaignPlayerSpec(Keeper);
            using var journal = new MemoryStream();
            ScenarioResult result = new ScenarioRunner(ScenarioCorpus.Database, Dk2InstallGate.Path).Run(scenario, new RunOptions
            {
                Journal = journal,
                PlayerRun = PlayerRunCommand.PlayerRunFor(ScenarioCorpus.Database, spec, new PlayerRunId("level1-seed1"), null),
                AfterTick = (tick, simulation) =>
                {
                    WorldState world = simulation.World;
                    if (won is null && world.Player(Keeper).Status == PlayerStatus.Won)
                    {
                        won = tick;
                        PlayerFinale finale = world.Player(Keeper).Finale;
                        gem = finale.GemObjectId;
                        gemTile = (finale.GemTileX, finale.GemTileY);
                        if (world.ObjectById(gem) is { } thing) gemPoint = (thing.PosX, thing.PosY);
                    }
                    for (int i = 0; i < world.PlayerCount; i++)
                    {
                        ref readonly PlayerState other = ref world.PlayerAtIndex(i);
                        if (other.PlayerId != Keeper && other.Status == PlayerStatus.Won && !othersWon.Contains(other.PlayerId))
                            othersWon.Add(other.PlayerId);
                    }
                },
            });
            InputJournal read = InputJournal.Read(new MemoryStream(journal.ToArray()));
            return new Attempt(result, result.PlayerRun!, read, won, othersWon, gem, gemTile, gemPoint);
        }
    }

    [InstallFact]
    public void AttemptUsesUnmodifiedLevelStart()
    {
        Scenario scenario = ScenarioCorpus.Load(ScenarioFile);
        Assert.Equal(("campaign:level1", "dk2", 1UL, true, (byte)3, 14400), (scenario.Map, scenario.Random, scenario.Seed,
            scenario.FogOfWar, scenario.ViewingPlayer, scenario.DurationTicks));
        Assert.True(scenario.Setup.PlacedThings, "Level 1 must load its own placed things.");
        Assert.Empty(scenario.UnknownKeys);
        Assert.Empty(scenario.Setup.Players);
        Assert.Empty(scenario.Setup.Rooms);
        Assert.Empty(scenario.Setup.Creatures);
        Assert.Empty(scenario.Setup.Objects);
        Assert.Empty(scenario.Commands);
        Assert.Empty(scenario.Work);
        Assert.Empty(scenario.ComputerKeepers);
        Assert.Empty(scenario.KeeperPersonalities);
        Assert.Empty(scenario.Expect);

        // The file's start is the level's own: a scenario built from nothing but the level, seed and view
        // starts in the same state, so no setup key, present or future, grants anything.
        var bare = new Scenario
        {
            Id = "bare-level1", Map = "campaign:level1", Seed = 1, Random = "dk2", FogOfWar = true, ViewingPlayer = 3,
            DurationTicks = 1, Setup = new ScenarioSetup { PlacedThings = true },
        };
        scenario.DurationTicks = 1;
        var runner = new ScenarioRunner(ScenarioCorpus.Database, Dk2InstallGate.Path);
        Assert.Equal(runner.Run(bare).HashPerTick[0], runner.Run(scenario).HashPerTick[0]);
        Assert.Equal(runner.Run(bare).HashPerTick[0], Seed1.Value.Result.HashPerTick[0]);

        // Everything the attempt applied came from its own keeper, through the player session.
        Attempt attempt = Seed1.Value;
        List<PlayerCommand> applied = [.. attempt.Journal.FrameTicks.SelectMany(t => attempt.Journal.FrameAt(t).All)];
        Assert.NotEmpty(applied);
        Assert.All(applied, c => Assert.Equal(Keeper, c.PlayerId));
        int submitted = attempt.Player.Decisions.Sum(d => d.Receipts.Count(r => r.Status == PlayerReceiptStatus.Submitted));
        Assert.Equal(submitted, applied.Count);
        output.WriteLine($"start hash {attempt.Result.HashPerTick[0]:X16}; {applied.Count} applied commands, all player 3's");
    }

    [InstallFact]
    public void ControllerCanReachNativeWinVerdict()
    {
        Attempt attempt = Seed1.Value;
        PlayerRunCoordinator player = attempt.Player;
        Assert.True(player.Stop == PlayerRunStop.Won, $"{player.Stop}: {player.Limitation}");
        Assert.NotNull(attempt.NativeWinTick);
        // The coordinator reads the verdict the simulation itself set on its last completed tick.
        Assert.Equal(attempt.NativeWinTick, player.ResultTick?.Value);
        Assert.Empty(attempt.OthersWon);
        Assert.Null(player.Limitation);
        Assert.Equal("completed", attempt.Journal.EndReason);
        Assert.Equal(attempt.Result.TicksRun, attempt.Journal.TicksRun);
        Assert.Empty(attempt.Result.ExpectationFailures);
        Assert.Equal(PlayerRunOutcome.Completed, PlayerRunReport.Outcome(player, evidenceComplete: true));
        output.WriteLine($"won at t{player.ResultTick?.Value} after {player.Decisions.Count} decisions; run ended at t{attempt.Result.TicksRun}");
    }

    [InstallFact]
    public void WinningJournalReplaysWithoutController()
    {
        Attempt attempt = Seed1.Value;
        Assert.Equal(PlayerRunStop.Won, attempt.Player.Stop);
        var runner = new ScenarioRunner(ScenarioCorpus.Database, Dk2InstallGate.Path);
        Scenario scenario = ScenarioCorpus.Load(ScenarioFile);

        // No controller and no observations, first with the monitors off and then on. Both apply only
        // the journal's frames and must give the recorded hashes.
        ScenarioResult plain = runner.Replay(scenario, attempt.Journal, new RunOptions());
        ScenarioResult watched = runner.Replay(scenario, attempt.Journal,
            new RunOptions { Coverage = true, ProgressEvery = 60, SnapshotCheckEvery = 600 });
        foreach (ScenarioResult replay in new[] { plain, watched })
        {
            Assert.Null(replay.PlayerRun);
            Assert.Null(replay.Replay!.FirstDivergentTick);
            Assert.Equal(attempt.Journal.Hashes.Count, replay.Replay.ComparedTicks);
            Assert.Equal(attempt.Result.TicksRun, replay.TicksRun);
            Assert.Equal(attempt.Result.HashPerTick, replay.HashPerTick);
        }
        output.WriteLine($"{plain.Replay!.ComparedTicks} recorded hashes matched twice; final {plain.FinalStateHash:X16}");
    }

    [InstallFact]
    public void WinningRunReachesFinaleGemPickup()
    {
        Attempt attempt = Seed1.Value;
        PlayerRunCoordinator player = attempt.Player;
        Assert.Equal(PlayerRunStop.Won, player.Stop);
        // Level 1 hands the gem over by creature action 20, so the finale applies.
        Assert.NotEqual(0, attempt.GemAtWin);
        // The Lord of the Land carried it and dropped it where he died, on that tile's centre (ADR-0725).
        SimEvent died = Assert.Single(attempt.Result.Events, e => e.Kind == "creature_died" && e.Detail.Contains("type=Lord Of The Land"));
        Assert.Contains($"at=({attempt.GemTileAtWin.X},{attempt.GemTileAtWin.Y})", died.Detail);
        Assert.Equal((CreatureState.CentreOf(attempt.GemTileAtWin.X), CreatureState.CentreOf(attempt.GemTileAtWin.Y)), attempt.GemPointAtWin);
        Assert.True(player.Finale == PlayerFinaleCoverage.ReachedPickup,
            $"finale {player.Finale}: the Horned Reaper did not take the Portal Gem within {player.Options.FinaleTicks} ticks");
        long win = player.ResultTick!.Value.Value, pickup = player.FinalePickupTick!.Value.Value;
        Assert.InRange(pickup, win + 1, win + player.Options.FinaleTicks);
        Assert.Single(attempt.Result.Events, e => e.Kind == "reaper_created" && e.Tick.Value == win);
        Assert.Single(attempt.Result.Events, e => e.Kind == "gem_taken" && e.Tick.Value == pickup);

        // The replay reproduces the pickup on the same tick with no controller present.
        ScenarioResult replay = new ScenarioRunner(ScenarioCorpus.Database, Dk2InstallGate.Path)
            .Replay(ScenarioCorpus.Load(ScenarioFile), attempt.Journal);
        Assert.Single(replay.Events, e => e.Kind == "gem_taken" && e.Tick.Value == pickup);
        output.WriteLine($"won t{win}, Reaper took the gem t{pickup} ({pickup - win} ticks later)");
    }

    [InstallFact]
    public void AMatchingReceiptLetsTheWinClaimAcceptance()
    {
        // The receipt is written here from the bytes this test runs on, so it proves the claim path and
        // the tree scan, not that the build was fresh; tools/build_receipt.py makes the real one.
        string harness = AppContext.BaseDirectory;
        string repo = BuildReceipt.RepositoryRootAbove(harness)!;
        string root = Path.Combine(Path.GetTempPath(), $"dk2-accept-{Guid.NewGuid():N}");
        string receipt = Path.Combine(root, "receipt");
        Directory.CreateDirectory(receipt);
        try
        {
            static string Line(string path, string name) =>
                $"{Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)))}  {name}\n";
            File.WriteAllText(Path.Combine(receipt, BuildReceipt.OutputFile), string.Concat(Directory.EnumerateFiles(harness)
                .Where(f => !f.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)).Select(f => Line(f, Path.GetFileName(f)))));
            string[] inputs = [".cs", ".csproj", ".props", ".targets"];
            File.WriteAllText(Path.Combine(receipt, BuildReceipt.SourceFile), string.Concat(
                Directory.EnumerateFiles(Path.Combine(repo, "src"), "*", SearchOption.AllDirectories)
                    .Select(f => (Path: f, Name: Path.GetRelativePath(repo, f).Replace('\\', '/')))
                    .Where(f => inputs.Contains(Path.GetExtension(f.Path).ToLowerInvariant())
                                && f.Name.Split('/') is var parts && !(parts.Length > 3 && parts[2] is "bin" or "obj"))
                    .Select(f => Line(f.Path, f.Name))));

            RunTemplate template = HarnessCommandLine.RunOptions(["run", "x", "--campaign-player", "3", "--player-receipt", receipt])!;
            var runner = new ScenarioRunner(ScenarioCorpus.Database, Dk2InstallGate.Path);
            var error = new StringWriter();
            int exit = PlayerRunCommand.Run(runner, ScenarioCorpus.Database, Dk2InstallGate.Path, ScenarioCorpus.Load(ScenarioFile), template,
                Path.Combine(root, "out"), "accept", TextWriter.Null, error, tune: null, harnessDirectory: harness);
            Assert.True(exit == 0, error.ToString());
            using JsonDocument report = JsonDocument.Parse(File.ReadAllText(
                Path.Combine(root, "out", "campaign-player-level1", "accept", PlayerRunReport.RunFile)));
            string acceptance = report.RootElement.GetProperty("acceptance").GetString()!;
            Assert.True(report.RootElement.GetProperty("acceptance_claimed").GetBoolean(), acceptance);
            Assert.StartsWith("claimed: campaign:level1 (seed 1) won from its unmodified start", acceptance, StringComparison.Ordinal);
            // The in-process replay proved every recorded hash: the run's ticks plus its initial hash.
            Assert.Contains($"matched all {report.RootElement.GetProperty("ticks_run").GetInt32() + 1} recorded hashes", acceptance, StringComparison.Ordinal);
            output.WriteLine(acceptance);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
