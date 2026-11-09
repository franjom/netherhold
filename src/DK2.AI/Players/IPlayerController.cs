// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Simulation.Players;

namespace DK2.AI.Players;

/// <summary>A replaceable player controller: a scripted bot, an external agent or a future keeper.
/// It sees only <see cref="PlayerObservation"/>s and never a world; its memory stays outside the
/// simulation. Construction receives the run and player identity. The host numbers a run's actions
/// 1, 2, ... in the order decisions list them, so receipts can be matched to the actions sent.</summary>
public interface IPlayerController
{
    PlayerDecision Decide(PlayerObservation observation);
}

/// <summary>One decision: the actions to request now, how many ticks to advance before the next
/// decision, and a short reason for the decision log.</summary>
public sealed class PlayerDecision
{
    /// <summary>A reason is a log line, not a transcript.</summary>
    public const int MaxReasonLength = 256;

    public PlayerDecision(IEnumerable<PlayerAction> actions, int advanceTicks, string reason)
    {
        if (actions is null) throw new ArgumentNullException(nameof(actions));
        if (advanceTicks <= 0)
            throw new ArgumentOutOfRangeException(nameof(advanceTicks), advanceTicks, "A decision advances at least one tick.");
        if (reason is null) throw new ArgumentNullException(nameof(reason));
        if (reason.Length > MaxReasonLength)
            throw new ArgumentException($"A reason has at most {MaxReasonLength} characters.", nameof(reason));
        PlayerAction[] copy = actions.ToArray();
        foreach (PlayerAction action in copy)
            if (action is null) throw new ArgumentException("A decision cannot hold a null action.", nameof(actions));
        Actions = Array.AsReadOnly(copy);
        AdvanceTicks = advanceTicks;
        Reason = reason;
    }

    /// <summary>Requests nothing and waits <paramref name="advanceTicks"/>.</summary>
    public static PlayerDecision Wait(int advanceTicks, string reason) => new([], advanceTicks, reason);

    /// <summary>The controller cannot go on: the host ends the run as a controller limitation, a
    /// diagnostic result rather than an engine failure.</summary>
    public static PlayerDecision Limited(string reason) => new([], 1, reason) { IsLimitation = true };

    public bool IsLimitation { get; private init; }

    public IReadOnlyList<PlayerAction> Actions { get; }
    public int AdvanceTicks { get; }
    public string Reason { get; }
}
