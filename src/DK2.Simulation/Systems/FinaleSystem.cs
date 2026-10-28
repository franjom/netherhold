// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Simulation.Systems;

/// <summary>The finale Reaper's states 228-234 and the celebration's fireworks (<see cref="FinaleRules"/>).
/// Ours runs the states beside the other Reaper states, in the creature's update, and the fireworks
/// after the last creature's; DK2's player frame fires the fireworks, an order ours does not mirror.</summary>
public sealed class FinaleSystem : ICreatureUpdateStep
{
    public string Name => "finale";

    public void Execute(SimulationContext context) => CreatureUpdateSystem.RunAlone(context, this);

    public void Update(SimulationContext context, CreatureState creature) => FinaleRules.Run(context, creature);

    public void EndTurn(SimulationContext context) => FinaleRules.Fireworks(context);
}
