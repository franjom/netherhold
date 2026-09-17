// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Simulation.Steering;

namespace DK2.Simulation.Tests.Steering;

/// <summary>DK2's <c>Repel_Obstacle</c> (docs/research/steering-behaviours.md §2): a binary,
/// full-force shove from soft obstacles and a recorded contact for solid ones.</summary>
public class RepelObstacleTests
{
    private static Fixed16Vector T(double x, double y) => FakeSteeringWorld.Tiles(x, y);

    private static (SteeringBody body, FakeSteeringWorld world) Setup()
    {
        var world = new FakeSteeringWorld("#######", "#.....#", "#.....#", "#######");
        var body = new SteeringBody { Pos = T(3.5, 1.5) };
        return (body, world);
    }

    [Fact]
    public void AnOverlappingSoftObstacleShovesAtMaxForceWithNoFalloff()
    {
        (SteeringBody body, FakeSteeringWorld world) = Setup();
        world.Obstacles.Add(new SteeringObstacle(body.Pos - new Fixed16Vector(0x1000, 0), 0x1000, Solid: false, Flag2: false));

        Assert.Equal(new Fixed16Vector(SeparationRules.ShippedMaxForce, 0), RepelObstacleRules.Compute(body, world));
        Assert.Empty(body.SolidContacts);
    }

    [Fact]
    public void ASolidObstacleIsRecordedForTheSlideNotPushedAgainst()
    {
        (SteeringBody body, FakeSteeringWorld world) = Setup();
        world.Obstacles.Add(new SteeringObstacle(body.Pos - new Fixed16Vector(0x1000, 0), 0x1000, Solid: true, Flag2: true));

        Assert.Equal(Fixed16Vector.Zero, RepelObstacleRules.Compute(body, world));
        Assert.Equal([new SolidContact(new Fixed16Vector(Fixed16.One, 0), true)], body.SolidContacts);
    }

    [Fact]
    public void ContactIsCentresWithinSizePlusRadius()
    {
        (SteeringBody body, FakeSteeringWorld world) = Setup();
        int reach = 0x1000 + body.Radius;
        world.Obstacles.Add(new SteeringObstacle(body.Pos - new Fixed16Vector(reach + 0x100, 0), 0x1000, false, false));

        Assert.Equal(Fixed16Vector.Zero, RepelObstacleRules.Compute(body, world));
    }

    [Fact]
    public void NothingIsShovedNearTheLastPointWhenTheSnapFlagIsSet()
    {
        (SteeringBody body, FakeSteeringWorld world) = Setup();
        world.Obstacles.Add(new SteeringObstacle(body.Pos - new Fixed16Vector(0x1000, 0), 0x1000, false, false));
        body.PathFlags = 4;
        body.PathTotalCount = 1;
        body.Subpath[0] = body.Pos + new Fixed16Vector(0x8000, 0);

        Assert.Equal(Fixed16Vector.Zero, RepelObstacleRules.Compute(body, world));
    }

    [Fact]
    public void NothingIsShovedInEscapeStuckMode()
    {
        (SteeringBody body, FakeSteeringWorld world) = Setup();
        world.Obstacles.Add(new SteeringObstacle(body.Pos - new Fixed16Vector(0x1000, 0), 0x1000, false, false));
        body.EscapeStuckMode = true;

        Assert.Equal(Fixed16Vector.Zero, RepelObstacleRules.Compute(body, world));
    }

    [Fact]
    public void AtMostTwelveSolidContactsAreKept()
    {
        (SteeringBody body, FakeSteeringWorld world) = Setup();
        for (int i = 1; i <= 13; i++)
            world.Obstacles.Add(new SteeringObstacle(body.Pos - new Fixed16Vector(0x100 * i, 0), 0x1000, true, false));

        RepelObstacleRules.Compute(body, world);

        Assert.Equal(SteeringBody.SolidContactSlots, body.SolidContacts.Count);
    }
}
