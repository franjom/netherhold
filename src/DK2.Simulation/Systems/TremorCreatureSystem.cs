// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Simulation.Systems;

public sealed class TremorCreatureSystem : ICreatureUpdateStep
{
    public string Name => "tremor-creatures";
    public void Execute(SimulationContext context) => CreatureUpdateSystem.RunAlone(context, this);
    public void BeginTurn(SimulationContext context) => context.TremorRecoveryCompletedThisTick.Clear();
    public void Update(SimulationContext context, CreatureState creature) => TremorRules.RunRecovery(context, creature);
}
