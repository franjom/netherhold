// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Simulation.Systems;

public sealed class ObjectReactionSystem : ISimulationSystem
{
    public string Name => "ObjectReactions";

    public void Execute(SimulationContext context)
    {
        for (int index = context.World.Objects.Count - 1; index >= 0; index--)
        {
            ObjectState target = context.World.Objects[index];
            // State 18 runs in ObjectMovementSystem's thing-order pass (Q803). The lever's 48/49 stays here (draws nothing).
            if (target.ObjectTypeId == AutoFurnitureRules.CasinoLeverObjectId && target.ProcessState is 48 or 49)
            {
                target.ProcessAgeTicks++;
                if (target.ProcessAgeTicks == 1) target.ProcessDeadline = context.Tick.Value;
                int slot = target.ProcessState == 48 ? 5 : 6;
                ObjectDef? definition = context.Database.ObjectById(target.ObjectTypeId);
                if (definition is null || target.ProcessAgeTicks > AnimationTimingRules.FirstCycleHoldTicks(definition.ArtSlot(slot)))
                {
                    target.ProcessCount = 1;
                    target.ProcessState = 0;
                }
            }
        }
    }
}
