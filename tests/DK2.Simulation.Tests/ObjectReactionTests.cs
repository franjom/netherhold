// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Simulation.Systems;
using DK2.Core;

namespace DK2.Simulation.Tests;

public class ObjectReactionTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(13)]
    public void SimultaneousStruckObjectsTakeTheirRandomDirectionsNewestFirst(int seed)
    {
        var database = Dk2TerrainFixture.Database();
        var world = WorldState.FromMap(database, Dk2TerrainFixture.Map(), [0, 1, 3]);
        ObjectState older = world.SpawnObject(database, 35, 3, 40);
        ObjectState newer = world.SpawnObject(database, 35, 3, 41);
        older.ProcessState = newer.ProcessState = ObjectReactionRules.StruckProcess;
        var expected = new SimRandom((ulong)seed);
        bool newerPositive = expected.NextInt(2) == 0;
        bool olderPositive = expected.NextInt(2) == 0;
        var simulation = new GameSimulation(database, world, (ulong)seed, [new ObjectReactionSystem()]);
        simulation.Tick();
        Assert.Equal(newerPositive, newer.ProcessEnding);
        Assert.Equal(olderPositive, older.ProcessEnding);
    }

    [Fact]
    public void StruckObjectUsesOriginalDecayingSpinAndEndsAtTheThreshold()
    {
        var database = Dk2TerrainFixture.Database();
        var world = WorldState.FromMap(database, Dk2TerrainFixture.Map(), [0, 1, 3]);
        ObjectState target = world.SpawnObject(database, 35, 3, 40);
        target.ProcessState = ObjectReactionRules.StruckProcess;
        var simulation = new GameSimulation(database, world, 7, [new ObjectReactionSystem()]);
        simulation.Tick();
        int sign = target.ProcessEnding ? 1 : -1;
        Assert.Equal(512, target.TimerTicks);
        Assert.Equal((sign * 448) & 2047, target.Heading);
        for (int i = 1; i < 21; i++) simulation.Tick();
        Assert.Equal((byte)ObjectReactionRules.StruckProcess, target.ProcessState);
        simulation.Tick();
        Assert.Equal((byte)0, target.ProcessState);
    }
}
