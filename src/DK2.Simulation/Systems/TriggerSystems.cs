// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation.Systems;

/// <summary>The list-5 action points, after creature updates (0x004B7830).</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/trigger-walkers.md")]
public sealed class ActionPointTriggerSystem : ISimulationSystem
{
    public string Name => "ActionPointTriggers";
    public void Execute(SimulationContext context) => context.Triggers?.RunAreas();
}

/// <summary>The player roots at the start of the player frame (0x004B9EEC).</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/trigger-walkers.md")]
public sealed class PlayerTriggerSystem : ISimulationSystem
{
    public string Name => "PlayerTriggers";
    public void Execute(SimulationContext context) => context.Triggers?.RunPlayers();
}
