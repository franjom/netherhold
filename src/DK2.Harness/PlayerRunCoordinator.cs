// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Globalization;
using DK2.AI.Players;
using DK2.Core;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;
using DK2.Simulation.Players;

namespace DK2.Harness;

/// <summary>A controlled-player run (campaign player plan, task 4). Limits are test policy, not
/// DK2 rules; task 6 exposes them on the command line.</summary>
public sealed record PlayerRunOptions
{
    public PlayerRunOptions(IPlayerController controller, PlayerId player, PlayerRunId runId)
    {
        Controller = controller ?? throw new ArgumentNullException(nameof(controller));
        if (!runId.IsValid) throw new ArgumentException("A player run needs a run id.", nameof(runId));
        Player = player;
        RunId = runId;
    }

    public IPlayerController Controller { get; }
    public PlayerId Player { get; }
    public PlayerRunId RunId { get; }

    /// <summary>The run stops after this many ticks even if the scenario lasts longer.</summary>
    public int MaxTicks { get; init; } = 14400;

    /// <summary>A decision may advance at most this many ticks.</summary>
    public int MaxAdvanceTicks { get; init; } = 40;

    /// <summary>Ticks simulated after a win, with no decisions, for the Horned Reaper finale to reach
    /// the gem; 0 skips it.</summary>
    public int FinaleTicks { get; init; } = 400;

    public PlayerSessionLimits Limits { get; init; } = new();

    /// <summary>A controller's name for the journal; the controller's type name by default.</summary>
    public string ControllerName => Controller.GetType().FullName ?? Controller.GetType().Name;

    internal void Validate()
    {
        if (MaxTicks <= 0) throw new ArgumentOutOfRangeException(nameof(MaxTicks));
        if (MaxAdvanceTicks <= 0) throw new ArgumentOutOfRangeException(nameof(MaxAdvanceTicks));
        if (FinaleTicks < 0) throw new ArgumentOutOfRangeException(nameof(FinaleTicks));
    }
}

/// <summary>Why a player run stopped.</summary>
public enum PlayerRunStop : byte
{
    /// <summary>Still running, or the scenario's duration or the tick limit ended it with no result.</summary>
    None,
    Won,
    Defeated,
    ControllerLimited,
    Cancelled,
}

/// <summary>What the post-win window saw of the Horned Reaper finale.</summary>
public enum PlayerFinaleCoverage : byte
{
    /// <summary>No win, or the window was 0.</summary>
    Unchecked,
    /// <summary>The Reaper picked up the gem (<see cref="PlayerRunCoordinator.FinalePickupTick"/>).</summary>
    ReachedPickup,
    /// <summary>The window ended first: a suspected anomaly, not a defeat.</summary>
    NotReached,
    /// <summary>The level has no Portal Gem for the Reaper to fetch (Levels 10, 11A-C, 12, 13, 17:
    /// campaign-win-sequence.md §2), or the finale never began for this player.</summary>
    NotApplicable,
    /// <summary>The run's tick budget ended the window first; the win stands, the finale is unchecked.</summary>
    CutShort,
}

/// <summary>One decision, for the run's decision log.</summary>
public sealed record PlayerDecisionRecord(GameTick Tick, PlayerRevision Revision, int AdvanceTicks, string Reason,
    IReadOnlyList<PlayerRequest> Requests, IReadOnlyList<PlayerReceipt> Receipts, int Commands);

/// <summary>
/// Drives one controlled player inside <see cref="ScenarioRunner"/>'s single tick loop. After each
/// completed tick it watches the player's own result; when the controller's advance has run out it
/// captures an observation, asks for a decision, submits the requests and drains the admitted
/// commands for the next input frame (a decision after input tick t sees stamp t+1 and its commands
/// enter frame t+1). After a win it stops deciding, refuses everything, and simulates the finale
/// window; a defeat or a controller limitation stops the run at once. A controller that throws or
/// returns nothing is a controller limitation, never an engine failure. Replay never creates one.
/// Not thread-safe: the host's loop calls it serially, <see cref="Cancel"/> included.
/// </summary>
public sealed class PlayerRunCoordinator
{
    private readonly PlayerRunOptions _options;
    private readonly PlayerObservationBuilder _builder;
    private readonly PlayerSession _session;
    private readonly List<PlayerDecisionRecord> _decisions = [];
    private long _revision, _nextActionId = 1;
    private long _nextDecisionTick;
    private long _finaleEndTick = -1;

