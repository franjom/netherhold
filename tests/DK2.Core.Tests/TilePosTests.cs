// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Core.Tests;

public class TilePosTests
{
    [Fact]
    public void ChebyshevDistance_CountsDiagonalAsOne()
    {
        Assert.Equal(1, new TilePos(0, 0).ChebyshevDistance(new TilePos(1, 1)));
        Assert.Equal(5, new TilePos(2, 3).ChebyshevDistance(new TilePos(7, 1)));
    }

    [Fact]
    public void ManhattanDistance_SumsAxes()
    {
        Assert.Equal(2, new TilePos(0, 0).ManhattanDistance(new TilePos(1, 1)));
    }
}
