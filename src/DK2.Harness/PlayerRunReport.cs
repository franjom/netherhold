// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Globalization;
using System.Text;
using System.Text.Json;
using DK2.Presentation;
using DK2.Simulation.Players;

namespace DK2.Harness;

/// <summary>A player run's verdict (campaign player plan, task 6). Only a won run with whole evidence
/// is Completed; a run out of ticks or one its controller gave up is a controller limitation, never
/// an engine failure; a run stopped by its wall-time or output budget, or whose evidence did not
/// all reach disk, is incomplete evidence.</summary>
public enum PlayerRunOutcome : byte { Completed, Defeated, ControllerLimited, TechnicalFailure, IncompleteEvidence }

/// <summary>The command line's player-run settings; test policy, not DK2 rules.</summary>
public sealed record CampaignPlayerSpec(
    Data.Model.PlayerId Player, int MaxTicks = 14400, int WallSeconds = 600, int IntervalTicks = 4,
    int MaxActions = 4096, int StallTicks = 1200, int Retries = 3, int FinaleTicks = 400, int OutputMiB = 64)
{
    /// <summary>Bytes kept back from the output budget for the final journal record and report.</summary>
    public const long ReserveBytes = 1024 * 1024;

    public long OutputBytes => (long)OutputMiB * 1024 * 1024;
}

/// <summary>Builds a player run's verdict and writes its evidence beside the journal:
/// <c>player-run.json</c> (verdict, limits, coverage, identity), <c>player-decisions.tsv</c> and
/// <c>player-actions.tsv</c> (what the controller saw and did). Player-visible decision evidence
/// and the host's privileged verdict are kept apart in the files.</summary>
public static class PlayerRunReport
{
    public const string RunFile = "player-run.json", DecisionsFile = "player-decisions.tsv", ActionsFile = "player-actions.tsv";

    /// <summary>What the report says no player run of this build checks; listed in every report.</summary>
    public static IReadOnlyList<string> Untested { get; } =
    [
        "Unity rendering, audio and the real UI are not exercised (stages 4-5)",
        "no external model or agent drives the player (stage 3)",
        "front end, briefing, save/load, results and campaign continuation are not played",
        "milestones are not recorded as such; player-decisions.tsv and player-actions.tsv hold what was done",
    ];

    public static PlayerRunOutcome Outcome(PlayerRunCoordinator player, bool evidenceComplete)
    {
        if (player is null) throw new ArgumentNullException(nameof(player));
        if (!evidenceComplete || player.Stop == PlayerRunStop.Cancelled) return PlayerRunOutcome.IncompleteEvidence;
        return player.Stop switch
        {
            PlayerRunStop.Won => PlayerRunOutcome.Completed,
            PlayerRunStop.Defeated => PlayerRunOutcome.Defeated,
            _ => PlayerRunOutcome.ControllerLimited,
        };
    }

    /// <summary>The process exit code: 0 only for Completed, 2 for incomplete evidence, else 1.</summary>
    public static int ExitCode(PlayerRunOutcome outcome) => outcome switch
    {
        PlayerRunOutcome.Completed => 0,
        PlayerRunOutcome.IncompleteEvidence => 2,
        _ => 1,
    };

    /// <summary>What a won run still does not establish: the CLI cannot see a build receipt.</summary>
    public const string AcceptanceNotClaimed =
        "not claimed: the fresh-build receipt and the installed-data requirement are checked by task 7's procedure, not this command";

