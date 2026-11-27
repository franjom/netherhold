// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using DK2.Data.Model;

namespace DK2.Presentation.Tests;

/// <summary>The classic light frame moved out of Unity's DungeonScene (migration plan step 2j).</summary>
public sealed class ClassicLightPublisherTests
{
    private const int Size = 64;
    private static readonly CameraViewport Viewport = new CameraViewport(640, 640, 640);
    private static readonly ClassicCameraPose TopDown = new ClassicCameraPose(new Vector3(20, 15, -30), -Vector3.UnitY, Vector3.UnitZ, 90, 40, 90, 0);

    private sealed class RecordingSink : ITileLightSink
    {
        public readonly List<TilePos> Sets = [], Removes = [];
        public int Flushes;
        public void Set(ClassicLightTable table)
        {
            Sets.Add(table.Tile);
        }

        public void Remove(TilePos tile)
        {
            Removes.Add(tile);
        }

        public void Flush()
        {
            Flushes++;
        }

        public void Reset() { Sets.Clear(); Removes.Clear(); Flushes = 0; }
    }

    private static (ClassicLightPublisher Publisher, ClassicWorldLightCache Cache, PortalCentreLights Portals) Create(
        ClassicLightFlags torchFlags = ClassicLightFlags.NormalTorch)
    {
        var cache = new ClassicWorldLightCache(Size, Size);
        var portals = new PortalCentreLights(new EffectTime(0));
        return (new ClassicLightPublisher(cache, Size, Size, portals, torchFlags), cache, portals);
    }

    private static HashSet<TilePos> OnMap(IEnumerable<TilePos> tiles)
    {
#pragma warning disable IDE0305 // Preserve the original collection construction or overload.
        return tiles.Where(t => (uint)t.X < Size && (uint)t.Y < Size).ToHashSet();
#pragma warning restore IDE0305
    }

    private static TilePos[] Tiles(ClassicCameraPose pose, float distance, CameraViewport? viewport = null)
    {
        return ClassicLightVisibility.EnumerateTiles(ClassicLightVisibility.BuildPolygon(pose, viewport ?? Viewport, distance));
    }

    private static ClassicCameraPose Shifted(float x)
    {
        return TopDown with { Position = TopDown.Position + new Vector3(x, 0, 0) };
    }

    // Looking down and north at 45 degrees from 5 above the floor, so its view distance moves its far edge.
    private static readonly ClassicCameraPose Tilted = new ClassicCameraPose(new Vector3(40, 5, -20), Vector3.Normalize(new Vector3(0, -1, 1)),
        Vector3.Normalize(new Vector3(0, 1, 1)), 60, 40, 45, 0);

    [Fact]
    public void TheFirstFrameSetsEveryVisibleOnMapTableThenFlushes()
    {
        (ClassicLightPublisher? publisher, ClassicWorldLightCache _, PortalCentreLights _) = Create();
        var sink = new RecordingSink();

        Assert.Equal(ClassicLightPublication.Changed, publisher.Prepare(TopDown, Viewport, 10));
        publisher.Publish(sink);

        HashSet<TilePos> expected = OnMap(Tiles(TopDown, 10));
        Assert.NotEmpty(expected);
#pragma warning disable IDE0305 // Preserve the original collection construction or overload.
        Assert.Equal(expected, sink.Sets.ToHashSet());
#pragma warning restore IDE0305
        Assert.Equal(expected.Count, sink.Sets.Count);
        Assert.Empty(sink.Removes);
        Assert.Equal(1, sink.Flushes);
    }

    [Fact]
    public void ASettledFrameIsUnchangedUntilATableIsWrittenOutsideIt()
    {
        (ClassicLightPublisher? publisher, ClassicWorldLightCache _, PortalCentreLights _) = Create();
        var sink = new RecordingSink();
        ClassicLightPublication result = ClassicLightPublication.Changed;
        for (int frame = 0; frame < 4 && result == ClassicLightPublication.Changed; frame++)
        {
            result = publisher.Prepare(TopDown, Viewport, 10);
            if (result == ClassicLightPublication.Changed)
            {
                publisher.Publish(sink);
            }
        }
        Assert.Equal(ClassicLightPublication.Unchanged, result);

        publisher.MarkTablesWritten();
        Assert.Equal(ClassicLightPublication.Changed, publisher.Prepare(TopDown, Viewport, 10));
        publisher.Publish(sink);
        Assert.Equal(ClassicLightPublication.Unchanged, publisher.Prepare(TopDown, Viewport, 10));
    }

