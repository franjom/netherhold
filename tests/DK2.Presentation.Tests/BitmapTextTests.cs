// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Presentation.Tests;

public class BitmapTextTests
{
    [Theory]
    [InlineData(10, 640, 10)]
    [InlineData(10, 800, 12)]
    [InlineData(10, 1024, 14)]
    [InlineData(10, 512, 8)]
    [InlineData(10, 400, 8)]
    [InlineData(14, 400, 10)]
    [InlineData(10, 1920, 10)]
    [InlineData(10, 1600, 10)]
    [InlineData(8, 512, 7)]
    [InlineData(18, 1024, 22)]
    [InlineData(12, 640, 12)]
    public void TheSubtitleFontFollowsDk2sWidthRule(int baseSize, int width, int pointSize) =>
        Assert.Equal(pointSize, InterfaceFonts.IngamePointSize(baseSize, width));

    [Theory]
    [InlineData(200, "FONT_INGAME7.bf4")]
    [InlineData(480, "FONT_INGAME12.bf4")]
    [InlineData(1080, "FONT_INGAME26.bf4")]
    [InlineData(1440, "FONT_INGAME36.bf4")]
    [InlineData(2160, "FONT_INGAME48.bf4")]
    public void OurLegibleSizeIsTheLargestShippedFontWithinAFortiethOfTheHeight(int height, string file) =>
        Assert.Equal(file, InterfaceFonts.LegibleFileName(height));

    [Theory]
    [InlineData(480, "FONT_INGAME8.bf4")]
    [InlineData(768, "FONT_INGAME12.bf4")]
    [InlineData(1080, "FONT_INGAME18.bf4")]
    [InlineData(1440, "FONT_INGAME24.bf4")]
    public void HudTextIsTheLargestShippedFontWithinASixtiethOfTheHeight(int height, string file) =>
        Assert.Equal(file, InterfaceFonts.HudTextFileName(height));

    [Fact]
    public void InferredWrapKeepsADoubleSpaceInsideALine()
    {
        BitmapTextLine line = Assert.Single(BitmapTextLayout.Wrap(Font(), "aa.  bb ", 100));

        Assert.Equal("aa.  bb", line.Text);
        Assert.Equal(52, line.Width);
    }

    [Fact]
    public void InferredWrapDropsSpacesAtABreakAndAddsNoEmptyLine()
    {
        IReadOnlyList<BitmapTextLine> lines = BitmapTextLayout.Wrap(Font(), "aa bb  cc ", 45);

        Assert.Equal(["aa bb", "cc"], lines.Select(l => l.Text));
        Assert.Equal(["aa bb"], BitmapTextLayout.Wrap(Font(), "aa bb ", 45).Select(l => l.Text));
        Assert.Equal(["aa", "", "bb"], BitmapTextLayout.Wrap(Font(), "aa\n\nbb", 45).Select(l => l.Text));
    }

    [Fact]
    public void InferredWrapBreaksAtSpacesByAdvance()
    {
        BitmapFont font = Font();

        IReadOnlyList<BitmapTextLine> lines = BitmapTextLayout.Wrap(font, "aa bb cc", 50);

        // a = 10, space = 5: "aa bb" is 45, "aa bb cc" 70.
        Assert.Equal(["aa bb", "cc"], lines.Select(l => l.Text));
        Assert.Equal([45, 20], lines.Select(l => l.Width));
    }

    [Fact]
    public void InferredWrapBreaksAWordWiderThanTheBoxInside()
    {
        IReadOnlyList<BitmapTextLine> lines = BitmapTextLayout.Wrap(Font(), "aaaaaaa", 30);

        Assert.Equal(["aaa", "aaa", "a"], lines.Select(l => l.Text));
    }

    private static BitmapFont Font() => new(18,
    [
        new BitmapGlyph('a', 8, 8, 0, 0, 10, new byte[64]),
        new BitmapGlyph('b', 8, 8, 0, 0, 10, new byte[64]),
        new BitmapGlyph('c', 8, 8, 0, 0, 10, new byte[64]),
        new BitmapGlyph(' ', 0, 0, 0, 0, 5, []),
        new BitmapGlyph('.', 2, 2, 0, 0, 2, new byte[4]),
    ]);
}
