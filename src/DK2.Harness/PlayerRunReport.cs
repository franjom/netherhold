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

/// <summary>The player-run limits a playbook's caps may stand in for (Task 3b).</summary>
[Flags]
public enum PlayerLimitFlags : byte { None = 0, MaxTicks = 1, MaxActions = 2, WallSeconds = 4, OutputMiB = 8 }

/// <summary>The command line's player-run settings; test policy, not DK2 rules.</summary>
public sealed record CampaignPlayerSpec(
    Data.Model.PlayerId Player, int MaxTicks = 14400, int WallSeconds = 600, int IntervalTicks = 4,
    int MaxActions = 4096, int StallTicks = 1200, int Retries = 3, int FinaleTicks = 400, int OutputMiB = 64,
    string? Receipt = null, string? Knowledge = null, int? Researchable = null)
{
    /// <summary>The knowledge directory the player reads (Task 3a), or null for none; <c>run</c> fills in
    /// the repository's <c>knowledge/</c> unless <c>--player-knowledge</c> names one or <c>none</c>.</summary>
    public string? KnowledgeDirectory
    {
        get
        {
            return Knowledge is null || Knowledge == KeeperKnowledgeFile.None ? null : Knowledge;
        }
    }

    /// <summary>The level's playbook file (Task 3b, <c>--playbook</c>), or null for none.</summary>
    public string? Playbook { get; init; }

    /// <summary>The limits the command line gave; a playbook's cap replaces only the others.</summary>
    public PlayerLimitFlags GivenLimits { get; init; }

    /// <summary>The playbook once <c>run</c> has loaded and reviewed it; null without one.</summary>
    public PlaybookFile.Loaded? LoadedPlaybook { get; init; }

    /// <summary>The knowledge rules the playbook switches on.</summary>
    public IReadOnlyList<string> PlaybookRules
    {
        get
        {
            return LoadedPlaybook?.Playbook.Rules ?? [];
        }
    }

    /// <summary>The limit flags a playbook's caps stand in for, each with its bit.</summary>
    public static readonly IReadOnlyList<(string Flag, PlayerLimitFlags Limit)> CappedFlags =
    [
        ("--player-max-ticks", PlayerLimitFlags.MaxTicks), ("--player-max-actions", PlayerLimitFlags.MaxActions),
        ("--player-wall-seconds", PlayerLimitFlags.WallSeconds), ("--player-output-mib", PlayerLimitFlags.OutputMiB),
    ];

    /// <summary>Bytes kept back from the output budget for the final journal record and report.</summary>
    public const long ReserveBytes = 1024 * 1024;

    public long OutputBytes
    {
        get
        {
            return (long)OutputMiB * 1024 * 1024;
        }
    }
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
        "Rendering, audio and the real UI are not exercised (stages 4-5)",
        "no external model or agent drives the player (stage 3)",
        "front end, briefing, save/load, results and campaign continuation are not played",
        "milestones are not recorded as such; player-decisions.tsv and player-actions.tsv hold what was done",
    ];

    public static PlayerRunOutcome Outcome(PlayerRunCoordinator player, bool evidenceComplete)
    {
        if (player is null)
        {
            throw new ArgumentNullException(nameof(player));
        }

        if (!evidenceComplete || player.Stop == PlayerRunStop.Cancelled)
        {
            return PlayerRunOutcome.IncompleteEvidence;
        }

        return player.Stop switch
        {
            PlayerRunStop.Won => PlayerRunOutcome.Completed,
            PlayerRunStop.Defeated => PlayerRunOutcome.Defeated,
            _ => PlayerRunOutcome.ControllerLimited,
        };
    }

    /// <summary>The process exit code: 0 only for Completed, 2 for incomplete evidence, else 1.</summary>
    public static int ExitCode(PlayerRunOutcome outcome)
    {
        return outcome switch
        {
            PlayerRunOutcome.Completed => 0,
            PlayerRunOutcome.IncompleteEvidence => 2,
            _ => 1,
        };
    }

    /// <summary>The prefix of every acceptance line that claims nothing; the reasons follow it.</summary>
    public const string NotClaimed = "not claimed: ";

    /// <summary>Why <paramref name="scenario"/> is not a shipped level's unmodified start (task 7): empty
    /// when it is. Listing computer keepers is not a modification here; the run command compares the
    /// list with the level's own PLAYERS records.</summary>
    public static IReadOnlyList<string> ModifiedStart(Scenario scenario)
    {
        if (scenario is null)
        {
            throw new ArgumentNullException(nameof(scenario));
        }

        var why = new List<string>();
        if (!scenario.Map.StartsWith(ScenarioRunner.CampaignPrefix, StringComparison.OrdinalIgnoreCase))
        {
            why.Add($"map {scenario.Map} is not a shipped campaign level");
        }

        if (scenario.Random != "dk2")
        {
            why.Add("the random generator is not DK2's (random: dk2)");
        }

        if (!scenario.FogOfWar)
        {
            why.Add("fog of war is off, so the map is revealed");
        }

        if (!scenario.Setup.PlacedThings)
        {
            why.Add("the level's placed things are not loaded");
        }

        if (scenario.Setup.Players.Count > 0)
        {
            why.Add("the setup grants players gold, mana or spells");
        }

        if (scenario.Setup.Rooms.Count > 0)
        {
            why.Add("the setup adds rooms");
        }

        if (scenario.Setup.Creatures.Count > 0)
        {
            why.Add("the setup adds creatures");
        }

        if (scenario.Setup.Objects.Count > 0)
        {
            why.Add("the setup adds objects");
        }

        if (scenario.Commands.Count > 0)
        {
            why.Add("the scenario scripts commands");
        }

        if (scenario.Work.Count > 0)
        {
            why.Add("the scenario injects tile work");
        }

        if (scenario.KeeperPersonalities.Count > 0)
        {
            why.Add("the scenario replaces computer keepers' personalities");
        }

        if (scenario.UnknownKeys.Count > 0)
        {
            why.Add($"the scenario has keys this build does not read: {string.Join(", ", scenario.UnknownKeys)}");
        }

        return why;
    }

    /// <summary>The verdict file's bytes, built before writing so the output budget can include it.
    /// <paramref name="acceptance"/> is the claim line, from the run command's acceptance check.</summary>
    public static byte[] Verdict(ScenarioResult result, PlayerRunOutcome outcome, CampaignPlayerSpec spec, string? note, string acceptance)
    {
        if (result.PlayerRun is not PlayerRunCoordinator player)
        {
            throw new ArgumentException("Not a player run.", nameof(result));
        }

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
            acceptance,
            acceptance_claimed = !acceptance.StartsWith(NotClaimed, StringComparison.Ordinal),
            receipt = spec.Receipt,
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
            development = Development(player.LastObservation),
            diagnostics = player.Diagnostics.Summary(player.Decisions),
            knowledge = spec.KnowledgeDirectory is string knowledge ? KnowledgeSummary(knowledge, spec.Researchable, spec.PlaybookRules) : null,
            playbook = spec.LoadedPlaybook is { } playbook ? new
            {
                path = spec.Playbook, sha256 = playbook.Sha256, level = playbook.Playbook.Level, rules = playbook.Playbook.Rules,
                tunables = playbook.Playbook.Tunables.Select(t => new { path = t.Path, value = t.Value }),
                warnings = playbook.Warnings,
            } : null,
            coverage_limits = PlayerObservationBuilder.CoverageLimits,
            untested = Untested,
            manifest = PlayerRunManifest.FileName,
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Which knowledge the player read (Task 3a): the two files' SHA-256 and the rules that acted.</summary>
    private static object KnowledgeSummary(string directory, int? researchable, IReadOnlyList<string> playbookRules)
    {
        KeeperKnowledgeFile.Loaded loaded = KeeperKnowledgeFile.Load(directory, researchable, playbookRules);
        return new
        {
            rooms_sha256 = loaded.RoomsSha256, rules_sha256 = loaded.RulesSha256, acting_rules = loaded.ActingRules, researchable,
            slot_targets = loaded.SlotTargets.OrderBy(t => t.Key).ToDictionary(t => t.Key.ToString(), t => t.Value),
        };
    }

    /// <summary>What the player had built and kept by its last observation (campaign bot stage 3 plan,
    /// Task 2): owned room tiles by type, own creatures by type id, gold. Null when it never decided.</summary>
    private static object? Development(PlayerObservation? last)
    {
        return last is null ? null : new
        {
            tick = last.Stamp.Tick.Value,
            gold = last.Gold.Value,
            room_tiles = last.Rooms.GroupBy(r => r.Type.ToString()).OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.TileCount)),
            creatures = last.Creatures.Count,
            creature_types = last.Creatures.GroupBy(c => c.Type.Value).OrderBy(g => g.Key)
            .ToDictionary(g => g.Key.ToString(CultureInfo.InvariantCulture), g => g.Count()),
        };
    }

    /// <summary>The decision and action tables: what the controller saw and sent.</summary>
    public static void WriteTables(string directory, ScenarioResult result)
    {
        if (result.PlayerRun is not PlayerRunCoordinator player)
        {
            throw new ArgumentException("Not a player run.", nameof(result));
        }

        Directory.CreateDirectory(directory);
        var decisions = new StringBuilder("tick\trevision\tadvance_ticks\trequests\tcommands\treason\n");
        foreach (PlayerDecisionRecord d in player.Decisions)
        {
            decisions.Append(CultureInfo.InvariantCulture, $"{d.Tick.Value}\t{d.Revision.Value}\t{d.AdvanceTicks}\t{d.Requests.Count}\t{d.Commands}\t{Tsv(d.Reason)}\n");
        }

        WriteNew(Path.Combine(directory, DecisionsFile), Encoding.UTF8.GetBytes(decisions.ToString()));

        var actions = new StringBuilder("action_id\tdecided_tick\tkind\taction\tstatus\treason\tscheduled_tick\n");
        foreach (PlayerDecisionRecord d in player.Decisions)
        {
            for (int i = 0; i < d.Requests.Count; i++)
            {
                PlayerRequest q = d.Requests[i];
                PlayerReceipt r = d.Receipts[i];
                actions.Append(CultureInfo.InvariantCulture,
                    $"{q.Id.Value}\t{d.Tick.Value}\t{q.Action.Kind}\t{Tsv(q.Action.ToString())}\t{r.Status}\t{r.Reason}\t{r.ScheduledTick?.Value}\n");
            }
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

    private static string Tsv(string? text)
    {
        return (text ?? "").Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');
    }

    /// <summary>Evidence is never overwritten.</summary>
    public static void WriteNew(string path, byte[] bytes)
    {
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        file.Write(bytes);
    }
}

/// <summary>Evidence did not reach disk: the run's verdict is incomplete evidence, not a technical failure.</summary>
public sealed class EvidenceWriteException : IOException
{
    public EvidenceWriteException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

/// <summary>A configuration the run refuses before any tick (exit 2, not a technical failure).</summary>
public sealed class PlayerRunRefusedException : ArgumentException
{
    public PlayerRunRefusedException(string message, string? parameter = null) : base(message, parameter)
    {
    }
}

/// <summary>A write-only stream that counts what passes through and reports when a budget less its
/// reserve is reached, so the run can stop with room left for the final journal record and report.</summary>
public sealed class BudgetedStream : Stream
{
    private readonly Stream _inner;
    private readonly long _budgetBytes;
    private readonly long _reserveBytes;

    public BudgetedStream(Stream inner, long budgetBytes, long reserveBytes)
    {
        _inner = inner;
        _budgetBytes = budgetBytes;
        _reserveBytes = reserveBytes;
    }

    public long Written { get; private set; }

    /// <summary>True once writing on would eat into the reserve.</summary>
    public bool Exhausted
    {
        get
        {
            return Written >= _budgetBytes - _reserveBytes;
        }
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        Write(buffer.AsSpan(offset, count));
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (Written + buffer.Length > _budgetBytes)
        {
            throw new EvidenceWriteException($"The output budget of {_budgetBytes} bytes is exhausted.");
        }

        try { _inner.Write(buffer); }
        catch (IOException ex) { throw new EvidenceWriteException($"The journal could not be written: {ex.Message}", ex); }
        Written += buffer.Length;
    }

    public override void WriteByte(byte value)
    {
        Write([value]);
    }

    public override void Flush()
    {
        try { _inner.Flush(); }
        catch (IOException ex) { throw new EvidenceWriteException($"The journal could not be flushed: {ex.Message}", ex); }
    }
    public override bool CanRead
    {
        get
        {
            return false;
        }
    }

    public override bool CanSeek
    {
        get
        {
            return false;
        }
    }

    public override bool CanWrite
    {
        get
        {
            return true;
        }
    }

    public override long Length
    {
        get
        {
            return Written;
        }
    }

    public override long Position
    {
        get
        {
            return Written;
        }

        set
        {
            throw new NotSupportedException();
        }
    }
    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    protected override void Dispose(bool disposing)
    {
        try
        {
            if (disposing)
            {
                _inner.Dispose();
            }
        }
        catch (IOException ex) when (ex is not EvidenceWriteException)
        {
            throw new EvidenceWriteException($"The journal could not be closed: {ex.Message}", ex);
        }
        finally { base.Dispose(disposing); }
    }
}
