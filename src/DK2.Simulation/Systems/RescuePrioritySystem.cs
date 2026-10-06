// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Simulation.Commands;

namespace DK2.Simulation.Systems;

/// <summary>Applies priority commands before this tick's worker selection (ADR-0261).</summary>
public sealed class RescuePrioritySystem : ISimulationSystem
{
    public string Name => "rescue_priority";

    public void Execute(SimulationContext context)
    {
        foreach (SetRescuePriorityCommand mark in context.Commands<SetRescuePriorityCommand>())
        {
            if (!context.World.PlayerIds.Contains(mark.PlayerId)) continue;
            CreatureState? creature = context.World.CreatureById(mark.CreatureId);
            if (creature is not null) RescuePriorityRules.Set(creature, mark.PlayerId, mark.Enabled);
        }
    }
}