    /// <summary>The verdict file's bytes, built before writing so the output budget can include it.</summary>
    public static byte[] Verdict(ScenarioResult result, PlayerRunOutcome outcome, CampaignPlayerSpec spec, string? note)
    {
        if (result.PlayerRun is not PlayerRunCoordinator player) throw new ArgumentException("Not a player run.", nameof(result));
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            outcome = outcome.ToString(),
            stop = player.Stop == PlayerRunStop.None ? "NoResult" : player.Stop.ToString(),
            limitation = player.Limitation,
            note,
            result_tick = player.ResultTick?.Value,
            finale = player.Finale.ToString(),
            finale_pickup_tick = player.FinalePickupTick?.Value,
            suspected_anomaly = player.Stop == PlayerRunStop.Won && player.Finale == PlayerFinaleCoverage.NotReached
                ? "the Horned Reaper did not pick up the Portal Gem within the finale window" : null,
            acceptance = AcceptanceNotClaimed,
            ticks_run = result.TicksRun,
            final_state_hash = result.FinalStateHash.ToString("x16", CultureInfo.InvariantCulture),
            run_id = player.Options.RunId.Value,
            player = player.Options.Player.Value,
            controller = player.Options.ControllerName,
            limits = new
            {
                max_ticks = spec.MaxTicks, wall_seconds = spec.WallSeconds, interval_ticks = spec.IntervalTicks,
                max_actions = spec.MaxActions, stall_ticks = spec.StallTicks, retries = spec.Retries,
                finale_ticks = spec.FinaleTicks, output_mib = spec.OutputMiB,
                requests_per_decision = player.Options.Limits.MaxRequestsPerObservation, max_advance_ticks = player.Options.MaxAdvanceTicks,
            },
            decisions = player.Decisions.Count,
            receipts = player.Decisions.SelectMany(d => d.Receipts).GroupBy(r => $"{r.Status}/{r.Reason}")
                .OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count()),
            coverage_limits = PlayerObservationBuilder.CoverageLimits,
            untested = Untested,
            manifest = PlayerRunManifest.FileName,
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>The decision and action tables: what the controller saw and sent.</summary>
    public static void WriteTables(string directory, ScenarioResult result)
    {
        if (result.PlayerRun is not PlayerRunCoordinator player) throw new ArgumentException("Not a player run.", nameof(result));
        Directory.CreateDirectory(directory);
        var decisions = new StringBuilder("tick\trevision\tadvance_ticks\trequests\tcommands\treason\n");
        foreach (PlayerDecisionRecord d in player.Decisions)
            decisions.Append(CultureInfo.InvariantCulture, $"{d.Tick.Value}\t{d.Revision.Value}\t{d.AdvanceTicks}\t{d.Requests.Count}\t{d.Commands}\t{Tsv(d.Reason)}\n");
        WriteNew(Path.Combine(directory, DecisionsFile), Encoding.UTF8.GetBytes(decisions.ToString()));

        var actions = new StringBuilder("action_id\tdecided_tick\tkind\taction\tstatus\treason\tscheduled_tick\n");
        foreach (PlayerDecisionRecord d in player.Decisions)
            for (int i = 0; i < d.Requests.Count; i++)
            {
                PlayerRequest q = d.Requests[i];
                PlayerReceipt r = d.Receipts[i];
                actions.Append(CultureInfo.InvariantCulture,
                    $"{q.Id.Value}\t{d.Tick.Value}\t{q.Action.Kind}\t{Tsv(q.Action.ToString())}\t{r.Status}\t{r.Reason}\t{r.ScheduledTick?.Value}\n");
            }
        WriteNew(Path.Combine(directory, ActionsFile), Encoding.UTF8.GetBytes(actions.ToString()));
    }

    /// <summary>The verdict file of a run that produced no result: a refusal, or one the engine or host
    /// broke off (<paramref name="outcome"/> "Refused" or "TechnicalFailure").</summary>
    public static void WriteFailure(string directory, PlayerRunId runId, string outcome, Exception failure)
    {
        Directory.CreateDirectory(directory);
        WriteNew(Path.Combine(directory, RunFile), JsonSerializer.SerializeToUtf8Bytes(new
        {
            outcome,
            run_id = runId.Value,
            error = $"{failure.GetType().FullName}: {failure.Message}",
            untested = Untested,
            manifest = PlayerRunManifest.FileName,
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string Tsv(string? text) => (text ?? "").Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');

    /// <summary>Evidence is never overwritten.</summary>
    public static void WriteNew(string path, byte[] bytes)
    {
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        file.Write(bytes);
    }
}

/// <summary>Evidence did not reach disk: the run's verdict is incomplete evidence, not a technical failure.</summary>
public sealed class EvidenceWriteException(string message, Exception? inner = null) : IOException(message, inner);

/// <summary>A configuration the run refuses before any tick (exit 2, not a technical failure).</summary>
public sealed class PlayerRunRefusedException(string message, string? parameter = null) : ArgumentException(message, parameter);

/// <summary>A write-only stream that counts what passes through and reports when a budget less its
/// reserve is reached, so the run can stop with room left for the final journal record and report.</summary>
public sealed class BudgetedStream(Stream inner, long budgetBytes, long reserveBytes) : Stream
{
    public long Written { get; private set; }

    /// <summary>True once writing on would eat into the reserve.</summary>
    public bool Exhausted => Written >= budgetBytes - reserveBytes;

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (Written + buffer.Length > budgetBytes) throw new EvidenceWriteException($"The output budget of {budgetBytes} bytes is exhausted.");
        try { inner.Write(buffer); }
        catch (IOException ex) { throw new EvidenceWriteException($"The journal could not be written: {ex.Message}", ex); }
        Written += buffer.Length;
    }

    public override void WriteByte(byte value) => Write([value]);

    public override void Flush()
    {
        try { inner.Flush(); }
        catch (IOException ex) { throw new EvidenceWriteException($"The journal could not be flushed: {ex.Message}", ex); }
    }
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => Written;
    public override long Position { get => Written; set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        try
        {
            if (disposing) inner.Dispose();
        }
        catch (IOException ex) when (ex is not EvidenceWriteException)
        {
            throw new EvidenceWriteException($"The journal could not be closed: {ex.Message}", ex);
        }
        finally { base.Dispose(disposing); }
    }
}
