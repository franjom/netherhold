// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation.Tests;

public sealed class HudPointerMapTests
{
    /// <summary>The panels under the pointer, and a record of what was asked, in order.</summary>
    private readonly struct Panels(HudPointerPanel? under, List<HudPointerPanel> asked) : IHudPointerPanels
    {
        public bool Contains(HudPointerPanel panel)
        {
            asked.Add(panel);
            return panel == under;
        }
    }

    /// <summary>The pointer over no panel, recording nothing, so a call allocates only what the map does.</summary>
    private readonly struct NoPanels : IHudPointerPanels
    {
        public bool Contains(HudPointerPanel panel) => false;
    }

    [Fact]
    public void EveryPanelButTheQueryCountsWhateverTheGates()
    {
        for (HudPointerPanel panel = HudPointerPanel.Minimap; panel < HudPointerPanel.CreatureQuery; panel++)
            Assert.True(HudPointerMap.Over(new Panels(panel, []), fullMapVisible: false, queryHasTarget: false, modal: true));
        Assert.False(HudPointerMap.Over(new Panels(null, []), fullMapVisible: false, queryHasTarget: true, modal: false));
    }

    [Fact]
    public void TheOpenFullMapHoldsTheWholeScreen() =>
        Assert.True(HudPointerMap.Over(new Panels(null, []), fullMapVisible: true, queryHasTarget: false, modal: true));

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, false, false)]   // nothing queried: the details are not shown
    [InlineData(true, true, false)]     // a modal menu covers them
    public void TheQueryDetailsCountOnlyWhileACreatureIsQueriedAndNoMenuIsModal(bool target, bool modal, bool over)
    {
        var asked = new List<HudPointerPanel>();
        Assert.Equal(over, HudPointerMap.Over(new Panels(HudPointerPanel.CreatureQuery, asked), fullMapVisible: false,
            queryHasTarget: target, modal: modal));
        Assert.Equal(target && !modal, asked.Contains(HudPointerPanel.CreatureQuery));
    }

    [Fact]
    public void PanelsAreAskedInUnitysRunnerOrderAndTheFirstHitStops()
    {
        // DungeonRunner.PointerOverHud's order before the move.
        HudPointerPanel[] unity =
        [
            HudPointerPanel.Minimap, HudPointerPanel.HoverHud, HudPointerPanel.ResourceStrip, HudPointerPanel.GameOptions,
            HudPointerPanel.BottomPanel, HudPointerPanel.MessageTabs, HudPointerPanel.BattleTab, HudPointerPanel.CreatureOverview,
            HudPointerPanel.OptionsMenu, HudPointerPanel.CreatureQuery,
        ];
        var asked = new List<HudPointerPanel>();
        HudPointerMap.Over(new Panels(null, asked), fullMapVisible: false, queryHasTarget: true, modal: false);
        Assert.Equal(unity, asked);

        asked.Clear();
        HudPointerMap.Over(new Panels(HudPointerPanel.GameOptions, asked), fullMapVisible: false, queryHasTarget: true, modal: false);
        Assert.Equal(unity[..4], asked);
    }

    [Fact]
    public void AQueryAllocatesNothing()
    {
        HudPointerMap.Over(new NoPanels(), fullMapVisible: false, queryHasTarget: true, modal: false);   // warmed
        long before = GC.GetAllocatedBytesForCurrentThread();
        bool over = HudPointerMap.Over(new NoPanels(), fullMapVisible: false, queryHasTarget: true, modal: false);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.False(over);
        Assert.Equal(0, allocated);
    }
}
