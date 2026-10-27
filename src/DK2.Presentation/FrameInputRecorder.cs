// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>
/// The bookkeeping of one journaled frame (ADR-0553 Phase 1): which commands of the host's pending
/// queue are new, when they were queued relative to the frame's tick hand-off, the intents built,
/// and the ticks run. The host owns the queue and calls in order: <see cref="BeginFrame"/>,
/// any <see cref="Intent"/>, at most one <see cref="HandOff"/> just before it clears the queue,
/// <see cref="Ticks"/>, then <see cref="EndFrame"/>. Main thread only.
/// </summary>
public sealed class FrameInputRecorder
{
    private readonly List<PlayerIntent> _intents = [];
    private readonly List<QueuedCommand> _commands = [];
    private FrameInputRecord? _frame;
    private int _seen;
    private bool _handedOff;
    private int _handed;

    /// <summary>The frame begun and not yet ended, with what it has gathered so far; null between frames.</summary>
    public FrameInputRecord? Open => _frame == null ? null : Gathered(_frame);

    /// <summary>Starts a frame from the host's raw sample. Commands queued since the last frame
    /// ended came from callbacks between frames.</summary>
    public void BeginFrame(FrameInputRecord raw, IReadOnlyList<PlayerCommand> pending)
    {
        _intents.Clear();
        _commands.Clear();
        _handedOff = false;
        _handed = 0;
        Take(pending, CommandQueuedWhen.BeforeUpdate);
        _frame = raw;
    }

    public void Intent(in PlayerIntent intent)
    {
        if (_frame != null) _intents.Add(intent);
    }

    /// <summary>The whole pending queue is about to go to this frame's first tick and be cleared.</summary>
    public void HandOff(IReadOnlyList<PlayerCommand> pending)
    {
        if (_frame == null) return;
        Take(pending, CommandQueuedWhen.DuringFrame);
        _handed = pending.Count;
        _handedOff = true;
        _seen = 0;
    }

    public void Ticks(GameTick start, int ticks)
    {
        if (_frame != null) _frame = _frame with { TickAtStart = start, TicksRun = ticks };
    }

    /// <summary>Ends the frame and returns its record, or null if none was begun.</summary>
    public FrameInputRecord? EndFrame(IReadOnlyList<PlayerCommand> pending, double tickDebt)
    {
        if (_frame == null) return null;
        Take(pending, _handedOff ? CommandQueuedWhen.AfterHandOff : CommandQueuedWhen.DuringFrame);
        FrameInputRecord done = Gathered(_frame) with { TickDebtAfter = tickDebt };
        _frame = null;
        return done;
    }

    private void Take(IReadOnlyList<PlayerCommand> pending, CommandQueuedWhen when)
    {
        // A queue shorter than seen was cleared by someone other than HandOff; start over.
        if (pending.Count < _seen) _seen = 0;
        for (int i = _seen; i < pending.Count; i++) _commands.Add(new QueuedCommand(when, pending[i]));
        _seen = pending.Count;
    }

    private FrameInputRecord Gathered(FrameInputRecord frame) =>
        frame with { Intents = _intents.ToArray(), Commands = _commands.ToArray(), CommandsHandedToTick = _handed };
}
