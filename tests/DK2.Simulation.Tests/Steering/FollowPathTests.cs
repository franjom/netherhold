// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Simulation.Steering;

namespace DK2.Simulation.Tests.Steering;

/// <summary>DK2's <c>Follow_Path</c> (docs/research/steering-behaviours.md §5): stuck handling,
/// the two arms' advance and arrival rules, the turn gate and the two seek laws.</summary>
public class FollowPathTests
{
    private const int FacingPlusX = 0x200;

    private static Fixed16Vector T(double x, double y) => FakeSteeringWorld.Tiles(x, y);

    private static (SteeringBody body, SteeringMover mover, FakeSteeringWorld world) Setup(
        bool imp, Fixed16Vector pos, params Fixed16Vector[] route)
    {
        var world = new FakeSteeringWorld("##########", "#........#", "#........#", "##########")
        {
            FixedRoute = route.ToList(),
        };
        var body = new SteeringBody { Pos = pos };
        var mover = new SteeringMover { IsWorker = imp, TurnRate = 1024, Heading = FacingPlusX };
        RouteWindow.Request(body, world, mover.Profile, route[^1], 0);
        return (body, mover, world);
    }

    [Theory]
    [InlineData(0x7FFF, Fixed16.One)]
    [InlineData(0x001F, FollowPathRules.RerouteFraction)]
    [InlineData(0x0003, FollowPathRules.EscapeClearFraction)]
    [InlineData(0x8000, 0)]
    public void TheStuckFractionCountsTheLowFifteenBits(int history, int expected) =>
        Assert.Equal(expected, FollowPathRules.StuckFraction((ushort)history));

    [Fact]
    public void WithNoRouteTheForceIsABrake()
    {
        var body = new SteeringBody { Vel = new Fixed16Vector(0x1000, -0x800) };
        var world = new FakeSteeringWorld("###", "#.#", "###");

        Fixed16Vector force = FollowPathRules.Compute(body, new SteeringMover(), world);

        Assert.Equal(new Fixed16Vector(-0x1000, 0x800), force);
    }

    [Fact]
    public void AnImpStepsToItsNextPointHalfATileOutAndSeeksItWithTheImpGain()
    {
        (SteeringBody body, SteeringMover mover, FakeSteeringWorld world) = Setup(true, T(1.6, 1.5), T(2.0, 1.5), T(6.5, 1.5));

        Fixed16Vector force = FollowPathRules.Compute(body, mover, world);

        Assert.Equal(1, body.SubpathIndex);
        Assert.Equal((T(6.5, 1.5) - T(1.6, 1.5)).Scale(FollowPathRules.ImpGain), force);
    }

    [Fact]
    public void AnImpExactlyHalfATileOutDoesNotStepYet()
    {
        (SteeringBody body, SteeringMover mover, FakeSteeringWorld world) = Setup(true, T(1.5, 1.5), T(2.0, 1.5), T(6.5, 1.5));

        FollowPathRules.Compute(body, mover, world);

        Assert.Equal(0, body.SubpathIndex);
    }

    [Theory]
    [InlineData(1.7, 0)]
    [InlineData(1.8, 1)]
    public void ACreatureStepsToItsNextPointAtOneAndATwoTenthsRadii(double x, int index)
    {
        (SteeringBody body, SteeringMover mover, FakeSteeringWorld world) = Setup(false, T(x, 1.5), T(2.0, 1.5), T(6.5, 1.5));

        FollowPathRules.Compute(body, mover, world);

        Assert.Equal(index, body.SubpathIndex);
    }

    [Fact]
    public void ACreatureAlsoStepsPastAPointInsideAnObstacleFromEightTenthsOut()
    {
        (SteeringBody body, SteeringMover mover, FakeSteeringWorld world) = Setup(false, T(1.3, 1.5), T(2.0, 1.5), T(6.5, 1.5));
        world.Obstacles.Add(new SteeringObstacle(T(2.1, 1.5), 0x4000, Solid: true, Flag2: false));

        FollowPathRules.Compute(body, mover, world);

        Assert.Equal(1, body.SubpathIndex);
    }

