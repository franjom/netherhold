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
    [InlineData(8, 512, 7)]
    [InlineData(18, 1024, 22)]
    [InlineData(12, 640, 12)]
    public void TheSubtitleFontFollowsDk2sWidthRule(int baseSize, int width, int pointSize) =>
        Assert.Equal(pointSize, InterfaceFonts.IngamePointSize(baseSize, width));

    [Theory]
    [InlineData(1920, 1024)]
    [InlineData(1024, 1024)]
    [InlineData(1023, 800)]
    [InlineData(640, 640)]
    [InlineData(320, 400)]
    public void AModernScreenStandsForTheLargestDk2WidthThatFits(int screen, int reference) =>
        Assert.Equal(reference, InterfaceFonts.ReferenceWidth(screen));

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
    ]);
}
