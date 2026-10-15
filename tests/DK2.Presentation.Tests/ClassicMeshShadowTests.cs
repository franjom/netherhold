// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Presentation;

namespace DK2.Presentation.Tests;

public class ClassicMeshShadowTests
{
    [Fact]
    public void ReceiverIsOnTheGroundAndExtendsAwayFromTheLight()
    {
        Assert.True(ClassicMeshShadow.TryProject(new(new(0, 1.5f, 0), .5f, 1.01f),
            new(new(-1, 2, 0), 2.5f), out var shadow));
        Assert.Equal(1.01f, shadow.Centre.Y);
        Assert.Equal(Vector3.UnitX, shadow.AlongAxis);
        Assert.InRange(shadow.Centre.X, .24966f, .24968f);
        Assert.InRange(shadow.HalfLength, .56115f, .56118f);
        Assert.Equal(.5f, shadow.HalfWidth);
        Assert.Equal(.7f, shadow.Strength);
    }

    [Theory]
    [InlineData(1f, .7f)]
    [InlineData(2f, .3f)]
    [InlineData(2.4f, .06f)]
    public void StrengthUsesNativeDistanceFadeAndCap(float distance, float expected)
    {
        Assert.True(ClassicMeshShadow.TryProject(new(new(0, 1, 0), .25f, 1),
            new(new(-distance, 1, 0), 2.5f), out var shadow));
        Assert.InRange(shadow.Strength, expected - .00001f, expected + .00001f);
    }

    [Fact]
    public void SelectionChecksThreeDimensionalRangeAndReceiverFadeDistance()
    {
        var caster = new ClassicShadowCaster(new(0, 1, 0), .25f, 1);
        Assert.False(ClassicMeshShadow.TryProject(caster, new(new(0, 3.5f, 0), 2.5f), out _));
        Assert.False(ClassicMeshShadow.TryProject(caster, new(new(-1, 1, 0), 1f), out _));
        Assert.True(ClassicMeshShadow.TryProject(caster, new(new(-1, 1, 0), 1.2f), out var shadow));
        Assert.InRange(shadow.Strength, .24999f, .25001f);
    }

    [Fact]
    public void ALightDirectlyAboveProducesFiniteUnstretchedGeometry()
    {
        Assert.True(ClassicMeshShadow.TryProject(new(new(0, 1.5f, 0), .3f, 1),
            new(new(0, 2, 0), 2.5f), out var shadow));
        Assert.Equal(new Vector3(0, 1, 0), shadow.Centre);
        Assert.Equal(.3f, shadow.HalfLength);
        Assert.Equal(0, shadow.AngleRadians);
        Assert.Equal(Vector3.Zero, Vector3.Cross(shadow.CrossAxis, shadow.AlongAxis) - -Vector3.UnitY);
    }
}
