// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using DK2.Data.Model;

namespace DK2.Presentation.Tests;

public class ClassicCameraControlsTests
{
    [Theory]
    [InlineData(0, ClassicCameraKeys.Left, ClassicCameraKeys.Right, 200)]
    [InlineData(4095, ClassicCameraKeys.Right, ClassicCameraKeys.Left, 3895)]
    public void OpposingEdgeAndKeyboardCommandsClampSeparately(int startRaw,
        ClassicCameraKeys edge, ClassicCameraKeys key, int expectedRaw)
    {
        var controls = new ClassicCameraControls(new ClassicCamera(Profile()), bounds:
            new CameraFocusBounds(Vector3.Zero, new Vector3(4095 / 4096f, 0, 0)));
        var result = controls.Advance(new Vector3(startRaw / 4096f, 0, 0), key,
            TimeSpan.FromMilliseconds(20), edge);
        Assert.Equal(expectedRaw / 4096f, result.X);
    }

    private static CameraThing Profile() => new(new byte[84])
    {
        CameraId = 3, Zoom = 40960, MinZoom = 16384, MaxZoom = 53248,
        Lens = 8192, AnglePitch = 220, Flags = 0x26,
    };

    [Theory]
    [InlineData(40960, 840)]
    [InlineData(16384, 336)]
    [InlineData(53247, 1090)]
    public void FirstRightFrameCombinesTimedStepWithScrollAcceleration(int zoom, int expectedRaw)
    {
        var camera = new ClassicCamera(Profile()); camera.SetZoom(new Fp32(zoom));
        var controls = new ClassicCameraControls(camera);
        Assert.Equal(new Vector3(expectedRaw / 4096f, 0, 0),
            controls.Advance(System.Numerics.Vector3.Zero, ClassicCameraKeys.Right, TimeSpan.FromMilliseconds(20)));
    }

    [Fact]
    public void HeldScrollAcceleratesToSixAndCoastsForFiveReleaseFrames()
    {
        var controls = new ClassicCameraControls(new ClassicCamera(Profile()));
        foreach (int raw in new[] { 840, 1160, 1480, 1800, 2120, 2120 })
            Assert.Equal(raw / 4096f, controls.Advance(System.Numerics.Vector3.Zero, ClassicCameraKeys.Right, TimeSpan.FromMilliseconds(20)).X);
        foreach (int raw in new[] { 1600, 1280, 960, 640, 320, 0 })
            Assert.Equal(raw / 4096f, controls.Advance(System.Numerics.Vector3.Zero, ClassicCameraKeys.None, TimeSpan.FromMilliseconds(20)).X);
    }

    [Fact]
    public void SpeedScrollMultipliesTheTimedStepWithoutMultiplyingTheCoast()
    {
        var controls = new ClassicCameraControls(new ClassicCamera(Profile()));
        Assert.Equal(2240 / 4096f,
            controls.Advance(System.Numerics.Vector3.Zero, ClassicCameraKeys.Right | ClassicCameraKeys.Fast, TimeSpan.FromMilliseconds(20)).X);
        Assert.Equal(320 / 4096f, controls.Advance(System.Numerics.Vector3.Zero, ClassicCameraKeys.None, TimeSpan.FromMilliseconds(20)).X);
    }

    [Fact]
    public void DiagonalKeysRemainSeparateNativeCommands()
    {
        var controls = new ClassicCameraControls(new ClassicCamera(Profile()));
        Assert.Equal(new Vector3(840 / 4096f, 0, 840 / 4096f),
            controls.Advance(System.Numerics.Vector3.Zero, ClassicCameraKeys.Right | ClassicCameraKeys.Up, TimeSpan.FromMilliseconds(20)));
    }

    [Fact]
    public void RotationUsesMillisecondsAndFollowsThePanInTheSameFrame()
    {
        var camera = new ClassicCamera(Profile());
        var controls = new ClassicCameraControls(camera);
        Assert.Equal(new Vector3(840 / 4096f, 0, 0), controls.Advance(System.Numerics.Vector3.Zero,
            ClassicCameraKeys.Right | ClassicCameraKeys.TurnRight, TimeSpan.FromMilliseconds(20)));
        Assert.Equal(20 * 360f / 2048, camera.Pose(Vector3.Zero, new(1600, 1600, 1200)).YawDegrees);
    }

