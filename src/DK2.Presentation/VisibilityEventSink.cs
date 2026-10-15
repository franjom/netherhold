// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>Applies visibility changes before downstream events are filtered.
/// Caller exclusively owns the world and view during event delivery.</summary>
public sealed class VisibilityEventSink(WorldView view, WorldState world, PlayerId viewer,
    ISimulationEventSink? downstream = null) : ISimulationEventSink
{
    public void Emit(in SimEvent simEvent)
    {
        view.SetPossessionView(world.Player(viewer).PossessedCreatureId != 0);
        view.ApplySightRegion(simEvent, world);
        downstream?.Emit(simEvent);
    }
}
