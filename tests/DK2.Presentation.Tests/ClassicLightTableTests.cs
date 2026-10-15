// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using DK2.Data.Model;

namespace DK2.Presentation.Tests;

public class ClassicLightTableTests
{
    private static readonly TilePos Tile = new(0, 0);
    private static ClassicPointLight Source(int identity, float distance = 1, float height = 0)
        => ClassicPointLight.FromTorch(new TorchView(identity, Tile,
            (int)((.5f + distance) * Fp32.One), Fp32.One / 2, (int)(height * Fp32.One), yaw: 0,
            red: identity, green: 0, blue: 0, intensity: 128, radius: 4608, mesh: default));

    [Fact]
    public void TerrainSourceUsesAuthoredHeightBoostedColourAndNativeDefaults()
    {
        ClassicPointLight light = ClassicPointLight.FromTerrain(new TilePos(2, 3), new Fp32(-2048), new(510, 128, 0));
        Assert.Equal(new Vector3(2.5f, -.5f, -3.5f), light.Position);
        Assert.Equal(new Vector3(2, 128f / 255f, 0), light.Colour);
        Assert.Equal(1, light.Intensity);
        Assert.Equal(1.125f, light.Radius);
    }

    [Fact]
    public void StaticSelectionKeepsTwelveNearestWithoutSortingSlots()
    {
        var table = new ClassicLightTable(Tile);
        ClassicPointLight[] sources = Enumerable.Range(1, 15).Select(i => Source(i, (16 - i) / 8f)).ToArray();
        table.RebuildStatic(sources);
        Assert.Equal(12, table.StaticCount);
        Assert.Equal(12, table.Count);
        Assert.Equal(Enumerable.Range(4, 12), table.Lights.ToArray().Select(Id).Order());
        Assert.Equal(new[] { 13, 14, 15, 4 }, table.Lights.ToArray().Take(4).Select(Id));
    }

    [Fact]
    public void CandidateBoundsAreStrictAndHorizontal()
    {
        var table = new ClassicLightTable(Tile);
        table.RebuildStatic(new[] { Source(1, 2.125f), Source(2, 2.125f - 1f / Fp32.One, 100) });
        Assert.Equal(1, table.Count);
        Assert.Equal(2, Id(table.Lights[0]));
    }

    [Fact]
    public void DynamicSourcesCannotReplaceProtectedStaticSlots()
    {
        var table = new ClassicLightTable(Tile);
        table.RebuildStatic(Enumerable.Range(1, 12).Select(i => Source(i, 2)).ToArray());
        table.RebuildDynamic(Enumerable.Range(20, 15).Select(i => Source(i, (35 - i) / 16f)).ToArray());
        Assert.Equal(24, table.Count);
        Assert.Equal(Enumerable.Range(1, 12), table.Lights.ToArray().Take(12).Select(Id));
        Assert.Equal(Enumerable.Range(23, 12), table.Lights.ToArray().Skip(12).Select(Id).Order());
    }

    [Fact]
    public void DynamicSourcesCanUseAllTwentyFourSlotsWithoutStaticSources()
    {
        var table = new ClassicLightTable(Tile);
        table.RebuildDynamic(Enumerable.Range(1, 27).Select(i => Source(i, (28 - i) / 16f)).ToArray());
        Assert.Equal(0, table.StaticCount);
        Assert.Equal(24, table.Count);
        Assert.Equal(Enumerable.Range(4, 24), table.Lights.ToArray().Select(Id).Order());
    }

    [Fact]
    public void EqualDistanceCandidatesKeepEncounterOrderAndFirstFarthestSlotIsReplaced()
    {
        var table = new ClassicLightTable(Tile);
        table.RebuildStatic(Enumerable.Range(1, 13).Select(i => Source(i)).Append(Source(14, .5f)).ToArray());
        Assert.Equal(new[] { 14, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 }, table.Lights.ToArray().Select(Id));
    }

    [Fact]
    public void DynamicRefreshClearsPreviousFrameWhileStaticRefreshDropsBothLists()
    {
        var table = new ClassicLightTable(Tile);
        table.RebuildStatic(new[] { Source(1) });
        table.RebuildDynamic(new[] { Source(2) });
        table.RebuildDynamic(ReadOnlySpan<ClassicPointLight>.Empty);
        Assert.Equal(new[] { 1 }, table.Lights.ToArray().Select(Id));
        table.RebuildStatic(new[] { Source(3) });
        Assert.Equal(new[] { 3 }, table.Lights.ToArray().Select(Id));
        Assert.Equal(1, table.StaticCount);
    }

    private static int Id(ClassicPointLight light) => (int)MathF.Round(light.Colour.X * 255);
}
