// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Assets;
using DK2.Core;
using DK2.Data.Model;

namespace DK2.Presentation.Tests;

public class ClassicObjectLightTests
{
    private static readonly KwdLight Arches = new(0, 0, 15360, 10240, 0x3800, 244, 244, 204, 0);
    private static ObjectView Object(int id, int x = 5, KwdLight? descriptor = null) => new(
        id, 86, new PlayerId(3), new TilePos(x, 5), default(AssetRef), 0,
        x * Fp32.One + 2048, 5 * Fp32.One + 2048, Fp32.One, 1024, Fp32.One,
        light: descriptor ?? Arches);

    [Fact]
    public void ArchesAutoEnableHighColourBitsAndUseNativeRadiusIntensityAndMode()
    {
        ClassicPointLight light = ClassicPointLight.FromObject(Object(1))!.Value;
        Assert.Equal(new Vector3(5.5f, 4.75f, -5.5f), light.Position);
        Assert.Equal(new Vector3(500f / 255, 500f / 255, 460f / 255), light.Colour);
        Assert.Equal(6826f / Fp32.One, light.Radius);
        Assert.Equal(180f / 128, light.Intensity);
        Assert.Equal((ClassicLightFlags)0x191, light.Flags);
        Assert.Equal((ClassicLightFlags)0x95, ClassicPointLight.FromObject(Object(1), cheapLighting: true)!.Value.Flags);
    }

    [Fact]
    public void EmptyDescriptorsStayDarkAndRadiusUsesUnsignedShiftBeforeClamp()
    {
        var empty = new KwdLight(0, 0, 0, 1000, 0, 0, 0, 0, 255);
        Assert.Null(ClassicPointLight.FromObject(Object(1, descriptor: empty)));
        Assert.Null(ClassicPointLight.FromObject(Object(1, descriptor: Arches with { Radius = int.MinValue })));
        Assert.Equal(10112f / Fp32.One,
            ClassicPointLight.FromObject(Object(1, descriptor: Arches with { Radius = -1 }))!.Value.Radius);
    }

    [Fact]
    public void OwnerTintUsesIntegerDivisionAndMinimum32()
    {
        ClassicPointLight light = ClassicPointLight.FromObject(Object(1,
            descriptor: new KwdLight(0, 0, 0, 8192, 0x81, 255, 100, 5, 255)))!.Value;
        Assert.Equal(new Vector3(230f / 255, 32f / 255, 32f / 255), light.Colour);
    }

    [Fact]
    public void LinkedPositionInterpolatesWithoutRotatingTheDescriptorOffset()
    {
        var thing = new ObjectView(1, 86, new PlayerId(3), new TilePos(6, 5), default, 0,
            6 * Fp32.One + 2048, 5 * Fp32.One + 2048, Fp32.One, 512, Fp32.One,
            previousPosX: new Fp32(4 * Fp32.One + 2048), light: Arches with { X = 1024 });
        Assert.Equal(new Vector3(5.75f, 4.75f, -5.5f), ClassicPointLight.FromObject(thing, fraction: .5f)!.Value.Position);
    }

    [Fact]
    public void DynamicSourcesMoveDisappearAndDoNotConsumeStaticSlots()
    {
        var tile = new TilePos(5, 5);
        var cache = new ClassicWorldLightCache(16, 16);
        cache.UpdateObjects(new[] { Object(1), Object(2) }, 1);
        cache.PrepareFrame(new[] { tile });
        Assert.Equal(0, cache.VisibleTables[0].StaticCount);
        Assert.Equal(2, cache.VisibleTables[0].Count);
        Assert.Equal(2, cache.GetCell(tile).DynamicSources.Length);
        cache.UpdateObjects(new[] { Object(1, x: 8) }, 1);
        cache.PrepareFrame(new[] { tile, new TilePos(8, 5) });
        Assert.Empty(cache.GetCell(tile).DynamicSources.ToArray());
        Assert.Single(cache.GetCell(new TilePos(8, 5)).DynamicSources.ToArray());
        cache.UpdateObjects(Array.Empty<ObjectView>(), 1);
        cache.PrepareFrame(new[] { tile });
        Assert.Equal(0, cache.VisibleTables[0].Count);
    }

    [Fact]
    public void PreparedBorderSourceLightsVisibleTilesWithoutMakingItsOwnTileVisible()
    {
        var cache = new ClassicWorldLightCache(16, 16);
        cache.UpdateObjects(new[] { Object(1, x: 6) }, 1);
        cache.PrepareFrame(new[] { new TilePos(5, 5) });
        Assert.False(cache.IsVisible(new TilePos(6, 5)));
        Assert.Single(cache.GetCell(new TilePos(6, 5)).DynamicSources.ToArray());
        Assert.Equal(1, cache.VisibleTables[0].Count);
        cache.Clear();
        cache.PrepareFrame(new[] { new TilePos(5, 5) });
        Assert.Equal(0, cache.VisibleTables[0].Count);
    }

    [Fact]
    public void WarmDynamicPublicationAndGatherAllocateNothing()
    {
        var cache = new ClassicWorldLightCache(16, 16);
        ObjectView[] objects = { Object(1), Object(2) };
        TilePos[] visible = { new(5, 5), new(6, 5) };
        for (int i = 0; i < 100; i++) { cache.UpdateObjects(objects, .5f); cache.PrepareFrame(visible); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) { cache.UpdateObjects(objects, .5f); cache.PrepareFrame(visible); }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
