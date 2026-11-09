// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;
using DK2.Simulation.Commands;
using DK2.Simulation.Players;

namespace DK2.Harness;

/// <summary>Bounds on one player session: new requests per observation, and actions admitted over
/// the run. Test policy, not DK2 rules (campaign player plan, task 6 defaults).</summary>
public sealed record PlayerSessionLimits
{
    public PlayerSessionLimits(int maxRequestsPerObservation = 4, int maxAdmittedActions = 4096)
    {
        if (maxRequestsPerObservation <= 0) throw new ArgumentOutOfRangeException(nameof(maxRequestsPerObservation));
        if (maxAdmittedActions <= 0) throw new ArgumentOutOfRangeException(nameof(maxAdmittedActions));
        MaxRequestsPerObservation = maxRequestsPerObservation;
        MaxAdmittedActions = maxAdmittedActions;
    }

    public int MaxRequestsPerObservation { get; }
    public int MaxAdmittedActions { get; }
}

/// <summary>
/// Admits one controlled player's requests against the observation they name and hands the
/// resulting commands to the input frame after it (campaign player plan, task 3). Checks never
/// consult a world: run and player identity; then the ledger (an exact repeat returns its receipt,
/// the same id with other content is refused); then, for a new id, freshness, the end of the game,
/// bounds and the <see cref="PlayerActionAdapter"/>. Only requests judged on the current observation
/// enter the ledger (a cancelled one stays there: retry under a new id); a transient refusal (wrong
/// run or player, stale, future, too many, cancelled run) is answered and may reuse its id. Queued
/// commands carry the entity ids of their observation, so they leave only through <see cref="Drain"/>
/// before the next <see cref="Observe"/>, which cancels any still queued. Not thread-safe.
/// </summary>
public sealed class PlayerSession
{
    private sealed class Entry(PlayerRequest request, PlayerReceipt receipt, IReadOnlyList<PlayerCommand> commands)
    {
        public PlayerRequest Request { get; } = request;
        public PlayerReceipt Receipt { get; set; } = receipt;
        /// <summary>Released once drained or cancelled, so the ledger keeps only what retries need.</summary>
        public IReadOnlyList<PlayerCommand> Commands { get; set; } = commands;
    }

    private readonly PlayerRunId _run;
    private readonly PlayerId _player;
    private readonly PlayerActionAdapter _adapter;
    private readonly PlayerSessionLimits _limits;
    private readonly Dictionary<PlayerActionId, Entry> _ledger = [];
    private readonly List<Entry> _queued = [];
    private readonly List<PlayerReceipt> _changed = [];
    private PlayerObservationCapture? _current;
    private GameTick? _lastDrain;
    private int _requestsOnCurrent, _admitted;

    public PlayerSession(PlayerRunId runId, PlayerId player, PlayerActionAdapter adapter, PlayerSessionLimits? limits = null)
    {
        if (!runId.IsValid) throw new ArgumentException("A session needs a run id.", nameof(runId));
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        if (adapter.Player != player) throw new ArgumentException("The adapter serves another player.", nameof(adapter));
        _run = runId;
        _player = player;
        _limits = limits ?? new PlayerSessionLimits();
    }

    public PlayerRunId RunId => _run;
    public PlayerId Player => _player;
    public bool IsCancelled { get; private set; }
    public int QueuedCount => _queued.Count;
    public int AdmittedCount => _admitted;

    /// <summary>The observation requests are now judged against. Revisions only increase; requests
    /// still queued from the previous observation are cancelled as stale, never carried forward, and
    /// those receipts are returned (they also reach the following observation).</summary>
    public IReadOnlyList<PlayerReceipt> Observe(PlayerObservationCapture capture)
    {
        if (capture is null) throw new ArgumentNullException(nameof(capture));
        PlayerStamp stamp = capture.Observation.Stamp;
        if (stamp.RunId != _run || stamp.Player != _player)
            throw new ArgumentException("The capture belongs to another run or player.", nameof(capture));
        if (_current?.Observation.Stamp.Revision is PlayerRevision last && stamp.Revision <= last)
            throw new ArgumentException($"Revision {stamp.Revision} does not follow {last}.", nameof(capture));
        IReadOnlyList<PlayerReceipt> withdrawn = Withdraw(PlayerRefusalReason.StaleObservation);
        _current = capture;
        _requestsOnCurrent = 0;
        return withdrawn;
    }

    /// <summary>Receipts issued or changed since the last call, in order, for the next observation.
    /// One id may appear twice (Queued, then Submitted). The host bounds requests per decision, which
    /// bounds this list.</summary>
    public IReadOnlyList<PlayerReceipt> TakeReceipts()
    {
        if (_changed.Count == 0) return [];
        PlayerReceipt[] taken = [.. _changed];
        _changed.Clear();
        return taken;
    }

