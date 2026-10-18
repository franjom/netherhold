// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using DK2.Presentation;
using Xunit;

namespace DK2.Presentation.Tests;

public sealed class MinimapNorthTests
{
    [Theory]
    [InlineData(0, 94, 610)]
    [InlineData(512, 38, 666)]
    [InlineData(1024, 94, 722)]
    [InlineData(1536, 150, 666)]
    [InlineData(2560, 38, 666)]
    public void NorthOrbitsAgainstCameraHeading(int heading, float x, float y)
    {
        Vector2 actual = MinimapNorth.SpriteOriginPixels(new((ushort)heading), 768);
        Assert.Equal(x, actual.X, precision: 3);
        Assert.Equal(y, actual.Y, precision: 3);
    }

    [Fact]
    public void NativeOffsetsAndTruncationAreAppliedAfterScreenTransform()
    {
        Vector2 actual = MinimapNorth.SpriteOriginPixels(new(256), 1440);
        Assert.Equal(new Vector2(106, 1180), actual);
        Assert.Equal(new Vector2(74, 819), MinimapNorth.SpriteOriginPixels(new(256), 1000));
        Assert.Throws<ArgumentOutOfRangeException>(() => MinimapNorth.SpriteOriginPixels(new(0), 0));
    }
}
