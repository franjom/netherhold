// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;

namespace DK2.Presentation.Tests;

public class RewardTabletTests
{
    [Fact]
    public void TheTabletGrowsOverThirtyTwoFramesTurningOnceThenDipsAndSettles()
    {
        var tablet = new RewardTablet();
        tablet.Restart();
        var random = new Random(1);
        tablet.DrawFrame(0, [], random);
        Assert.Equal(0.75f / 32, tablet.Scale, 6);                 // 1/32 of the gap
        Assert.Equal(0, tablet.Angle);
        for (int i = 1; i < 32; i++) tablet.DrawFrame(0, [], random);
        Assert.Equal(31 * 64, tablet.Angle);                        // frame 31: 1984 of 2048
        Assert.Equal(0.75f, tablet.Scale, 5);
        tablet.DrawFrame(0, [], random);
        Assert.Equal(0, tablet.Angle);                              // the turn ends square
        Assert.Equal(0.75f * 0.95f, tablet.Scale, 5);
        tablet.DrawFrame(0, [], random);
        Assert.Equal(0.75f, tablet.Scale);
    }

    [Fact]
    public void AfterFrameThirtyFourWithNoSparklesTheTabletHoldsTwoSecondsThenIsDone()
    {
        var tablet = new RewardTablet();
        tablet.Restart();
        var random = new Random(1);
        for (int i = 0; i < 35; i++) tablet.DrawFrame(1000, [], random);
        Assert.False(tablet.Done);                                  // frame 35 arms the hold at 3000
        tablet.DrawFrame(2999, [], random);
        Assert.False(tablet.Done);
        tablet.DrawFrame(3000, [], random);
        Assert.True(tablet.Done);
        tablet.Restart();
        Assert.False(tablet.Done);
        Assert.Equal(0, tablet.Scale);
    }

    [Fact]
    public void WhileGrowingEachPointGivesTwoSparklesThatLiveEightToElevenFramesAndHoldTheTabletBack()
    {
        var tablet = new RewardTablet();
        tablet.Restart();
        var random = new Random(7);
        Vector3[] points = [new(0.05f, 0.05f, 0), new(-0.05f, 0.05f, 0), new(0, 0, 0.0097f)];
        tablet.DrawFrame(0, points, random);
        Assert.Equal(6, tablet.Sparkles.Length);                    // born then aged once
        foreach (RewardTablet.Sparkle s in tablet.Sparkles)
        {
            Assert.InRange(s.Life, 7, 10);
            Assert.InRange(s.VelocityX, -2f, 2f);
            Assert.InRange(s.VelocityY, -2f, 2f);
        }
        for (int i = 1; i < 32; i++) tablet.DrawFrame(0, points, random);
        for (int i = 0; i < 4; i++) tablet.DrawFrame(10_000 + i * 5_000, points, random);
        Assert.Equal(36, tablet.Frame);
        Assert.NotEqual(0, tablet.Sparkles.Length);                 // frame 31's still live:
        Assert.False(tablet.Done);                                  // no hold is armed, however late
        double now = 30_000;
        while (tablet.Sparkles.Length != 0) tablet.DrawFrame(now, points, random);
        Assert.False(tablet.Done);                                  // the last one gone arms the hold
        tablet.DrawFrame(now + 1_999, points, random);
        Assert.False(tablet.Done);
        tablet.DrawFrame(now + 2_000, points, random);
        Assert.True(tablet.Done);
        Assert.Equal(0, tablet.Sparkles.Length);                    // none born after frame 31
    }

    [Theory]
    [InlineData(1, 0)]      // Alarm
    [InlineData(12, 11)]    // Dark_Library
    [InlineData(42, 41)]    // Training
    [InlineData(43, 42)]    // Treasury
    [InlineData(51, 50)]    // reaper_talisman-01d
    public void TheFrontsFiftyOneFramesShowItemNAtFrameNMinusOne(byte item, int frame) =>
        Assert.Equal(frame, RewardTablet.TextureFrame(51, item));

    [Fact]
    public void AOneTextureMaterialAlwaysShowsItsOnlyFrame() => Assert.Equal(0, RewardTablet.TextureFrame(1, 43));
}
