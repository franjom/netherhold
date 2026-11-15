// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>The engine-free creature overview (ADR-0805).</summary>
public sealed class CreatureOverviewHudTests
{
    private static readonly HudScreen FullHd = new(1920, 1080);
    private static readonly HudVisibility Shown = new(ShowNormalHud: true, AllowOrdinaryInput: true);

    private static (CreatureOverviewHud Hud, CreatureOverviewType Goblin) Overview()
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        WorldState world = WorldState.FromMap(db, GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "Level1")!,
            [PlayerIds.FirstKeeper], fogOfWarEnabled: false);
        CreatureDef goblin = db.CreatureByName("Goblin")!;
        world.SpawnCreature(db, goblin.CreatureId, PlayerIds.FirstKeeper, new(8, 8));
        world.SpawnCreature(db, goblin.CreatureId, PlayerIds.FirstKeeper, new(9, 8));
        var view = new CreatureOverview(db, PlayerIds.FirstKeeper, (_, fallback) => fallback);
        view.Capture(world);
        return (new CreatureOverviewHud(type => "portrait " + type.Value, view), new CreatureOverviewType(goblin.CreatureId));
    }

    private static HudDraw[] Build(CreatureOverviewHud hud, (float, float) pointer = default, BottomPanelRect? area = null)
    {
        var frame = new HudFrame();
        hud.Build(FullHd, 1920, Shown, area ?? BottomPanelLayout.Panel, pointer, frame);
        return frame.Draws.ToArray();
    }

    [InstallFact]
    public void TheGoblinsColumnShowsItsPortraitAndItsCountOfTwo()
    {
        (CreatureOverviewHud hud, CreatureOverviewType goblin) = Overview();
        HudDraw[] draws = Build(hud);
        HudDraw portrait = Assert.Single(draws, d => d.Image.Path == "portrait " + goblin.Value);
        Assert.Equal(912, portrait.Rect.X);
        // Total mode: one count, centred on the column's count widget, at 1709.
        HudDraw two = Assert.Single(draws, d => d.Image.Path == "GUI\\Icons\\i-2");
        Assert.Equal(new HudRect(924 + 54 - 20, 1709, 48, 48), two.Rect);
        Assert.Equal("GUI\\Tabs\\t-cp-total", draws[^1].Image.Path);
    }

    [InstallFact]
    public void HoveringAColumnLightsItsCountsAndNamesItsType()
    {
        (CreatureOverviewHud hud, CreatureOverviewType goblin) = Overview();
        HudDraw[] draws = Build(hud, (930, 1700));
        HudDraw lit = Assert.Single(draws, d => d.Image.Path == "GUI\\Tabs\\t-cp-hilight" && d.Rect.X == 924);
        Assert.Equal(new HudColor(201, 255, 0), lit.Tint);
        Assert.Equal(goblin, hud.HoveredColumnType(930, 1700));
        Assert.Null(hud.HoveredColumnType(930, 1590));
    }

    [InstallFact]
    public void AModeButtonSwitchesTheCountsAndAColumnPressAsksForThatRowsCreature()
    {
        (CreatureOverviewHud hud, CreatureOverviewType goblin) = Overview();
        Build(hud);
        Assert.False(hud.TryPress(700, 1616 + 64 + 10, true, false, false, false, out _));
        Assert.Equal(CreatureOverviewMode.Jobs, hud.Mode);
        HudDraw[] draws = Build(hud);
        Assert.Equal("GUI\\Tabs\\t-cp-jobs", draws[^1].Image.Path);
        Assert.Equal(new HudColor(20, 255, 10), Assert.Single(draws, d => d.Rect == new HudRect(692, 1680, 44, 52)).Tint);
        Assert.Equal(2316 + 2, hud.HoveredTextId(700, 1690));

        // The third jobs row (64 apart from 1616), focused with the secondary button.
        Assert.True(hud.TryPress(930, 1616 + 2 * 64 + 5, false, true, false, false, out CreatureOverviewRequest request));
        Assert.Equal(new CreatureOverviewRequest(goblin, CreatureOverviewSelection.FocusNext,
            CreatureOverview.RowFilter(CreatureOverviewMode.Jobs, 2)), request);
    }

    [InstallFact]
    public void UnderSixHundredFortyPixelsTheCountsUseTheLowResDigitsAndOnePageGreysTheStrips()
    {
        (CreatureOverviewHud hud, _) = Overview();
        var frame = new HudFrame();
        hud.Build(FullHd, 600, Shown, BottomPanelLayout.Panel, default, frame);
        HudDraw[] draws = frame.Draws.ToArray();
        Assert.Contains(draws, d => d.Image.Path == "GUI\\Icons\\lo-2");
        Assert.DoesNotContain(draws, d => d.Image.Path?.StartsWith("GUI\\Icons\\i-") == true);
        Assert.Equal(HudColor.Grey(100), Assert.Single(draws, d => d.Image.Path == "GUI\\Pages\\page_forward").Tint);
    }

    [InstallFact]
    public void InFightingModeTheHoveredRowIsTheOneUnderThePointerAt84UnitSteps()
    {
        (CreatureOverviewHud hud, _) = Overview();
        Build(hud);
        hud.TryPress(700, 1616 + 2 * 64 + 10, true, false, false, false, out _);
        Assert.Equal(CreatureOverviewMode.Fighting, hud.Mode);
        HudDraw lit = Assert.Single(Build(hud, (930, 1616 + 84 + 5)), d => d.Image.Path == "GUI\\Tabs\\t-cp-hilight" && d.Rect.X == 924);
        Assert.Equal(new HudRect(924, 1616 + 84, 108, 84), lit.Rect);
    }

    [InstallFact]
    public void ThePanelMovesWithItsPlaceAndHidesWhenClosed()
    {
        (CreatureOverviewHud hud, CreatureOverviewType goblin) = Overview();
        HudDraw[] draws = Build(hud, area: new BottomPanelRect(472 + 900, 1600, 900, 300));
        Assert.Equal(912 + 900, Assert.Single(draws, d => d.Image.Path == "portrait " + goblin.Value).Rect.X);
        Assert.True(hud.ContainsPointer(1400, 1700));
        Assert.False(hud.ContainsPointer(500, 1700));

        var frame = new HudFrame();
        hud.Build(FullHd, 1920, Shown, null, default, frame);
        Assert.Equal(0, frame.Count);
        Assert.False(hud.ContainsPointer(1400, 1700));
        Assert.Equal(0, hud.HoveredTextId(700, 1690));
    }
}
