// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Kwd;
using DK2.Data.Model;

namespace DK2.Data.Tests;

public class KwdTerrainLightingTests
{
    private static TerrainDef Read(byte[] bytes) => KwdTerrainRooms.ParseTerrain([
        new KwdChunk { Type = KwdRecordType.Terrain, ItemCount = 1, Content = bytes, HeaderBlock = [] }]).Single();

    [Fact]
    public void AuthoredRgbChannelsRoundTripAndEditsPreserveUnrelatedBytes()
    {
        byte[] source = new byte[552];
        int[] offsets = [0x1DB, 0x1DC, 0x1DD, 0x201, 0x202, 0x203];
        for (int i = 0; i < offsets.Length; i++) source[offsets[i]] = (byte)(10 + i);
        TerrainDef terrain = Read(source);
        Assert.Equal(new TerrainLighting(10, 11, 12, 13, 14, 15), terrain.Lighting);
        var image = new KwdRecordImage(KwdRecordType.Terrain, source);
        Assert.Equal(source, KwdTerrainRooms.WriteTerrainRecord(terrain, image));
        byte[] edited = KwdTerrainRooms.WriteTerrainRecord(
            terrain with { Lighting = new TerrainLighting(21, 22, 23, 24, 25, 26) }, image);
        byte[] expected = (byte[])source.Clone();
        for (int i = 0; i < offsets.Length; i++) expected[offsets[i]] = (byte)(21 + i);
        Assert.Equal(expected, edited);
        Assert.Equal(source, KwdTerrainRooms.WriteTerrainRecord(terrain with { Lighting = null }, image));
    }

    [Theory]
    [InlineData((TerrainFlags)0, 13, 14, 15)]
    [InlineData(TerrainFlags.AmbientColorRed, 268, 14, 15)]
    [InlineData(TerrainFlags.AmbientColorGreen, 13, 269, 15)]
    [InlineData(TerrainFlags.AmbientColorBlue, 13, 14, 270)]
    public void LoaderAdds255ToFlaggedAmbientChannels(TerrainFlags flags, int r, int g, int b)
    {
        var lighting = new TerrainLighting(10, 11, 12, 13, 14, 15);
        Assert.Equal(new TerrainLightColour(r, g, b), lighting.AmbientColour(flags));
    }

    [Theory]
    [InlineData((TerrainFlags)0, 10, 11, 12)]
    [InlineData(TerrainFlags.TerrainColorRed, 265, 11, 12)]
    [InlineData(TerrainFlags.TerrainColorGreen, 10, 266, 12)]
    [InlineData(TerrainFlags.TerrainColorBlue, 10, 11, 267)]
    public void TerrainChannelsHaveTheirOwnIndependentBoostFlags(TerrainFlags flags, int r, int g, int b)
    {
        var lighting = new TerrainLighting(10, 11, 12, 13, 14, 15);
        Assert.Equal(new TerrainLightColour(r, g, b), lighting.TerrainColour(flags));
        Assert.Equal(new TerrainLightColour(13, 14, 15), lighting.AmbientColour(flags));
    }

    [Fact]
    public void BoostedAuthoredMaximumRemains510()
    {
        var lighting = new TerrainLighting(255, 255, 255, 255, 255, 255);
        Assert.Equal(new TerrainLightColour(510, 510, 510), lighting.AmbientColour(
            TerrainFlags.AmbientColorRed | TerrainFlags.AmbientColorGreen | TerrainFlags.AmbientColorBlue));
    }
}
