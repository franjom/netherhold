// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using DK2.Data.Model;
namespace DK2.Presentation.Tests;
public sealed class ClassicFullMapTests
{
    [Fact]
    public void RoomSymbolUsesNativeArtAndRequiresTheRoomCentreToBeExplored()
    {
        Assert.False(ClassicFullMapMarkers.TryRoom(RoomTypeId.Treasury, new TilePos(7, 9), false, out _));
        Assert.False(ClassicFullMapMarkers.TryRoom(RoomTypeId.HeroGate3By1, new TilePos(7, 9), true, out _));
        Assert.True(ClassicFullMapMarkers.TryRoom(RoomTypeId.HeroStoneBridge, new TilePos(7, 9), true, out var marker));
        Assert.Equal(MinimapMarkerKind.FullMapRoom, marker.Kind);
        Assert.Equal("gui\\rooms\\Bridge_Wooden", ClassicFullMapMarkers.RoomArt(marker.RoomType));
        Assert.Equal(new FpPosition3(new Fp32(7 * 4096), new Fp32(9 * 4096), default), marker.Position);
    }

    [Theory]
    [InlineData(640, 480, 466, 33, 128, 192)]
    [InlineData(1024, 768, 745, 52, 205, 308)]
    [InlineData(2560, 1440, 1864, 99, 512, 576)]
    public void PreviewUsesTruncatedNativeEndpointsAndContinuousUnclampedPointer(
        int width, int height, int x, int y, int previewWidth, int previewHeight)
    {
        var layout = ClassicFullMapLayout.Fit(width, height, 100, 80);
        var preview = layout.Preview(width, height, layout.Origin + layout.Size * .125f);
        Assert.Equal(new Vector2(x, y), preview.Origin);
        Assert.Equal(new Vector2(previewWidth, previewHeight), preview.Size);
        Assert.Equal(new Vector3(12.5f, 8, -10), preview.Pose.Position);
        Assert.Equal(-Vector3.UnitY, preview.Pose.Forward);
        Assert.Equal(Vector3.UnitZ, preview.Pose.Up);
        Assert.Equal(10, preview.Pose.FarClipDistance);
        var outside = layout.Preview(width, height, layout.Origin - layout.Size / 10);
        Assert.True(Vector3.Distance(new Vector3(-10, 8, 8), outside.Pose.Position) < .00001f);
    }

    [Fact]
    public void CreatureSquaresRequireExplorationEvenForTheViewingKeepersCreature()
    {
        var colours = Enumerable.Range(0, 37).Select(i => 0xff000000u | (uint)i).ToArray();
        var palette = new MinimapPalette(colours);
        var owner = new PlayerId(3);
        Assert.False(ClassicFullMapMarkers.TryCreature(default, owner, inHand: false, explored: false, palette, out _));
        Assert.False(ClassicFullMapMarkers.TryCreature(default, owner, inHand: true, explored: true, palette, out _));
        Assert.True(ClassicFullMapMarkers.TryCreature(default, owner, inHand: false, explored: true, palette, out var marker));
        Assert.Equal(palette.Colours[20], marker.ColourArgb);
        Assert.Equal(MinimapMarkerKind.FullMapCreature, marker.Kind);
    }

    [Fact]
    public void FitsNativeRectanglePreservingMapAspect()
    {
        var layout = ClassicFullMapLayout.Fit(1024,768,100,80);
        Assert.Equal(new Vector2(65,113),layout.Origin);
        Assert.Equal(new Vector2(615,492),layout.Size);
        Assert.Equal(new TilePos(50,40),layout.ResolveTile(layout.Origin+layout.Size/2));
    }
    [Fact]
    public void PointerCoordinatesTruncateWithoutMinimapBoundsClamp()
    {
        var layout = ClassicFullMapLayout.Fit(1024,768,100,80);
        Assert.Equal(new TilePos(-2,0),layout.ResolveTile(layout.Origin-new Vector2(layout.Size.X*.025f,0)));
        Assert.Equal(new TilePos(100,80),layout.ResolveTile(layout.Origin+layout.Size));
    }
}
