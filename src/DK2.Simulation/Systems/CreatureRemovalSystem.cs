// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Systems;

/// <summary>The thing pass's second walk over the creature list (<c>0x004B7830</c>): a creature whose
/// health is below 1 goes through the bookkeeping (<c>0x004B5C30</c>, then slot <c>+0x30</c>
/// <c>0x0048AF80</c>). Ours keeps the collapse window in <see cref="CreatureHealthSystem"/>, where a
/// collapsed creature waits at health 0; here only the one DK2 removes outright with no window: a
/// creature a level script removed (action 35, byte 0, <see cref="CreatureState.DeathUncounted"/>),
/// standing or already down. A corpse is a separate thing in DK2 and is not met.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/trigger-action-arms.md")]
public sealed class CreatureRemovalSystem : ISimulationSystem
{
    private const string SystemName = "creature-removal";

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        WorldState world = context.World;
        for (int i = world.Creatures.Count - 1; i >= 0; i--)
        {
            CreatureState state = world.Creatures[i];
            if (!state.DeathUncounted || state.Health > 0 || CreatureDeathRules.IsCorpse(state)) continue;
            CreatureDef definition = context.Database.CreatureById(state.CreatureTypeId);
            CreatureHealthSystem.Die(context, SystemName, definition, state);
        }
    }
}