    [Fact]
    public void LongFramesCapTimedPanAtOneSecondButRetainTheNativeTurnDelta()
    {
        var camera = new ClassicCamera(Profile());
        var controls = new ClassicCameraControls(camera);
        Assert.Equal(10880 / 4096f, controls.Advance(System.Numerics.Vector3.Zero,
            ClassicCameraKeys.Right | ClassicCameraKeys.TurnRight, TimeSpan.FromSeconds(2)).X);
        Assert.Equal(2000 * 360f / 2048, camera.Pose(Vector3.Zero, new(1600, 1600, 1200)).YawDegrees);
    }

    [Fact]
    public void FractionalMillisecondsAccumulateAcrossIdleFrames()
    {
        var camera = new ClassicCamera(Profile());
        var controls = new ClassicCameraControls(camera);
        for (int i = 0; i < 3; i++) controls.Advance(System.Numerics.Vector3.Zero, ClassicCameraKeys.TurnRight, TimeSpan.FromTicks(15_000));
        Assert.Equal(4 * 360f / 2048, camera.Pose(Vector3.Zero, new(1600, 1600, 1200)).YawDegrees);
    }

    [Theory]
    [InlineData(8, 10, 1045)]
    [InlineData(4, 20, 1480)]
    public void AuthoredTickRateAndScrollPreferenceRemainDistinct(int tickRate, int scrollSpeed, int expectedRaw)
    {
        var controls = new ClassicCameraControls(new ClassicCamera(Profile()), tickRate, scrollSpeed);
        Assert.Equal(expectedRaw / 4096f, controls.Advance(System.Numerics.Vector3.Zero, ClassicCameraKeys.Right, TimeSpan.FromMilliseconds(20)).X);
    }

    [Fact]
    public void DiagonalYawKeepsNativeSignedShiftRounding()
    {
        var camera = new ClassicCamera(Profile(), new Dk2Angle(256));
        var controls = new ClassicCameraControls(camera);
        Assert.Equal(new Vector3(590 / 4096f, 0, -590 / 4096f),
            controls.Advance(System.Numerics.Vector3.Zero, ClassicCameraKeys.Right, TimeSpan.FromMilliseconds(20)));
    }

    [Fact]
    public void ProfileLocksSuppressPanAndTurn()
    {
        var camera = new ClassicCamera(Profile() with { Flags = 0x67 });
        var controls = new ClassicCameraControls(camera);
        Assert.Equal(Vector3.Zero, controls.Advance(System.Numerics.Vector3.Zero, ClassicCameraKeys.Right | ClassicCameraKeys.TurnRight,
            TimeSpan.FromMilliseconds(20)));
        Assert.Equal(0, camera.Pose(Vector3.Zero, new(1600, 1600, 1200)).YawDegrees);
    }

    [Fact]
    public void EdgeAndKeyScrollingSubmitSeparateStepsAndShareAcceleration()
    {
        var controls = new ClassicCameraControls(new ClassicCamera(Profile()));
        Assert.Equal(1400 / 4096f, controls.Advance(System.Numerics.Vector3.Zero, ClassicCameraKeys.Right | ClassicCameraKeys.Fast,
            TimeSpan.FromMilliseconds(20), edgeScroll: ClassicCameraKeys.Left).X);
        Assert.Equal(Vector3.Zero, controls.Advance(System.Numerics.Vector3.Zero, ClassicCameraKeys.None, TimeSpan.FromMilliseconds(20)));
    }

    [Fact]
    public void EdgeScrollingDoesNotInheritTheKeyboardSpeedModifier()
    {
        var controls = new ClassicCameraControls(new ClassicCamera(Profile()));
        Assert.Equal(840 / 4096f, controls.Advance(System.Numerics.Vector3.Zero, ClassicCameraKeys.Fast,
            TimeSpan.FromMilliseconds(20), edgeScroll: ClassicCameraKeys.Right).X);
    }
}
