// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation.Systems;

/// <summary>Chicken and Rat handlers share the object's newest-first update order.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/prison-rats.md")]
public sealed class ObjectMovementSystem : ISimulationSystem
{
    public string Name => "ObjectMovement";
    public void Execute(SimulationContext context)
    {
        if (context.World.Objects.Any(o => o.ObjectTypeId == RatRules.RatObjectId)) context.Navigation.Refresh();
        for (int i = context.World.Objects.Count - 1; i >= 0; i--)
        {
            ObjectState thing = context.World.Objects[i];
            if (thing.ObjectTypeId == RatRules.RatObjectId) RatRules.Tick(context, thing);
            else if (thing.ObjectTypeId == HatcheryRules.ChickenObjectId) ChickenMovementRules.Tick(context, thing);
        }
    }
}
