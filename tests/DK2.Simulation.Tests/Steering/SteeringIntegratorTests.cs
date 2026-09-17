// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Simulation.Steering;

namespace DK2.Simulation.Tests.Steering;

/// <summary>The combiner and Euler step (docs/research/navigation-and-steering.md §3.3, §3.4).</summary>
public class SteeringIntegratorTests
{
    private static Fixed16Vector T(double x, double y) => FakeSteeringWorld.Tiles(x, y);

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
        var body = new SteeringBody();
        var unit = new Fixed16Vector(Fixed16.One, 0);

        Fixed16Vector total = SteeringIntegrator.Combine(body, unit, unit, unit, unit);

        Assert.Equal(0x9999 + 0x6666 + 0x5999 + 0x2147, total.X);
    }

    [Fact]
    public void TheSumIsClampedAgainAndTheVelocityIsCappedAtMaxSpeed()
    {
        var body = new SteeringBody { Pos = T(2.5, 2.5) };

        SteeringIntegrator.Step(body, new Fixed16Vector(3 << 16, 0));

        Assert.Equal(new Fixed16Vector(SeparationRules.ShippedMaxSpeed, 0), body.Vel);
        Assert.Equal(T(2.5, 2.5) + body.Vel, body.Pos);
        Assert.Equal(Fixed16.Normalise(body.Vel), body.LocalXAxis);
        Assert.Equal(body.LocalXAxis.Perpendicular(), body.LocalYAxis);
    }

    [Fact]
    public void AStepThatWouldOvershootTheLastPointSnapsOntoItWithTheFlag()
    {
        var body = new SteeringBody { Pos = T(2.5, 2.5), Vel = new Fixed16Vector(SeparationRules.ShippedMaxSpeed, 0), PathFlags = 4, PathTotalCount = 1 };
        body.Subpath[0] = T(2.8, 2.5);

        SteeringIntegrator.Step(body, Fixed16Vector.Zero);

        Assert.Equal(T(2.8, 2.5), body.Pos);
        Assert.Equal(Fixed16Vector.Zero, body.Vel);
    }

    [Fact]
    public void WithoutTheFlagTheBodyOvershoots()
    {
        var body = new SteeringBody { Pos = T(2.5, 2.5), Vel = new Fixed16Vector(SeparationRules.ShippedMaxSpeed, 0), PathTotalCount = 1 };
        body.Subpath[0] = T(2.8, 2.5);

        SteeringIntegrator.Step(body, Fixed16Vector.Zero);

        Assert.Equal(T(2.5, 2.5) + new Fixed16Vector(SeparationRules.ShippedMaxSpeed, 0), body.Pos);
    }

    [Fact]
    public void TheHistoryTakesAStuckBitOnlyWhenProgressDoesNotGrow()
    {
        var body = new SteeringBody { Pos = T(1.5, 1.5) };
        Fixed16Vector[] route = [T(3.5, 1.5), T(6.5, 1.5)];
        RouteWindow.Load(body, route, route[^1], 0);

        SteeringIntegrator.Step(body, Fixed16Vector.Zero);
        Assert.Equal(1, body.PathStuckBuffer);

        SteeringIntegrator.Step(body, new Fixed16Vector(Fixed16.One, 0));
        Assert.Equal(0b10, body.PathStuckBuffer);

        SteeringIntegrator.Step(body, new Fixed16Vector(-Fixed16.One, 0));
        Assert.Equal(0b101, body.PathStuckBuffer);
    }
}