    [Fact]
    public void AMovedCameraRemovesOnlyTheTilesNoLongerVisible()
    {
        (ClassicLightPublisher? publisher, ClassicWorldLightCache _, PortalCentreLights _) = Create();
        var sink = new RecordingSink();
        publisher.Prepare(TopDown, Viewport, 10);
        publisher.Publish(sink);
        var before = sink.Sets.ToHashSet();
        sink.Reset();

        Assert.Equal(ClassicLightPublication.Changed, publisher.Prepare(Shifted(6), Viewport, 10));
        publisher.Publish(sink);

        HashSet<TilePos> after = OnMap(Tiles(Shifted(6), 10));
        Assert.NotEmpty(before.Intersect(after));
#pragma warning disable IDE0305 // Preserve the original collection construction or overload.
        Assert.Equal(before.Except(after).ToHashSet(), sink.Removes.ToHashSet());
        Assert.NotEmpty(sink.Removes);
        Assert.Equal(after, sink.Sets.ToHashSet());
#pragma warning restore IDE0305
    }

    [Fact]
    public void AChangedViewDistanceFindsTheTilesAgain()
    {
        (ClassicLightPublisher? publisher, ClassicWorldLightCache _, PortalCentreLights _) = Create();
        var sink = new RecordingSink();
        Assert.NotEqual(OnMap(Tiles(Tilted, 10)), OnMap(Tiles(Tilted, 14)));

        publisher.Prepare(Tilted, Viewport, 10);
        publisher.Publish(sink);
        sink.Reset();
        publisher.Prepare(Tilted, Viewport, 14);
        publisher.Publish(sink);

#pragma warning disable IDE0305 // Preserve the original collection construction or overload.
        Assert.Equal(OnMap(Tiles(Tilted, 14)), sink.Sets.ToHashSet());
#pragma warning restore IDE0305
    }

    [Fact]
    public void ASecondCameraJoinsItsTilesFoundAtViewDistanceTen()
    {
        (ClassicLightPublisher? publisher, ClassicWorldLightCache _, PortalCentreLights _) = Create();
        var sink = new RecordingSink();
        var secondViewport = new CameraViewport(640, 320, 240);
        Assert.NotEqual(OnMap(Tiles(Tilted, 10, secondViewport)), OnMap(Tiles(Tilted, 11, secondViewport)));
        Assert.NotEqual(OnMap(Tiles(Tilted, 10, secondViewport)), OnMap(Tiles(Tilted, 9, secondViewport)));
        Assert.Equal(10f, ClassicLightPublisher.SecondCameraViewDistance);

        publisher.Prepare(TopDown, Viewport, 24, Tilted, secondViewport);
        publisher.Publish(sink);

        HashSet<TilePos> expected = OnMap(Tiles(TopDown, 24).Concat(Tiles(Tilted, 10, secondViewport)));
#pragma warning disable IDE0305 // Preserve the original collection construction or overload.
        Assert.Equal(expected, sink.Sets.ToHashSet());
#pragma warning restore IDE0305
        Assert.Equal(expected.Count, sink.Sets.Count);
    }

    [Fact]
    public void AnEmptyViewportPreparesNothing()
    {
        (ClassicLightPublisher? publisher, ClassicWorldLightCache? cache, PortalCentreLights _) = Create();
        Assert.Equal(ClassicLightPublication.Skipped, publisher.Prepare(TopDown, new CameraViewport(640, 0, 640), 10));
        Assert.Equal(default, cache.Frame);
    }

    [Fact]
    public void ClearForgetsThePublishedTiles()
    {
        (ClassicLightPublisher? publisher, ClassicWorldLightCache _, PortalCentreLights _) = Create();
        var sink = new RecordingSink();
        publisher.Prepare(TopDown, Viewport, 10);
        publisher.Publish(sink);
        sink.Reset();

        publisher.Clear();
        publisher.Prepare(Shifted(30), Viewport, 10);
        publisher.Publish(sink);

        Assert.Empty(sink.Removes);
    }

