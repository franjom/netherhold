// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;

namespace DK2.Presentation.Tests;

public class ClassicInstancePointLightTests
{
    [Fact]
    public void RadiusIsSampledAtInstanceWhileVertexDisplacementChangesIncidence()
    {
        ClassicPointLight source = Light();
        var light = ClassicInstancePointLight.Sample(source, Vector3.UnitX, Matrix4x4.Identity);
        Assert.Equal(0.9375f, light.Attenuation);
        Assert.Equal(-Vector3.UnitX, light.Direction);
        Assert.Equal(0.9375f, light.ContributionAt(Vector3.Zero, -Vector3.UnitX).X);
        Assert.Equal(0.703125f, light.ContributionAt(new Vector3(-.25f, 0, 0), -Vector3.UnitX).X);
        Assert.Equal(1f, source.ContributionAt(new Vector3(.75f, 0, 0), -Vector3.UnitX).X);
    }

    [Fact]
    public void DirectionUsesThreeRowDotProductsWithoutTranslationOrNormalization()
    {
        var transform = new Matrix4x4(
            0, -2, 0, 81,
            3, 0, 0, 82,
            0, 0, 4, 83,
            91, 92, 93, 1);
        var light = ClassicInstancePointLight.Sample(Light(32), Vector3.UnitX, transform);
        Assert.Equal(new Vector3(0, -3, 0), light.Direction);
        Assert.Equal(0.703125f, light.ContributionAt(Vector3.Zero, -Vector3.UnitY).X);
    }

    [Fact]
    public void DirectionUsesEveryMatrixCoefficientForAThreeComponentOffset()
    {
        var transform = new Matrix4x4(
            1, 2, 3, 81,
            4, 5, 6, 82,
            7, 8, 9, 83,
            91, 92, 93, 1);
        var light = ClassicInstancePointLight.Sample(Light(), new Vector3(1, .5f, .25f), transform);
        Assert.Equal(new Vector3(-2.75f, -8, -13.25f), light.Direction);
    }

    [Fact]
    public void DisplacedNormalSumPreservesNativeXThenZThenYOrder()
    {
        var light = ClassicInstancePointLight.Sample(Light(), Vector3.One, Matrix4x4.Identity);
        Vector3 contribution = light.ContributionAt(Vector3.Zero, new Vector3(-1e20f, -1, 1e20f));
        Assert.InRange(contribution.X, .1458332f, .1458335f);
    }

    [Theory]
    [InlineData(2f)]
    [InlineData(3f)]
    public void InstanceAtOrOutsideRadiusContributesNothingEvenIfVertexIsCloser(float distance)
    {
        var light = ClassicInstancePointLight.Sample(Light(), new Vector3(distance, 0, 0), Matrix4x4.Identity);
        Assert.Equal(0, light.Attenuation);
        Assert.Equal(Vector3.Zero, light.ContributionAt(new Vector3(-distance, 0, 0), -Vector3.UnitX));
    }

    [Fact]
    public void IntensityActsAfterDotAndBeforePerLightCapWhileBackFacesAreSkipped()
    {
        var light = ClassicInstancePointLight.Sample(Light(64), Vector3.UnitX, Matrix4x4.Identity);
        Assert.Equal(Vector3.Zero, light.ContributionAt(Vector3.Zero, Vector3.UnitX));
        Assert.Equal(Vector3.Zero, light.ContributionAt(Vector3.Zero, Vector3.UnitY));
        Assert.Equal(0.46875f, light.ContributionAt(Vector3.Zero, -Vector3.UnitX).X);
        var bright = ClassicInstancePointLight.Sample(Light(255), Vector3.UnitX, Matrix4x4.Identity);
        Assert.Equal(new Vector3(1, 128f / 255f, 0), bright.ContributionAt(Vector3.Zero, -Vector3.UnitX));
    }

    [Fact]
    public void SingularPositiveIncidenceCapsAndIndeterminateProductsRemainExplicitlyUnsupported()
    {
        var light = ClassicInstancePointLight.Sample(Light(), Vector3.Zero, Matrix4x4.Identity);
        Assert.True(float.IsPositiveInfinity(light.Attenuation));
        Assert.Equal(1, light.ContributionAt(-Vector3.UnitX, Vector3.UnitX).X);
        Assert.Equal(Vector3.Zero, light.ContributionAt(Vector3.UnitX, Vector3.UnitX));
        Assert.Throws<NotSupportedException>(() => light.ContributionAt(Vector3.Zero, Vector3.UnitX));
        var zeroIntensity = ClassicInstancePointLight.Sample(Light(0), Vector3.UnitX * .1f, Matrix4x4.Identity);
        Assert.Throws<NotSupportedException>(() => zeroIntensity.ContributionAt(Vector3.Zero, -Vector3.UnitX));
        Assert.Equal(Vector3.Zero, zeroIntensity.ContributionAt(Vector3.Zero, Vector3.UnitX));
    }

    [Fact]
    public void LookupOffsetSwitchesOutOfSingularBinJustBeforeQuarterTileDistance()
    {
        var below = ClassicInstancePointLight.Sample(Light(), new Vector3(.24999f, 0, 0), Matrix4x4.Identity);
        var at = ClassicInstancePointLight.Sample(Light(), new Vector3(.25f, 0, 0), Matrix4x4.Identity);
        Assert.True(float.IsPositiveInfinity(below.Attenuation));
        Assert.Equal(15.99609375f, at.Attenuation);
    }

    [Fact]
    public void SamplingKeepsLookupQuantizationAndIndependentLightsCanSumAboveOne()
    {
        var light = ClassicInstancePointLight.Sample(Light(), new Vector3(1.02f, 0, 0), Matrix4x4.Identity);
        Assert.InRange(light.ContributionAt(Vector3.Zero, -Vector3.UnitX).X, .9433715f, .9433735f);
        var near = ClassicInstancePointLight.Sample(Light(), Vector3.UnitX * .5f, Matrix4x4.Identity);
        Vector3 one = near.ContributionAt(Vector3.Zero, -Vector3.UnitX);
        Assert.Equal(2, (one + one).X);
        Assert.Equal(Vector3.Zero, default(ClassicInstancePointLight).ContributionAt(Vector3.Zero, Vector3.UnitY));
    }

    private static ClassicPointLight Light(byte intensity = 128) => ClassicPointLight.FromTorch(
        new TorchView(0, new TilePos(0, 0), 0, 0, 0, 0, 255, 128, 0, intensity, 8192, default));
}
