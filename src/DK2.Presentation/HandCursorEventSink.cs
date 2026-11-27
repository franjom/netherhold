// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Collections.Concurrent;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>Plays local drop feedback only after the simulation accepts the release.</summary>
public sealed class HandCursorEventSink : ISimulationEventSink
{
    private readonly HandCursorFeedback _feedback;
    private readonly PlayerId _viewer;
    private readonly ISimulationEventSink? _downstream;

    public HandCursorEventSink(HandCursorFeedback feedback, PlayerId viewer,
        ISimulationEventSink? downstream = null)
    {
        _feedback = feedback;
        _viewer = viewer;
        _downstream = downstream;
    }

    private readonly ConcurrentQueue<HandDropFeedback> _pending = new ConcurrentQueue<HandDropFeedback>();

    public void Emit(in SimEvent simEvent)
    {
        if (simEvent.HandDrop is HandDropFeedback drop && drop.Player == _viewer)
        {
            _pending.Enqueue(drop);
        }

        _downstream?.Emit(simEvent);
    }

    /// <summary>Apply accepted drops on the presentation thread, using its animation clock.</summary>
    public void Drain(long milliseconds)
    {
        while (_pending.TryDequeue(out HandDropFeedback drop))
        {
            _feedback.Play(drop.Gold ? HandCursorKind.DropGold : HandCursorKind.DropThing, milliseconds);
        }
    }
}
