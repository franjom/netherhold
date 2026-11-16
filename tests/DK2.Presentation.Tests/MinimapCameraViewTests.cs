// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using DK2.Data.Model;

namespace DK2.Presentation.Tests;

/// <summary>The minimap's reading of the drawn camera (plan step 3b): the floor-plane centre and corners and the heading,
/// moved out of Unity's <c>DungeonMinimap</c>, and the footprint's triangle list.</summary>
public sealed class MinimapCameraViewTests
{
    private static readonly CameraViewport FullHd = new(1920, 1920, 1080);

    private static ClassicCamera Camera(ushort yaw = 256, ushort pitch = 220) => new(new CameraThing(new byte[84])
    {
        CameraId = 3, Z = 4096, Zoom = 40960, MinZoom = 16384, MaxZoom = 53248, Lens = 8192, AnglePitch = pitch, Flags = 0x26,
    }, new Dk2Angle(yaw)) { ReferenceScreenWidth = ClassicCameraSetup.ReferenceScreenWidth };

    private static readonly Vector3 Focus = new(40.5f, TileHeights.Floor, -30.5f);

    private static MinimapCameraView View(ClassicCamera camera, Vector3 focus) =>
        MinimapCameraView.Of(camera.Pose(focus, FullHd), FullHd);

    /// <summary>Where a world point lands in the viewport (0 to 1 from the bottom left), by the off-centre perspective
    /// Unity's runner gives the camera (<c>projection[1, 2] = -shift</c>), worked out independently of the ray.</summary>
    private static Vector2 Project(in MinimapCameraView view, Vector3 point)
    {
        Vector3 forward = Vector3.Normalize(view.Forward);
        Vector3 right = Vector3.Normalize(Vector3.Cross(view.Up, forward));
        Vector3 up = Vector3.Cross(forward, right);
        Vector3 d = point - view.Position;
        float depth = Vector3.Dot(d, forward), cot = 1 / MathF.Tan(view.VerticalFieldOfView * MathF.PI / 360);
        float ndcX = Vector3.Dot(d, right) / depth * cot / view.Aspect, ndcY = Vector3.Dot(d, up) / depth * cot + view.ProjectionCentreShift;
        return new Vector2((ndcX + 1) / 2, (ndcY + 1) / 2);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(256)]
    [InlineData(512)]
    [InlineData(1000)]
    [InlineData(2047)]
    public void EachFloorCornerProjectsBackToItsViewportCorner(ushort yaw)
    {
        MinimapCameraView view = View(Camera(yaw), Focus);
        Assert.NotEqual(0, view.ProjectionCentreShift);
        Span<Vector2> corners = stackalloc Vector2[4];
        Assert.True(view.TryFloorCorners(corners));
        Vector2[] expected = [new(0, 1), new(1, 1), new(1, 0), new(0, 0)];
        for (int i = 0; i < 4; i++)
        {
            Vector2 at = Project(view, new Vector3(corners[i].X, TileHeights.Floor, -corners[i].Y));
            Assert.Equal(expected[i].X, at.X, 3);
            Assert.Equal(expected[i].Y, at.Y, 3);
        }
    }

    [Fact]
    public void TheCentreIsTheFocusOnlyWhenTheViewAxisIsTheViewportsMiddle()
    {
        MinimapCameraView view = View(Camera(), Focus);
        Assert.True(view.TryFloorCentre(out Vector3 shifted));
        Assert.Equal(TileHeights.Floor, shifted.Y, 5);
        Assert.True(Vector3.Distance(shifted, Focus) > .5f);
        Vector2 at = Project(view, shifted);
        Assert.Equal(.5f, at.X, 3); Assert.Equal(.5f, at.Y, 3);
        Assert.True((view with { ProjectionCentreShift = 0 }).TryFloorCentre(out Vector3 centred));
        Assert.True(Vector3.Distance(centred, Focus) < 1e-3f);
    }

    [Fact]
    public void ARayThatMissesTheFloorGivesNoCornersAndNoCentre()
    {
        var level = new MinimapCameraView(new Vector3(0, 5, 0), Vector3.UnitZ, Vector3.UnitY, 60, 16 / 9f, 0);
        Assert.False(level.TryFloorCorners(new Vector2[4]));
        Assert.False(level.TryFloorCentre(out _));
        var upward = level with { Forward = Vector3.Normalize(new Vector3(0, 1, 1)), Up = Vector3.Normalize(new Vector3(0, 1, -1)) };
        Assert.False(upward.TryFloorCentre(out _));
        Assert.Throws<ArgumentException>(() => level.TryFloorCorners(new Vector2[3]));
    }

    [Fact]
    public void TheHeadingIsTheClassicCamerasYawForEveryYaw()
    {
        for (ushort yaw = 0; yaw < 2048; yaw++)
            Assert.Equal(yaw, View(Camera(yaw), Focus).Heading.Raw);
    }

