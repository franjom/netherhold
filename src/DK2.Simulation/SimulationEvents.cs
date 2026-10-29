// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>A hand release accepted by the drop rules; presentation only.</summary>
public readonly record struct HandDropFeedback(PlayerId Player, bool Gold);

/// <summary>Something the simulation did, recorded for observation only.</summary>
/// <remarks>Events are the harness's and the oracle comparison's window into the simulation,
/// and are strictly write-only: nothing in the simulation ever reads an event back, so
/// attaching a recorder cannot change behaviour. Asserted by
/// <c>DeterminismTests.EventRecording_DoesNotAffectState</c>.</remarks>
/// <param name="Tick">The tick during which it happened.</param>
/// <param name="System">Which system emitted it.</param>
/// <param name="Kind">Stable machine-readable event name.</param>
/// <param name="Detail">Human-readable specifics; never parsed by the simulation.</param>
public readonly record struct SimEvent(GameTick Tick, string System, string Kind, string Detail)
{
    public HandDropFeedback? HandDrop { get; init; }
    public RoomConstructionEffect? ConstructionEffect { get; init; }
    public ViewEffectRequest? EffectRequest { get; init; }
    public SightRegionRequest? SightRegion { get; init; }
    public ScriptedViewRequest? ScriptedView { get; init; }
    public override string ToString() => $"{Tick} [{System}] {Kind}: {Detail}";
}

public interface ISimulationEventSink
{
    void Emit(in SimEvent simEvent);
}

/// <summary>Discards everything. The default, so events cost nothing when unobserved.</summary>
public sealed class NullEventSink : ISimulationEventSink
{
    public static readonly NullEventSink Instance = new();

    private NullEventSink() { }

    public void Emit(in SimEvent simEvent) { }
}

/// <summary>Keeps every event in memory. Used by tests and the scenario harness.</summary>
public sealed class ListEventSink : ISimulationEventSink
{
    private readonly List<SimEvent> _events = [];

    public IReadOnlyList<SimEvent> Events => _events;

    public void Emit(in SimEvent simEvent) => _events.Add(simEvent);

    public void Clear() => _events.Clear();
}
