// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Collections.Concurrent;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>Queues the simulation's events on the thread that emits them and replays them on the
/// frame's thread when the batch they belong to is taken over (ADR-0694): the sinks downstream
/// (sight regions, the hand's feedback, sounds, scripted views, construction particles) write the
/// view, which the frame reads meanwhile.</summary>
public sealed class DeferredEventSink(ISimulationEventSink downstream) : ISimulationEventSink
{
    private readonly ConcurrentQueue<SimEvent> _pending = new();

    public void Emit(in SimEvent simEvent) => _pending.Enqueue(simEvent);

    /// <summary>Hands every queued event to the downstream sink, in order. Frame thread only.</summary>
    public void Replay()
    {
        while (_pending.TryDequeue(out SimEvent simEvent)) downstream.Emit(simEvent);
    }
}
