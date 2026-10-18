// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Presentation.Tests;

public sealed class MinimapHeartTrailTests
{
    private static readonly FpPosition3 Focus = new(new(20 << 12), new(20 << 12), new(0));

    [Theory]
    [InlineData(0, 84)]
    [InlineData(1, 85)]
    [InlineData(2, 86)]
    [InlineData(3, 87)]
    [InlineData(4, 84)]
    public void DotsStartAtHeartWithTurnPhaseAndStopBeforeCentre(int tick, int firstX)
    {
        var points = new MinimapRasterPoint[MinimapHeartTrail.MaximumDots];
        int count = MinimapHeartTrail.Build(new(40, 20), Focus, new(0), new(tick), points);
        Assert.Equal(3, count);
        Assert.Equal(new(firstX, 64), points[0]);
        Assert.Equal(new(firstX - 8, 64), points[2]);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 2)]
    public void WholeMapAndNearbyHeartHaveNoTrail(int zoom, int expected)
    {
        var points = new MinimapRasterPoint[MinimapHeartTrail.MaximumDots];
        Assert.Equal(expected, MinimapHeartTrail.Build(new(28, 20), Focus, new(zoom), new(0), points));
    }

    [Fact]
    public void FractionalFocusIsSubtractedBeforeDirectionNormalization()
    {
        var points = new MinimapRasterPoint[MinimapHeartTrail.MaximumDots];
        FpPosition3 focus = new(new((20 << 12) + 0xC00), new(20 << 12), new(0));
        int count = MinimapHeartTrail.Build(new(30, 20), focus, new(2), new(0), points);
        Assert.Equal(8, count);
        Assert.Equal(new(101, 64), points[0]);
        Assert.Equal(new(73, 64), points[count - 1]);
    }

    [Theory]
    [InlineData(-1, 0, 76, 1)]
    [InlineData(-4097, 0, 77, 2)]
    [InlineData(-1, 1, 87, 4)]
    public void NegativeFractionalFocusUsesSignedRemainderAndArithmeticShift(int rawX, int zoom, int firstX, int count)
    {
        var points = new MinimapRasterPoint[MinimapHeartTrail.MaximumDots];
        Assert.Equal(count, MinimapHeartTrail.Build(new(10, 0), new(new(rawX), new(0), new(0)),
            new(zoom), new(0), points));
        Assert.Equal(new(firstX, 64), points[0]);
    }

    [Fact]
    public void FarHeartKeepsOnlyStrictlyInteriorDotsWithoutAllocating()
    {
        var points = new MinimapRasterPoint[MinimapHeartTrail.MaximumDots];
        int count = MinimapHeartTrail.Build(new(1000, 20), Focus, new(4), new(3), points);
        Assert.InRange(count, 1, MinimapHeartTrail.MaximumDots);
        foreach (var point in points.AsSpan(0, count))
        {
            Assert.InRange(point.X, 1, 127);
            Assert.InRange(point.Y, 1, 127);
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) MinimapHeartTrail.Build(new(1000, 20), Focus, new(4), new(i), points);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Theory]
    [InlineData(10000, 10000)]
    [InlineData(-10000, 10000)]
    [InlineData(1000, -20000)]
    [InlineData(-17, -31)]
    public void SkippingTheInvisiblePrefixPreservesTheFullNativeWalk(int x, int y)
    {
        var points = new MinimapRasterPoint[MinimapHeartTrail.MaximumDots];
        for (int zoom = 0; zoom <= 4; zoom++)
            for (int phase = 0; phase < 4; phase++)
            {
                int vx = x << zoom, vy = y << zoom;
                int length = IntegerMath.SquareRoot(unchecked((uint)(vx * vx + vy * vy)));
                int nx = unchecked(vx * 256) / length, ny = unchecked(vy * 256) / length;
                int ax = unchecked(nx * (length + phase)), ay = unchecked(ny * (length + phase));
                var expected = new List<MinimapRasterPoint>();
                for (int i = 0; i < (length - 5) / 4; i++)
                {
                    int px = 64 + ax / 256, py = 64 + ay / 256;
                    if (px is > 0 and < 128 && py is > 0 and < 128) expected.Add(new(px, py));
                    ax = unchecked(ax - nx * 4); ay = unchecked(ay - ny * 4);
                }
                int count = MinimapHeartTrail.Build(new(x, y), default, new(zoom), new(phase), points);
                Assert.Equal(expected, points.Take(count));
            }
    }
}
