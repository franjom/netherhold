// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.
using DK2.Core;

namespace DK2.Simulation.Tests;

public class ImpTeleportSnapshotTests
{
    [Theory]
    [InlineData(ImpTeleportAction.Departure)]
    [InlineData(ImpTeleportAction.Arrival)]
    public void InFlightActionAndDeadlineAffectHashAndSurviveRestore(ImpTeleportAction action)
    {
        GameSimulation simulation = TestWorld.Simulation();
        CreatureState creature = simulation.World.Creatures[0];
        ulong before = simulation.StateHash();
        creature.TeleportAction = action;
        ulong actionOnly = simulation.StateHash();
        Assert.NotEqual(before, actionOnly);
        creature.TeleportActionUntilTick = 123;
        Assert.NotEqual(actionOnly, simulation.StateHash());
        FpPosition3[] targets =
        [
            new FpPosition3(new Fp32(10), new Fp32(0), new Fp32(0)),
            new FpPosition3(new Fp32(10), new Fp32(20), new Fp32(0)),
            new FpPosition3(new Fp32(10), new Fp32(20), new Fp32(30)),
        ];
        foreach (FpPosition3 target in targets)
        {
            ulong previous = simulation.StateHash();
            creature.TeleportTarget = target;
            Assert.NotEqual(previous, simulation.StateHash());
        }
        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(simulation), TestWorld.Database());
        Assert.Equal(action, restored.World.Creatures[0].TeleportAction);
        Assert.Equal(123, restored.World.Creatures[0].TeleportActionUntilTick);
        Assert.Equal(creature.TeleportTarget, restored.World.Creatures[0].TeleportTarget);
        Assert.Equal(simulation.StateHash(), restored.StateHash());
    }
}
