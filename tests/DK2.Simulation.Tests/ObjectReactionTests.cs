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
        var simulation = new GameSimulation(database, world, (ulong)seed, [new ObjectMovementSystem()]);
        simulation.Tick();
        Assert.Equal(newerPositive, newer.ProcessEnding);
        Assert.Equal(olderPositive, older.ProcessEnding);
    }

    // State 18 is an object process run in the object pass in thing order (0x0049BD20): a newer
    // struck object's random(2) comes before an older chicken's reaction draw, not after every bird (Q803).
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(13)]
    public void AStruckObjectDrawsInThingOrderAmongTheBirds(int seed)
    {
        var database = Dk2TerrainFixture.Database();
        var world = WorldState.FromMap(database, Dk2TerrainFixture.Map(), [0, 1, 3]);
        ObjectState bird = world.SpawnObject(database, HatcheryRules.ChickenObjectId, 3, 41);
        bird.ProcessState = (byte)ChickenProcess.Reaction;
        bird.ProcessAgeTicks = 0;
        ObjectState struck = world.SpawnObject(database, 35, 3, 40);
        struck.ProcessState = ObjectReactionRules.StruckProcess;
        var expected = new SimRandom((ulong)seed);
        bool positive = expected.NextInt(2) == 0;
        int reaction = expected.NextInt(16) + 8;
        var simulation = new GameSimulation(database, world, (ulong)seed, [new ObjectMovementSystem(), new ObjectReactionSystem()]);
        simulation.Tick();
        Assert.Equal(positive, struck.ProcessEnding);
        Assert.Equal(reaction - 1, bird.ChickenReactionTicks);
    }

    [Fact]
    public void StruckObjectUsesOriginalDecayingSpinAndEndsAtTheThreshold()
    {
        var database = Dk2TerrainFixture.Database();
        var world = WorldState.FromMap(database, Dk2TerrainFixture.Map(), [0, 1, 3]);
        ObjectState target = world.SpawnObject(database, 35, 3, 40);
        target.ProcessState = ObjectReactionRules.StruckProcess;
        var simulation = new GameSimulation(database, world, 7, [new ObjectMovementSystem()]);
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
