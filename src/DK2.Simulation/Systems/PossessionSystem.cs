// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Simulation.Commands;
using DK2.Simulation.Combat;

namespace DK2.Simulation.Systems;

[Dk2Behavior(Confidence.StronglyInferred, "docs/research/keeper-spell-possession.md")]
public sealed class PossessionSystem : ISimulationSystem
{
    public string Name => "possession";

    public void Execute(SimulationContext context)
    {
        foreach (PlayerCommand command in context.Input.All)
            if (command is LeavePossessionCommand leave && context.World.HasPlayer(leave.PlayerId))
                PossessionRules.Leave(context, ref context.World.Player(leave.PlayerId));
        context.Navigation.Refresh();
        var sight = new WorldSight(context.Navigation, context.World, context.Database);
        foreach (CreatureState creature in context.World.Creatures)
            PossessionRules.RefreshInfluence(context, creature, sight);
    }
}
