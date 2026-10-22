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
    public void AnObjectAndAnEffectWithOneIdAreSeparateSources_EachKindUpdatedOnItsOwn()
    {
        var tile = new TilePos(5, 5);
        var cache = new ClassicWorldLightCache(16, 16);
        var effect = new EffectLightView(1, Arches, new PlayerId(3), 5 * Fp32.One + 2048, 5 * Fp32.One + 2048, 0, 1, 1);
        cache.UpdateObjects(new[] { Object(1) }, 1);
        cache.UpdateEffects(new[] { effect });
        cache.PrepareFrame(new[] { tile });
        Assert.Equal(2, cache.GetCell(tile).DynamicSources.Length);
        // Effect intensity is 128/128, the object's 180/128.
        Assert.Contains(cache.GetCell(tile).DynamicSources.ToArray(), source => source.Intensity == 1f);
        // An update of objects alone keeps the effect's light, and the reverse.
        cache.UpdateObjects(Array.Empty<ObjectView>(), 1);
        cache.PrepareFrame(new[] { tile });
        Assert.Equal(1f, Assert.Single(cache.GetCell(tile).DynamicSources.ToArray()).Intensity);
        cache.UpdateObjects(new[] { Object(1) }, 1);
        cache.UpdateEffects(Array.Empty<EffectLightView>());
        cache.PrepareFrame(new[] { tile });
        Assert.Equal(180f / 128, Assert.Single(cache.GetCell(tile).DynamicSources.ToArray()).Intensity);
    }

    /// <summary>0x00447D50 makes the Hand's light white (511), intensity 255, radius 0x1C00;
    /// 0x0043B220 holds it at height 0x1C00 over the pick and blacks it out off explored ground
    /// and on the map's outer row and column (0x0043BCB8).</summary>
    [Fact]
    public void TheHandsLightIsAWhiteLampOverThePickOnExploredGroundOnly_ClassificationAssumedAnObjects()
    {
        ClassicPointLight hand = ClassicPointLight.FromHand(5.25f, 5.5f, raised: false, explored: true, 16, 16)!.Value;
        Assert.Equal(new System.Numerics.Vector3(5.25f, 1.75f, -5.5f), hand.Position);
        Assert.Equal(511 / 255f, hand.Colour.X);
        Assert.Equal((255 / 128f, 1.75f), (hand.Intensity, hand.Radius));
        Assert.Equal(ClassicLightFlags.NormalObject, hand.Flags);
        Assert.Equal(1.875f, ClassicPointLight.FromHand(5, 6, raised: true, explored: true, 16, 16)!.Value.Position.Y);
        Assert.Null(ClassicPointLight.FromHand(5, 6, raised: false, explored: false, 16, 16));
        Assert.Null(ClassicPointLight.FromHand(0.5f, 6, raised: false, explored: true, 16, 16));
        Assert.Null(ClassicPointLight.FromHand(5, 15.5f, raised: false, explored: true, 16, 16));
        Assert.NotNull(ClassicPointLight.FromHand(14.5f, 1.5f, raised: false, explored: true, 16, 16));

        var cache = new ClassicWorldLightCache(16, 16);
        var tile = new TilePos(5, 5);
        cache.UpdateObjects(new[] { Object(1) }, 1);
        cache.UpdateHand(hand);
        cache.PrepareFrame(new[] { tile });
        Assert.Equal(2, cache.GetCell(tile).DynamicSources.Length);
        // An object update keeps the Hand's light; a null Hand takes it away.
        cache.UpdateObjects(Array.Empty<ObjectView>(), 1);
        cache.PrepareFrame(new[] { tile });
        Assert.Equal(255 / 128f, Assert.Single(cache.GetCell(tile).DynamicSources.ToArray()).Intensity);
        cache.UpdateHand(null);
        cache.PrepareFrame(new[] { tile });
        Assert.Empty(cache.GetCell(tile).DynamicSources.ToArray());
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
