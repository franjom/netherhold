// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.
using DK2.Core;

namespace DK2.Presentation.Tests;

public sealed class PossessionHudTests
{
    private static readonly HudScreen Screen = new(1920, 1080);
    private static PossessionHudSnapshot Snapshot => new("portrait", 50, new GoldAmount(1350),
        [new("pick", 100, true), new("boot", 25, false)], ["follower"]);

    [Theory]
    [InlineData(false, false, "throw-mode", false)]
    [InlineData(false, true, "throw_book-mode", true)]
    [InlineData(true, false, "throw_imp-mode", true)]
    [InlineData(true, true, "throw_imp-mode", true)]
    public void ThrowArtUsesTheIndependentPossessionCarryFact(bool holding, bool selected, string suffix, bool backing)
    {
        PossessionHudIcon icon = PossessionHudView.ThrowIcon(holding ? PossessionCarryState.HoldingThing : PossessionCarryState.Empty, selected);
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
        Assert.Equal(PossessionHud.HealthBounds.Width / 2, health.Rect.Width);
    }

    [Fact]
    public void SelectionAndRechargeUseTheOriginalArtAndTheReservedSlotsKeepTheirPlaces()
    {
        var frame = new HudFrame();
        new PossessionHud().Build(Screen, Snapshot, frame);
        HudDraw[] draws = frame.Draws.ToArray();
        Assert.Contains(draws, d => d.Image.Path == "GUI\\icons\\selected-creature");
        Assert.Contains(draws, d => d.Image.Path == "boot" && d.Kind == HudDrawKind.Sector && d.Percent == 25);
        Assert.Contains(draws, d => d.Image.Path == "follower" && d.Rect.Y == 1460);
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
    public void FiltersCoverTheViewportAfterTopPanelAndBeforeFollowers()
    {
        var frame = new HudFrame();
        new PossessionHud().Build(Screen, Snapshot with { Overlay = "GUI\\Filters\\F-Frozen" }, frame);
        HudDraw[] draws = frame.Draws.ToArray();
        int overlay = Array.FindIndex(draws, d => d.Image.Path == "GUI\\Filters\\F-Frozen");
        Assert.Equal(new HudRect(0, 0, Screen.VirtualWidth, 1920), draws[overlay].Rect);
        Assert.True(Array.FindIndex(draws, d => d.Image.Path == "portrait") < overlay);
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
        Assert.All(frame.Draws.ToArray(), d => Assert.True(d.Rect.Y >= 0 && d.Rect.Y + d.Rect.Height <= height));
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
        var facts = Snapshot with { Overlays = ["sniper", "creature", "frozen"] };
        hud.BuildFilters(layout, facts, filters);
        hud.BuildCreaturePanel(layout, facts, creature);
        hud.BuildChrome(layout, facts, chrome);
        Assert.Equal(3, filters.Draws.Length);
        Assert.DoesNotContain(chrome.Draws.ToArray(), d => facts.Overlays.Contains(d.Image.Path));
        Assert.DoesNotContain(chrome.Draws.ToArray(), d => d.Image.Path == "portrait");
        Assert.Contains(creature.Draws.ToArray(), d => d.Image.Path == "portrait");
    }
}
