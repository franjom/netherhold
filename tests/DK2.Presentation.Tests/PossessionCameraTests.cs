// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Data.Model;

namespace DK2.Presentation.Tests;

/// <summary>The possessed view in host axes, and the sounds its footfalls ask for.</summary>
public sealed class PossessionCameraTests
{
    private static void Near(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < 1e-5f, $"{actual} is not {expected}");

    [Theory]
    [InlineData(0, 0f, 1f)]          // yaw 0 walks minus Y, which is north
    [InlineData(0x200, 1f, 0f)]      // a quarter turn walks plus X, east
    [InlineData(0x400, 0f, -1f)]
    public void TheLevelViewLooksAlongTheHeadingTheStepWalks(int yaw, float x, float z) =>
        Near(new Vector3(x, 0, z), PossessionCamera.Forward(yaw, PossessionView.Horizon));

    [Fact]
    public void APitchBelowTheHorizonLooksDown()
    {
        float half = MathF.Sqrt(.5f);

        Near(new Vector3(0, -half, half), PossessionCamera.Forward(0, PossessionView.MinPitch));
        Near(new Vector3(0, half, half), PossessionCamera.Forward(0, PossessionView.MaxPitch));
    }

    [Fact]
    public void ThePoseIsTheViewsPointWithHeightUpAndYNegated()
    {
        var view = new PossessionView();
        view.Enter(0x5800, 0x2000, 0x1000, new PossessedBody(100, 200, 50, false, false, false, false, 0x200, 0x600, 0, 0, 4, false, 0x200));

        PossessionCameraPose pose = PossessionCamera.Pose(view);

        Near(new Vector3(5.5f, 1.375f, -2f), pose.Position);
        Near(new Vector3(1, 0, 0), pose.Forward);
        Assert.Equal(0f, pose.RollDegrees);
    }

    [Fact]
    public void TheFirstPersonLensGivesDk2sVeryWideView_StronglyInferredFromBinary()
    {
        var ordinary = new CameraThing(new byte[84]) { Lens = 8192 };
        var possession = new CameraThing(new byte[84]) { Lens = 4096, ViewDistance = 49152 };
        var native = new CameraViewport(1600, 1600, 1200);

        float wide = PossessionCamera.VerticalFieldOfView(possession, native, null);
        float narrow = PossessionCamera.VerticalFieldOfView(ordinary, native, null);

        // A focal length of 320 pixels whatever the screen: 600 pixels of half height over it.
        Assert.Equal(2 * MathF.Atan(600f / 320f) * 180 / MathF.PI, wide, 3);
        Assert.Equal(123.9f, wide, 1);
        Assert.Equal(2 * MathF.Tan(narrow * MathF.PI / 360), MathF.Tan(wide * MathF.PI / 360), 4);
        // On DK2's 640 by 480 the same lens is 73.7 degrees: the angle grows with the resolution.
        Assert.Equal(73.7f, PossessionCamera.VerticalFieldOfView(possession, new CameraViewport(640, 640, 480), null), 1);
        // A reference width keeps the 1600-wide angle on another screen of the same shape.
        Assert.Equal(wide, PossessionCamera.VerticalFieldOfView(possession, new CameraViewport(800, 800, 600), 1600), 3);
        Assert.Equal(12f, PossessionCamera.FarClipDistance(possession));
    }

    [Fact]
    public void TheLensFormulaIsTheOrdinaryCamerasOwn()
    {
        var profile = new CameraThing(new byte[84]) { Zoom = 40960, MinZoom = 16384, MaxZoom = 53248, Lens = 8192, AnglePitch = 220 };
        var viewport = new CameraViewport(1920, 1920, 1080);
        var camera = new ClassicCamera(profile) { ReferenceScreenWidth = 1600 };

        Assert.Equal(camera.Pose(Vector3.Zero, viewport).VerticalFieldOfView, ClassicCamera.VerticalFieldOfView(8192, viewport, 1600));
    }

    [Theory]
    [InlineData(false, false, 0x332u)]
    [InlineData(true, false, 0x333u)]
    [InlineData(false, true, 0x337u)]
    [InlineData(true, true, 0x338u)]
    public void AFootfallAsksItsSoundAtTheFootsPlace_StronglyInferredFromBinary(bool right, bool wet, uint id)
    {
        SoundRequest? request = PossessionFootsteps.For("IMP", new PossessionFootstep(right, wet, 0x1000, 0x3000, 0x800));

        Assert.NotNull(request);
        Assert.Equal(("imp", id), (request.Value.Category.Name, request.Value.Id.Value));
        Near(new Vector3(1, .5f, -3), request.Value.World!.Value);
        Assert.Equal(default, request.Value.Voice);
        Assert.Null(PossessionFootsteps.For("", new PossessionFootstep(right, wet, 0, 0, 0)));
    }
}
