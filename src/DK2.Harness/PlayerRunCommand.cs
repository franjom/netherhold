// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Diagnostics;
using System.Globalization;
using DK2.AI.Players;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Players;

namespace DK2.Harness;

/// <summary>
/// <c>run --campaign-player</c> for one scenario (campaign player plan, task 6). It makes a new
/// folder <c>&lt;out&gt;/&lt;scenario&gt;/&lt;run id&gt;</c> (never an existing one) and writes the manifest
/// before the first tick, so a killed or crashed attempt is still identified. The journal goes through
/// a <see cref="BudgetedStream"/>; wall time and the output budget stop the run. The verdict file is
/// written last, once the whole folder is known to fit the budget. A refusal exits 2 (Refused); a
/// write that fails, 2 (incomplete evidence); any other exception, 1 (TechnicalFailure, its journal,
/// manifest and verdict kept). Otherwise <see cref="PlayerRunReport.ExitCode"/>. A mismatching build
/// receipt refuses the run before its folder; when acceptance is claimed is <see cref="ClaimGaps"/>,
/// <see cref="ReplayGap"/> and the harness README's "Build receipt and acceptance".
/// </summary>
public static class PlayerRunCommand
{
    public static int Run(ScenarioRunner runner, GameDatabase database, string? installDirectory, Scenario scenario,
        RunTemplate template, string outRoot, string? runId, TextWriter output, TextWriter error) =>
        Run(runner, database, installDirectory, scenario, template, outRoot, runId, output, error, tune: null);

    /// <summary>As above; <paramref name="tune"/> lets a test change the run's options (a failing host hook).</summary>
    internal static int Run(ScenarioRunner runner, GameDatabase database, string? installDirectory, Scenario scenario,
        RunTemplate template, string outRoot, string? runId, TextWriter output, TextWriter error,
        Func<PlayerRunOptions, PlayerRunOptions>? tune, string? harnessDirectory = null)
    {
        if (template.Player is not CampaignPlayerSpec spec) throw new ArgumentException("No campaign player.", nameof(template));
        BuildReceipt.Verification? receipt = null;
        if (spec.Receipt is string receiptDirectory)
        {
            string harness = harnessDirectory ?? AppContext.BaseDirectory;
            receipt = BuildReceipt.Verify(receiptDirectory, harness, BuildReceipt.RepositoryRootAbove(harness));
            receipt = receipt with { Problems = [.. receipt.Problems, .. BuildReceipt.LoadedElsewhere(harness)] };
            if (!receipt.Matches)
            {
                error.WriteLine($"{scenario.Id}: refused: this harness and tree are not the build in receipt '{receiptDirectory}':");
                foreach (string problem in receipt.Problems) error.WriteLine($"    {problem}");
                return 2;
            }
        }
        PlayerRunId id;
        try { id = new PlayerRunId(runId ?? DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture)); }
        catch (ArgumentException ex)
        {
            error.WriteLine($"{scenario.Id}: the run id is not usable ({ex.Message})");
            return 2;
        }
        string directory = Path.Combine(outRoot, scenario.Id, id.Value);
        if (Directory.Exists(directory) || File.Exists(directory))
        {
            error.WriteLine($"{scenario.Id}: '{directory}' exists; evidence is never overwritten.");
            return 2;
        }

