// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Systems;

[Dk2Behavior(Confidence.Confirmed, "docs/research/training-actions.md#3-action-gates--confirmed")]
public sealed class TrainingActionSystem : ISimulationSystem
{
    public string Name => "TrainingActions";

    public void Execute(SimulationContext context)
    {
        for (int i = context.World.Creatures.Count - 1; i >= 0; i--)
        {
            CreatureState trainee = context.World.Creatures[i];
            if (!trainee.Possessed && trainee.TrainingActionId != 0)
                TrainingStep.PollAction(context, trainee);
        }
    }
}
