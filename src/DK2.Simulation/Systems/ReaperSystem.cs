// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Simulation.Systems;

public sealed class ReaperSystem : ICreatureUpdateStep
{
    public string Name => "reaper";
    public void Execute(SimulationContext context) => CreatureUpdateSystem.RunAlone(context, this);
    public void Update(SimulationContext context, CreatureState creature) => ReaperRules.RunLifecycle(context, creature);
}
