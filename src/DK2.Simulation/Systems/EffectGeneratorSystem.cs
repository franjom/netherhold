// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation.Systems;

/// <summary>The thing pass's last list, kind 7 (<c>0x004B19A0</c> links it): every effect generator's
/// update, newest first, so the level-start generator before the THINGS ones (ADR-0648).</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/hero-parties-and-effect-generators.md")]
public sealed class EffectGeneratorSystem : ISimulationSystem
{
    public string Name => "effect generator";

    public void Execute(SimulationContext context)
    {
        IReadOnlyList<EffectGeneratorState> generators = context.World.EffectGenerators;
        if (generators.Count == 0) return;
        context.Navigation.Refresh();
        for (int i = generators.Count - 1; i >= 0; i--) EffectGeneratorRules.Tick(context, generators[i]);
    }
}
