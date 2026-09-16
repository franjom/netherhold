// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Simulation;

namespace DK2.Simulation.Tests;

/// <summary>
/// DK2's Separation force (docs/research/navigation-and-steering.md §3.2, ADR-0156).
///
/// Pure arithmetic against the binary's own operations. Nothing here runs a simulation, because
/// nothing calls the rule: how the original integrates a behaviour force into motion is Q343.
/// </summary>
public class SeparationTests
{
    [Fact]
    public void TheDistanceIsTheLongerAxisPlusHalfTheShorter()
    {
        Assert.Equal(100, SeparationRules.OctileDistance(100, 0));
        Assert.Equal(100, SeparationRules.OctileDistance(0, -100));
        Assert.Equal(150, SeparationRules.OctileDistance(100, 100));
        Assert.Equal(120, SeparationRules.OctileDistance(-100, 40));
    }

    /// <summary>The approximation never under-reports, which is what keeps a creature pushing
    /// slightly too early rather than slightly too late.</summary>
    [Theory]
    [InlineData(3, 4)]
    [InlineData(300, 400)]
    [InlineData(1000, 1)]
    [InlineData(7, 7)]
    public void TheDistanceNeverUnderstatesTheTrueLength(int dx, int dy)
    {
        double euclidean = Math.Sqrt((double)dx * dx + (double)dy * dy);
        Assert.True(SeparationRules.OctileDistance(dx, dy) >= (int)euclidean);
    }

    [Fact]
    public void BodiesPushOnlyWhileTheyOverlap()
    {
        Assert.True(SeparationRules.Applies(99, 100));
        Assert.True(SeparationRules.Applies(100, 100));
        Assert.False(SeparationRules.Applies(101, 100));
    }

    /// <summary>Concentric bodies are excluded, not saturated: with no offset there is no
    /// direction to push along, so the original drops the pair rather than dividing by zero.
    /// The saturation arm is for <i>nearly</i> concentric.</summary>
    [Fact]
    public void ExactlyConcentricBodiesAreSkippedEntirely()
    {
        Assert.False(SeparationRules.Applies(0, 100));
    }

    [Fact]
    public void JustTouchingIsTheWholeRatioAndConcentricIsNone()
    {
        Assert.Equal(SeparationRules.One, SeparationRules.Ratio(100, 100));
        Assert.Equal(SeparationRules.One / 2, SeparationRules.Ratio(50, 100));
    }

    [Fact]
    public void AnAlmostConcentricPairPushesAtMaxForce()
    {
        int ratio = SeparationRules.Ratio(1, 1000);
        Assert.True(ratio <= SeparationRules.SaturationRatio);
        Assert.Equal(4242, SeparationRules.Magnitude(ratio, 4242, false, false));
    }

    /// <summary>The blocker arm is a plain reciprocal, so a pair just touching pushes with
    /// nothing at all and the force grows only as they sink into each other.</summary>
    [Fact]
    public void AgainstABlockerTheForceVanishesAtTheMomentOfContact()
    {
        Assert.Equal(0, SeparationRules.Magnitude(SeparationRules.One, 9999, true, false));
    }

    [Fact]
    public void AgainstABlockerHalfOverlapPushesWithOne()
    {
        int ratio = SeparationRules.Ratio(50, 100);
        Assert.Equal(SeparationRules.One, SeparationRules.Magnitude(ratio, 9999, true, false));
    }

    /// <summary>Ignoring blockers is what sends a body down the square-root arm instead, and the
    /// two arms disagree — which is the point of the flag.</summary>
    [Fact]
    public void IgnoringBlockersTakesTheOtherArm()
    {
        int ratio = SeparationRules.Ratio(50, 100);
        int blocked = SeparationRules.Magnitude(ratio, 9999, true, false);
        int ignored = SeparationRules.Magnitude(ratio, 9999, true, true);
        Assert.NotEqual(blocked, ignored);
    }

    /// <summary>A neighbour that does not block is treated the same as one whose blocking is
    /// ignored: both arms are the square root.</summary>
    [Fact]
    public void ANonBlockingNeighbourAndAnIgnoredBlockerAgree()
    {
        int ratio = SeparationRules.Ratio(70, 100);
        Assert.Equal(
            SeparationRules.Magnitude(ratio, 9999, false, false),
            SeparationRules.Magnitude(ratio, 9999, true, true));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(4, 2)]
    [InlineData(65536, 256)]
    [InlineData(1000000, 1000)]
    public void TheSquareRootAgreesWithTheRealOneOnExactSquares(int value, int expected)
    {
        Assert.Equal(expected, SeparationRules.IntegerSquareRoot(value));
    }

