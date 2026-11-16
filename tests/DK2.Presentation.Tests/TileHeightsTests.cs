// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation.Tests;

public sealed class TileHeightsTests
{
    [Fact]
    public void AWallStandsOneTileAboveTheFloorYouStandOn()
    {
        Assert.Equal(1f, TileHeights.Floor);
        Assert.Equal(TileHeights.Floor, TileHeights.Open);
        Assert.Equal(TileHeights.Floor + 1f, TileHeights.Solid);
    }
}
