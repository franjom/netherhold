// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation.Systems;

/// <summary>The player update's manufacture loss, once a game second (<c>0x004C3340</c>, ADR-0203):
/// a keeper with points and no one manufacturing loses some.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/state-category-hooks.md")]
public sealed class ManufactureDecaySystem : ISimulationSystem
{
    public string Name => "ManufactureDecay";

    public void Execute(SimulationContext context)
    {
        if ((context.Tick.Value + 1) % SimulationTiming.TicksPerSecond == 0)
            ManufactureRules.Decay(context.World, context.Database);
    }
}
