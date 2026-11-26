// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation.Tests;

/// <summary>Menu 43 as a draw list (<see cref="BattleTabHud"/>), as Unity's <c>ClassicBattleTab</c> shows it.</summary>
public sealed class BattleTabHudTests
{
    private static readonly HudVisibility Shown = new(true, true);
    private static readonly HudScreen FourThree = new(1024, 768);

    [Fact]
    public void OnlyTheShownButtonsDrawAndAHiddenOneLeavesItsGap()
    {
        var hud = new BattleTabHud(id => "t" + id);
        var frame = new HudFrame();
        hud.Build(FourThree, Shown, false, new BattleTabSnapshot(true, false, true), 150 * 6, frame);
        Assert.Equal(2, frame.Count);
        Assert.Equal(new HudDraw(HudDrawKind.Quad, "GUI\\Icons\\a-fight-02", new HudRect(2080, 1460, 128, 128), HudColor.White),
            frame.Draws[0]);
        Assert.Equal(new HudDraw(HudDrawKind.Quad, "GUI\\Icons\\a-alarm-02", new HudRect(2080 + 264, 1460, 128, 128), HudColor.White),
            frame.Draws[1]);
        Assert.Equal(BattleTabButton.Battle, hud.ButtonAt(2080, 1460));
        Assert.Equal(BattleTabButton.None, hud.ButtonAt(2080 + 128, 1460));   // the right edge is outside
        Assert.Equal(BattleTabButton.None, hud.ButtonAt(2080 + 140, 1500));   // the cancel is hidden
        Assert.Equal(BattleTabButton.Alarm, hud.ButtonAt(2080 + 300, 1587));
    }

    [Fact]
    public void AHiddenHudDrawsAndHitsNothing()
    {
        var hud = new BattleTabHud(id => "t" + id);
        var frame = new HudFrame();
        hud.Build(FourThree, new HudVisibility(false, true), false, new BattleTabSnapshot(true, true, true), 0, frame);
        Assert.Equal(0, frame.Count);
        Assert.Equal(BattleTabButton.None, hud.ButtonAt(2100, 1500));
        Assert.Equal(HoverTextSnapshot.Empty, hud.HoverText(2100, 1500));
    }

    [Fact]
    public void TheCollapsedPanelDropsTheHitAreas()
    {
        var hud = new BattleTabHud(id => "t" + id);
        hud.Build(FourThree, Shown, true, new BattleTabSnapshot(true, false, false), 0, new HudFrame());
        Assert.Equal(BattleTabButton.None, hud.ButtonAt(2100, 1500));
        Assert.Equal(BattleTabButton.Battle, hud.ButtonAt(2100, 1800));
    }

    [Fact]
    public void PressesNeedInputAndOnlyLeftAndRightAreRaised()
    {
        var hud = new BattleTabHud(id => "t" + id);
        var raised = new List<(BattleTabButton, int)>();
        hud.Clicked += (button, mouse) => raised.Add((button, mouse));
        hud.Build(FourThree, new HudVisibility(true, false), false, new BattleTabSnapshot(false, true, false), 0, new HudFrame());
        // Shown but not taking input: hovered, not hit for the world, and the press passes through.
        Assert.Equal(BattleTabButton.CallToArms, hud.ButtonAt(2220, 1500));
        Assert.False(hud.ContainsPointer(2220, 1500));
        Assert.False(hud.PointerDown(2220, 1500, 0));
        hud.Build(FourThree, Shown, false, new BattleTabSnapshot(false, true, false), 0, new HudFrame());
        Assert.True(hud.ContainsPointer(2220, 1500));
        Assert.True(hud.PointerDown(2220, 1500, 0));
        Assert.True(hud.PointerDown(2220, 1500, 1));
        Assert.True(hud.PointerDown(2220, 1500, 2));   // taken, not raised
        Assert.False(hud.PointerDown(100, 100, 0));
        Assert.Equal([(BattleTabButton.CallToArms, 0), (BattleTabButton.CallToArms, 1)], raised);
    }

