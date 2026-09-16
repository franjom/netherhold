// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation.Systems;

/// <summary>Reconciles persistent Graveyard tallies after room topology changes.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/creature-death.md")]
public sealed class GraveyardSystem : ISimulationSystem
{
    public string Name => "Graveyard";

    public void Execute(SimulationContext context) =>
        GraveyardRules.Reconcile(context.World, context.Database);
}
