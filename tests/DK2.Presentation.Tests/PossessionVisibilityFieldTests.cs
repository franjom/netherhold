// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
namespace DK2.Presentation.Tests;
public sealed class PossessionVisibilityFieldTests
{
    private static readonly CameraViewport Screen = new CameraViewport(1920, 1920, 1080);
    private static readonly ClassicCameraPose North = new ClassicCameraPose(new Vector3(20.5f, 1.5f, -20.5f), Vector3.UnitZ,
        Vector3.UnitY, 90f, 12f, 90f, 0f);
    [Fact]
    public void TheScaledViewPolygonSuppliesTheWalkAndTheWallHidesTheTilesBehindIt()
    {
        bool[] solid = new bool[40 * 40];
        for (int x = 0; x < 40; x++)
        {
            solid[(17 * 40) + x] = true;
        }

        var field = new PossessionVisibilityField(40, 40);
        field.Build(North, Screen, 12f, solid);
        Assert.True(field.Contains(new TilePos(20, 17)));
        Assert.True(field.Contains(new TilePos(20, 18)));
        Assert.False(field.Contains(new TilePos(20, 16)));
        Assert.False(field.Contains(new TilePos(20, 30)));
        Assert.False(field.Contains(new TilePos(30, 9)));
    }
    [Fact]
    public void OffMapEntriesAreSkippedAndZeroDistanceClearsTheLastPicture()
    {
        var field = new PossessionVisibilityField(40, 40);
        bool[] solid = new bool[40 * 40];
        field.Build(North with { Position = new Vector3(0.5f, 1.5f, -0.5f) }, Screen, 12f, solid);
        Assert.True(field.Contains(new TilePos(0, 0)));
        Assert.False(field.Contains(new TilePos(-1, 0)));
        Assert.False(field.Contains(new TilePos(0, -1)));
        Assert.False(field.Contains(new TilePos(40, 0)));
        field.Build(North, Screen, 0f, solid);
        Assert.DoesNotContain((byte)255, field.Mask.ToArray());
    }
    [Fact]
    public void MovingTheCameraRebuildsItsVisibleTilesWithoutAllocatingAfterWarmup()
    {
        var field = new PossessionVisibilityField(40, 40);
        bool[] solid = new bool[40 * 40];
        ClassicCameraPose south = North with { Forward = -Vector3.UnitZ };
        field.Build(North, Screen, 12f, solid);
        Assert.True(field.Contains(new TilePos(20, 12)));
        field.Build(south, Screen, 12f, solid);
        Assert.False(field.Contains(new TilePos(20, 12)));
        Assert.True(field.Contains(new TilePos(20, 28)));
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            field.Build((i & 1) == 0 ? North : south, Screen, 12f, solid);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
