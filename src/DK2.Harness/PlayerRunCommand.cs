// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Diagnostics;
using System.Globalization;
using DK2.AI.Players;
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
/// manifest and verdict kept). Otherwise <see cref="PlayerRunReport.ExitCode"/>.
/// </summary>
public static class PlayerRunCommand
{
    public static int Run(ScenarioRunner runner, GameDatabase database, string? installDirectory, Scenario scenario,
        RunTemplate template, string outRoot, string? runId, TextWriter output, TextWriter error) =>
        Run(runner, database, installDirectory, scenario, template, outRoot, runId, output, error, tune: null);

    /// <summary>As above; <paramref name="tune"/> lets a test change the run's options (a failing host hook).</summary>
    internal static int Run(ScenarioRunner runner, GameDatabase database, string? installDirectory, Scenario scenario,
        RunTemplate template, string outRoot, string? runId, TextWriter output, TextWriter error,
        Func<PlayerRunOptions, PlayerRunOptions>? tune)
    {
        if (template.Player is not CampaignPlayerSpec spec) throw new ArgumentException("No campaign player.", nameof(template));
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
            byte[] verdict = PlayerRunReport.Verdict(result, outcome, spec, note);
            long total = FolderBytes(directory) + verdict.Length;
            if (total > spec.OutputBytes)
            {
                outcome = PlayerRunOutcome.IncompleteEvidence;
                note = $"the evidence ({total} bytes) exceeds the {spec.OutputMiB} MiB output budget";
                verdict = PlayerRunReport.Verdict(result, outcome, spec, note);
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
