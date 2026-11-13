// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Simulation.Tests;

/// <summary>The chicken update's tail alone (<see cref="ChickenMovementRules.Decay"/>), for harnesses that
/// leave the birds' movement out: an eaten bird, its health zeroed, is removed in it (Q796).</summary>
internal sealed class ChickenTailSystem : ISimulationSystem
{
    public string Name => "chicken tail";

    public void Execute(SimulationContext context)
    {
        foreach (ObjectState chicken in context.World.Objects.Where(o => o.ObjectTypeId == HatcheryRules.ChickenObjectId).ToList())
            ChickenMovementRules.Decay(context, chicken);
    }
}
