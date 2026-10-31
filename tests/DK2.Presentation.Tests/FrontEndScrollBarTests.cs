// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation.Tests;

public sealed class FrontEndScrollBarTests
{
    private static readonly FrontEndRect StatisticsList = new(760, 968, 1500, 512);   // widget 180

    private static FrontEndScrollBar Armed()
    {
        FrontEndScrollBar bar = FrontEndScrollBar.Beside(StatisticsList, CampaignResults.ScrollBarGap,
            CampaignResults.ScrollBarWidth, CampaignResults.ScrollBarHeight);
        bar.Bar.Arm(CampaignResults.ScrollMaximum);
        return bar;
    }

    /// <summary>0x0052A0D0: x + width + byte 5 (90); 0x00552420 makes it 48 × 452; 0x00529160's thumb is
    /// max(16, 356 / 10) = 35 units below the 48-unit up square: 12 × 8 pixels, inside the 14-pixel art.</summary>
    [Fact]
    public void TheStatisticsBarSitsRightOfTheListWithATrackLengthThumb()
    {
        FrontEndScrollBar bar = Armed();
        Assert.Equal(new FrontEndRect(2350, 968, 48, 452), bar.Rect);
        Assert.Equal((0, 9), (bar.Bar.Position, bar.Bar.Range));
        Assert.Equal(new FrontEndRect(587, 254, 12, 8), bar.ThumbRect());   // 0x0052C630 truncates
    }

    [Theory]
    [InlineData(590, 245, ScrollBarPart.Up)]
    [InlineData(590, 350, ScrollBarPart.Down)]
    [InlineData(590, 256, ScrollBarPart.Thumb)]
    [InlineData(590, 300, ScrollBarPart.Track)]
    [InlineData(580, 300, ScrollBarPart.None)]
    [InlineData(590, 356, ScrollBarPart.None)]
    public void TheHoverTestSplitsTheBarIntoArrowSquaresThumbAndTrack(float x, float y, ScrollBarPart part) =>
        Assert.Equal(part, Armed().HitTest(new FrontEndPoint(x, y)));

    /// <summary>One step on the press, then one per <see cref="FrontEndScrollBar.HeldStepSeconds"/> held
    /// (the remake's rate; DK2's front-end update rate is unread). A track press steps once.</summary>
    [Fact]
    public void AHeldArrowStepsOnThePressThenAtItsIntervalAndTheTrackOncePerPress()
    {
        FrontEndScrollBar bar = Armed();
        var down = new FrontEndPoint(590, 350);
        bar.Update(down, true, 0); bar.Update(down, true, FrontEndScrollBar.HeldStepSeconds / 2);
        Assert.Equal(1, bar.Bar.Position);
        bar.Update(down, true, FrontEndScrollBar.HeldStepSeconds);
        Assert.Equal(2, bar.Bar.Position);
        bar.Update(down, false, 0.2);
        Assert.Equal(2, bar.Bar.Position);
        var track = new FrontEndPoint(590, 330);
        bar.Update(track, true, 1); bar.Update(track, true, 2); bar.Update(track, true, 3);
        Assert.Equal(3, bar.Bar.Position);
        bar.Update(track, false, 3);
        var up = new FrontEndPoint(590, 245);
        for (int i = 0; i < 20; i++) bar.Update(up, true, 4 + i * FrontEndScrollBar.HeldStepSeconds);
        Assert.Equal(0, bar.Bar.Position);
    }

    [Fact]
    public void AHeldThumbFollowsThePointerDownTheTrackAndHoldsOutsideIt()
    {
        FrontEndScrollBar bar = Armed();
        bar.Update(new FrontEndPoint(590, 256), true, 0);
        bar.Update(new FrontEndPoint(590, 300), true, 0);
        int middle = bar.Bar.Position;
        Assert.InRange(middle, 1, 8);
        bar.Update(new FrontEndPoint(620, 340), true, 0);        // beside the bar: 0x00528B20 skips the drag
        bar.Update(new FrontEndPoint(590, 350), true, 0);        // on the down arrow: likewise
        Assert.Equal(middle, bar.Bar.Position);
        bar.Update(new FrontEndPoint(590, 342), true, 0);
        Assert.Equal(9, bar.Bar.Position);
    }

    [Fact]
    public void ADisarmedBarIgnoresInput()
    {
        FrontEndScrollBar bar = FrontEndScrollBar.Beside(StatisticsList, CampaignResults.ScrollBarGap);
        bar.Update(new FrontEndPoint(590, 365), true, 0);
        Assert.Equal(0, bar.Bar.Position);
    }

    /// <summary>0x00551BA0: the counter restarts at 8 and counts down before drawing, so the hovered frame and
    /// the three after the pointer leaves show full glow, then it fades over three.</summary>
    [Fact]
    public void TheGlowHoldsThenFadesAfterThePointerLeaves()
    {
        FrontEndScrollBar bar = Armed();
        bar.Frame(ScrollBarPart.Up);
        var frames = new List<int> { bar.GlowFrame(0) };
        for (int i = 0; i < 7; i++) { bar.Frame(ScrollBarPart.None); frames.Add(bar.GlowFrame(0)); }
        Assert.Equal(new[] { 3, 3, 3, 3, 2, 1, 0, -1 }, frames);
        Assert.Equal(-1, bar.GlowFrame(1));
        bar.Frame(ScrollBarPart.Thumb);
        Assert.Equal(3, bar.GlowFrame(2));
    }

    [Fact]
    public void TheArtSitsEightLeftAndSixAboveTheBar()
    {
        FrontEndScrollBar bar = Armed();
        Assert.Equal(new FrontEndPoint(579, 236), bar.ArtOrigin(0));
        Assert.Equal(new FrontEndPoint(579, 341), bar.ArtOrigin(1));
        Assert.Equal(new FrontEndPoint(579, 246), bar.ArtOrigin(2));
    }
}
