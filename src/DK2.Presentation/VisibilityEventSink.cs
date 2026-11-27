// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>Applies visibility changes before downstream events are filtered.
/// Caller exclusively owns the world and view during event delivery.</summary>
public sealed class VisibilityEventSink : ISimulationEventSink
{
    private readonly WorldView _view;
    private readonly Func<WorldState> _world;
    private readonly PlayerId _viewer;
    private readonly ISimulationEventSink? _downstream;

    public VisibilityEventSink(WorldView view, Func<WorldState> world, PlayerId viewer,
        ISimulationEventSink? downstream = null)
    {
        _view = view;
        _world = world;
        _viewer = viewer;
        _downstream = downstream;
    }

    /// <summary>Over one world; the runner passes the world its frame reads, which changes
    /// instance with the presented world (ADR-0694).</summary>
    public VisibilityEventSink(WorldView view, WorldState world, PlayerId viewer, ISimulationEventSink? downstream = null)
        : this(view, () => world, viewer, downstream)
    {
    }

    public void Emit(in SimEvent simEvent)
    {
        WorldState current = _world();
        _view.SetPossessionView(current.Player(_viewer).PossessedCreatureId != 0);
        _view.ApplySightRegion(simEvent, current);
        _downstream?.Emit(simEvent);
    }
}
