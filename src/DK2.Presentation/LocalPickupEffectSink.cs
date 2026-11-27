// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Simulation;
namespace DK2.Presentation;
/// <summary>Consumes the authoritative echo of dust already shown for a local pickup.</summary>
public sealed class LocalPickupEffectSink : ISimulationEventSink
{
    private readonly LocalPickupFeedback _feedback;
    private readonly ISimulationEventSink _downstream;

    public LocalPickupEffectSink(LocalPickupFeedback feedback, ISimulationEventSink downstream)
    {
        _feedback = feedback;
        _downstream = downstream;
    }

    public void Emit(in SimEvent simEvent)
    {
        if (simEvent.System == "Hand" && simEvent.EffectRequest is { } effect && _feedback.ConsumeDust(effect))
        {
            return;
        }

        _downstream.Emit(simEvent);
    }
}
