// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art;
using DK2.Assets;

namespace DK2.Convert.Tests;

public sealed class StatusFlowerArtTests
{
    [Fact]
    public void CasinoOverlayRegistersAValidUiAssetAndItsNativeAnimationDescriptor()
    {
        ArtEntry entry = Assert.Single(StatusFlowerArt.Entries);
        Assert.Equal(AssetRef.Create(AssetRef.UiCategory, "casino_dollar", AssetKind.Mesh), entry.Reference);
        Assert.NotNull(entry.Source);
        Assert.Equal("Dollar", entry.Source.Name); Assert.Equal(6, entry.Source.Type);
        Assert.Equal(30, entry.Source.FrameCount); Assert.Equal(30, entry.Source.FramesPerSecond);
    }
}
