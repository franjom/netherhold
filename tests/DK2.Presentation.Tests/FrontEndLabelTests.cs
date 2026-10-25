// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Presentation.Tests;

public class FrontEndLabelTests
{
    [Fact]
    public void NativeAlignmentUsesTheAdvanceCanvasEvenWhenItsInkIsTiny()
    {
        var font = new BitmapFont(12, new[] { new BitmapGlyph('A', 1, 1, 1, 3, 10, new byte[] { 15 }) });
        var label = FrontEndLabelGeometry.Measure(font, "A");
        Assert.Equal(new FrontEndRect(6, 7, 1, 1), label.PlaceInk(new(0, 0, 20, 20), FrontEndTextAlignment.Centre));
        Assert.Equal(new FrontEndRect(6, 3, 1, 1), label.PlaceInk(new(0, 0, 20, 20), FrontEndTextAlignment.Centre, FrontEndVerticalAlignment.Top));
    }

    [Fact]
    public void ShellTextAndDialogTitleUseTheirOwnNativeFontsAndAlignmentModes()
    {
        FrontEndWidget option = FrontEndPages.Get(FrontEndPage.Graphics).Widgets.Single(w => w.Id.Value == 115);
        FrontEndWidget title = FrontEndPages.Get(FrontEndPage.Dialog).Widgets.Single(w => w.Id.Value == 684);
        Assert.Equal("FONT_INGAME16.bf4", FrontEndLabelStyles.FontFor(option.Draw).FileName);
        Assert.Equal("FONT_INGAME24.bf4", FrontEndLabelStyles.FontFor(title.Draw).FileName);
        Assert.Equal(FrontEndTextAlignment.Left, FrontEndLabelStyles.AlignmentFor(option));
        Assert.Equal(FrontEndTextAlignment.Centre, FrontEndLabelStyles.AlignmentFor(title));
    }

    [Fact]
    public void ReplacementGlyphsKeepTheOriginalInkBoundsAndAdvances()
    {
        var font = new BitmapFont(6, new[]
        {
            new BitmapGlyph('A', 3, 3, 1, 2, 5, new byte[] { 0,15,0, 15,15,15, 15,0,15 }),
            new BitmapGlyph(' ', 0, 0, 0, 0, 2, Array.Empty<byte>()),
            new BitmapGlyph('g', 2, 4, 0, 2, 3, new byte[] { 15,15, 15,15, 0,15, 15,15 }),
        });
        var label = FrontEndLabelGeometry.Measure(font, "A g");
        Assert.Equal(new FrontEndRect(1, 2, 8, 4), label.InkBounds);
        Assert.Equal(new FrontEndRect(1, 2, 3, 3), label.Glyphs[0].InkBounds);
        Assert.Equal(new FrontEndRect(7, 2, 2, 4), label.Glyphs[1].InkBounds);
        Assert.Equal(new FrontEndRect(46, 19, 8, 4), label.PlaceInk(new(0, 0, 100, 40), FrontEndTextAlignment.Centre));
        Assert.Equal(new FrontEndRect(45, 17, 10, 6), label.PlaceCanvas(new(0, 0, 100, 40), FrontEndTextAlignment.Centre));
        Assert.Equal(10, label.Advance);
    }

    [Fact]
    public void MissingReplacementCharactersCanFallBackWithoutChangingLayout()
    {
        var font = new BitmapFont(5, new[] { new BitmapGlyph('A', 1, 1, 0, 1, 2, new byte[] { 15 }) });
        var label = FrontEndLabelGeometry.Measure(font, "AA");
        Assert.Equal(new FrontEndRect(0, 1, 3, 1), label.InkBounds);
        Assert.Equal(2, label.Glyphs.Count);
    }
}
