// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Presentation.Tests;

public class ClassicLightClassificationTests
{
    [Fact]
    public void TerrainAndTorchModesPreserveNativeClassificationWithoutChangingInputs()
    {
        const int normalTorchFlags = 0x1B2, cheapTorchFlags = 0x74, terrainFlags = 0x64;
        var torch = new TorchView(0, new TilePos(1, 2), 4096, 8192, 6144, 0, 300, 200, 100, 128, 6144, default);
        var normal = ClassicPointLight.FromTorch(torch, ClassicLightFlags.NormalTorch);
        var cheap = ClassicPointLight.FromTorch(torch, ClassicLightFlags.CheapTorch);
        Assert.Equal((ClassicLightFlags)normalTorchFlags, normal.Flags);
        Assert.Equal((ClassicLightFlags)cheapTorchFlags, cheap.Flags);
        Assert.Equal(normal.Position, cheap.Position);
        Assert.Equal(normal.Colour, cheap.Colour);
        Assert.Equal(normal.Radius, cheap.Radius);
        Assert.Equal(normal.Intensity, cheap.Intensity);
        Assert.Equal((ClassicLightFlags)terrainFlags,
            ClassicPointLight.FromTerrain(new TilePos(1, 2), new Fp32(0), new TerrainLightColour(1, 2, 3)).Flags);
    }

    [Fact]
    public void BoundedSelectionRetainsClassificationBeforeDrawFiltering()
    {
        var tile = new TilePos(0, 0);
        var torch = new TorchView(0, tile, 2048, 2048, 4096, 0, 255, 255, 255, 128, 6144, default);
        var table = new ClassicLightTable(tile);
#pragma warning disable IDE0300 // Preserve the original array construction or overload.
        table.RebuildStatic(new[]
        {
#pragma warning restore IDE0300
            ClassicPointLight.FromTorch(torch, ClassicLightFlags.NormalTorch),
            ClassicPointLight.FromTerrain(tile, new Fp32(0), new TerrainLightColour(255, 0, 0)),
        });
        Assert.Equal(2, table.Count);
        Assert.Equal(ClassicLightFlags.NormalTorch, table.Lights[0].Flags);
        Assert.Equal(ClassicLightFlags.Terrain, table.Lights[1].Flags);
    }
}
