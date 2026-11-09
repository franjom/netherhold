// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Globalization;
using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Players;

/// <summary>One player run: observations and requests from another run are refused, never retargeted.
/// It names manifests and evidence folders, so it is 1-64 characters of <c>[A-Za-z0-9._-]</c>.
/// <c>default</c> is not a run: <see cref="IsValid"/> is false and the host refuses it.</summary>
public readonly record struct PlayerRunId
{
    public const int MaxLength = 64;

    private readonly string? _value;

    public PlayerRunId(string value)
    {
        if (value is null) throw new ArgumentNullException(nameof(value));
        if (value.Length is 0 or > MaxLength)
            throw new ArgumentException($"A run id has 1-{MaxLength} characters.", nameof(value));
        foreach (char c in value)
            if (!(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '_' or '-'))
                throw new ArgumentException("A run id uses only letters, digits, '.', '_' and '-'.", nameof(value));
        _value = value;
    }

    public string Value => _value ?? "";

    public bool IsValid => _value is not null;

    public override string ToString() => Value;
}

/// <summary>A controller's request identity, unique within its run; a retried request repeats it.
/// <c>default</c> (0) is not an id: <see cref="IsValid"/> is false.</summary>
public readonly record struct PlayerActionId
{
    public PlayerActionId(long value)
    {
        if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value), value, "Action ids start at 1.");
        Value = value;
    }

    public long Value { get; }

    public bool IsValid => Value > 0;

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>The completed-tick observation a request depends on; increases by one per capture.
/// <c>default</c> (0) is not a revision: <see cref="IsValid"/> is false.</summary>
public readonly record struct PlayerRevision : IComparable<PlayerRevision>
{
    public PlayerRevision(long value)
    {
        if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value), value, "Revisions start at 1.");
        Value = value;
    }

    public long Value { get; }

    public bool IsValid => Value > 0;

    public int CompareTo(PlayerRevision other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>An opaque name for a visible thing, valid only in the observation that issued it. It is
/// the thing's position in that observation's list, never an entity slot, so a reused slot cannot
/// make an old handle name a different thing.</summary>
public readonly record struct PlayerThingHandle
{
    public PlayerThingHandle(PlayerRevision revision, int ordinal)
    {
        if (!revision.IsValid) throw new ArgumentOutOfRangeException(nameof(revision), "A handle needs a revision.");
        if (ordinal < 0) throw new ArgumentOutOfRangeException(nameof(ordinal), ordinal, "Ordinals start at 0.");
        Revision = revision;
        Ordinal = ordinal;
    }

    public PlayerRevision Revision { get; }
    public int Ordinal { get; }

    public bool IsValid => Revision.IsValid;

    public override string ToString() => $"{Revision}:{Ordinal}";
}

/// <summary>What an observation or request belongs to: run, player, completed tick and revision.</summary>
public readonly record struct PlayerStamp(PlayerRunId RunId, PlayerId Player, GameTick Tick, PlayerRevision Revision)
{
    /// <summary>False for <c>default</c> or a stamp with a default run or revision.</summary>
    public bool IsValid => RunId.IsValid && Revision.IsValid;
}

/// <summary>Where the player's level is: a skippable intro camera path, ordinary play, or ended.</summary>
public enum PlayerPhase : byte { Intro, Playing, Ended }

/// <summary>The player's own result, with <see cref="PlayerStatus"/>'s codes.</summary>
public enum PlayerResult : byte
{
    Playing = PlayerStatus.Playing,
    Conquered = PlayerStatus.Conquered,
    Won = PlayerStatus.Won,
    Lost = PlayerStatus.Lost,
}

/// <summary>Submitted means the command entered a recorded input frame, not that the work succeeded.</summary>
public enum PlayerReceiptStatus : byte { Queued, Submitted, Refused, Cancelled }

/// <summary>Why a request was refused or cancelled. Every reason is decided from the run, the request
/// and the admitted observation, so none can tell two hidden states apart.</summary>
public enum PlayerRefusalReason : byte
{
    None,
    WrongRun,
    WrongPlayer,
    StaleObservation,
    FutureObservation,
    ConflictingId,
    UnknownHandle,
    NotOffered,
    OutOfBounds,
    WrongPhase,
    Terminal,
    TooManyRequests,
    ActionBudgetExhausted,
    RunCancelled,
}

/// <summary>One controller request against the observation it names. Identities may be
/// <c>default</c> (the host refuses those as a wrong run); the action may not be null.</summary>
public sealed record PlayerRequest
{
    public PlayerRequest(PlayerStamp basedOn, PlayerActionId id, PlayerAction action)
    {
        BasedOn = basedOn;
        Id = id;
        Action = action; // the init accessor rejects null, here and in `with`
    }

    public PlayerStamp BasedOn { get; init; }
    public PlayerActionId Id { get; init; }
    public PlayerAction Action { get; init => field = value ?? throw new ArgumentNullException(nameof(value)); }
}

/// <summary>The host's answer to a request. Refused and Cancelled carry a reason, the others none;
/// <see cref="ScheduledTick"/> is set exactly when Submitted: the input tick the command entered.</summary>
public readonly record struct PlayerReceipt
{
    public PlayerReceipt(PlayerActionId id, PlayerReceiptStatus status,
        PlayerRefusalReason reason = PlayerRefusalReason.None, GameTick? scheduledTick = null)
    {
        bool refusal = status is PlayerReceiptStatus.Refused or PlayerReceiptStatus.Cancelled;
        if (refusal == (reason == PlayerRefusalReason.None))
            throw new ArgumentException($"A {status} receipt {(refusal ? "needs" : "takes no")} reason.", nameof(reason));
        if ((status == PlayerReceiptStatus.Submitted) != scheduledTick.HasValue)
            throw new ArgumentException("Only a Submitted receipt has a scheduled tick.", nameof(scheduledTick));
        Id = id;
        Status = status;
        Reason = reason;
        ScheduledTick = scheduledTick;
    }

    public PlayerActionId Id { get; }
    public PlayerReceiptStatus Status { get; }
    public PlayerRefusalReason Reason { get; }
    public GameTick? ScheduledTick { get; }
}
