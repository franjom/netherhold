// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation.Tests;

public sealed class TextScrollBarTests
{
    [Fact]
    public void TextThatFitsLeavesTheBarDisabled()
    {
        var bar = new TextScrollBar();
        bar.Reset(textPixels: 200, boxPixels: 200);
        Assert.False(bar.Enabled);
        bar.Step(1);
        Assert.Equal(0, bar.Position);
    }

    [Fact]
    public void StepsMoveOnePixelClampedToTheRange()
    {
        var bar = new TextScrollBar();
        bar.Reset(textPixels: 302, boxPixels: 300);
        Assert.Equal(2, bar.Range);
        bar.Step(-1);
        Assert.Equal(0, bar.Position);
        bar.Step(1); bar.Step(1); bar.Step(1);
        Assert.Equal(2, bar.Position);
        bar.Reset(textPixels: 302, boxPixels: 300);
        Assert.Equal(0, bar.Position);
    }

    /// <summary>Menu40's 288-tall bar: track 160; with any real overflow the thumb is the 16 minimum.</summary>
    [Fact]
    public void ThumbIsTheMinimumAndRunsTheTrack()
    {
        var bar = new TextScrollBar();
        bar.Reset(textPixels: 500, boxPixels: 300);
        Assert.Equal((64, 16), bar.Thumb(TextScrollBar.Height));
        for (int i = 0; i < 200; i++) bar.Step(1);
        Assert.Equal((64 + 160 - 16, 16), bar.Thumb(TextScrollBar.Height));
    }

    [Fact]
    public void DragMapsThePointerOverTheTrack()
    {
        var bar = new TextScrollBar();
        bar.Reset(textPixels: 459, boxPixels: 300);
        bar.DragTo(64 + 80, TextScrollBar.Height);
        Assert.Equal(80 * 160 / 160, bar.Position);
        bar.DragTo(0, TextScrollBar.Height);
        Assert.Equal(0, bar.Position);
        bar.DragTo(1000, TextScrollBar.Height);
        Assert.Equal(159, bar.Position);
    }
}
