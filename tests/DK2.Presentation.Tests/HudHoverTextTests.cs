// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Presentation.Tests;

public sealed class HudHoverTextTests
{
    private sealed class Sources : IHudHoverSources
    {
        public int Widget;
        public CreatureOverviewType? Column;
        public bool Tabs, Battle, Panel;
        public ClassicHudAction Corner;
        public KeeperResourceKind Kind;
        public int TypeTexts, Asked;
        public readonly HoverTextSnapshot TabsText = new("tabs", "", default);
        public readonly HoverTextSnapshot BattleText = new("battle status", "battle", default);
        public readonly HoverTextSnapshot PanelText = new("panel", "", default);

        public int OverviewTextId() { Asked++; return Widget; }
        public CreatureOverviewType? OverviewColumn() { Asked++; return Column; }
        public HoverTextSnapshot CreatureTypeText(CreatureOverviewType column)
        {
            TypeTexts++;
            return new HoverTextSnapshot("type " + column.Value, "", default);
        }
        public bool OverMessageTabs() { Asked++; return Tabs; }
        public HoverTextSnapshot MessageTabsText()
        {
            return TabsText;
        }

        public bool OverBattleTab() { Asked++; return Battle; }
        public HoverTextSnapshot BattleTabText()
        {
            return BattleText;
        }

        public bool OverBottomPanel() { Asked++; return Panel; }
        public HoverTextSnapshot BottomPanelText()
        {
            return PanelText;
        }

        public ClassicHudAction MinimapControl() { Asked++; return Corner; }
        public KeeperResourceKind Resource() { Asked++; return Kind; }
    }

    private static readonly KeeperResourceSnapshot Facts = new(new GoldAmount(100), new ManaAmount(50), default, default);

    private int _resolves;
    private string Resolve(uint id) { _resolves++; return "s" + id; }

    [Fact]
    public void TheFirstPanelWithSomethingUnderThePointerWinsInTheRunnersOrder()
    {
        var hover = new HudHoverText(Resolve);
        var all = new Sources
        {
            Widget = 2316, Column = new CreatureOverviewType(3), Tabs = true, Battle = true, Panel = true,
            Corner = ClassicHudAction.SelectQuery, Kind = KeeperResourceKind.Gold,
        };
        Assert.Equal("s2316", hover.Capture(all, Facts).TooltipName);
        all.Widget = 0;
        Assert.Equal("type 3", hover.Capture(all, Facts).TopStatus);
        all.Column = null;
        Assert.Equal(all.TabsText, hover.Capture(all, Facts));
        all.Tabs = false;
        Assert.Equal(all.BattleText, hover.Capture(all, Facts));
        all.Battle = false;
        Assert.Equal(all.PanelText, hover.Capture(all, Facts));
        all.Panel = false;
        Assert.Equal("s" + HudWidgetText.QueryCorner, hover.Capture(all, Facts).TooltipName);
        all.Corner = ClassicHudAction.None;
        Assert.Equal("s2141", hover.Capture(all, Facts).TooltipName);
        all.Kind = KeeperResourceKind.None;
        Assert.Equal(HoverTextSnapshot.Empty, hover.Capture(all, Facts));
    }

    [Fact]
    public void ALaterSourceIsNotAskedOnceAnEarlierOneAnswers()
    {
        var hover = new HudHoverText(Resolve);
        var sources = new Sources { Tabs = true, Panel = true };
        hover.Capture(sources, Facts);
        Assert.Equal(3, sources.Asked);
    }

    [Fact]
    public void AnUnchangedHoverBuildsNoTextAgain()
    {
        var hover = new HudHoverText(Resolve);
        var sources = new Sources { Corner = ClassicHudAction.ZoomIn };
        hover.Capture(sources, Facts);
        int afterCorner = _resolves;
        hover.Capture(sources, Facts);
        Assert.Equal(afterCorner, _resolves);

        sources.Corner = ClassicHudAction.None; sources.Column = new CreatureOverviewType(5);
        hover.Capture(sources, Facts); hover.Capture(sources, Facts);
        Assert.Equal(1, sources.TypeTexts);

        sources.Column = null; sources.Kind = KeeperResourceKind.Mana;
        hover.Capture(sources, Facts);
        int afterMana = _resolves;
        hover.Capture(sources, Facts);
        Assert.Equal(afterMana, _resolves);
        hover.Capture(sources, Facts with { Mana = new ManaAmount(51) });
        Assert.True(_resolves > afterMana);
    }

    [Fact]
    public void TheOverviewWidgetsAndTheMinimapCornersShareOneKeptText()
    {
        var hover = new HudHoverText(Resolve);
        var sources = new Sources { Corner = ClassicHudAction.SelectSell };
        Assert.Equal("s" + HudWidgetText.SellCorner, hover.Capture(sources, Facts).TooltipName);
        sources.Corner = ClassicHudAction.None; sources.Widget = 2308;
        Assert.Equal("s2308", hover.Capture(sources, Facts).TooltipName);
        sources.Widget = 0; sources.Corner = ClassicHudAction.SelectSell;
        Assert.Equal("s" + HudWidgetText.SellCorner, hover.Capture(sources, Facts).TooltipName);
    }
}
