// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;

namespace DK2.Presentation.Tests;

public class ClassicPointLightTests
{
    [Fact]
    public void PositiveIncidenceInTheSingularBinTakesTheNativeFullColourBranch()
    {
        Assert.Equal(new Vector3(1f, 128f / 255f, 0f),
            Light().ContributionAt(new Vector3(0.1f, 0, 0), -Vector3.UnitX));
    }

    [Fact]
    public void SingularBinIsExplicitlyUnsupportedForZeroIntensityTangentAndCoincidentVertices()
    {
        Assert.Throws<NotSupportedException>(() => Light(0).ContributionAt(new Vector3(0.1f, 0, 0), -Vector3.UnitX));
        Assert.Throws<NotSupportedException>(() => Light().ContributionAt(new Vector3(0.1f, 0, 0), Vector3.UnitY));
        Assert.Throws<NotSupportedException>(() => Light().ContributionAt(Vector3.Zero, Vector3.UnitX));
        Assert.Equal(Vector3.Zero, Light(0).ContributionAt(Vector3.UnitX, -Vector3.UnitX));
        Assert.Equal(Vector3.Zero, Light().ContributionAt(Vector3.UnitX, Vector3.UnitY));
    }

    [Fact]
    public void ASpriteTakesTheRadialTermAtItsCentreWithoutANormal()
    {
        // 0x0057C080: radius 2 tiles; at distance 1 the bin is round(16 - 0.49999) = 16, so the
        // factor is (0.25 + 16/16) x (4 - 1) / 4 = 0.9375; the first bin gives the full colour.
        Vector3 colour = new(1f, 128f / 255f, 0f);
        Assert.Equal(colour * 0.9375f, Light().RadialAt(new Vector3(1, 0, 0)));
        Assert.Equal(colour * 0.9375f, Light().RadialAt(new Vector3(0, 1, 0)));
        Assert.Equal(colour, Light().RadialAt(new Vector3(0.1f, 0, 0)));
        Assert.Equal(Vector3.Zero, Light().RadialAt(new Vector3(2.5f, 0, 0)));
        Assert.Equal(Vector3.Zero, Light(0).RadialAt(new Vector3(1, 0, 0)));
    }

    [Fact]
    public void InputConversionRequiresTheNativeRadiusClamp()
    {
        var torch = new TorchView(0, new TilePos(0, 0), 0, 0, 0, 0, 255, 255, 255, 128, 10113, default);
        Assert.Throws<ArgumentOutOfRangeException>(() => ClassicPointLight.FromTorch(torch));
    }

    private static ClassicPointLight Light(byte intensity = 128) => ClassicPointLight.FromTorch(
        new TorchView(0, new TilePos(0, 0), 0, 0, 0, 0, 255, 128, 0, intensity, 8192, default));

    [Fact]
    public void TorchInputsPreserveHdrColourAndNativeScales()
    {
        ClassicPointLight light = ClassicPointLight.FromTorch(new TorchView(0, new TilePos(0, 0),
            4096, 8192, 6144, 0, 300, 200, 100, 180, 6144, default));
        Assert.Equal(new Vector3(1, 1.5f, -2), light.Position);
        Assert.Equal(300f / 255f, light.Colour.X);
        Assert.Equal(180f / 128f, light.Intensity);
        Assert.Equal(1.5f, light.Radius);
    }

    [Theory]
    [InlineData(1f, 0.9375f)]
    [InlineData(0.5f, 1f)]
    [InlineData(2f, 0f)]
    [InlineData(3f, 0f)]
    public void NativeContributionUsesLookupRadiusAndPerLightCap(float distance, float expected)
    {
        Vector3 contribution = Light().ContributionAt(new Vector3(distance, 0, 0), -Vector3.UnitX);
        Assert.InRange(contribution.X, expected - 0.000001f, expected + 0.000001f);
    }

    [Fact]
    public void BackFacingNormalReceivesNoLightAndIntensityActsBeforeTheCap()
    {
        Assert.Equal(Vector3.Zero, Light().ContributionAt(Vector3.UnitX, Vector3.UnitX));
        Assert.Equal(0.46875f, Light(64).ContributionAt(Vector3.UnitX, -Vector3.UnitX).X);
        Assert.Equal(1f, Light(255).ContributionAt(Vector3.UnitX, -Vector3.UnitX).X);
    }

    [Fact]
    public void LookupQuantizationAndIndependentLightsKeepTheirNativeResponse()
    {
        var vertex = new Vector3(1.02f, 0, 0);
        Assert.InRange(Light().ContributionAt(vertex, -Vector3.UnitX).X, 0.9433715f, 0.9433735f);
        Vector3 one = Light().ContributionAt(new Vector3(0.5f, 0, 0), -Vector3.UnitX);
        Assert.Equal(2f, (one + one).X);
    }
}
