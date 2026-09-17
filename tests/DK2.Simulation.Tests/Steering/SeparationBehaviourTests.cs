// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Simulation.Steering;

namespace DK2.Simulation.Tests.Steering;

/// <summary>DK2's <c>Separation</c> assembled (docs/research/navigation-and-steering.md §3.2): the
/// push, its cosine and size scaling through the original's overflowing multiply, the arrival
/// exemption and the repulsion from a remembered entity.</summary>
public class SeparationBehaviourTests
{
    private static Fixed16Vector T(double x, double y) => FakeSteeringWorld.Tiles(x, y);

    private static (SteeringBody a, SteeringBody b, SteeringMover mover, FakeSteeringWorld world) Touching()
    {
        var world = new FakeSteeringWorld("#######", "#.....#", "#.....#", "#######");
        var a = new SteeringBody { Pos = T(3.5, 1.5) };
        var b = new SteeringBody { Pos = a.Pos + new Fixed16Vector(0x1999, 0) };
        world.Neighbours.Add(new SteeringNeighbour(a, 2));
        world.Neighbours.Add(new SteeringNeighbour(b, 2));
        return (a, b, new SteeringMover { SizeClass = 2 }, world);
    }

    [Fact]
    public void TheSizeShareIsTheNeighboursShareOfThePairExceptOneCell()
    {
        Assert.Equal(0x8000, SeparationRules.SizeShare(1, 1));
        Assert.Equal(0xD53F, SeparationRules.SizeShare(1, 5));
        Assert.Equal(0x2A7E, SeparationRules.SizeShare(5, 1));
        Assert.Equal(0x71A9, SeparationRules.SizeShare(5, 4));
        Assert.Equal(0xC000, SeparationRules.SizeShare(4, 5));
    }

    /// <summary>The original multiplies two 16.16 numbers without shifting: the product is 65536
    /// times too large and wraps negative once (k + 0.2) x share reaches one half.</summary>
    [Fact]
    public void TheScaleIsAnUnshiftedProductThatWraps()
    {
        Assert.Equal(0x59998000, SeparationRules.Scale(0, 0x8000));
        Assert.True(SeparationRules.Scale(Fixed16.One, 0x8000) < 0);
        Assert.True(SeparationRules.Scale(-Fixed16.One, 0x8000) > 0);
        Assert.True(SeparationRules.Scale(0, 0xD53F) < 0);
    }

    [Fact]
    public void ATouchingNeighbourProducesAForceAndTheBodyItselfDoesNot()
    {
        (SteeringBody a, SteeringBody b, SteeringMover mover, FakeSteeringWorld world) = Touching();

        Assert.Equal(Fixed16Vector.Zero, SeparationBehaviour.Push(a, 2, a, 2));
        Assert.NotEqual(Fixed16Vector.Zero, SeparationBehaviour.Push(a, 2, b, 2));
        Assert.NotEqual(Fixed16Vector.Zero, SeparationBehaviour.Compute(a, mover, world));
    }

    /// <summary>The push is (n x magnitude) x scale with the layer's own multiply, wrap included,
    /// so it is checked against that chain rather than against a geometric expectation.</summary>
    [Fact]
    public void ThePushIsTheNormalTimesTheMagnitudeTimesTheScaleWrapAndAll()
    {
        (SteeringBody a, SteeringBody b, _, _) = Touching();
        a.Vel = new Fixed16Vector(-0x1000, 0);

        Fixed16Vector v = a.Pos - b.Pos;
        Fixed16Vector n = Fixed16.Normalise(v);
        int ratio = SeparationRules.Ratio(v.Octile(), a.Radius + b.Radius);
        Fixed16Vector push = n.Scale(SeparationRules.Magnitude(ratio, a.MaxForce, false, false));
        int scale = SeparationRules.Scale(n.Dot(Fixed16.Normalise(a.Vel)), SeparationRules.SizeShare(2, 2));

        Assert.Equal(push.Scale(scale), SeparationBehaviour.Push(a, 2, b, 2));
        Assert.True(scale < 0, "moving away from an equal neighbour wraps the scale negative");
    }

    [Fact]
    public void NoPushBeyondTheSummedRadiiOrFromABlocker()
    {
        (SteeringBody a, SteeringBody b, _, _) = Touching();
        b.Pos = a.Pos + new Fixed16Vector(0x7000, 0);
        Assert.Equal(Fixed16Vector.Zero, SeparationBehaviour.Push(a, 2, b, 2));

        b.Pos = a.Pos + new Fixed16Vector(0x1999, 0);
        a.IsBlocker = true;
        Assert.Equal(Fixed16Vector.Zero, SeparationBehaviour.Push(a, 2, b, 2));
    }

    [Fact]
    public void ArrivedWithTheFlagNothingPushes()
    {
        (SteeringBody a, _, SteeringMover mover, FakeSteeringWorld world) = Touching();
        a.PathFlags = 8;
        a.PathTotalCount = 1;
        a.Subpath[0] = a.Pos + new Fixed16Vector(0x4000, 0);

        Assert.Equal(Fixed16Vector.Zero, SeparationBehaviour.Compute(a, mover, world));
    }

    /// <summary>The remembered entity repels straight away from itself, with no scale: within its
    /// four tiles plus the radius, and not beyond.</summary>
    [Fact]
    public void TheRememberedEntityRepelsWithinItsReach()
    {
        var body = new SteeringBody { Pos = T(3.5, 1.5) };
        var other = new SteeringBody { Pos = T(6.5, 1.5) };

        Fixed16Vector near = SeparationBehaviour.KeepAway(body, other, 4);
        Assert.True(near.X < 0 && near.Y == 0, $"was {near}");

        other.Pos = T(8.5, 1.5);
        Assert.Equal(Fixed16Vector.Zero, SeparationBehaviour.KeepAway(body, other, 4));
        Assert.Equal(Fixed16Vector.Zero, SeparationBehaviour.KeepAway(body, body, 4));
    }
}
