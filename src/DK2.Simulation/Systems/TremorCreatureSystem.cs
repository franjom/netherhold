// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Simulation.Systems;

public sealed class TremorCreatureSystem : ISimulationSystem
{
    public string Name => "tremor-creatures";
    public void Execute(SimulationContext context) => TremorRules.RunRecovery(context);
}
