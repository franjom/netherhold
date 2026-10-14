// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using System.Buffers.Binary;
using DK2.Core;
using DK2.Data;
using DK2.Data.Kwd;
using DK2.Data.Model;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

public class ClassicCameraTests
{
    private static CameraThing Profile() => new(new byte[84])
    {
        CameraId = 3, Z = 4096, Zoom = 40960, MinZoom = 16384, MaxZoom = 53248,
        Lens = 8192, AnglePitch = 220, Flags = 0x26,
    };

    [Fact]
    public void LevelStartUsesNativeDiagonalYawWithoutChangingAuthoredPitch()
    {
        CameraThing profile = Profile();
        var camera = new ClassicCamera(profile, initialYaw: ClassicCamera.LevelStartYaw);
        ClassicCameraPose pose = camera.Pose(Vector3.Zero, new CameraViewport(640, 640, 480));
        Assert.Equal(45f, pose.YawDegrees);
        Assert.Equal(51.328125f, pose.PitchDegrees);
        Assert.True(pose.Position.X < 0);
        Assert.True(pose.Position.Z < 0);
        Assert.InRange(Math.Abs(pose.Position.X - pose.Position.Z), 0f, 0.00001f);
        Assert.Equal((ushort)0, profile.AngleYaw);
        camera.Rotate(new Dk2Angle(256));
        Assert.Equal(90f, camera.Pose(Vector3.Zero, new CameraViewport(640, 640, 480)).YawDegrees);
    }

