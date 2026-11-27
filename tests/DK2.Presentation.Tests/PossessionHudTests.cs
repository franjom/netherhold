// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Presentation.Tests;

public sealed class PossessionHudTests
{
    private static readonly HudScreen Screen = new(1920, 1080);
    private static PossessionHudSnapshot Snapshot
    {
        get
        {
            return new("portrait", 50, new GoldAmount(1350),
        [new("pick", 100, true), new("boot", 25, false)], ["follower"]);
        }
    }

    [Fact]
    public void PossessionPanelWaitsForLandingAndRespectsTheGuiToggle()
    {
        Assert.False(PossessionHud.ShowPanel(true, new(true, true), firstPersonCameraSettled: false));
        Assert.True(PossessionHud.ShowPanel(true, new(true, true), firstPersonCameraSettled: true));
        Assert.False(PossessionHud.ShowPanel(true, new(false, true), firstPersonCameraSettled: true));
        Assert.False(PossessionHud.ShowPanel(false, new(true, true), firstPersonCameraSettled: true));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LegacyBooleanFactsPreserveEntryVisibilityWithoutInferringAReturnRoute(bool guiVisible)
    {
        var ordinary = new HudVisibility(guiVisible, true);
        Assert.Equal(new HudVisibility(guiVisible, false),
            PossessionHud.OrdinaryVisibility(ordinary, possessionCameraHeld: true, possessing: true, firstPersonCameraSettled: false));
        Assert.Equal(new HudVisibility(false, false),
            PossessionHud.OrdinaryVisibility(ordinary, possessionCameraHeld: true, possessing: true, firstPersonCameraSettled: true));
        Assert.Equal(new HudVisibility(false, false),
            PossessionHud.OrdinaryVisibility(ordinary, possessionCameraHeld: true, possessing: false, firstPersonCameraSettled: false));
        Assert.Equal(ordinary,
            PossessionHud.OrdinaryVisibility(ordinary, possessionCameraHeld: false, possessing: false, firstPersonCameraSettled: false));
    }
    [Fact]
    public void PossessionMinimapUsesTheGuiVisibilityWithoutOrdinaryInput()
    {
        Assert.Equal(new HudVisibility(true, false), PossessionHud.MinimapVisibility(new(true, true), true));
        Assert.Equal(new HudVisibility(false, false), PossessionHud.MinimapVisibility(new(false, true), true));
        Assert.Equal(new HudVisibility(true, true), PossessionHud.MinimapVisibility(new(true, true), false));
        Assert.Equal(new HudVisibility(false, false), PossessionHud.MinimapVisibility(new(true, true), false, possessionCameraHeld: true));
    }

    [Theory]
    [InlineData(false, false, "throw-mode", false)]
    [InlineData(false, true, "throw_book-mode", true)]
    [InlineData(true, false, "throw_imp-mode", true)]
    [InlineData(true, true, "throw_imp-mode", true)]
    public void ThrowArtUsesTheIndependentPossessionCarryAndBookFacts(bool holding, bool holdingBook, string suffix, bool backing)
    {
        PossessionHudIcon icon = PossessionHudView.ThrowIcon(holding ? PossessionCarryState.HoldingThing : PossessionCarryState.Empty, holdingBook);
        Assert.Equal("GUI\\Icons\\1st-person\\" + suffix, icon.Image);
        Assert.Equal(backing, icon.Selected);
    }

    [Fact]
    public void NoPossessedCreatureDrawsNothing()
    {
        var frame = new HudFrame();
        new PossessionHud().Build(Screen, null, frame);
        Assert.Empty(frame.Draws.ToArray());
    }

    [Fact]
    public void HealthUsesTheHealthFractionAndCropsItsTexture()
    {
        var frame = new HudFrame();
        new PossessionHud().Build(Screen, Snapshot, frame);
        HudDraw health = Assert.Single(frame.Draws.ToArray(), d => d.Image.Path == PossessionHud.HealthFill);
        Assert.Equal(0.5f, health.Uv!.Value.Width);
        float sx = Math.Min(1, Screen.VirtualWidth / 2560);
        Assert.Equal((292 * sx - (140 - 128 * sx)) / 2, health.Rect.Width, 3);
        HudDraw[] draws = frame.Draws.ToArray();
        Assert.True(Array.FindIndex(draws, d => d.Image.Path == PossessionHud.HealthFill)
            < Array.FindIndex(draws, d => d.Image.Path == "GUI\\Map\\m-h-tone-bg"));
    }

    [Fact]
    public void SelectionAndRechargeUseTheOriginalArtAndTheReservedSlotsKeepTheirPlaces()
    {
        var frame = new HudFrame();
        new PossessionHud().Build(Screen, Snapshot, frame);
        HudDraw[] draws = frame.Draws.ToArray();
        Assert.Contains(draws, d => d.Image.Path == "GUI\\icons\\selected-creature");
        Assert.Contains(draws, d => d.Image.Path == "boot" && d.Kind == HudDrawKind.Sector && d.Percent == 25);
        Assert.Contains(draws, d => d.Image.Path == "follower" && d.Rect.Y == 1636);
        Assert.All(draws, d => Assert.True(d.Rect.X >= 0 && d.Rect.X + d.Rect.Width <= Screen.VirtualWidth));
    }

    [Fact]
    public void PossessionBlocksOrdinaryPanelsAndTheirInputButLeavingRestoresThePriorGates()
    {
        var normal = new HudVisibility(true, true);
        Assert.Equal(new HudVisibility(false, false), PossessionHud.OrdinaryVisibility(normal, true));
        Assert.Equal(normal, PossessionHud.OrdinaryVisibility(normal, false));
        Assert.Equal(new HudVisibility(false, false), PossessionHud.OrdinaryVisibility(new(false, false), false));
    }

    [Fact]
    public void FiltersCoverTheViewportBehindTheTopPanelAndFollowers()
    {
        var frame = new HudFrame();
        new PossessionHud().Build(Screen, Snapshot with { Overlay = "GUI\\Filters\\F-Frozen" }, frame);
        HudDraw[] draws = frame.Draws.ToArray();
        int overlay = Array.FindIndex(draws, d => d.Image.Path == "GUI\\Filters\\F-Frozen");
        Assert.Equal(new HudRect(0, 0, Screen.VirtualWidth, 1920), draws[overlay].Rect);
        Assert.True(Array.FindIndex(draws, d => d.Image.Path == "portrait") > overlay);
        Assert.True(Array.FindIndex(draws, d => d.Image.Path == "follower") > overlay);
    }

    [Fact]
    public void GroupSelectionUsesTheGroupBacking()
    {
        var frame = new HudFrame();
        new PossessionHud().Build(Screen, Snapshot with { Actions = [new("GUI\\Icons\\1st-person\\group-mode", 100, true)] }, frame);
        Assert.Contains(frame.Draws.ToArray(), d => d.Image.Path == "GUI\\icons\\selected_group");
        Assert.DoesNotContain(frame.Draws.ToArray(), d => d.Image.Path == "GUI\\icons\\selected-creature");
    }

    [Fact]
    public void ScaleKeepsTheBottomBarAndFiltersWithinTheVisibleSurface()
    {
        var frame = new HudFrame();
        var layout = new HudScaleLayout(1920, 1080, 150);
        new PossessionHud().Build(layout, Snapshot with { Overlay = "GUI\\Filters\\F-Frozen" }, frame);
        float height = 1920 / layout.Factor;
        Assert.All(frame.Draws.ToArray().Where(d => d.Image.Path != "follower"),
            d => Assert.True(d.Rect.Y >= 0 && d.Rect.Y + d.Rect.Height <= height));
        Assert.All(frame.Draws.ToArray().Where(d => d.Image.Path == "GUI\\Windows\\Bar-00" && d.Rect.Y != 20),
            d => Assert.Equal(layout.Screen.VirtualWidth, d.Rect.Width));
        Assert.Equal(height, frame.Draws.ToArray().Single(d => d.Image.Path == "GUI\\Filters\\F-Frozen").Rect.Height);
    }

    [Fact]
    public void FiltersComposeInNativeOrder()
    {
        var frame = new HudFrame();
        new PossessionHud().Build(Screen, Snapshot with { Overlays = ["sniper", "creature", "frozen"] }, frame);
        Assert.Equal(new[] { "sniper", "creature", "frozen" }, frame.Draws.ToArray()
            .Where(d => new[] { "sniper", "creature", "frozen" }.Contains(d.Image.Path)).Select(d => d.Image.Path));
    }

    [Fact]
    public void SeparateFilterAndChromeFramesAllowTheMinimapBetweenThem()
    {
        var filters = new HudFrame();
        var creature = new HudFrame();
        var chrome = new HudFrame();
        var layout = new HudScaleLayout(1920, 1080, 100);
        var hud = new PossessionHud();
        PossessionHudSnapshot facts = Snapshot with { Overlays = ["sniper", "creature", "frozen"] };
        hud.BuildFilters(layout, facts, filters);
        hud.BuildCreaturePanel(layout, facts, creature);
        hud.BuildChrome(layout, facts, chrome);
        Assert.Equal(3, filters.Draws.ToArray().Count(d => facts.Overlays.Contains(d.Image.Path)));
        Assert.DoesNotContain(chrome.Draws.ToArray(), d => facts.Overlays.Contains(d.Image.Path));
        Assert.DoesNotContain(chrome.Draws.ToArray(), d => d.Image.Path == "portrait");
        Assert.Contains(creature.Draws.ToArray(), d => d.Image.Path == "portrait");
    }

    [Fact]
    public void WidescreenContentKeepsTheNativeFourByThreeProportionsAndGroupAnchor()
    {
        var layout = new HudScaleLayout(1920, 1080, 100);
        var top = new HudFrame();
        var lower = new HudFrame();
        var hud = new PossessionHud();
        PossessionHudSnapshot facts = Snapshot with { Actions = [new("pick", 100, true), new("GUI\\Icons\\1st-person\\group-mode", 100, true)] };
        hud.BuildCreaturePanel(layout, facts, top);
        hud.BuildChrome(layout, facts, lower);
        HudDraw portrait = Assert.Single(top.Draws.ToArray(), d => d.Image.Path == "portrait");
        Assert.Equal(292.5f, portrait.Rect.X * layout.Screen.Scale, 3);
        Assert.Equal(72, portrait.Rect.Width * layout.Screen.Scale, 3);
        Assert.Equal(72, portrait.Rect.Height * layout.Screen.Scale, 3);
        Assert.DoesNotContain(top.Draws.ToArray(), d => d.Image.Path!.EndsWith("group-mode"));
        HudDraw group = Assert.Single(lower.Draws.ToArray(), d => d.Image.Path == "GUI\\Icons\\1st-person\\group-mode");
        Assert.Equal(270, group.Rect.X * layout.Screen.Scale, 3);
        Assert.Equal(1716, group.Rect.Y);
        Assert.Equal(184, group.Rect.Height);
        Assert.Contains(lower.Draws.ToArray(), d => d.Image.Path == "GUI\\icons\\selected_group" && d.Rect == group.Rect);
        Assert.Contains(top.Draws.ToArray(), d => d.Image.Path == PossessionHud.GoldIcon);
        Assert.DoesNotContain(lower.Draws.ToArray(), d => d.Image.Path == "GUI\\Windows\\Bar-00");
    }

    [Fact]
    public void AbilityRolesKeepTheirLowerColumnSlotsWhileStaffAndSpellsStayOnTop()
    {
        var hud = new PossessionHud();
        var top = new HudFrame();
        var lower = new HudFrame();
        var layout = new HudScaleLayout(1600, 1200, 100);
        PossessionHudSnapshot facts = Snapshot with { Actions = [new("staff", 100, false), new("fire", 100, false, true),
            new("bat", 100, true) { Role = PossessionHudIconRole.PrimaryAbility },
            new("hypnotise", 100, false) { Role = PossessionHudIconRole.SecondaryAbility },
            new("group", 100, true) { Role = PossessionHudIconRole.Group }] };
        hud.BuildCreaturePanel(layout, facts, top);
        hud.BuildChrome(layout, facts, lower);
        Assert.DoesNotContain(top.Draws.ToArray(), d => d.Image.Path is "bat" or "hypnotise" or "group");
        Assert.Contains(top.Draws.ToArray(), d => d.Image.Path == "staff" && d.Rect == new HudRect(1412, 8, 128, 128));
        Assert.Contains(top.Draws.ToArray(), d => d.Image.Path == "fire" && d.Rect.Y == 8);
        Assert.Contains(lower.Draws.ToArray(), d => d.Image.Path == "bat" && d.Rect == new HudRect(480, 1460, 128, 128));
        Assert.Contains(lower.Draws.ToArray(), d => d.Image.Path == "hypnotise" && d.Rect == new HudRect(480, 1588, 128, 128));
        Assert.Contains(lower.Draws.ToArray(), d => d.Image.Path == "group" && d.Rect == new HudRect(480, 1716, 128, 184));
        facts = facts with { Actions = [facts.Actions[^1]] };
        lower.Clear();
        hud.BuildChrome(layout, facts, lower);
        Assert.Contains(lower.Draws.ToArray(), d => d.Image.Path == "group" && d.Rect.Y == 1716);
    }

    [Fact]
    public void TopBarRepeatsAtItsNativeHeightAndFollowersKeepTheReadAxisArithmetic()
    {
        var frame = new HudFrame();
        var layout = new HudScaleLayout(1920, 1080, 100);
        new PossessionHud().Build(layout, Snapshot with { Followers = ["follower", "second"] }, frame);
        HudDraw[] bars = [.. frame.Draws.ToArray().Where(d => d.Image.Path == "GUI\\Windows\\Bar-00" && d.Rect.Y == 20)];
        Assert.True(bars.Length > 1);
        Assert.All(bars, d => Assert.Equal(88, d.Rect.Height));
        Assert.Equal(512, bars[0].Rect.Width, 3);
        HudDraw follower = Assert.Single(frame.Draws.ToArray(), d => d.Image.Path == "follower");
        HudDraw second = Assert.Single(frame.Draws.ToArray(), d => d.Image.Path == "second");
        Assert.Equal(628, follower.Rect.X, 3);
        Assert.Equal(1636, follower.Rect.Y, 3);
        Assert.Equal(128, follower.Rect.Width, 3);
        Assert.Equal(264, follower.Rect.Height);
        Assert.Equal(136, second.Rect.X - follower.Rect.X, 3);
    }

    [Fact]
    public void SevenFollowersUseTheReadFourByThreeRowSeenInTheNativeMistressCapture()
    {
        var frame = new HudFrame();
        string[] followers = [.. Enumerable.Range(0, 7).Select(i => "follower-" + i)];
        new PossessionHud().Build(new HudScaleLayout(1600, 1200, 100), Snapshot with { Followers = followers }, frame);
        for (int i = 0; i < followers.Length; i++)
        {
            HudDraw portrait = Assert.Single(frame.Draws.ToArray(), d => d.Image.Path == followers[i]);
            Assert.Equal(new HudRect(628 + i * 136, 1636, 128, 264), portrait.Rect);
        }
    }

    [Fact]
    public void FirstTopActionAddsTheVerticalGoldWidthAndInsetToTheHorizontallyScaledGoldX()
    {
        var frame = new HudFrame();
        new PossessionHud().BuildCreaturePanel(new HudScaleLayout(1200, 1200, 100),
            Snapshot with { Actions = [new("staff", 100, false)] }, frame);
        Assert.Equal(956 * 0.75f + 496 - 40,
            Assert.Single(frame.Draws.ToArray(), d => d.Image.Path == "staff").Rect.X);
    }

    [Fact]
    public void SelectionArtOverlaysTheOpaqueIconAndRechargeSector()
    {
        var frame = new HudFrame();
        new PossessionHud().Build(new HudScreen(1600, 1200), Snapshot with { Actions = [new("staff", 50, true)] }, frame);
        HudDraw[] draws = frame.Draws.ToArray();
        int overlay = Array.FindIndex(draws, d => d.Image.Path == "GUI\\icons\\selected-creature");
        Assert.True(overlay > Array.FindLastIndex(draws, d => d.Image.Path == "staff"));
    }

    [Theory]
    [InlineData(1600, 1200, 100)]
    [InlineData(1920, 1080, 100)]
    [InlineData(1920, 1080, 150)]
    public void PresentationPlaneKeepsHealthInsideItsFrameAndLowerControlsOnScreen(int width, int height, int percent)
    {
        var layout = new HudScaleLayout(width, height, percent);
        var frame = new HudFrame();
        new PossessionHud().Build(layout, Snapshot with { HealthPercent = 100,
            Actions = [new("bat", 100, false) { Role = PossessionHudIconRole.PrimaryAbility },
                new("hypnotise", 100, false) { Role = PossessionHudIconRole.SecondaryAbility },
                new("group", 100, false) { Role = PossessionHudIconRole.Group }] }, frame);
        HudDraw[] draws = frame.Draws.ToArray();
        HudRect border = Assert.Single(draws, d => d.Image.Path == "GUI\\Map\\m-hp_bg").Rect;
        HudRect fill = Assert.Single(draws, d => d.Image.Path == PossessionHud.HealthFill).Rect;
        Assert.True(fill.X >= border.X && fill.Y >= border.Y);
        Assert.True(fill.X + fill.Width <= border.X + border.Width);
        Assert.True(fill.Y + fill.Height <= border.Y + border.Height);
        Assert.All(draws.Where(d => d.Image.Path is "bat" or "hypnotise" or "group" or "follower"),
            d => Assert.InRange(d.Rect.Y + d.Rect.Height, 0, 1920 / layout.Factor));
    }

    [Fact]
    public void RawNativeAxisMetricRemainsAvailableWithoutBecomingTheWidescreenPresentationPolicy()
    {
        Assert.Equal(4 / 3f, PossessionHud.NativeXScale(new HudScaleLayout(1920, 1080, 100)), 3);
    }
}