    [Fact]
    public void ClearForgetsTheCachesSources()
    {
        (ClassicLightPublisher? publisher, ClassicWorldLightCache? cache, PortalCentreLights _) = Create();
        var lit = new TilePos(20, 30);
        cache.SetTerrain(lit, ClassicPointLight.FromTerrain(lit, new Fp32(0), new TerrainLightColour(255, 0, 0)));
        publisher.Prepare(TopDown, Viewport, 10);
        Assert.True(cache.TryGetVisibleTable(lit, out ClassicLightTable before));
        Assert.NotEqual(0, before.Count);

        publisher.Clear();
        publisher.Prepare(TopDown, Viewport, 10);

        Assert.NotEmpty(cache.VisibleTables.ToArray());
        Assert.All(cache.VisibleTables.ToArray(), table => Assert.Equal(0, table.Count));
    }

    [Fact]
    public void EntranceLightsAreCreatedOncePerRequest()
    {
        (ClassicLightPublisher? publisher, ClassicWorldLightCache _, PortalCentreLights? portals) = Create();
        publisher.PublishPortalLights([new TilePos(3, 4)], new EffectTime(10));
        publisher.PublishPortalLights([new TilePos(3, 4), new TilePos(7, 8)], new EffectTime(20));
        publisher.PublishPortalLights([new TilePos(3, 4), new TilePos(7, 8)], new EffectTime(30));

        IReadOnlyList<PortalLightView> lights = portals.Advance(new EffectTime(30));
        Assert.Equal(2, lights.Count);
        Assert.Equal(new Vector3(3.5f, 2000f / Fp32.One, -4.5f), lights[0].Light.Position);
        Assert.Equal(new Vector3(7.5f, 2000f / Fp32.One, -8.5f), lights[1].Light.Position);
    }

    [Theory]
    [InlineData(ClassicLightFlags.CheapTorch, ClassicLightFlags.CheapObject)]
    [InlineData(ClassicLightFlags.NormalTorch, ClassicLightFlags.NormalObject)]
    public void EntranceLightsFollowTheScenesCheapLighting(ClassicLightFlags torchFlags, ClassicLightFlags expected)
    {
        (ClassicLightPublisher? publisher, ClassicWorldLightCache? cache, PortalCentreLights _) = Create(torchFlags);
        var portal = new TilePos(20, 30);
        publisher.PublishPortalLights([portal], new EffectTime(10));
        publisher.Prepare(TopDown, Viewport, 10);

        Assert.True(cache.TryGetVisibleTable(portal, out ClassicLightTable table));
        Assert.Contains(table.Lights.ToArray(), light => light.Flags == expected);
    }

    [Fact]
    public void CheapLightingPicksTheCheapTorchFlags()
    {
        Assert.Equal(ClassicLightFlags.CheapTorch, ClassicLightFlagsRules.Torch(cheapLighting: true));
        Assert.Equal(ClassicLightFlags.NormalTorch, ClassicLightFlagsRules.Torch(cheapLighting: false));
        Assert.True(ClassicLightFlags.CheapTorch.IsCheapTorch());
        Assert.False(ClassicLightFlags.NormalTorch.IsCheapTorch());
        Assert.False(ClassicLightFlags.None.IsCheapTorch());
    }

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(true, false, false, true)]
    [InlineData(false, true, false, true)]
    [InlineData(false, false, true, true)]
    public void ATileIsFilledAsExploredWhenExploredScannedOrLit(bool explored, bool scanned, bool lit, bool expected)
    {
        ClassicPointLight? light = lit ? ClassicPointLight.FromTerrain(new TilePos(1, 2), new Fp32(0), new TerrainLightColour(255, 0, 0)) : null;
        var tile = new TileView(new TilePos(1, 2), default, default, new PlayerId(0), tagged: false, solid: false, goldHeld: 0,
            healthPermille: 1000, explored: explored, scanned: scanned, pointLight: light);
        Assert.Equal(expected, tile.LitAsExplored);
    }
}
