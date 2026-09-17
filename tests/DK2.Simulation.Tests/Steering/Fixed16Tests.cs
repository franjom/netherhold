// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Simulation.Steering;

namespace DK2.Simulation.Tests.Steering;

/// <summary>The steering layer's 16.16 helpers, each against the operation the original performs
/// (docs/research/steering-behaviours.md; navigation-and-steering.md §3.4).</summary>
public class Fixed16Tests
{
    [Fact]
    public void ProductsKeepTheMiddleBitsAndQuotientsShiftTheDividend()
    {
        Assert.Equal(0x4000, Fixed16.Mul(0x8000, 0x8000));
        Assert.Equal(-1, Fixed16.Mul(-1, 0x8000));
        Assert.Equal(0x8000, Fixed16.Div(Fixed16.One, 0x20000));
        Assert.Equal(-0x8000, Fixed16.Div(-Fixed16.One, 0x20000));
    }

    /// <summary>Under 1/16 tile the length is exact; above it the inputs lose eight bits, and above
    /// 64 tiles eighteen, before they are squared.</summary>
    [Fact]
    public void TheLengthIsExactInTheSmallBandAndCoarserAbove()
    {
        Assert.Equal(5, Fixed16.Length(3, 4));
        Assert.Equal(Fixed16.One, Fixed16.Length(Fixed16.One, 0));
        Assert.Equal(5 << 16, Fixed16.Length(3 << 16, 4 << 16));
        Assert.Equal(5 << 22, Fixed16.Length(3 << 22, 4 << 22));
        Assert.Equal(0x1000, Fixed16.Length(0x1001, 0));
    }

    [Fact]
    public void NormaliseGivesAUnitVectorInEveryBand()
    {
        Assert.Equal(new Fixed16Vector(Fixed16.One, 0), Fixed16.Normalise(new Fixed16Vector(0x800, 0)));
        Assert.Equal(new Fixed16Vector(0x9999, 0xCCCC), Fixed16.Normalise(new Fixed16Vector(3 << 16, 4 << 16)));
        Assert.Equal(new Fixed16Vector(0x9900, 0xCC00), Fixed16.Normalise(new Fixed16Vector(3 << 22, 4 << 22)));
        Assert.Equal(Fixed16Vector.Zero, Fixed16.Normalise(Fixed16Vector.Zero));
    }

    [Fact]
    public void TruncateLeavesAShortVectorAndScalesALongOne()
    {
        var v = new Fixed16Vector(3 << 16, 4 << 16);
        Assert.Equal(v, Fixed16.Truncate(v, 6 << 16));
        Assert.Equal(v, Fixed16.Truncate(v, 5 << 16));
        Assert.Equal(new Fixed16Vector(0x9999, 0xCCCC), Fixed16.Truncate(v, Fixed16.One));
    }

    [Fact]
    public void OctileAndPerpendicularAreTheOriginalsSmallHelpers()
    {
        Assert.Equal(150, Fixed16.Octile(100, -100));
        Assert.Equal(new Fixed16Vector(-2, 1), new Fixed16Vector(1, 2).Perpendicular());
        Assert.Equal((3, -1), new Fixed16Vector(0x3FFFF, -1).Tile());
    }
}