    public PlayerRunCoordinator(GameDatabase database, PlayerRunOptions options)
    {
        if (database is null) throw new ArgumentNullException(nameof(database));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        options.Validate();
        _builder = new PlayerObservationBuilder(database, options.Player);
        _session = new PlayerSession(options.RunId, options.Player, new PlayerActionAdapter(database, options.Player), options.Limits);
    }

    public PlayerRunOptions Options => _options;
    public PlayerSession Session => _session;
    public IReadOnlyList<PlayerDecisionRecord> Decisions => _decisions;

    public PlayerRunStop Stop { get; private set; }
    public bool Stopped => Stop != PlayerRunStop.None;
    /// <summary>The controller limitation that stopped the run, if one did.</summary>
    public string? Limitation { get; private set; }
    /// <summary>The input tick whose completion showed the player's result, if it ended.</summary>
    public GameTick? ResultTick { get; private set; }
    public PlayerFinaleCoverage Finale { get; private set; }
    public GameTick? FinalePickupTick { get; private set; }

    /// <summary>Called after input tick <paramref name="completedTick"/> completed. Returns the
    /// commands for the next input frame; with <paramref name="anotherTick"/> false it only records the
    /// result, since no frame follows to carry a decision.</summary>
    public IReadOnlyList<PlayerCommand> AfterTick(WorldState world, GameTick completedTick, bool anotherTick = true)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        if (Stopped) return [];
        ref readonly PlayerState player = ref world.Player(_options.Player);

        if (_finaleEndTick >= 0) return FinaleTick(player, completedTick);
        if (player.Status != PlayerStatus.Playing)
        {
            ResultTick = completedTick;
            _session.Cancel();
            if (player.Status != PlayerStatus.Won) return Halt(PlayerRunStop.Defeated);
            if (_options.FinaleTicks == 0) return Halt(PlayerRunStop.Won);
            _finaleEndTick = completedTick.Value + _options.FinaleTicks;
            return FinaleTick(player, completedTick);
        }