    [Fact]
    public void TheBladesGiveTheirTextAndTheOthersNone()
    {
        var hud = new BattleTabHud(id => "t" + id);
        hud.Build(FourThree, Shown, false, new BattleTabSnapshot(true, true, true), 0, new HudFrame());
        Assert.Equal(new HoverTextSnapshot("t2166", "t2165", default), hud.HoverText(2100, 1500));
        Assert.Equal(HoverTextSnapshot.Empty, hud.HoverText(2220, 1500));
        Assert.Equal(HoverTextSnapshot.Empty, hud.HoverText(2350, 1500));
    }

    [Fact]
    public void ARebuildThatHidesTheBladesTakesAwayTheirTextAndHit()
    {
        var hud = new BattleTabHud(id => "t" + id);
        hud.Build(FourThree, Shown, false, new BattleTabSnapshot(true, false, false), 0, new HudFrame());
        Assert.Equal(BattleTabButton.Battle, hud.ButtonAt(2100, 1500));
        hud.Build(FourThree, Shown, false, new BattleTabSnapshot(false, false, false), 0, new HudFrame());
        Assert.Equal(BattleTabButton.None, hud.ButtonAt(2100, 1500));
        Assert.Equal(HoverTextSnapshot.Empty, hud.HoverText(2100, 1500));
    }

    [Theory]
    [InlineData(1024, 768)]
    [InlineData(1600, 1200)]
    [InlineData(1280, 960)]
    public void AFourThreeScreenKeepsTheReferencePlace(int width, int height)
    {
        HudScreen screen = new HudScaleLayout(width, height, 100).Screen;
        Assert.Equal(2560f, screen.VirtualWidth);
        Assert.Equal(2080f, BattleTabView.MenuLeft(screen));
        var hud = new BattleTabHud(id => "t" + id);
        var frame = new HudFrame();
        hud.Build(screen, Shown, false, new BattleTabSnapshot(true, true, true), 0, frame);
        Assert.Equal(new HudRect(2080, 1460, 128, 128), frame.Draws[0].Rect);
        Assert.Equal(new HudRect(2080 + 136, 1460, 128, 128), frame.Draws[1].Rect);
        Assert.Equal(new HudRect(2080 + 264, 1460, 128, 128), frame.Draws[2].Rect);
    }

    [Theory]
    [InlineData(1920, 1080, 100)]   // 16:9
    [InlineData(2560, 1080, 100)]   // 21:9
    [InlineData(3440, 1440, 100)]   // 21:9
    [InlineData(1280, 1024, 100)]   // 5:4, narrower than the reference
    [InlineData(1920, 1080, 150)]   // a larger HUD scale narrows the logical screen
    public void TheMenuStaysFlushRightAndItsHitsFollowTheArt(int width, int height, int percent)
    {
        HudScreen screen = new HudScaleLayout(width, height, percent).Screen;
        var hud = new BattleTabHud(id => "t" + id);
        var frame = new HudFrame();
        hud.Build(screen, Shown, false, new BattleTabSnapshot(true, true, true), 0, frame);
        float left = BattleTabView.MenuLeft(screen);
        // The 480-wide menu ends on the right edge, as it does in the reference; its widgets keep their local offsets.
        Assert.Equal(screen.VirtualWidth, left + 480, 2);
        Assert.Equal(left, frame.Draws[0].Rect.X);
        Assert.Equal(left + 136, frame.Draws[1].Rect.X);
        Assert.Equal(left + 264, frame.Draws[2].Rect.X);
        Assert.Equal(BattleTabButton.Battle, hud.ButtonAt(left + 1, 1461));
        Assert.Equal(BattleTabButton.Alarm, hud.ButtonAt(left + 264 + 127, 1587));
        Assert.Equal(new HoverTextSnapshot("t2166", "t2165", default), hud.HoverText(left + 64, 1500));
        if (left > 2080)
        {
            Assert.Equal(BattleTabButton.None, hud.ButtonAt(2100, 1500));
        }
    }

    [Fact]
    public void ItPreloadsEveryArtTheMenuDraws() =>
        Assert.Equal(BattleTabView.AllArt, BattleTabHud.Images().Select(image => image.Path));
}
