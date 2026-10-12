// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Systems;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

public class CallToArmsAnimationTests
{
    [InstallTheory]
    [InlineData(CallToArmsRules.Activated)]
    [InlineData(CallToArmsRules.Recalling)]
    public void CancellingAnEstablishedFlagPlaysTheWholeShippedReleaseClip(byte process)
    {
        string install = Dk2InstallGate.Path;
        GameDatabase db = GameDatabaseLoader.LoadForCampaignLevel(install, "Level1");
        LevelMap map = GameDatabaseLoader.LoadCampaignMap(install, "Level1")!;
        var world = WorldState.FromMap(db, map, [0, 1, 3]);
        ObjectDef definition = db.ObjectById(CallToArmsRules.ObjectTypeId)!;
        int duration = AnimationTimingRules.FirstCycleHoldTicks(definition.ArtSlot(7));
        Assert.Equal((byte)6, definition.ArtSlot(7).ResourceType);
        Assert.True(duration > 0);
        ObjectState flag = world.SpawnObject(db, CallToArmsRules.ObjectTypeId, 3, 168);
        flag.ProcessState = CallToArmsRules.Recalling;
        flag.ProcessDeadline = long.MaxValue;
        flag.AnimationStartedTick = 0;
        world.Player(3).CallToArms = new CallToArmsState(true, flag.Id, 8, 8, 1);
        var simulation = new GameSimulation(db, world, 1, [new CallToArmsSystem()]);
        for (int i = 0; i < duration + 100; i++) simulation.Tick();
        long cancelledAt = world.CurrentTick.Value;
        flag.ProcessState = process;
        flag.ProcessEnding = true;
        simulation.Tick();
        Assert.Contains(flag, world.Objects);
        Assert.Equal(CallToArmsRules.Releasing, flag.ProcessState);
        Assert.Equal(cancelledAt, flag.AnimationStartedTick);
        for (int i = 1; i < duration; i++) simulation.Tick();
        Assert.Contains(flag, world.Objects);
        Assert.True(world.Player(3).CallToArms.On);
        simulation.Tick();
        Assert.DoesNotContain(flag, world.Objects);
        Assert.False(world.Player(3).CallToArms.On);
    }
}
