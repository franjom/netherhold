// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation.Systems;

/// <summary>DK2's state 96, the chicken (keeper-spell-inferno-chicken.md §3): a creature standing in
/// it runs <see cref="ChickenRules.Handle"/> every tick, and one walking on its behalf hands back to
/// it when the route is done, so the handler runs again the tick after. Held and fallen creatures are
/// left alone. The timer is counted down by <see cref="CreatureStatusSystem"/>.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/keeper-spell-inferno-chicken.md")]
public sealed class ChickenSystem : ISimulationSystem
{
    private const string SystemName = "chicken";

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        IReadOnlyList<CreatureState> creatures = context.World.Creatures;
        for (int i = 0; i < creatures.Count; i++)
        {
            CreatureState creature = creatures[i];
            if (CreatureEligibility.IsExcluded(creature, CreatureGuards.Sidelined)) continue;
            if (StateShadow.IsTravelling(creature))
            {
                if (creature.UtilityNewState != ChickenRules.ChickenState || creature.HasRoute) continue;
                StateShadow.Arrive(context, SystemName, creature);
                continue;
            }
            if (creature.Dk2StateId == ChickenRules.ChickenState) ChickenRules.Handle(context, creature);
        }
    }
}
