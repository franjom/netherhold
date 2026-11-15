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

    [Theory]
    [InlineData(768, "FONT_INGAME8.bf4")]
    [InlineData(1080, "FONT_INGAME10.bf4")]
    [InlineData(1440, "FONT_INGAME14.bf4")]
    public void HudTooltipsAreAboutThirtyPercentOverAHundredTwentiethOfTheHeight(int height, string file) =>
        Assert.Equal(file, InterfaceFonts.HudTooltipFileName(height));

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

    [Fact]
    public void InferredPlacementCentresTheBlockAndEachLine()
    {
        // Line height 18, scale 2: two lines are 72 high in a 100 high box, so the block starts 14 down.
        BitmapTextLine[] lines = [new("aa", 20), new("b", 10)];

        PlacedBitmapGlyph[] placed = Placed(Font(), new HudRect(10, 20, 100, 100), lines, 2f, centred: true);

        Assert.Equal(['a', 'a', 'b'], placed.Select(p => p.Glyph.Character));
        Assert.Equal(new HudRect(40, 34, 16, 16), placed[0].Rect);
        Assert.Equal(new HudRect(60, 34, 16, 16), placed[1].Rect);
        Assert.Equal(new HudRect(50, 70, 16, 16), placed[2].Rect);
    }

    [Fact]
    public void UncentredPlacementStartsEachLineAtTheBoxEdge()
    {
        PlacedBitmapGlyph[] placed = Placed(Font(), new HudRect(10, 20, 100, 100), [new("ab", 20)], 1f, centred: false);

        Assert.Equal([10f, 20f], placed.Select(p => p.Rect.X));
    }

    [Fact]
    public void PlacementAppliesOffsetsRoundsTheCornerAndSkipsBlanks()
    {
        var font = new BitmapFont(10,
        [
            new BitmapGlyph('j', 4, 6, -1, 3, 5, new byte[24]),
            new BitmapGlyph(' ', 0, 0, 0, 0, 3, []),
        ]);

        // '?' is not in the font: no space taken. ' ' has no width: its advance, but not placed.
        PlacedBitmapGlyph[] placed = Placed(font, new HudRect(0.3f, 0, 100, 20), [new("j? j", 13)], 1.5f, centred: false);

        // Block: 15 high in a 20 high box, so y starts at 2.5. First j: x 0.3 - 1.5 = -1.2 -> -1, y 2.5 + 4.5 = 7.
        // Second j: pen 0.3 + 7.5 + 4.5 = 12.3, x 10.8 -> 11.
        Assert.Equal(2, placed.Length);
        Assert.Equal(new HudRect(-1, 7, 6, 9), placed[0].Rect);
        Assert.Equal(new HudRect(11, 7, 6, 9), placed[1].Rect);
    }

    [Fact]
    public void PlacingRetainedLinesDoesNotAllocate()
    {
        BitmapFont font = Font();
        IReadOnlyList<BitmapTextLine> lines = BitmapTextLayout.Wrap(font, "aa bb cc", 50);
        var box = new HudRect(0, 0, 200, 100);
        float sum = 0;
        foreach (PlacedBitmapGlyph glyph in BitmapTextPlacement.Place(font, box, lines, 1f)) sum += glyph.Rect.X;

        long before = GC.GetAllocatedBytesForCurrentThread();
        foreach (PlacedBitmapGlyph glyph in BitmapTextPlacement.Place(font, box, lines, 1f)) sum += glyph.Rect.X;

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(sum > 0);
    }

    [Theory]
    [InlineData(1920f, 1f, 1920)]
    [InlineData(1919f, 2f, 959)]
    [InlineData(100f, 3f, 33)]
    public void TheWrapWidthIsTheBoxWidthInFontPixelsRoundedDown(float boxWidth, float scale, int wrapWidth) =>
        Assert.Equal(wrapWidth, BitmapTextPlacement.WrapWidth(boxWidth, scale));

    [Fact]
    public void TheSubtitleBoxesAreTheRemakesLayout()
    {
        Assert.Equal(new HudRect(0, 945, 1920, 135), SubtitleLayout.SpeechBox(1920, 1080));
        Assert.Equal(new HudRect(0, 1080 * 0.70f, 1920, 135), SubtitleLayout.WinScreenBox(1920, 1080));
        Assert.Equal(new HudRect(0, 0, 1920, 1080), SubtitleLayout.TitleBox(1920, 1080));
        Assert.Equal(2f, SubtitleLayout.TitleScale);
        Assert.Equal(new HudRect(1920 * 0.05f, 945, 1920 * 0.9f, 135), SubtitleLayout.TrueTypeLabel(SubtitleLayout.SpeechBox(1920, 1080), 1920));
    }

    [Theory]
    [InlineData(1080, 1f, 36)]
    [InlineData(1080, 2f, 72)]
    [InlineData(300, 1f, 12)]
    [InlineData(375, 1f, 12)] // 12.5 halves to even
    [InlineData(405, 1f, 14)] // 13.5 halves to even
    public void TheTrueTypeSubtitleIsAThirtiethOfTheHeightAtLeastTwelve(int height, float scale, int size) =>
        Assert.Equal(size, SubtitleLayout.TrueTypePixelSize(height, scale));

    private static PlacedBitmapGlyph[] Placed(BitmapFont font, HudRect box, IReadOnlyList<BitmapTextLine> lines, float scale, bool centred)
    {
        var placed = new List<PlacedBitmapGlyph>();
        foreach (PlacedBitmapGlyph glyph in BitmapTextPlacement.Place(font, box, lines, scale, centred)) placed.Add(glyph);
        return [.. placed];
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
