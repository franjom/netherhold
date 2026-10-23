// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation.Tests;

/// <summary>Tab pinning (0x00416C20), panel placement (0x00417310) and the category grid (0x00417470);
/// docs/research/tab-alerts.md section 3.</summary>
public sealed class BottomPanelTabsTests
{
    private const BottomPanelCategory Creatures = BottomPanelCategory.Creatures, Rooms = BottomPanelCategory.Rooms,
        Spells = BottomPanelCategory.Spells, Workshop = BottomPanelCategory.Workshop;

    private static BottomPanelOpenSet Set(params BottomPanelCategory[] categories)
    {
        BottomPanelOpenSet set = BottomPanelOpenSet.None;
        foreach (BottomPanelCategory category in categories) set = set.With(category);
        return set;
    }

    [Fact]
    public void AnOrdinaryClickClosesTheOthersAndOpensTheClickedTab()
    {
        var tabs = new BottomPanelTabs();
        tabs.Click(Rooms, shiftOnly: false);
        tabs.Click(Spells, shiftOnly: false);
        Assert.Equal(Set(Spells), tabs.Open);
        Assert.Null(tabs.Pinned);
    }

    [Fact]
    public void ShiftPinsATabBesideTheOpenOneAndAnOrdinaryClickKeepsThePin()
    {
        var tabs = new BottomPanelTabs();
        tabs.Click(Rooms, false);
        tabs.Click(Spells, shiftOnly: true);
        Assert.Equal(Spells, tabs.Pinned);
        Assert.Equal(Set(Rooms, Spells), tabs.Open);
        // The unpinned panel is replaced; the pinned one stays.
        tabs.Click(Workshop, false);
        Assert.Equal(Set(Workshop, Spells), tabs.Open);
        // Clicking the pinned tab closes the unpinned one and leaves it alone on the strip.
        tabs.Click(Spells, false);
        Assert.Equal(Set(Spells), tabs.Open);
        Assert.Equal(Spells, tabs.Pinned);
    }

    [Fact]
    public void AtMostOneTabIsPinned_ShiftOnAnotherTabDoesNothing()
    {
        var tabs = new BottomPanelTabs();
        tabs.Click(Rooms, false);
        tabs.Click(Spells, true);
        tabs.Click(Workshop, true);
        Assert.Equal(Spells, tabs.Pinned);
        Assert.Equal(Set(Rooms, Spells), tabs.Open);
    }

    [Fact]
    public void UnpinningClosesThePanelOnlyWhenAnotherIsOpen()
    {
        var tabs = new BottomPanelTabs();
        tabs.Click(Rooms, false);
        tabs.Click(Spells, true);
        tabs.Click(Spells, true);
        Assert.Null(tabs.Pinned);
        Assert.Equal(Set(Rooms), tabs.Open);

        tabs.Click(Rooms, true);
        tabs.Click(Rooms, true);
        Assert.Null(tabs.Pinned);
        Assert.Equal(Set(Rooms), tabs.Open);
    }

    [Fact]
    public void AClickWhileCollapsedExpandsFirstAndTheOpenSetSurvivesTheCollapse()
    {
        var tabs = new BottomPanelTabs();
        tabs.Click(Rooms, false);
        tabs.Click(Creatures, true);
        tabs.Collapsed = true;
        Assert.Equal(BottomPanelOpenSet.None, tabs.Open);
        Assert.True(tabs.Click(Spells, false));
        Assert.False(tabs.Collapsed);
        Assert.Equal(Set(Spells, Creatures), tabs.Open);
        Assert.False(tabs.Click(Spells, false));
    }

    [Fact]
    public void OpenPanelsShareTheSpanWithThePinnedOneOnTheRight()
    {
        Assert.Equal(new BottomPanelRect(472, 1600, 1800, 300), BottomPanelLayout.PanelOf(Rooms, Set(Rooms), null));
        Assert.Equal(new BottomPanelRect(472, 1600, 1800, 300), BottomPanelLayout.PanelOf(Spells, Set(Spells), Spells));
        Assert.Equal(new BottomPanelRect(472, 1600, 900, 300), BottomPanelLayout.PanelOf(Rooms, Set(Rooms, Spells), Spells));
        Assert.Equal(new BottomPanelRect(1372, 1600, 900, 300), BottomPanelLayout.PanelOf(Spells, Set(Rooms, Spells), Spells));
        Assert.Null(BottomPanelLayout.PanelOf(Workshop, Set(Rooms, Spells), Spells));
    }

    [Theory]
    // One full panel: 264-unit cells up to 6 entries (6 a page), then 13 x 2 128-unit cells.
    [InlineData(1800, 0, 264, 6, 1)]
    [InlineData(1800, 6, 264, 6, 1)]
    [InlineData(1800, 7, 128, 13, 2)]
    // Half a panel: 264-unit cells only up to 2 entries (3 a page), else 6 x 2.
    [InlineData(900, 2, 264, 3, 1)]
    [InlineData(900, 3, 128, 6, 2)]
    public void TheGridUsesLargeCellsWhileTheEntriesFit(int width, int count, int cell, int columns, int rows) =>
        Assert.Equal(new BottomPanelGrid(cell, columns, rows), BottomPanelLayout.Grid(width, count));

    [Fact]
    public void SmallCellsFillColumnByColumnAndCarryTheSmallSparkle()
    {
        BottomPanelGrid grid = BottomPanelLayout.Grid(1800, 20);
        Assert.Equal(26, grid.PageSize);
        Assert.Equal(5, grid.WidgetBank);
        Assert.Equal(6, BottomPanelLayout.Grid(1800, 3).WidgetBank);
        var panel = new BottomPanelRect(1372, 1600, 900, 300);
        Assert.Equal(new BottomPanelRect(1448, 1612, 128, 128), grid.Slot(panel, 0));
        Assert.Equal(new BottomPanelRect(1448, 1748, 128, 128), grid.Slot(panel, 1));
        Assert.Equal(new BottomPanelRect(1584, 1612, 128, 128), grid.Slot(panel, 2));
        Assert.Equal(1, grid.Pages(26));
        Assert.Equal(2, grid.Pages(27));
    }

    [Fact]
    public void EachPanelHasItsArrowsAtItsLeftEdgeAndTheOverviewNarrows()
    {
        var panel = new BottomPanelRect(1372, 1600, 900, 300);
        Assert.Equal(new BottomPanelRect(1372, 1612, 64, 132), BottomPanelLayout.PreviousPage(panel));
        Assert.Equal(new BottomPanelRect(1372, 1744, 64, 132), BottomPanelLayout.NextPage(panel));
        Assert.Equal(11, BottomPanelLayout.OverviewColumns(1800));
        Assert.Equal(4, BottomPanelLayout.OverviewColumns(900));
    }
}
