// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation.Tests;

public sealed class HudWidgetTextTests
{
    [Theory]
    [InlineData(HudWidgetText.QueryCorner, 2795)]
    [InlineData(HudWidgetText.SellCorner, 2152)]
    [InlineData(HudWidgetText.ZoomCorner, 2146)]
    [InlineData(HudWidgetText.OptionsCorner, 2148)]
    [InlineData(2153, 2154)]
    [InlineData(2159, 2160)]
    [InlineData(2308, 2309)]
    [InlineData(2322, 2323)]
    [InlineData(0x928, 0x929)]
    [InlineData(2309, 0)]
    [InlineData(0x92A, 0)]
    [InlineData(1, 0)]
    public void TheHoverStatusSwitchMapsQueryToItsCreatureLineAndTheRestToTheNextString(int textId, int status) =>
        Assert.Equal(status, HudWidgetText.StatusTextId(textId));

    [Fact]
    public void AWidgetHoverNamesTheWidgetAndPutsItsStatusLineInTheStrip_PopupProducerUnread()
    {
        HoverTextSnapshot hover = HudWidgetText.Hover(HudWidgetText.Tab(BottomPanelCategory.Rooms), id => "s" + id);
        Assert.Equal("s2155", hover.TooltipName);
        Assert.Equal("s2156", hover.TopStatus);
        Assert.Equal(new WorldHoverHit(WorldHoverKind.Widget, default, 2155), hover.Hit);
    }
}
