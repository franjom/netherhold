// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Pathfinding;
using DK2.Simulation.Steering;

namespace DK2.Simulation.Tests.Steering;

/// <summary>The combiner, the slide, the Euler step and the wall clamp
/// (docs/research/navigation-and-steering.md §3.3–§3.4; steering-behaviours.md §4).</summary>
public class SteeringIntegratorTests
{
    private static Fixed16Vector T(double x, double y) => FakeSteeringWorld.Tiles(x, y);

    private static FakeSteeringWorld Room() => new("#######", "#.....#", "#.....#", "#.....#", "#######");

    private static void Step(SteeringBody body, Fixed16Vector total, FakeSteeringWorld? world = null) =>
        SteeringIntegrator.Step(body, total, MoverProfile.Walker, world ?? Room());

    [Fact]
    public void EachForceIsClampedToMaxForceBeforeItIsWeighted()
    {
        var body = new SteeringBody();
        var huge = new Fixed16Vector(10 << 16, 0);

        Fixed16Vector total = SteeringIntegrator.Combine(body, huge, Fixed16Vector.Zero, Fixed16Vector.Zero, Fixed16Vector.Zero);

        Assert.Equal(Fixed16.Truncate(huge, body.MaxForce).Scale(AvoidWallsRules.Weight), total);
        Assert.InRange(total.X, Fixed16.Mul(0x17F00, AvoidWallsRules.Weight), Fixed16.Mul(0x18000, AvoidWallsRules.Weight));
    }

    [Fact]
    public void TheWeightsAreTheFourRegisteredOnes()
    {
        var unit = new Fixed16Vector(Fixed16.One, 0);

        Fixed16Vector total = SteeringIntegrator.Combine(new SteeringBody(), unit, unit, unit, unit);

        Assert.Equal(0x9999 + 0x6666 + 0x5999 + 0x2147, total.X);
    }

    [Fact]
    public void TheSumIsClampedAgainAndTheVelocityIsCappedAtMaxSpeed()
    {
        var body = new SteeringBody { Pos = T(2.5, 2.5) };

        Step(body, new Fixed16Vector(3 << 16, 0));

        Assert.Equal(new Fixed16Vector(SeparationRules.ShippedMaxSpeed, 0), body.Vel);
        Assert.Equal(T(2.5, 2.5) + body.Vel, body.Pos);
        Assert.Equal(new Fixed16Vector(65791, 0), body.LocalXAxis);
        Assert.Equal(body.LocalXAxis.Perpendicular(), body.LocalYAxis);
    }

    [Fact]
    public void AStepThatWouldOvershootTheLastPointSnapsOntoItWithTheFlag()
    {
        var body = new SteeringBody { Pos = T(2.5, 2.5), Vel = new Fixed16Vector(SeparationRules.ShippedMaxSpeed, 0), PathFlags = 4, PathTotalCount = 1 };
        body.Subpath[0] = T(2.8, 2.5);

        Step(body, Fixed16Vector.Zero);

        Assert.Equal(T(2.8, 2.5), body.Pos);
        Assert.Equal(Fixed16Vector.Zero, body.Vel);
    }

    [Theory]
    [InlineData(4276, 29941, 9356, 65511)]
    [InlineData(21421, 21421, 46869, 46869)]
    [InlineData(-25965, 15578, -56813, 34085)]
    [InlineData(-9640, 28908, -21093, 63251)]
    public void ACoastingBodyKeepsTheCapturedVelocityAxes(int vx, int vy, int ax, int ay)
    {
        var body = new SteeringBody { Pos = T(2.5, 2.5), Vel = new Fixed16Vector(vx, vy), MaxSpeed = 65536 };

        Step(body, Fixed16Vector.Zero);

        Assert.Equal(new Fixed16Vector(vx, vy), body.Vel);
        Assert.Equal(new Fixed16Vector(ax, ay), body.LocalXAxis);
        Assert.Equal(new Fixed16Vector(-ay, ax), body.LocalYAxis);
    }

    [Fact]
    public void ArrivalSnapClearsTheAxesEvenWhenTheAppliedForceIsNonzero()
    {
        var body = new SteeringBody { Pos = T(2.5, 2.5), PathFlags = 4, PathTotalCount = 1 };
        body.Subpath[0] = T(2.6, 2.5);

        Step(body, new Fixed16Vector(0x4000, 0));

        Assert.Equal(Fixed16Vector.Zero, body.Vel);
        Assert.Equal(Fixed16Vector.Zero, body.LocalXAxis);
        Assert.Equal(Fixed16Vector.Zero, body.LocalYAxis);
    }

    [Fact]
    public void WithoutTheFlagTheBodyOvershoots()
    {
        var body = new SteeringBody { Pos = T(2.5, 2.5), Vel = new Fixed16Vector(SeparationRules.ShippedMaxSpeed, 0), PathTotalCount = 1 };
        body.Subpath[0] = T(2.8, 2.5);

        Step(body, Fixed16Vector.Zero);

        Assert.Equal(T(2.5, 2.5) + new Fixed16Vector(SeparationRules.ShippedMaxSpeed, 0), body.Pos);
    }

