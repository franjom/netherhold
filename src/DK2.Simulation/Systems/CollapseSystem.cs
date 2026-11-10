// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Systems;

/// <summary>Collapses the creatures damage has just brought to zero health. DK2's health routine does
/// it inside the damage call (<c>0x004B2FB5</c>, <c>0x0048B770</c>), so the fall's heading picks and the
/// gold drop draw there; ours runs after each damaging pass of the thing pass, which keeps those
/// draws ahead of the passes that follow (ADR-0732). A prisoner's death is the Prison's.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/death-fall-heading.md")]
public sealed class CollapseSystem : ISimulationSystem
{
    private readonly List<CreatureState> _due = [];

    public string Name => "Collapse";

    public void Execute(SimulationContext context)
    {
        _due.Clear();
        foreach (CreatureState creature in context.World.Creatures)
            if (creature.CollapseDue) _due.Add(creature);
        foreach (CreatureState creature in _due)
        {
            creature.CollapseDue = false;
            if (context.World.CreatureById(creature.Id) is null) continue;   // removed by an earlier death's triggers
            if (!CreatureDeathRules.HasCollapsed(creature) || CreatureDeathRules.IsDying(creature)
                || CreatureDeathRules.IsCorpse(creature) || PrisonRules.IsImprisoned(creature)) continue;
            CreatureDef definition = context.Database.CreatureById(creature.CreatureTypeId);
            CreatureHealthSystem.RunOutOfHealth(context, definition, creature);
        }
    }
}