        if (!anotherTick || completedTick.Value < _nextDecisionTick) return [];
        return Decide(world, completedTick);
    }

    /// <summary>Ends the run when the loop does: a win whose finale window was still open stands as
    /// Won (<see cref="PlayerFinaleCoverage.CutShort"/>); a run that reached its tick budget without a
    /// result keeps <see cref="PlayerRunStop.None"/>. Anything queued is withdrawn.</summary>
    public IReadOnlyList<PlayerReceipt> Finish()
    {
        if (!Stopped && _finaleEndTick >= 0) CutFinaleShort();
        return _session.Cancel();
    }

    /// <summary>Stops the run on an outside request (task 6's cancel control). A win already shown
    /// stands; the finale is then cut short.</summary>
    public IReadOnlyList<PlayerReceipt> Cancel()
    {
        if (!Stopped)
        {
            if (_finaleEndTick >= 0) CutFinaleShort();
            else Stop = PlayerRunStop.Cancelled;
        }
        return _session.Cancel();
    }

    private void CutFinaleShort()
    {
        Finale = PlayerFinaleCoverage.CutShort;
        Stop = PlayerRunStop.Won;
    }

    private IReadOnlyList<PlayerCommand> Decide(WorldState world, GameTick completedTick)
    {
        var stamp = new PlayerStamp(_options.RunId, _options.Player, world.CurrentTick, new PlayerRevision(++_revision));
        PlayerObservationCapture capture = _builder.Capture(world, stamp, _session.TakeReceipts());
        _session.Observe(capture);

        PlayerDecision? decision;
        try { decision = _options.Controller.Decide(capture.Observation); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Limit($"the controller threw {ex.GetType().Name}: {ex.Message}");
        }
        if (decision is null) return Limit("the controller returned no decision");
        if (decision.IsLimitation) return Limit(decision.Reason);
        if (decision.AdvanceTicks > _options.MaxAdvanceTicks)
            return Limit($"advance of {decision.AdvanceTicks} ticks exceeds {_options.MaxAdvanceTicks}");
        if (decision.Actions.Count > _options.Limits.MaxRequestsPerObservation)
            return Limit($"{decision.Actions.Count} actions in one decision exceed {_options.Limits.MaxRequestsPerObservation}");

        var requests = new PlayerRequest[decision.Actions.Count];
        var receipts = new PlayerReceipt[requests.Length];
        for (int i = 0; i < requests.Length; i++)
        {
            requests[i] = new PlayerRequest(stamp, new PlayerActionId(_nextActionId++), decision.Actions[i]);
            receipts[i] = _session.Submit(requests[i]);
        }
        // Commands built from this observation enter the very next frame (task 3's contract).
        IReadOnlyList<PlayerCommand> commands = _session.Drain(world.CurrentTick);
        // The log keeps each request's receipt as the session now holds it (Submitted, with its frame).
        for (int i = 0; i < receipts.Length; i++)
            if (_session.TryGetReceipt(requests[i].Id, out PlayerReceipt held)) receipts[i] = held;
        _decisions.Add(new PlayerDecisionRecord(stamp.Tick, stamp.Revision, decision.AdvanceTicks, decision.Reason,
            requests, receipts, commands.Count));
        _nextDecisionTick = completedTick.Value + decision.AdvanceTicks;
        return commands;
    }

    private IReadOnlyList<PlayerCommand> FinaleTick(in PlayerState player, GameTick completedTick)
    {
        PlayerFinale finale = player.Finale;
        if (finale.PickupTick > 0)
        {
            Finale = PlayerFinaleCoverage.ReachedPickup;
            FinalePickupTick = new GameTick(finale.PickupTick);
            return Halt(PlayerRunStop.Won);
        }
        // The finale begins on the verdict's own tick (FinaleRules.OnVerdict); with no gem it never
        // records one, and no Reaper will come for it.
        if (finale.GemObjectId == 0)
        {
            Finale = PlayerFinaleCoverage.NotApplicable;
            return Halt(PlayerRunStop.Won);
        }
        if (completedTick.Value >= _finaleEndTick)
        {
            Finale = PlayerFinaleCoverage.NotReached;
            return Halt(PlayerRunStop.Won);
        }
        return [];
    }

    private IReadOnlyList<PlayerCommand> Limit(string why)
    {
        Limitation = why;
        _session.Cancel();
        return Halt(PlayerRunStop.ControllerLimited);
    }

    private IReadOnlyList<PlayerCommand> Halt(PlayerRunStop stop)
    {
        Stop = stop;
        return [];
    }

    /// <summary>The journal's end reason: "completed" only for a win (task 4), so a replay or a
    /// listing can never take another outcome for a pass.</summary>
    public string EndReason(int ticksRun) => Stop switch
    {
        PlayerRunStop.Won => "completed",
        PlayerRunStop.Defeated => "player:defeated",
        PlayerRunStop.ControllerLimited => $"player:controller-limited: {Limitation}",
        PlayerRunStop.Cancelled => "player:cancelled",
        _ => string.Create(CultureInfo.InvariantCulture, $"player:no-result after {ticksRun} ticks"),
    };

    /// <summary>Additive journal header keys describing this run (the scenario's keys stay as they are).</summary>
    public IReadOnlyDictionary<string, string> JournalKeys() => new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [JournalHeader.PlayerRunSchema] = "1",
        ["player.run.id"] = _options.RunId.Value,
        ["player.id"] = _options.Player.ToString(),
        ["player.controller"] = _options.ControllerName,
        ["player.observation.schema"] = PlayerObservation.SchemaVersion.ToString(CultureInfo.InvariantCulture),
        ["player.visibility"] = "human view: WorldView fog gate, input targets, own HUD and messages",
        ["player.limits"] = string.Create(CultureInfo.InvariantCulture,
            $"max_ticks={_options.MaxTicks};max_advance={_options.MaxAdvanceTicks};finale_ticks={_options.FinaleTicks};" +
            $"requests_per_decision={_options.Limits.MaxRequestsPerObservation};max_actions={_options.Limits.MaxAdmittedActions}"),
        ["player.pacing"] = "headless: the simulation waits at completed ticks for each decision",
    };
}
