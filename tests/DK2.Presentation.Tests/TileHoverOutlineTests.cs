// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using System.Numerics;

namespace DK2.Presentation.Tests;

public sealed class TileHoverOutlineTests
{
    [Fact]
    public void OutlineHasSplitPerimeterAndVerticalCorners()
    {
        Span<int> indices = stackalloc int[48];
        Assert.Equal(48, TileHoverOutline.Indices((TileHoverEdges)63, indices));
        Assert.Equal(4, TileHoverOutline.Indices(TileHoverEdges.North, indices));
        Assert.Equal(new[] {0, 1, 1, 2}, indices[..4].ToArray());
        Assert.Equal(8, TileHoverOutline.Indices(TileHoverEdges.North | TileHoverEdges.Lower, indices));
        Span<Vector3> points = stackalloc Vector3[27];
        TileHoverOutline.Grid(new TilePos(3, 5), points);
        Assert.Equal(new Vector3(3, 5, -2.05f), points[0]);
        Assert.Equal(new Vector3(4, 6, -1.05f), points[26]);
    }

    [Fact]
    public void BrightnessClampsAndReversesWithoutCarryingOvershoot()
    {
        var pulse = new TileHoverPulse();
        Assert.Equal(.5f, pulse.Advance(125));
        Assert.Equal(1, pulse.Advance(500));
        Assert.Equal(.5f, pulse.Advance(125));
        Assert.Equal(0, pulse.Advance(500));
        Assert.Equal(.5f, pulse.Advance(125));
    }

    [Fact]
    public void ExactEndpointWaitsForStrictOvershootBeforeReversing()
    {
        var pulse = new TileHoverPulse();
        Assert.Equal(1, pulse.Advance(250));
        Assert.Equal(1, pulse.Advance(125));
        Assert.Equal(.5f, pulse.Advance(125));
        Assert.Equal(0, pulse.Advance(125));
        Assert.Equal(0, pulse.Advance(125));
        Assert.Equal(.5f, pulse.Advance(125));
    }
}
