// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.


using System.Numerics;

namespace DK2.Presentation.Tests;

public sealed class ClassicShadowCoverageTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OverlappingTrianglesResolveCoverageOnceAtEitherWinding(bool reverse)
    {
        const int size = ClassicMeshShadow.TextureSize;
        byte[] samples = new byte[size * size];
        Vector2 a = new Vector2(2, 2), b = new Vector2(3, 2), c = new Vector2(2, 3);

        if (reverse)
        {
            (a, c) = (c, a);
        }

        ClassicShadowCoverage.AddTriangle(samples, a, b, c);
        ClassicShadowCoverage.AddTriangle(samples, a, b, c);
        ClassicShadowCoverage.Resolve(samples);

        Assert.Equal(191, samples[2 * size + 2]);
        Assert.Equal(0, samples[2 * size + 1]);
        Assert.Equal(0, samples[3 * size + 2]);
    }

    [Fact]
    public void AClippedTriangleCanFillTheCornerWithoutWritingOutsideTheMask()
    {
        const int size = ClassicMeshShadow.TextureSize;
        byte[] samples = new byte[size * size];
        ClassicShadowCoverage.AddTriangle(samples, new Vector2(-2, -2), new Vector2(4, -2), new Vector2(-2, 4));
        ClassicShadowCoverage.Resolve(samples);
        Assert.Equal(255, samples[0]);
        Assert.Equal(0, samples[^1]);
    }
}