    /// <summary>A Bile Demon turns 170 a tick. Facing -y with its waypoint at +x it brakes for three
    /// ticks and moves on the fourth.</summary>
    [Fact]
    public void ACreatureStopsToTurnBeforeItMoves()
    {
        (SteeringBody body, SteeringMover mover, FakeSteeringWorld world) = Setup(false, T(1.5, 1.5), T(6.5, 1.5));
        mover.Heading = 0;
        mover.TurnRate = 170;
        body.Vel = new Fixed16Vector(0x100, 0);

        for (int tick = 0; tick < 3; tick++)
            Assert.Equal(-body.Vel, FollowPathRules.Compute(body, mover, world));

        Assert.True(FollowPathRules.Compute(body, mover, world).X > 0);
        Assert.Equal(FacingPlusX, mover.Heading);
    }

    [Fact]
    public void TheFinalApproachHalvesTheDistanceIntoASpeedCappedAtMaxSpeed()
    {
        (SteeringBody near, SteeringMover mover, FakeSteeringWorld world) = Setup(false, T(1.5, 1.5), T(1.9, 1.5));
        Assert.Equal(new Fixed16Vector(0x3333, 0), FollowPathRules.Compute(near, mover, world));

        (SteeringBody far, _, _) = Setup(false, T(1.5, 1.5), T(4.5, 1.5));
        Assert.Equal(new Fixed16Vector(SeparationRules.ShippedMaxSpeed, 0), FollowPathRules.Compute(far, mover, world));
    }

    [Fact]
    public void AnIntermediatePointIsSoughtWithTheCreatureGainLessTheCrowdWeight()
    {
        (SteeringBody body, SteeringMover mover, FakeSteeringWorld world) = Setup(false, T(1.5, 1.5), T(3.5, 1.5), T(6.5, 1.5));
        world.CrowdWeightValue = 0x8000;

        Fixed16Vector force = FollowPathRules.Compute(body, mover, world);

        Assert.Equal((T(3.5, 1.5) - T(1.5, 1.5)).Scale(FollowPathRules.CreatureGain - 0x8000), force);
    }

    [Theory]
    [InlineData(0x7000, false)]
    [InlineData(0x6000, true)]
    public void ACreatureArrivesOnlyWhenNearAndSlow(int speed, bool finished)
    {
        (SteeringBody body, SteeringMover mover, FakeSteeringWorld world) = Setup(false, T(1.5, 1.5), T(1.55, 1.5));
        body.Vel = new Fixed16Vector(speed, 0);

        FollowPathRules.Compute(body, mover, world);

        Assert.Equal(finished, body.PathFinished);
    }

    [Fact]
    public void AnImpArrivesWithinATenthOfATileAtAnySpeed()
    {
        (SteeringBody body, SteeringMover mover, FakeSteeringWorld world) = Setup(true, T(1.5, 1.5), T(1.59, 1.5));
        body.Vel = new Fixed16Vector(0x7000, 0);

        FollowPathRules.Compute(body, mover, world);

        Assert.True(body.PathFinished);
    }

    [Fact]
    public void WhenTheWindowRunsOutTheWholeRouteIsRequestedAgain()
    {
        Fixed16Vector[] route = Enumerable.Range(2, 7).Select(i => T(i, 1.5)).ToArray();
        (SteeringBody body, SteeringMover mover, FakeSteeringWorld world) = Setup(true, T(1.5, 1.5), route);

        for (int tick = 0; tick < 5; tick++)
        {
            body.Pos = body.CurrentPoint;
            FollowPathRules.Compute(body, mover, world);
        }

        Assert.Equal(2, world.RouteRequests);
        Assert.Equal(0, body.SubpathIndex);
        Assert.Equal(route[0], body.Subpath[0]);
    }

    [Fact]
    public void FiveStuckTicksOfFifteenRerouteAndEnterEscapeMode()
    {
        (SteeringBody body, SteeringMover mover, FakeSteeringWorld world) = Setup(false, T(1.5, 1.5), T(6.5, 1.5));
        body.PathStuckBuffer = 0x1F;

        FollowPathRules.Compute(body, mover, world);

        Assert.True(body.EscapeStuckMode);
        Assert.Equal(2, world.RouteRequests);
        Assert.Equal(0x1F, body.PathStuckBuffer);
    }

    [Theory]
    [InlineData(0x3, false)]
    [InlineData(0x7, true)]
    public void EscapeModeEndsAtTwoStuckTicksOfFifteen(int history, bool stillEscaping)
    {
        (SteeringBody body, SteeringMover mover, FakeSteeringWorld world) = Setup(false, T(1.5, 1.5), T(6.5, 1.5));
        body.EscapeStuckMode = true;
        body.PathStuckBuffer = (ushort)history;

        FollowPathRules.Compute(body, mover, world);

        Assert.Equal(stillEscaping, body.EscapeStuckMode);
    }
}