    /// <summary>The original's loop stops at the first guess that is no larger than the true root,
    /// so it never overshoots. Swept rather than sampled, because an off-by-one here would bias
    /// every force on the curve arm.</summary>
    [Fact]
    public void TheSquareRootNeverExceedsTheTrueRoot()
    {
        for (int value = 1; value < 20000; value++)
        {
            int root = SeparationRules.IntegerSquareRoot(value);
            Assert.True((long)root * root <= value, $"{root}^2 > {value}");
        }
    }

    /// <summary>On the curve arm the force is monotone: sinking further in never pushes less hard.
    /// Swept rather than sampled, because a curve that folded back would be invisible in samples.
    /// </summary>
    [Fact]
    public void SinkingDeeperNeverPushesLessHardOnTheCurve()
    {
        int previous = 0;
        for (int distance = 1000; distance >= 1; distance--)
        {
            int ratio = SeparationRules.Ratio(distance, 1000);
            if (ratio <= SeparationRules.SaturationRatio) break;

            int magnitude = SeparationRules.Magnitude(ratio, 1 << 20, false, false);
            Assert.True(magnitude >= previous, $"fell from {previous} to {magnitude} at {distance}");
            previous = magnitude;
        }

        Assert.True(previous > 0);
    }

    /// <summary>The saturation arm returns a smaller <i>magnitude</i> than the curve immediately
    /// above it, which looked incoherent until the caller was read (Q346, answered).
    ///
    /// It is not a discontinuity in the force: the combiner truncates every behaviour's force to
    /// max_force before weighting it, so the curve arm's very large magnitude is clamped to exactly
    /// what this arm returns directly. Pinned because the magnitudes are what this rule produces,
    /// and a consumer that stopped clamping would make the difference real.
    /// </summary>
    [Fact]
    public void TheSaturationArmCanUndercutTheCurveItReplaces()
    {
        int justOnTheCurve = SeparationRules.SaturationRatio + 1;
        int curve = SeparationRules.Magnitude(justOnTheCurve, SeparationRules.ShippedMaxForce, false, false);
        int saturated = SeparationRules.Magnitude(
            SeparationRules.SaturationRatio, SeparationRules.ShippedMaxForce, false, false);

        Assert.True(curve > saturated);
    }

    /// <summary>Across the threshold both arms end at the same place once the combiner has
    /// truncated them, which is the whole of Q346's answer. The two magnitudes differ by a factor
    /// of about 1700 and the clamp erases all of it.</summary>
    [Fact]
    public void BothArmsClampToMaxForceAtTheThreshold()
    {
        int below = SeparationRules.Magnitude(
            SeparationRules.SaturationRatio, SeparationRules.ShippedMaxForce, false, false);
        int above = SeparationRules.Magnitude(
            SeparationRules.SaturationRatio + 1, SeparationRules.ShippedMaxForce, false, false);

        Assert.Equal(SeparationRules.ShippedMaxForce, below);
        Assert.True(above > SeparationRules.ShippedMaxForce);

        static int Truncate(int magnitude, int limit) => magnitude > limit ? limit : magnitude;
        Assert.Equal(Truncate(below, SeparationRules.ShippedMaxForce),
                     Truncate(above, SeparationRules.ShippedMaxForce));
    }

    /// <summary>The three scalars are literals in the steering reset, not creature-record fields,
    /// so every creature separates identically. If a per-creature source is ever found, this is the
    /// assertion that should stop being true.</summary>
    [Fact]
    public void EveryCreatureSharesOneRadiusForceAndSpeed()
    {
        Assert.Equal(0x3333, SeparationRules.ShippedRadius);
        Assert.Equal(0x18000, SeparationRules.ShippedMaxForce);
        Assert.Equal(0x6666, SeparationRules.ShippedMaxSpeed);

        // Two creatures overlap only within 0.4 of each other, which is well under one tile.
        Assert.Equal(2 * 0x3333, SeparationRules.ShippedRadius * 2);
    }
}
