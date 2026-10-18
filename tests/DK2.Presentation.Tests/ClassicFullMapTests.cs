// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
namespace DK2.Presentation.Tests;
public sealed class ClassicFullMapTests
{
    [Fact]
    public void FitsNativeRectanglePreservingMapAspect()
    {
        var layout = ClassicFullMapLayout.Fit(1024,768,100,80);
        Assert.Equal(new Vector2(65,113),layout.Origin);
        Assert.Equal(new Vector2(615,492),layout.Size);
        Assert.Equal(new TilePos(50,40),layout.ResolveTile(layout.Origin+layout.Size/2));
    }
    [Fact]
    public void PointerCoordinatesTruncateWithoutMinimapBoundsClamp()
    {
        var layout = ClassicFullMapLayout.Fit(1024,768,100,80);
        Assert.Equal(new TilePos(-2,0),layout.ResolveTile(layout.Origin-new Vector2(layout.Size.X*.025f,0)));
        Assert.Equal(new TilePos(100,80),layout.ResolveTile(layout.Origin+layout.Size));
    }
}
