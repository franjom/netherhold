// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Systems;

/// <summary>The keeper's own frame choosing what to research: DK2's per-turn player update
/// (<c>0x004BB2A0</c>, at <c>0x004BB62C</c>) calls the chooser <b>whenever the target is 0</b>.
/// The adder never chooses from nothing (<see cref="ResearchTreeRules.Advance"/> returns at
/// once on a 0 target), so without this a level would never start researching, and an item
/// that becomes researchable later — a trigger, a lost book — would never be picked up.
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/research-tree.md")]
public sealed class ResearchSystem : ISimulationSystem
{
    public string Name => "Research";

    public void Execute(SimulationContext context)
    {
        WorldState world = context.World;
        for (int i = 0; i < world.PlayerCount; i++)
        {
            ref PlayerState player = ref world.PlayerAtIndex(i);
            if (!PlayerIds.IsKeeper(player.PlayerId.Value) || player.ResearchTarget != 0) continue;
            ResearchTreeRules.ChooseTarget(context, player.PlayerId);
        }
    }
}