        BudgetedStream? budgeted = null;
        try
        {
            Directory.CreateDirectory(directory);
            budgeted = new BudgetedStream(new FileStream(Path.Combine(directory, "input.journal"), FileMode.CreateNew, FileAccess.Write),
                spec.OutputBytes, CampaignPlayerSpec.ReserveBytes);
            PlayerRunManifest.Write(directory, scenario, installDirectory, id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            budgeted?.Dispose();
            error.WriteLine($"{scenario.Id}: evidence could not be written: {ex.Message}");
            return 2;
        }

        ScenarioResult result;
        var clock = Stopwatch.StartNew();
        try
        {
            using (budgeted)
            {
                PlayerRunOptions playerRun = PlayerRunFor(database, spec, id, () =>
                    clock.Elapsed.TotalSeconds >= spec.WallSeconds ? $"wall-time limit of {spec.WallSeconds} s reached"
                    : budgeted.Exhausted ? $"output budget of {spec.OutputMiB} MiB reached (1 MiB kept for the final records)"
                    : null);
                try
                {
                    RunOptions options = template.For(scenario, budgeted) with { PlayerRun = tune is null ? playerRun : tune(playerRun) };
                    result = runner.Run(scenario, options);
                }
                catch (PlayerRunRefusedException ex)
                {
                    error.WriteLine($"{scenario.Id}: refused: {ex.Message}");
                    return Failure(directory, id, "Refused", ex, error) ?? 2;
                }
                catch (EvidenceWriteException ex)
                {
                    error.WriteLine($"{scenario.Id}: evidence could not be written: {ex.Message}");
                    return 2;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    error.WriteLine($"{scenario.Id}: technical failure: {ex.GetType().FullName}: {ex.Message}");
                    return Failure(directory, id, PlayerRunOutcome.TechnicalFailure.ToString(), ex, error)
                        ?? PlayerRunReport.ExitCode(PlayerRunOutcome.TechnicalFailure);
                }
            }
            }
        catch (EvidenceWriteException ex)
        {
            // Closing the journal failed after the run: its last records may not be on disk.
            error.WriteLine($"{scenario.Id}: evidence could not be written: {ex.Message}");
            return 2;
        }

        PlayerRunCoordinator player = result.PlayerRun!;
        string? note = player.Stop switch
        {
            PlayerRunStop.Cancelled => player.Limitation,
            PlayerRunStop.None => $"no result within {result.TicksRun} ticks",
            _ => null,
        };
        try
        {
            ScenarioRunner.WriteReport(result, directory);
            PlayerRunReport.WriteTables(directory, result);
            // The output budget covers the whole folder, the verdict file included.
            PlayerRunOutcome outcome = PlayerRunReport.Outcome(player, evidenceComplete: true);
            List<string> gaps = ClaimGaps(scenario, player, outcome, receipt, fuzzed: template.Fuzz is not null,
                LevelComputerKeepers(installDirectory, scenario));
            int? replayed = null;
            if (gaps.Count == 0)
            {
                if (ReplayGap(runner, scenario, result, Path.Combine(directory, "input.journal"), out int compared) is string replayGap)
                    gaps.Add(replayGap);
                else replayed = compared;
            }
            byte[] verdict = PlayerRunReport.Verdict(result, outcome, spec, note, Acceptance(scenario, player, receipt, gaps, replayed));
            long total = FolderBytes(directory) + verdict.Length;
            if (total > spec.OutputBytes)
            {
                outcome = PlayerRunOutcome.IncompleteEvidence;
                note = $"the evidence ({total} bytes) exceeds the {spec.OutputMiB} MiB output budget";
                gaps.Add("the evidence does not fit the output budget");
                verdict = PlayerRunReport.Verdict(result, outcome, spec, note, Acceptance(scenario, player, receipt, gaps, replayed));
            }
            PlayerRunReport.WriteNew(Path.Combine(directory, PlayerRunReport.RunFile), verdict);

            output.WriteLine($"{scenario.Id,-36} {result.TicksRun,6} ticks  {outcome} ({player.Stop}{(player.Limitation is null ? "" : $": {player.Limitation}")}), " +
                             $"finale {player.Finale}  -> {directory}");
            if (player.Stop == PlayerRunStop.Won && player.Finale == PlayerFinaleCoverage.NotReached)
                error.WriteLine($"{scenario.Id}: suspected anomaly: the Horned Reaper did not pick up the Portal Gem within {spec.FinaleTicks} ticks");
            return PlayerRunReport.ExitCode(outcome);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"{scenario.Id}: the report could not be written: {ex.Message}");
            return PlayerRunReport.ExitCode(PlayerRunOutcome.IncompleteEvidence);
        }
    }

    /// <summary>What stands between a run and an acceptance claim, the replay aside; empty when only the
    /// replay is left to check. <paramref name="levelComputerKeepers"/> is the level's own computer keepers
    /// (its PLAYERS records), or null when they could not be read.</summary>
    internal static List<string> ClaimGaps(Scenario scenario, PlayerRunCoordinator player, PlayerRunOutcome outcome,
        BuildReceipt.Verification? receipt, bool fuzzed, IReadOnlyCollection<byte>? levelComputerKeepers)
    {
        var gaps = new List<string>();
        if (receipt is null) gaps.Add("no build receipt was given (--player-receipt)");
        gaps.AddRange(PlayerRunReport.ModifiedStart(scenario));
        if (fuzzed) gaps.Add("another player is driven by the fuzzer (--fuzz)");
        if (levelComputerKeepers is null)
            gaps.Add("the level's own computer keepers (its PLAYERS records) could not be read");
        else
        {
            byte[] level = [.. levelComputerKeepers.Where(id => id != player.Options.Player.Value).Order()];
            byte[] driven = [.. scenario.ComputerKeepers.Distinct().Order()];
            if (!level.SequenceEqual(driven))
                gaps.Add($"the scenario drives computer keepers [{string.Join(", ", driven)}], the level's records name [{string.Join(", ", level)}]");
        }
        if (outcome != PlayerRunOutcome.Completed) gaps.Add($"the run is {outcome}, not Completed");
        else if (player.Finale is not (PlayerFinaleCoverage.ReachedPickup or PlayerFinaleCoverage.NotApplicable))
            gaps.Add($"the finale is {player.Finale}: the Horned Reaper did not take the Portal Gem");
        return gaps;
    }

    /// <summary>Replays the run's journal in this process without the controller, on the same build and
    /// data; null when every recorded hash and the tick count match, else why not. Run only when nothing
    /// else blocks a claim, since it costs a second run.</summary>
    internal static string? ReplayGap(ScenarioRunner runner, Scenario scenario, ScenarioResult result, string journalPath, out int compared)
    {
        compared = 0;
        try
        {
            InputJournal journal;
            using (FileStream stream = File.OpenRead(journalPath))
                journal = InputJournal.Read(stream);
            ScenarioResult replay = runner.Replay(scenario, journal);
            if (replay.Replay is not { FirstDivergentTick: null } report || report.ComparedTicks != journal.Hashes.Count
                || replay.TicksRun != result.TicksRun)
                return $"the journal's replay without the controller does not match (first divergent tick {replay.Replay?.FirstDivergentTick?.ToString(CultureInfo.InvariantCulture) ?? "none"}, " +
                       $"{replay.TicksRun} ticks against {result.TicksRun})";
            compared = report.ComparedTicks;
            return null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return $"the journal could not be replayed: {ex.GetType().Name}: {ex.Message}";
        }
    }

    /// <summary>The computer keepers a campaign level's PLAYERS records name; null off a campaign map or
    /// when the records cannot be read.</summary>
    private static IReadOnlyCollection<byte>? LevelComputerKeepers(string? installDirectory, Scenario scenario)
    {
        if (installDirectory is null || !scenario.Map.StartsWith(ScenarioRunner.CampaignPrefix, StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            return GameDatabaseLoader.LoadCampaignPlayers(installDirectory, scenario.Map[ScenarioRunner.CampaignPrefix.Length..].Trim())
                ?.Where(p => p.IsComputerKeeper).Select(p => p.PlayerId).ToArray();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The claim line. A claim needs no gaps AND <paramref name="replayedHashes"/>, the count of
    /// recorded hashes the in-process replay matched: without that proof nothing is claimed.</summary>
    internal static string Acceptance(Scenario scenario, PlayerRunCoordinator player, BuildReceipt.Verification? receipt,
        IReadOnlyList<string> gaps, int? replayedHashes)
    {
        if (gaps.Count > 0) return PlayerRunReport.NotClaimed + string.Join("; ", gaps);
        if (replayedHashes is not int hashes || hashes <= 0) return PlayerRunReport.NotClaimed + "the journal was not replayed";
        string build = receipt!.Dirty
            ? $"head {receipt.Head ?? "not recorded"} plus uncommitted changes (tracked patch sha256 {receipt.PatchSha256 ?? "not recorded"}, untracked files in the receipt's source list)"
            : $"head {receipt.Head ?? "not recorded"}, a clean tree";
        return string.Create(CultureInfo.InvariantCulture,
            $"claimed: {scenario.Map} (seed {scenario.Seed}) won from its unmodified start, driving the computer keepers its PLAYERS records name, by {player.Options.ControllerName} at tick {player.ResultTick?.Value}, finale {player.Finale}, on the build in receipt {receipt.Directory} ({build}); an in-process replay of the journal without the controller matched all {hashes} recorded hashes. Headless only: rendering, audio, the real UI and the front end are not covered; report.md lists what this build cannot act on.");
    }

    /// <summary>The campaign player and its run options exactly as this command builds them, so an
    /// acceptance test drives the same controller under the same limits.</summary>
    internal static PlayerRunOptions PlayerRunFor(GameDatabase database, CampaignPlayerSpec spec, PlayerRunId id,
        Func<string?>? stopRequested)
    {
        IEnumerable<CreatureTypeId> workers = database.Creatures.Values
            .Where(c => (c.Flags & CreatureFlags.IsWorker) == CreatureFlags.IsWorker).Select(c => new CreatureTypeId(c.CreatureId));
        var controller = new CampaignPlayer(new CampaignPlayerOptions(id, spec.Player, workers)
        {
            AdvanceTicks = spec.IntervalTicks,
            StallWindowTicks = spec.StallTicks,
            RecoveryAttempts = spec.Retries,
        });
        return new PlayerRunOptions(controller, spec.Player, id)
        {
            MaxTicks = spec.MaxTicks,
            FinaleTicks = spec.FinaleTicks,
            Limits = new PlayerSessionLimits(maxAdmittedActions: spec.MaxActions),
            StopRequested = stopRequested,
        };
    }

    /// <summary>Writes a refusal's or failure's verdict file; 2 when even that could not be written.</summary>
    private static int? Failure(string directory, PlayerRunId id, string outcome, Exception failure, TextWriter error)
    {
        try
        {
            PlayerRunReport.WriteFailure(directory, id, outcome, failure);
            return null;
        }
        catch (Exception io) when (io is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"the {outcome} verdict could not be written: {io.Message}");
            return 2;
        }
    }

    private static long FolderBytes(string directory) =>
        new DirectoryInfo(directory).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);

    /// <summary>Before replaying a player-run journal: its manifest must match this build, data and
    /// scenario. Returns null to proceed, or the exit code 2 after printing why not.</summary>
    public static int? VerifyBeforeReplay(string journalPath, Scenario scenario, string? installDirectory, InputJournal journal,
        TextWriter output, TextWriter error)
    {
        if (!JournalHeader.IsPlayerRun(journal)) return null;
        PlayerRunId runId;
        try
        {
            runId = journal.Header.TryGetValue("player.run.id", out string? named) ? new PlayerRunId(named)
                : throw new ArgumentException("the player-run journal names no run id");
        }
        catch (ArgumentException ex)
        {
            error.WriteLine($"not replayed: incomplete evidence: {ex.Message}");
            return 2;
        }
        string directory = Path.GetDirectoryName(Path.GetFullPath(journalPath)) ?? ".";
        PlayerRunManifest.Verification verification = PlayerRunManifest.Verify(directory, scenario, installDirectory, runId);
        foreach (string note in verification.Diagnostic) output.WriteLine($"note: {note}");
        if (verification.MayReplay) return null;
        foreach (string problem in verification.Blocking) error.WriteLine($"not replayed: {problem}");
        return 2;
    }
}
