// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Pathfinding;
using DK2.Simulation.Steering;

namespace DK2.Simulation.Tests.Steering;

/// <summary>DK2's <c>Avoid_Walls</c> (docs/research/steering-behaviours.md §3 and §6): the
/// one-radius margin per axis against the predicted position, the corner reflection, and the
/// two-unit nudge of <c>escape_stuck_mode</c>.</summary>
public class AvoidWallsTests
{
    private static Fixed16Vector T(double x, double y) => FakeSteeringWorld.Tiles(x, y);

    private static FakeSteeringWorld Room() => new("#######", "#.....#", "#.....#", "#.....#", "#######");

    private static Fixed16Vector Force(FakeSteeringWorld world, Fixed16Vector pos, Fixed16Vector vel, bool stuck = false)
    {
        var body = new SteeringBody { Pos = pos, Vel = vel, EscapeStuckMode = stuck };
        body.Subpath[0] = T(5.5, 2.5);
        return AvoidWallsRules.Compute(body, MoverProfile.Walker, world);
    }

    [Fact]
    public void OpenFloorAheadGivesNoForce() =>
        Assert.Equal(Fixed16Vector.Zero, Force(Room(), T(3.5, 2.5), new Fixed16Vector(0x1000, 0)));

    /// <summary>Predicted 0.15 tile inside the 0.2 margin of the wall at x = 6: the push is twice
    /// that overshoot, back along x only.</summary>
    [Fact]
    public void AWallAheadPushesBackTwiceTheOvershootOnItsAxis() =>
        Assert.Equal(new Fixed16Vector(-0x5332, 0), Force(Room(), T(5.9, 2.5), new Fixed16Vector(0x1000, 0)));

    [Fact]
    public void AWallBelowActsOnYAlone() =>
        Assert.Equal(new Fixed16Vector(0, -0x5332), Force(Room(), T(3.5, 3.9), new Fixed16Vector(0, 0x1000)));

    [Fact]
    public void MovingTowardTheLowSideTheMarginIsInsideTheTileFace() =>
        Assert.Equal(new Fixed16Vector(0x5334, 0), Force(Room(), T(1.1, 2.5), new Fixed16Vector(-0x1000, 0)));

    /// <summary>The margin is judged on the prediction, which for a still body is where it stands:
    /// standing 0.05 inside the wall's margin is pushed out like moving into it.</summary>
    [Fact]
    public void AStillBodyInsideTheMarginIsPushedOutToo() =>
        Assert.Equal(new Fixed16Vector(-0x4CCC, 0), Force(Room(), T(5.95, 2.5), Fixed16Vector.Zero));

    /// <summary>The original's root is the integer square root of the 16.16 discriminant taken
    /// unscaled, so the "hit" lands almost at the corner itself: a body a step away misses (the
    /// distance exceeds its speed), a body already inside the corner circle is braked.</summary>
    [Fact]
    public void TheCornerReflectionFiresOnlyWithinOneStepOfTheCornerInTheOriginalsArithmetic()
    {
        var world = new FakeSteeringWorld("#######", "#.....#", "#..#..#", "#.....#", "#######");

        Assert.Equal(Fixed16Vector.Zero, Force(world, T(2.7, 1.7), new Fixed16Vector(0x4000, 0x4000)));
        Fixed16Vector inside = Force(world, T(2.95, 1.95), new Fixed16Vector(0x2000, 0x2000));
        Assert.True(inside.X < 0 && inside.Y < 0, $"inside the circle gave {inside}");
    }

    [Fact]
    public void TheRayCircleDistanceIsTheOriginalsNotTheTextbooks()
    {
        Fixed16Vector origin = Fixed16Vector.Zero, along = new(Fixed16.One, 0);

        int headOn = RayCircle.Distance(origin, along, new Fixed16Vector(3 * Fixed16.One, 0), Fixed16.One);
        Assert.InRange(headOn, 2 * Fixed16.One + 0x8000, 3 * Fixed16.One);
        Assert.Equal(RayCircle.Miss, RayCircle.Distance(origin, along, new Fixed16Vector(0, 3 * Fixed16.One), Fixed16.One));
        Assert.NotEqual(RayCircle.Miss, RayCircle.Distance(origin, along, new Fixed16Vector(3 * Fixed16.One, 2 * Fixed16.One), Fixed16.One));
        Assert.Equal(int.MinValue, RayCircle.Miss);
    }

    [Fact]
    public void InEscapeStuckModeTheNudgeIsTwoRawUnitsSideways() =>
        Assert.Equal(new Fixed16Vector(0, -2), Force(Room(), T(3.5, 2.5), Fixed16Vector.Zero, stuck: true));

    /// <summary>Heading +x, the first side tried is -y. With a wall there and floor on +y the nudge
    /// goes to +y; with both sides walled it goes to -y regardless.</summary>
    [Theory]
    [InlineData(6, 2)]
    [InlineData(5, -2)]
    public void TheNudgeTakesTheOtherSideOnlyWhenItIsOpen(int rows, int expectedY)
    {
        string[] map = ["#######", "#.....#", "#.###.#", "#.....#", "#.....#", "#######"];
        var world = new FakeSteeringWorld(rows == 6 ? map : [.. map[..4], map[5]]);
        var body = new SteeringBody { Pos = T(3.5, 3.5), EscapeStuckMode = true };
        body.Subpath[0] = T(5.5, 3.5);

        Fixed16Vector force = AvoidWallsRules.Compute(body, MoverProfile.Walker, world);

        Assert.Equal(new Fixed16Vector(0, expectedY), force);
    }
}
