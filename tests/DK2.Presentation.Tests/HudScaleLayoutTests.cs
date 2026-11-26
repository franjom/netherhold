// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;

namespace DK2.Presentation.Tests;

public class HudScaleLayoutTests
{
    [Theory]
    [InlineData(1024, 768, 50)]
    [InlineData(1024, 768, 125)]
    [InlineData(2560, 1440, 200)]
    public void BottomDockingKeepsBottomAndRightEdgesAndRoundTripsHits(int width, int height, int percent)
    {
        var layout = new HudScaleLayout(width, height, percent);
        var rightBottom = new Vector2(layout.LogicalWidth, height);
        Assert.Equal(new Vector2(width, height), layout.ToScreenTop(rightBottom, bottomDocked: true));
        var button = new Vector2(472 * height / 1920f, 1600 * height / 1920f);
        Vector2 screen = layout.ToScreenTop(button, bottomDocked: true);
        Assert.InRange(Vector2.Distance(button, layout.ToLocalTop(screen, bottomDocked: true)), 0, .001f);
    }
    [Theory]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(125)]
    [InlineData(200)]
    public void ScaledResourceHitFindsTheGoldWidgetAndRejectsTheUnscaledPosition(int percent)
    {
        var layout = new HudScaleLayout(2560, 1440, percent);
        var gold = new Vector2(800 * 1440 / 1920f, 64 * 1440 / 1920f);
        Vector2 screen = layout.ToScreenTop(gold, bottomDocked: false);
        Assert.Equal(KeeperResourceKind.Gold, KeeperResourceInteraction.HitTest(layout.ToLocalTop(screen, false), 1440));
        if (percent != 100)
        {
            Assert.NotEqual(KeeperResourceKind.Gold, KeeperResourceInteraction.HitTest(layout.ToLocalTop(gold, false), 1440));
        }
    }
    [Fact]
    public void RequestedScaleIsRetainedWhileEffectiveScaleFitsTheNativeStrip()
    {
        var capped = new HudScaleLayout(2560, 1440, 200);
        Assert.Equal(200, capped.RequestedPercent);
        Assert.InRange(capped.EffectivePercent, 133.33f, 133.34f);
        Assert.Equal(125, new HudScaleLayout(2560, 1440, 125).EffectivePercent);
        Assert.Equal(100, new HudScaleLayout(1024, 768, 200).EffectivePercent);
    }
    [Theory]
    [InlineData(1920, 1080, 100, 1920)]
    [InlineData(1920, 1080, 150, 1440)]   // capped at 4:3 of the height: factor 4/3
    [InlineData(1000, 500, 150, 667)]     // 666.67 rounds to the nearest pixel
    [InlineData(5, 1, 200, 2)]            // 2.5 rounds half to even, as Mathf.RoundToInt
    public void ScreenIsTheRoundedLogicalWidthOverTheFullHeight(int width, int height, int percent, int expectedWidth)
    {
        var layout = new HudScaleLayout(width, height, percent);
        Assert.Equal(new HudScreen(expectedWidth, height), layout.Screen);
    }
    [Theory]
    [InlineData(1024, 768, 100, 0)]
    [InlineData(1024, 768, 50, 384)]       // 768 - 768 * 0.5
    [InlineData(4, 5, 50, 3)]              // 5 - 2.5 = 2.5 rounds half up, not to even
    public void TheBottomOriginIsTheScaledOffsetRoundedHalfUp(int width, int height, int percent, float expected) =>
        Assert.Equal(expected, new HudScaleLayout(width, height, percent).BottomOriginPixels);

    [Fact]
    public void DefaultScalePreservesTheExistingCoordinates()
    {
        var layout = new HudScaleLayout(1024, 768, 100);
        var point = new Vector2(700, 600);
        Assert.Equal(point, layout.ToScreenTop(point, true));
        Assert.Equal(point, layout.ToLocalTop(point, false));
    }
}