    [Fact]
    public void TheHistoryTakesAStuckBitOnlyWhenProgressDoesNotGrow()
    {
        var body = new SteeringBody { Pos = T(1.5, 1.5) };
        Fixed16Vector[] route = [T(3.5, 1.5), T(6.5, 1.5)];
        RouteWindow.Load(body, route, route[^1], 0);
        var world = new FakeSteeringWorld("##########", "#........#", "##########");

        Step(body, Fixed16Vector.Zero, world);
        Assert.Equal(1, body.PathStuckBuffer);

        Step(body, new Fixed16Vector(Fixed16.One, 0), world);
        Assert.Equal(0b10, body.PathStuckBuffer);

        Step(body, new Fixed16Vector(-Fixed16.One, 0), world);
        Assert.Equal(0b101, body.PathStuckBuffer);
    }

    /// <summary>A body pressing into a solid obstacle on its -x side: the inward parts of force and
    /// velocity go, and a push of max_force along the obstacle is added on the side the motion
    /// leans to.</summary>
    [Fact]
    public void TheSlideRemovesTheInwardMotionAndPushesAlongTheObstacle()
    {
        var body = new SteeringBody { Vel = new Fixed16Vector(-0x1000, 0x800) };
        body.SolidContacts.Add(new SolidContact(new Fixed16Vector(Fixed16.One, 0), false));

        Fixed16Vector force = SteeringIntegrator.Slide(body, new Fixed16Vector(-0x8000, 0x100));

        Assert.Equal(0, body.Vel.X);
        Assert.Equal(0x800, body.Vel.Y);
        Assert.Equal(0, force.X);
        Assert.Equal(0x100 + SeparationRules.ShippedMaxForce, force.Y);
    }

    [Fact]
    public void TheSlideLeansTheOtherWayWhenTheMotionDoes()
    {
        var body = new SteeringBody { Vel = new Fixed16Vector(-0x1000, -0x800) };
        body.SolidContacts.Add(new SolidContact(new Fixed16Vector(Fixed16.One, 0), false));

        Fixed16Vector force = SteeringIntegrator.Slide(body, new Fixed16Vector(-0x8000, -0x100));

        Assert.Equal(-0x100 - SeparationRules.ShippedMaxForce, force.Y);
    }

    [Fact]
    public void AnObstacleTheMotionAlreadyLeavesIsIgnored()
    {
        var body = new SteeringBody { Vel = new Fixed16Vector(0x1000, 0) };
        body.SolidContacts.Add(new SolidContact(new Fixed16Vector(Fixed16.One, 0), false));
        var total = new Fixed16Vector(0x4000, 0x100);

        Assert.Equal(total, SteeringIntegrator.Slide(body, total));
        Assert.Equal(new Fixed16Vector(0x1000, 0), body.Vel);
    }

    [Fact]
    public void TheWallClampKeepsTheAxisMoveThatStaysOnFloorAndDropsTheOtherVelocity()
    {
        var body = new SteeringBody { Pos = T(3.5, 3.9), Vel = new Fixed16Vector(0x1000, 0x4000) };

        Step(body, Fixed16Vector.Zero);

        Assert.Equal(T(3.5, 3.9).X + 0x1000, body.Pos.X);
        Assert.Equal(T(3.5, 3.9).Y, body.Pos.Y);
        Assert.Equal(0, body.Vel.Y);
        Assert.Equal(new Fixed16Vector(Fixed16.One, 0), body.LocalXAxis);
        Assert.Equal(0, body.PathStuckBuffer & 1);
    }

    /// <summary>With both axis moves blocked the step is turned a quarter left, and the turned
    /// step is taken without another look at its tile: here it lands inside a wall. Reproduced.</summary>
    [Fact]
    public void TheWallClampTurnsTheStepWhenBothAxisMovesFail()
    {
        var world = new FakeSteeringWorld("#######", "#..#..#", "#.##..#", "#.....#", "#######");
        var body = new SteeringBody { Pos = T(2.9, 1.9), Vel = new Fixed16Vector(0x4000, 0x4000) };

        Step(body, Fixed16Vector.Zero, world);

        Assert.Equal(T(2.9, 1.9) + new Fixed16Vector(-0x4000, 0x4000), body.Pos);
        Assert.Equal(new Fixed16Vector(0x4000, 0x4000), body.Vel);
        Assert.Equal(Fixed16.Normalise(new Fixed16Vector(0x4000, 0x4000)), body.LocalXAxis);
    }

    [Fact]
    public void ARefusedStepLeavesTheBodyAndPushesAnExtraStuckBit()
    {
        var world = new FakeSteeringWorld("###", "#.#", "###");
        var body = new SteeringBody { Pos = T(1.9, 1.9), Vel = new Fixed16Vector(0x4000, 0x4000) };

        Step(body, Fixed16Vector.Zero, world);

        Assert.Equal(T(1.9, 1.9), body.Pos);
        Assert.Equal(0b10, body.PathStuckBuffer);
        Assert.Equal(Fixed16.Normalise(body.Vel), body.LocalXAxis);
    }
}
