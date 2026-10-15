// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Presentation.Tests;

public class ClassicTerrainTintTests
{
    [Theory]
    [InlineData(0, 255, 256, 0, 255, 0)]
    [InlineData(265, 510, 300, 9, 254, 44)]
    public void PieceEntriesKeepOnlyTheLowChannelBytes(int red, int green, int blue,
        int expectedRed, int expectedGreen, int expectedBlue)
        => Assert.Equal(new TerrainLightColour(expectedRed, expectedGreen, expectedBlue),
            ClassicTerrainTint.PieceColour(new TerrainLightColour(red, green, blue)));

    [Theory]
    [InlineData(TileConstruction.Normal, RoomTypeId.Treasury, false, true)]
    [InlineData(TileConstruction.DoubleQuad, RoomTypeId.Prison, false, true)]
    [InlineData(TileConstruction.DoubleQuad, RoomTypeId.Temple, false, true)]
    [InlineData(TileConstruction.QuadConstruction, RoomTypeId.WoodenBridge, true, true)]
    [InlineData(TileConstruction.QuadConstruction, RoomTypeId.WoodenBridge, false, false)]
    [InlineData(TileConstruction.QuadConstruction, RoomTypeId.StoneBridge, true, false)]
    [InlineData(TileConstruction.QuadConstruction, RoomTypeId.HeroStoneBridge, true, false)]
    [InlineData(TileConstruction.ThreeByThree, RoomTypeId.Portal, false, false)]
    public void QuarterColourFollowsTheNativeConstructionArms(TileConstruction construction,
        RoomTypeId room, bool overLava, bool tinted)
    {
        var colour = new TerrainLightColour(265, 20, 30);
        Assert.Equal(tinted ? colour : default,
            ClassicTerrainTint.RoomQuarterColour(construction, room, overLava, colour));
    }
}
