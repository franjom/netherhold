// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using static DK2.Presentation.FrontEndBriefingImage;

namespace DK2.Presentation.Tests;

public sealed class FrontEndBriefingImagesTests
{
    private static FrontEndPlacedImage[] For(uint mode, uint section, bool secret = false) =>
        FrontEndBriefingImages.For(mode, section, secret).ToArray();

    [Fact]
    public void TheBriefingPageFramesBothShotsWithItsObjectiveStrips()
    {
        Assert.Equal(new FrontEndPlacedImage[] { new(FirstShot, 20, 110), new(Objective00, 184, 110), new(Objective01, 184, 147) }, For(0, 2));
        Assert.Equal(new FrontEndPlacedImage[] { new(SecondShot, 20, 242), new(Objective00, 184, 242), new(Objective02, 184, 279) }, For(0, 3));
    }

    /// <summary>Page 26: BG-Objective-00 behind the rank, -01 behind the summary, -03 behind the list.</summary>
    [Fact]
    public void TheStatisticsPagePutsTheListStripUnderTheSecondShot()
    {
        Assert.Equal(new FrontEndPlacedImage[] { new(FirstShot, 20, 110), new(Objective00, 184, 110), new(Objective01, 184, 147) }, For(1, 2));
        Assert.Equal(new FrontEndPlacedImage[] { new(SecondShot, 20, 242), new(Objective03, 184, 242) }, For(1, 3));
    }

    [Fact]
    public void ASecretLevelHasNoSecondShotAndOnlyPage26KeepsItsStrip()
    {
        Assert.Equal(new FrontEndPlacedImage[] { new(Objective03, 184, 242) }, For(1, 3, secret: true));
        Assert.Empty(For(0, 3, secret: true));
        Assert.Equal(3, For(0, 2, secret: true).Length);
    }

    [Fact]
    public void OtherModesUseTheirOwnStrips()
    {
        Assert.Equal(new FrontEndPlacedImage[] { new(SecondShot, 20, 242), new(Objective04, 0, 242) }, For(2, 3));
        Assert.Equal(new FrontEndPlacedImage[] { new(SecondShot, 20, 242), new(Objective04, 0, 242) }, For(4, 3));
        FrontEndPlacedImage[] load = For(3, 2);
        Assert.Equal(new FrontEndPlacedImage(FirstShot, 20, 110), load[0]);
        Assert.Equal(new[] { 148, 178, 208, 238, 268, 298 }, load.Skip(1).Select(p => p.Y));
        Assert.All(load.Skip(1), p => Assert.Equal((Load01, 61), (p.Image, p.X)));
        Assert.Equal(new FrontEndPlacedImage[] { new(SecondShot, 20, 242) }, For(3, 3));
        Assert.Empty(For(1, 1));
    }

    [Fact]
    public void StripsNameTheirBackgroundTiles()
    {
        Assert.Equal("BG_Tiles/BG-Objective-03", FrontEndBriefingImages.ArtName(Objective03));
        Assert.Null(FrontEndBriefingImages.ArtName(FirstShot));
    }
}
