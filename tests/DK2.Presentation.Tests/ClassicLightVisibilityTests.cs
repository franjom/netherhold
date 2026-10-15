// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;

namespace DK2.Presentation.Tests;

public sealed class ClassicLightVisibilityTests
{
    [Fact]
    public void ScanIncludesBothEndsButExcludesMaximumRow()
    {
        TilePos[] polygon = [new(-2, -1), new(2, -1), new(2, 2), new(-2, 2)];
        TilePos[] actual = ClassicLightVisibility.EnumerateTiles(polygon);
        Assert.Equal(15, actual.Length);
        Assert.Equal(new TilePos(-2, -1), actual[0]);
        Assert.Equal(new TilePos(2, 1), actual[^1]);
        Assert.DoesNotContain(actual, t => t.Y == 2);
    }

    [Fact]
    public void ScanUsesTruncatedFixedPointSlopeAndArithmeticShift()
    {
        TilePos[] actual = ClassicLightVisibility.EnumerateTiles(
            [new(-2, 0), new(0, 3), new(2, 0)]);
        Assert.Equal(new TilePos[]
        {
            new(-2,0), new(-1,0), new(0,0), new(1,0), new(2,0),
            new(-2,1), new(-1,1), new(0,1), new(1,1),
            new(-1,2), new(0,2)
        }, actual);
    }

    [Fact]
    public void FlatPolygonAndEmptyPolygonProduceNoTargets()
    {
        Assert.Empty(ClassicLightVisibility.EnumerateTiles([]));
        Assert.Empty(ClassicLightVisibility.EnumerateTiles([new(0, 2), new(8, 2)]));
    }

    [Fact]
    public void WindingAndStartingCornerDoNotChangeScanOrder()
    {
        TilePos[] expected = ClassicLightVisibility.EnumerateTiles(
            [new(0, 0), new(4, 0), new(4, 3), new(0, 3)]);
        Assert.Equal(expected, ClassicLightVisibility.EnumerateTiles(
            [new(4, 3), new(4, 0), new(0, 0), new(0, 3)]));
    }

    [Fact]
    public void TopDownVolumeIncludesHeightSlabAndCameraSpaceMargin()
    {
        // Native basis is identity: world height is -Unity Y, native Y is -Unity Z.
        var pose = new ClassicCameraPose(new(20, 15, -30), -Vector3.UnitY,
            Vector3.UnitZ, 90, 40, 90, 0);
        TilePos[] polygon = ClassicLightVisibility.BuildPolygon(pose,
            new CameraViewport(640, 640, 640), 10);
        TilePos[] tiles = ClassicLightVisibility.EnumerateTiles(polygon);
        Assert.Contains(new TilePos(9, 19), tiles);
        Assert.Contains(new TilePos(31, 40), tiles);
        Assert.DoesNotContain(new TilePos(8, 30), tiles);
        Assert.DoesNotContain(new TilePos(20, 41), tiles);
    }

    [Fact]
    public void VolumeEntirelyBelowHeightSlabIsEmpty()
    {
        var pose = new ClassicCameraPose(new(20, -20, -30), -Vector3.UnitY,
            Vector3.UnitZ, 90, 40, 90, 0);
        Assert.Empty(ClassicLightVisibility.BuildPolygon(pose,
            new CameraViewport(640, 640, 640), 10));
    }

    [Fact]
    public void HeightPlaneClipsFarVolumeInsteadOfProjectingAllCornersToFloor()
    {
        var pose = new ClassicCameraPose(new(20, 5, -30), -Vector3.UnitY,
            Vector3.UnitZ, 90, 40, 90, 0);
        TilePos[] tiles = ClassicLightVisibility.EnumerateTiles(
            ClassicLightVisibility.BuildPolygon(pose, new CameraViewport(640, 640, 640), 10));
        Assert.Equal(13, tiles.Min(t => t.X));
        Assert.Equal(26, tiles.Max(t => t.X));
        Assert.Equal(23, tiles.Min(t => t.Y));
        Assert.Equal(35, tiles.Max(t => t.Y));
    }

    [Fact]
    public void FlagEightContractsFarCornersWithoutChangingCentreDistance()
    {
        var pose = new ClassicCameraPose(new(20, 15, -30), -Vector3.UnitY,
            Vector3.UnitZ, 90, 40, 90, 0);
        var viewport = new CameraViewport(640, 640, 640);
        TilePos[] ordinary = ClassicLightVisibility.EnumerateTiles(
            ClassicLightVisibility.BuildPolygon(pose, viewport, 10));
        TilePos[] scaled = ClassicLightVisibility.EnumerateTiles(
            ClassicLightVisibility.BuildPolygon(pose, viewport, 10, scaleViewVolume: true));
        Assert.True(scaled.Length < ordinary.Length);
        Assert.Contains(new TilePos(20, 30), scaled);
    }

    [Fact]
    public void FlagEightPreservesProtrudingFarCentreAtShallowPitch()
    {
        float pitch = 10 * MathF.PI / 180;
        var pose = new ClassicCameraPose(new(20, 5, -30),
            new(0, -MathF.Sin(pitch), MathF.Cos(pitch)),
            new(0, MathF.Cos(pitch), MathF.Sin(pitch)), 90, 40, 10, 0);
        TilePos[] polygon = ClassicLightVisibility.BuildPolygon(pose,
            new CameraViewport(640, 640, 640), 10, scaleViewVolume: true);
        // Centre reaches native Y=20.15; contracting it moves the minimum row to22.
        Assert.Equal(20, polygon.Min(t => t.Y));
        Assert.Contains(new TilePos(20, 20), ClassicLightVisibility.EnumerateTiles(polygon));
    }

    [Fact]
    public void NarrowViewportChangesHorizontalExtentOnly()
    {
        var pose = new ClassicCameraPose(new(20, 15, -30), -Vector3.UnitY,
            Vector3.UnitZ, 90, 40, 90, 0);
        TilePos[] tiles = ClassicLightVisibility.EnumerateTiles(
            ClassicLightVisibility.BuildPolygon(pose, new CameraViewport(640, 320, 640), 10));
        Assert.Equal(14, tiles.Min(t => t.X));
        Assert.Equal(26, tiles.Max(t => t.X));
        Assert.Equal(19, tiles.Min(t => t.Y));
        Assert.Equal(40, tiles.Max(t => t.Y));
    }

    [Fact]
    public void QuarterTurnExchangesNarrowViewportAxes()
    {
        var pose = new ClassicCameraPose(new(20, 15, -30), -Vector3.UnitY,
            Vector3.UnitX, 90, 40, 90, 90);
        TilePos[] tiles = ClassicLightVisibility.EnumerateTiles(
            ClassicLightVisibility.BuildPolygon(pose, new CameraViewport(640, 320, 640), 10));
        Assert.Equal(9, tiles.Min(t => t.X));
        Assert.Equal(31, tiles.Max(t => t.X));
        Assert.Equal(24, tiles.Min(t => t.Y));
        Assert.Equal(35, tiles.Max(t => t.Y));
    }

    [Fact]
    public void PresentationFarClipDoesNotChangeNativeVolume()
    {
        var pose = new ClassicCameraPose(new(20, 15, -30), -Vector3.UnitY,
            Vector3.UnitZ, 90, 40, 90, 0);
        var viewport = new CameraViewport(640, 640, 640);
        Assert.Equal(ClassicLightVisibility.BuildPolygon(pose, viewport, 10),
            ClassicLightVisibility.BuildPolygon(pose with { FarClipDistance = 400 }, viewport, 10));
    }
}
