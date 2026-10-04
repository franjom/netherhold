// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Simulation.Systems;

public sealed class ReaperRemovalSystem : ISimulationSystem
{
    public string Name => "reaper-removal";
    public void Execute(SimulationContext context) => ReaperRules.RemoveDeparted(context);
}
