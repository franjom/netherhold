// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation.Systems;

/// <summary>Reconciles persistent Temple histories after room topology changes.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/sacrifice.md")]
public sealed class TempleSacrificeSystem : ISimulationSystem
{
    public string Name => "TempleSacrifice";

    public void Execute(SimulationContext context) =>
        TempleSacrificeRules.Reconcile(context.World, context.Database);
}
