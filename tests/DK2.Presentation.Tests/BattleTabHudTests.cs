// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation.Tests;

/// <summary>Menu 43 as a draw list (<see cref="BattleTabHud"/>), as Unity's <c>ClassicBattleTab</c> shows it.</summary>
public sealed class BattleTabHudTests
{
    private static readonly HudVisibility Shown = new(true, true);

    [Fact]
    public void OnlyTheShownButtonsDrawAndAHiddenOneLeavesItsGap()
    {
        var hud = new BattleTabHud(id => "t" + id);
        var frame = new HudFrame();
        hud.Build(Shown, false, new BattleTabSnapshot(true, false, true), 150 * 6, frame);
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
        hud.Build(new HudVisibility(false, true), false, new BattleTabSnapshot(true, true, true), 0, frame);
        Assert.Equal(0, frame.Count);
        Assert.Equal(BattleTabButton.None, hud.ButtonAt(2100, 1500));
        Assert.Equal(HoverTextSnapshot.Empty, hud.HoverText(2100, 1500));
    }

    [Fact]
    public void TheCollapsedPanelDropsTheHitAreas()
    {
        var hud = new BattleTabHud(id => "t" + id);
        hud.Build(Shown, true, new BattleTabSnapshot(true, false, false), 0, new HudFrame());
        Assert.Equal(BattleTabButton.None, hud.ButtonAt(2100, 1500));
        Assert.Equal(BattleTabButton.Battle, hud.ButtonAt(2100, 1800));
    }

    [Fact]
    public void PressesNeedInputAndOnlyLeftAndRightAreRaised()
    {
        var hud = new BattleTabHud(id => "t" + id);
        var raised = new List<(BattleTabButton, int)>();
        hud.Clicked += (button, mouse) => raised.Add((button, mouse));
        hud.Build(new HudVisibility(true, false), false, new BattleTabSnapshot(false, true, false), 0, new HudFrame());
        // Shown but not taking input: hovered, not hit for the world, and the press passes through.
        Assert.Equal(BattleTabButton.CallToArms, hud.ButtonAt(2220, 1500));
        Assert.False(hud.ContainsPointer(2220, 1500));
        Assert.False(hud.PointerDown(2220, 1500, 0));
        hud.Build(Shown, false, new BattleTabSnapshot(false, true, false), 0, new HudFrame());
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
        hud.Build(Shown, false, new BattleTabSnapshot(true, true, true), 0, new HudFrame());
        Assert.Equal(new HoverTextSnapshot("t2166", "t2165", default), hud.HoverText(2100, 1500));
        Assert.Equal(HoverTextSnapshot.Empty, hud.HoverText(2220, 1500));
        Assert.Equal(HoverTextSnapshot.Empty, hud.HoverText(2350, 1500));
    }

    [Fact]
    public void ARebuildThatHidesTheBladesTakesAwayTheirTextAndHit()
    {
        var hud = new BattleTabHud(id => "t" + id);
        hud.Build(Shown, false, new BattleTabSnapshot(true, false, false), 0, new HudFrame());
        Assert.Equal(BattleTabButton.Battle, hud.ButtonAt(2100, 1500));
        hud.Build(Shown, false, new BattleTabSnapshot(false, false, false), 0, new HudFrame());
        Assert.Equal(BattleTabButton.None, hud.ButtonAt(2100, 1500));
        Assert.Equal(HoverTextSnapshot.Empty, hud.HoverText(2100, 1500));
    }

    [Fact]
    public void ItPreloadsEveryArtTheMenuDraws() =>
        Assert.Equal(BattleTabView.AllArt, BattleTabHud.Images().Select(image => image.Path));
}
