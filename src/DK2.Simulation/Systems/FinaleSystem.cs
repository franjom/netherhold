// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Simulation.Systems;

/// <summary>The finale Reaper's states 228-234 and the celebration's fireworks (<see cref="FinaleRules"/>).
/// Ours runs them beside the other Reaper states, before the mover; DK2's player frame fires the
/// fireworks, an order ours does not mirror.</summary>
public sealed class FinaleSystem : ISimulationSystem
{
    public string Name => "finale";

    public void Execute(SimulationContext context)
    {
        FinaleRules.Run(context);
        FinaleRules.Fireworks(context);
    }
}