    public PlayerReceipt Submit(PlayerRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (!request.BasedOn.IsValid || request.BasedOn.RunId != _run) return Answer(request.Id, PlayerRefusalReason.WrongRun);
        if (request.BasedOn.Player != _player) return Answer(request.Id, PlayerRefusalReason.WrongPlayer);
        if (!request.Id.IsValid) return Answer(request.Id, PlayerRefusalReason.OutOfBounds);

        if (_ledger.TryGetValue(request.Id, out Entry? known))
            return known.Request == request ? known.Receipt : Answer(request.Id, PlayerRefusalReason.ConflictingId);

        if (IsCancelled) return Answer(request.Id, PlayerRefusalReason.RunCancelled);
        if (_current is null || request.BasedOn.Revision > _current.Observation.Stamp.Revision)
            return Answer(request.Id, PlayerRefusalReason.FutureObservation);
        if (request.BasedOn != _current.Observation.Stamp) return Answer(request.Id, PlayerRefusalReason.StaleObservation);
        if (_requestsOnCurrent >= _limits.MaxRequestsPerObservation) return Answer(request.Id, PlayerRefusalReason.TooManyRequests);
        _requestsOnCurrent++;

        PlayerRefusalReason refusal;
        IReadOnlyList<PlayerCommand> commands = [];
        if (_current.Observation.Phase == PlayerPhase.Ended) refusal = PlayerRefusalReason.Terminal;
        else if (_admitted >= _limits.MaxAdmittedActions) refusal = PlayerRefusalReason.ActionBudgetExhausted;
        else
        {
            PlayerActionResolution resolution = _adapter.Resolve(_current, request.Action);
            refusal = resolution.Refusal;
            commands = resolution.Commands;
            // Checked before anything is recorded, so a broken adapter cannot leave a Queued receipt behind.
            foreach (PlayerCommand command in commands)
                if (!IsHumanCommand(command))
                    throw new InvalidOperationException($"{command.GetType().Name} is not a command a human issues in play.");
        }

        PlayerReceipt receipt = refusal == PlayerRefusalReason.None
            ? new PlayerReceipt(request.Id, PlayerReceiptStatus.Queued)
            : new PlayerReceipt(request.Id, PlayerReceiptStatus.Refused, refusal);
        var entry = new Entry(request, receipt, commands);
        _ledger[request.Id] = entry;
        if (refusal == PlayerRefusalReason.None)
        {
            _queued.Add(entry);
            _admitted++;
        }
        _changed.Add(receipt);
        return receipt;
    }

    /// <summary>The queued commands, in request order, for the input frame applied at
    /// <paramref name="appliesAt"/>; each becomes Submitted with that tick. The host must put exactly
    /// this list in that frame. A second call is empty; ticks never decrease.</summary>
    public IReadOnlyList<PlayerCommand> Drain(GameTick appliesAt)
    {
        if (_lastDrain is GameTick last && appliesAt < last)
            throw new ArgumentOutOfRangeException(nameof(appliesAt), appliesAt, $"Drained at {last} already.");
        _lastDrain = appliesAt;
        if (_queued.Count == 0) return [];
        var commands = new List<PlayerCommand>(_queued.Count);
        foreach (Entry entry in _queued)
        {
            commands.AddRange(entry.Commands);
            entry.Commands = [];
            entry.Receipt = new PlayerReceipt(entry.Receipt.Id, PlayerReceiptStatus.Submitted, scheduledTick: appliesAt);
            _changed.Add(entry.Receipt);
        }
        _queued.Clear();
        return commands;
    }

    /// <summary>Withdraws every queued request and refuses all later ones. Commands already drained
    /// stay in their input frames.</summary>
    public IReadOnlyList<PlayerReceipt> Cancel()
    {
        IsCancelled = true;
        return Withdraw(PlayerRefusalReason.RunCancelled);
    }

    private IReadOnlyList<PlayerReceipt> Withdraw(PlayerRefusalReason reason)
    {
        if (_queued.Count == 0) return [];
        var withdrawn = new PlayerReceipt[_queued.Count];
        for (int i = 0; i < _queued.Count; i++)
        {
            Entry entry = _queued[i];
            entry.Commands = [];
            entry.Receipt = withdrawn[i] = new PlayerReceipt(entry.Receipt.Id, PlayerReceiptStatus.Cancelled, reason);
            _changed.Add(entry.Receipt);
        }
        _queued.Clear();
        return withdrawn;
    }

    /// <summary>A refusal decided before the request was judged on the current observation: answered,
    /// reported with the next receipts, never ledgered.</summary>
    private PlayerReceipt Answer(PlayerActionId id, PlayerRefusalReason reason)
    {
        var receipt = new PlayerReceipt(id, PlayerReceiptStatus.Refused, reason);
        _changed.Add(receipt);
        return receipt;
    }

    /// <summary>Ordinary play, or the intro skip: nothing else may enter a controlled player's frame.</summary>
    private static bool IsHumanCommand(PlayerCommand command) =>
        HumanCommandPolicy.AllowsInPlay(command) || command is SkipCameraPathCommand;
}