    [Theory]
    [InlineData(0f, 0)]
    [InlineData(45f, 256)]
    [InlineData(359.9f, 2047)]
    [InlineData(359.99f, 2048)]
    [InlineData(.08f, 0)]
    [InlineData(.1f, 1)]
    [InlineData(.087890625f, 0)]
    [InlineData(.263671875f, 2)]
    [InlineData(.439453125f, 2)]
    public void TheHeadingRoundsToTheNearestUnitAndIsNotMasked(float degrees, int expected) =>
        Assert.Equal(expected, MinimapCameraView.HeadingOf(degrees).Raw);

    [Fact]
    public void TheYawIsTheTurnFromNorthClockwiseSeenFromAbove()
    {
        var north = new MinimapCameraView(Vector3.Zero, Vector3.UnitZ, Vector3.UnitY, 60, 1, 0);
        Assert.Equal(0f, north.YawDegrees);
        Assert.Equal(90f, (north with { Forward = Vector3.UnitX }).YawDegrees, 4);
        Assert.Equal(270f, (north with { Forward = -Vector3.UnitX }).YawDegrees, 4);
        Assert.Equal(180f, (north with { Forward = -Vector3.UnitZ }).YawDegrees, 4);
    }

    [Fact]
    public void TheFootprintTrianglesFollowTheNativeIndexListWithOutwardVerticesAtTheImagesTop()
    {
        var vertices = new Vector2[8];
        for (int i = 0; i < 8; i++) vertices[i] = new Vector2(10 + i, 600 + i);
        var triangles = new HudVertex[MinimapCameraFootprint.TriangleVertices];
        MinimapCameraFootprint.Triangles(vertices, triangles);
        int[] indices = [0, 2, 1, 1, 2, 3, 2, 4, 3, 3, 4, 5, 5, 4, 6, 7, 5, 6, 0, 7, 6, 0, 1, 7];
        for (int i = 0; i < indices.Length; i++)
            Assert.Equal(new HudVertex((10 + indices[i]) * 2.5f, (600 + indices[i]) * 2.5f, .5f, indices[i] % 2), triangles[i]);
        Assert.Throws<ArgumentException>(() => MinimapCameraFootprint.Triangles(vertices.AsSpan(0, 7), triangles));
        Assert.Throws<ArgumentException>(() => MinimapCameraFootprint.Triangles(vertices, triangles.AsSpan(0, 23)));
    }

    [Fact]
    public void ReadingTheViewAllocatesNothing()
    {
        MinimapCameraView view = View(Camera(), Focus);
        Span<Vector2> corners = stackalloc Vector2[4];
        view.TryFloorCorners(corners);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) { view.TryFloorCorners(corners); view.TryFloorCentre(out _); _ = view.Heading; }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(1024, 1280)]
    public void TheViewOfAPoseIsThePoseOnItsViewport(int width, int height)
    {
        var viewport = new CameraViewport(width, width, height);
        ClassicCameraPose pose = Camera(700).Pose(Focus, viewport);
        Assert.Equal(new MinimapCameraView(pose.Position, pose.Forward, pose.Up, pose.VerticalFieldOfView, width / (float)height,
            pose.ProjectionCentreShift), MinimapCameraView.Of(pose, viewport));
    }

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(1280, 1024)]
    public void TheFloorCornersOfAPoseMatchItsOwnOffCentreProjection(int width, int height)
    {
        var viewport = new CameraViewport(width, width, height);
        ClassicCameraPose pose = Camera(700).Pose(Focus, viewport);
        Assert.True(pose.ProjectionCentreShift > 0);
        Span<Vector2> corners = stackalloc Vector2[4];
        Assert.True(MinimapCameraView.Of(pose, viewport).TryFloorCorners(corners));
        // The pose's raw fields: the axis sits shift half heights above the middle, the field of view is vertical.
        Vector3 forward = Vector3.Normalize(pose.Forward);
        Vector3 right = Vector3.Normalize(Vector3.Cross(pose.Up, forward));
        Vector3 up = Vector3.Cross(forward, right);
        float tangent = MathF.Tan(pose.VerticalFieldOfView * MathF.PI / 360);
        Vector2[] expected = [new(0, 1), new(1, 1), new(1, 0), new(0, 0)];
        for (int i = 0; i < 4; i++)
        {
            Vector3 d = new Vector3(corners[i].X, TileHeights.Floor, -corners[i].Y) - pose.Position;
            float depth = Vector3.Dot(d, forward);
            float x = Vector3.Dot(d, right) / depth / tangent * height / width, y = Vector3.Dot(d, up) / depth / tangent;
            Assert.Equal(expected[i].X * 2 - 1, x, 3);
            Assert.Equal(expected[i].Y * 2 - 1, y + pose.ProjectionCentreShift, 3);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(256)]
    [InlineData(700)]
    [InlineData(1024)]
    [InlineData(1500)]
    [InlineData(2047)]
    public void LookingStraightDownTheHeadingIsTheYawAsJustOffVertical(ushort yaw)
    {
        MinimapCameraView down = View(Camera(yaw, pitch: 0), Focus);
        Assert.True(MathF.Abs(down.Forward.X) < 1e-4f && MathF.Abs(down.Forward.Z) < 1e-4f);
        Assert.Equal(yaw, View(Camera(yaw, pitch: 1), Focus).Heading.Raw);
        Assert.Equal(yaw, down.Heading.Raw);
    }
}
