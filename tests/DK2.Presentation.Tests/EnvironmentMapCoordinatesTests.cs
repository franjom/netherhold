// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Presentation;

namespace DK2.Presentation.Tests;

/// <summary>DK2's environment-pass coordinates (<c>0x00582290</c>, kmf-materials.md §8). The shader
/// that draws them repeats the formula inline; these tests pin the C# reference of it.</summary>
public class EnvironmentMapCoordinatesTests
{
    [Fact]
    public void ANormalAlongTheEyeSamplesTheCentre()
    {
        Vector2 uv = EnvironmentMapCoordinates.Of(new Vector3(0, 0, 5), new Vector3(0, 0, 1));
        Assert.Equal(new Vector2(0.5f, 0.5f), uv);
    }

    [Fact]
    public void TheEyeIsNormalisedAndTheNormalHalved()
    {
        // u = Nz·Ex − Nx·Ez + 0.5, v = Nz·Ey − Ny·Ez + 0.5 with E unit and N × 0.5.
        Vector2 near = EnvironmentMapCoordinates.Of(new Vector3(1, 0, 0), new Vector3(0, 0, 1));
        Vector2 far = EnvironmentMapCoordinates.Of(new Vector3(40, 0, 0), new Vector3(0, 0, 1));
        Assert.Equal(near, far);
        Assert.Equal(1f, near.X, 5);
        Assert.Equal(0.5f, near.Y, 5);

        Vector2 side = EnvironmentMapCoordinates.Of(new Vector3(0, 0, 2), new Vector3(1, 1, 0));
        Assert.Equal(0f, side.X, 5);
        Assert.Equal(0f, side.Y, 5);
    }

    [Fact]
    public void AZeroEyeSamplesTheCentre() =>
        Assert.Equal(new Vector2(0.5f, 0.5f), EnvironmentMapCoordinates.Of(Vector3.Zero, Vector3.UnitZ));

    [Fact]
    public void TheRenderFrameConvertsAsTheRendererDoes()
    {
        // RenderMesh.ToRenderFrame is (x, -z, -y); this is its inverse.
        var dk2 = new Vector3(1, 2, 3);
        var render = new Vector3(dk2.X, -dk2.Z, -dk2.Y);
        Assert.Equal(dk2, EnvironmentMapCoordinates.FromRenderFrame(render));
    }

    [Fact]
    public void FromRightHandedViewFlipsZ_UnconfirmedFrame()
    {
        // Pins our GUESS, not DK2: that its camera space is Direct3D's left-handed one (z into the
        // screen). The view matrix at 0x00760AC4 was not traced to its builder, so the axis
        // directions and the sign of v are unconfirmed (kmf-materials.md section 8).
        Assert.Equal(new Vector3(1, 2, -3), EnvironmentMapCoordinates.FromRightHandedView(new Vector3(1, 2, 3)));
    }
}
