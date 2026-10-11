// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation.Systems;

[Dk2Behavior(Confidence.StronglyInferred, "docs/research/hatchery-roaming.md")]
public sealed class ChickenMovementSystem : ISimulationSystem
{
    public string Name => "ChickenMovement";

    public void Execute(SimulationContext context)
    {
        for (int i = context.World.Objects.Count - 1; i >= 0; i--)
        {
            ObjectState chicken = context.World.Objects[i];
            if (chicken.ObjectTypeId == HatcheryRules.ChickenObjectId)
                ChickenMovementRules.Tick(context, chicken);
        }
    }
}