    [InstallFact]
    public void LevelOneNorthernGoldProjectsLeftOfTheHeartAtStartup()
    {
        LevelMap map = GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "Level1")!;
        GameDatabase database = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        Assert.Equal("Gold", database.TerrainById(map[14, 24].TerrainId).Name);
        var focus = new Vector3(14.5f, 1f, -28.5f);
        var gold = new Vector3(14.5f, 1f, -24.5f);
        var camera = new ClassicCamera(Profile(), initialYaw: ClassicCamera.LevelStartYaw);
        ClassicCameraPose pose = camera.Pose(focus, new CameraViewport(640, 640, 480));
        Vector3 right = Vector3.Normalize(Vector3.Cross(pose.Up, pose.Forward));
        Assert.True(Vector3.Dot(gold - focus, right) < 0);
        Assert.True(camera.PanDirection(Vector2.UnitX).Z < 0);
    }

    [InstallFact]
    public void LoadsTheShippedOrdinaryProfileDespiteRepeatedGlobalIds()
    {
        CameraThing profile = GameDatabaseLoader.LoadCameraPresets(Dk2InstallGate.Path, "Level8")
            .Single(c => c.CameraId == 3);
        Assert.Equal(40960, profile.Zoom);
        Assert.Equal(16384, profile.MinZoom);
        Assert.Equal(53248, profile.MaxZoom);
        Assert.Equal(8192, profile.Lens);
        Assert.Equal((ushort)220, profile.AnglePitch);
    }

    [Theory]
    [InlineData(640, 480, 10, 41.11209)]
    [InlineData(1280, 720, 5, 58.71551)]
    [InlineData(1920, 1080, 3.333333, 80.311998)]
    public void ProjectionUsesNativePixelLensAndScreenWidth(int width, int height, float distance, float fov)
    {
        var camera = new ClassicCamera(Profile());
        ClassicCameraPose pose = camera.Pose(Vector3.Zero,
            new CameraViewport(ScreenWidth: width, Width: width, Height: height));
        Assert.InRange(Vector3.Distance(Vector3.Zero, pose.Position), distance - 0.0001f, distance + 0.0001f);
        Assert.InRange(pose.VerticalFieldOfView, fov - 0.0001f, fov + 0.0001f);
        Assert.Equal(51.328125f, pose.PitchDegrees);
        Assert.True(pose.Position.Y > 0);
        Assert.True(pose.Position.Z < 0);
    }

    [Fact]
    public void PanelsChangeTheFrustumWithoutChangingTheFullScreenZoomScale()
    {
        var camera = new ClassicCamera(Profile());
        ClassicCameraPose full = camera.Pose(Vector3.Zero, new CameraViewport(640, 640, 480));
        ClassicCameraPose panel = camera.Pose(Vector3.Zero, new CameraViewport(640, 480, 360));
        Assert.Equal(full.Position, panel.Position);
        Assert.True(panel.VerticalFieldOfView < full.VerticalFieldOfView);
    }

    [Fact]
    public void WheelStepsAndSaturationUseTheNativeRawBounds()
    {
        var camera = new ClassicCamera(Profile());
        camera.ZoomIn();
        Assert.Equal(new Fp32(38720), camera.Zoom);
        camera.SetZoom(new Fp32(40960));
        camera.ZoomOut();
        Assert.Equal(new Fp32(43200), camera.Zoom);
        for (int i = 0; i < 100; i++) camera.ZoomIn();
        Assert.Equal(new Fp32(16384), camera.Zoom);
        for (int i = 0; i < 100; i++) camera.ZoomOut();
        Assert.Equal(new Fp32(53247), camera.Zoom);
    }

    [Fact]
    public void LockedRelativeZoomDoesNotChangeTheProfileDistance()
    {
        var camera = new ClassicCamera(Profile() with { Flags = 0x2E });
        camera.ZoomIn();
        camera.ZoomOut();
        Assert.Equal(new Fp32(40960), camera.Zoom);
    }

    [Fact]
    public void QuarterTurnKeepsFocusAndRotatesOffsetAndPanningTogether()
    {
        var camera = new ClassicCamera(Profile());
        var focus = new Vector3(14.5f, 1, -28.5f);
        ClassicCameraPose first = camera.Pose(focus, new CameraViewport(640, 640, 480));
        camera.Rotate(new Dk2Angle(512));
        ClassicCameraPose turned = camera.Pose(focus, new CameraViewport(640, 640, 480));
        Assert.Equal(90f, turned.YawDegrees);
        Assert.InRange(turned.Position.Z, focus.Z - 0.00001f, focus.Z + 0.00001f);
        Assert.True(turned.Position.X < focus.X);
        Assert.InRange(Vector3.Distance(focus, first.Position) - Vector3.Distance(focus, turned.Position), -0.00001f, 0.00001f);
        Assert.InRange(camera.PanDirection(Vector2.UnitY).X, 0.99999f, 1.00001f);
        camera.Rotate(new Dk2Angle(1536));
        Assert.Equal(0f, camera.Pose(focus, new CameraViewport(640, 640, 480)).YawDegrees);
    }

    [Fact]
    public void RejectsInvalidProjectionInputs()
    {
        Assert.Throws<ArgumentException>(() => new ClassicCamera(Profile() with { Lens = 0 }));
        Assert.Throws<ArgumentException>(() => new ClassicCamera(Profile() with { MaxZoom = 16384 }));
        var camera = new ClassicCamera(Profile());
        Assert.Throws<ArgumentOutOfRangeException>(() => camera.Pose(Vector3.Zero, new CameraViewport(0, 640, 480)));
    }

    [Fact]
    public void LockedYawAndVisibilityDistanceFollowTheAuthoredInputs()
    {
        var camera = new ClassicCamera(Profile() with { Flags = 0x27, ViewDistance = 30 * Fp32.One });
        camera.Rotate(new Dk2Angle(512));
        ClassicCameraPose pose = camera.Pose(Vector3.Zero, new CameraViewport(640, 640, 480));
        Assert.Equal(0f, pose.YawDegrees);
        Assert.Equal(30f, pose.FarClipDistance);
        Assert.Equal(20f, new ClassicCamera(Profile()).Pose(Vector3.Zero,
            new CameraViewport(640, 640, 480)).FarClipDistance);
    }

    [Fact]
    public void RollRotatesTheScreenUpWithoutMovingAnOverheadCamera()
    {
        var camera = new ClassicCamera(Profile() with { AnglePitch = 0, AngleRoll = 512 });
        ClassicCameraPose pose = camera.Pose(Vector3.Zero, new CameraViewport(640, 640, 480));
        Assert.InRange(Vector3.Distance(new Vector3(0, 10, 0), pose.Position), 0, 0.00001f);
        Assert.InRange(Vector3.Distance(Vector3.UnitX, pose.Up), 0, 0.00001f);
        Assert.InRange(Vector3.Distance(-Vector3.UnitY, pose.Forward), 0, 0.00001f);
    }

    [Fact]
    public void LaterGlobalRowsAndCampaignRowsOverrideWithoutReadingAnUnrelatedLevel()
    {
        string install = Path.Combine(Path.GetTempPath(), "dk2-camera-" + Guid.NewGuid().ToString("N"));
        string editor = Path.Combine(install, "Data", "editor");
        Directory.CreateDirectory(Path.Combine(editor, "maps"));
        try
        {
            KwdWriter.WriteFile(Path.Combine(editor, "GlobalVariables.kwd"),
                [CameraChunk((3, 1), (3, 2), (19, 99))]);
            KwdWriter.WriteFile(Path.Combine(editor, "maps", "ChosenThings.kld"), [CameraChunk((0x103, 3))]);
            File.WriteAllBytes(Path.Combine(editor, "maps", "UnrelatedThings.kld"), [0]);
            Assert.Equal(2, GameDatabaseLoader.LoadCameraPresets(install).Single().Zoom);
            Assert.Equal(3, GameDatabaseLoader.LoadCameraPresets(install, "Chosen").Single().Zoom);
        }
        finally { Directory.Delete(install, recursive: true); }
    }

    private static KwdChunk CameraChunk(params (ushort Id, int Zoom)[] rows)
    {
        var content = new byte[92 * rows.Length];
        for (int i = 0; i < rows.Length; i++)
        {
            Span<byte> record = content.AsSpan(i * 92, 92);
            BinaryPrimitives.WriteUInt32LittleEndian(record, 205);
            BinaryPrimitives.WriteUInt32LittleEndian(record.Slice(4), 84);
            BinaryPrimitives.WriteInt32LittleEndian(record.Slice(8 + 48), rows[i].Zoom);
            BinaryPrimitives.WriteUInt16LittleEndian(record.Slice(8 + 82), rows[i].Id);
        }
        var header = new byte[28];
        BinaryPrimitives.WriteInt32LittleEndian(header, rows.Length);
        return new KwdChunk
        {
            Type = KwdRecordType.Things, ItemCount = rows.Length, HeaderBlock = header, Content = content,
            ContainerHeader = new KwdContainerHeader(0, 192, (uint)content.Length, content.Length),
        };
    }
}
