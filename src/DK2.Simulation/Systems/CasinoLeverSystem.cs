// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation.Systems;

/// <summary>The owner's Casino lever command (<c>0x00513E50</c>), dispatched with the frame's other
/// actions (<c>0x005119C0</c>) before the creatures; the room's own tick is <see cref="CasinoSystem"/>
/// (ADR-0479).</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/state-category-hooks.md")]
public sealed class CasinoLeverSystem : ISimulationSystem
{
    public string Name => "Casino lever";

    public void Execute(SimulationContext context) => CasinoSystem.PullLevers(context);
}
