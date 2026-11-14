// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using DK2.Data.Model;

namespace DK2.Presentation.Tests;

/// <summary>The camera's timed move between the ordinary view and the first-person one
/// (possession-client.md §6).</summary>
public sealed class PossessionFlightTests
{
    private static readonly CameraFlightState From = new(0x10000, 0x20000, 0x1000, 40960, 8192, 61440, 0x7F0, 220, 0);
    private static readonly CameraFlightState To = new(0x12000, 0x1F000, 0x1600, 0, 4096, 49152, 0x010, 0x200, 0);

    [Fact]
    public void EachFrameClosesItsShareOfTheRemainingGap_StronglyInferredFromBinary()
    {
        var flight = new PossessionFlight();
        flight.Start(From, To, 1000);

        Assert.False(flight.Step(1500));

        // A quarter of the 2000 ms: a quarter of every gap, the yaw the short way through zero.
        Assert.Equal(new CameraFlightState(0x10800, 0x1FC00, 0x1180, 30720, 7168, 58368, 0x7F8, 293, 0), flight.State);
        Assert.Equal(.25f, flight.Progress);

        // 500 of the 1500 left: a third of what remains.
        Assert.False(flight.Step(2000));
        Assert.Equal(0x10800 + (0x12000 - 0x10800) / 3, flight.State.X);
        Assert.Equal(0, flight.State.Yaw);
        Assert.Equal(.5f, flight.Progress);
    }

    [Fact]
    public void TheMoveLandsOnTheTargetAtItsEnd_StronglyInferredFromBinary()
    {
        var flight = new PossessionFlight();
        flight.Start(From, To, 1000);
        flight.Step(1700);

        Assert.False(flight.Step(2999));
        Assert.True(flight.Active);
        Assert.True(flight.Step(3000));

        Assert.False(flight.Active);
        Assert.Equal(To, flight.State);
        Assert.Equal(1f, flight.Progress);
        Assert.False(flight.Step(3100));
    }

    [Fact]
    public void AnAngleTurnsDownWhenThatIsShorter_StronglyInferredFromBinary()
    {
        var flight = new PossessionFlight();
        flight.Start(From with { Yaw = 0x010, Roll = 0x7C0 }, To with { Yaw = 0x7F0, Roll = 0 }, 0);

        flight.Step(1000);

        Assert.Equal((0, 0x7E0), (flight.State.Yaw, flight.State.Roll));
    }

    [Fact]
    public void AGapThatDoesNotDivideIsCutTowardZero_StronglyInferredFromBinary()
    {
        var flight = new PossessionFlight();
        flight.Start(From with { X = 10, Y = -10, Pitch = 5 }, To with { X = 3, Y = -3, Pitch = 0 }, 0);

        flight.Step(1000);

        // -7 / 2 is -3 and 7 / 2 is 3, as idiv gives; the angle's -5 / 2 is -2.
        Assert.Equal((7, -7, 3), (flight.State.X, flight.State.Y, flight.State.Pitch));
    }

    private static ClassicCamera Ordinary() => new(
        new CameraThing(new byte[84]) { Zoom = 40960, MinZoom = 16384, MaxZoom = 53248, Lens = 8192, ViewDistance = 61440, AnglePitch = 220 },
        new Dk2Angle(0x7F3)) { ReferenceScreenWidth = 1600 };

    [Fact]
    public void TheOrdinaryCamerasStateGivesItsOwnPose()
    {
        ClassicCamera camera = Ordinary();
        var focus = new Vector3(16, 1, -32);
        var viewport = new CameraViewport(1920, 1920, 1080);

        CameraFlightState state = camera.State(focus);

        Assert.Equal(new CameraFlightState(0x10000, 0x20000, 0x1000, 40960, 8192, 61440, 0x7F3, 220, 0), state);
        Assert.Equal(camera.Pose(focus, viewport), camera.Pose(state, viewport, 0));
    }

    [Theory]
    [InlineData(0, 0x200)]
    [InlineData(0x200, 0x200)]
    [InlineData(0x555, 0x180)]
    [InlineData(0x7C0, 0x2F0)]
    public void AtTheEyeThePoseIsTheFirstPersonCamerasOwn(int yaw, int pitch)
    {
        ClassicCamera camera = Ordinary();
        var preset = new CameraThing(new byte[84]) { Lens = 4096, ViewDistance = 49152 };
        var state = new CameraFlightState(0x5800, 0x2000, 0x1600, 0, preset.Lens, preset.ViewDistance, yaw, pitch, 0);

        ClassicCameraPose pose = camera.Pose(state, new CameraViewport(1920, 1920, 1080), 1);

        Assert.True(Vector3.Distance(PossessionCamera.ToHost(state.X, state.Y, state.Z), pose.Position) < 1e-5f);
        Assert.True(Vector3.Distance(PossessionCamera.Forward(yaw, pitch), pose.Forward) < 1e-5f, $"{pose.Forward}");
        Assert.True(pose.Up.Y > 0);
        Assert.Equal(PossessionCamera.VerticalFieldOfView(preset), pose.VerticalFieldOfView, 4);
        Assert.Equal(PossessionCamera.FarClipDistance(preset), pose.FarClipDistance);
        Assert.Equal(0f, pose.ProjectionCentreShift);
    }

    [Fact]
    public void TheWayBackIsOverTheSamePointWithTheYawRoundedToEight_StronglyInferredFromBinary()
    {
        ClassicCamera camera = Ordinary();
        var eye = new CameraFlightState(0x5800, 0x2000, 0x1600, 0, 4096, 49152, 0x7FD, 0x250, 0x30);

        CameraFlightState back = camera.Return(eye, 0x1000);

        // The last four yaws round up to 0x800, unmasked, as DK2's 16-bit word does.
        Assert.Equal(new CameraFlightState(0x5800, 0x2000, 0x1000, 40960, 8192, 61440, 0x800, 220, 0), back);
        Assert.Equal(0x148, camera.Return(eye with { Yaw = 0x14B }, 0x1000).Yaw);
        Assert.Equal(0x150, camera.Return(eye with { Yaw = 0x14C }, 0x1000).Yaw);

        camera.Face(new Dk2Angle((ushort)back.Yaw));
        Assert.Equal(back with { Yaw = 0 }, camera.State(new Vector3(5.5f, 1, -2)));
    }
}
