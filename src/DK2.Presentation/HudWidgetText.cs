// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation;

/// <summary>
/// A HUD widget's hover text. The static widget record's text id (<c>+0x30</c>, copied to runtime
/// <c>+0x6F</c>) is CONFIRMED. The status-strip line comes from the hover status switch
/// <c>0x0042DB60</c>, which maps each id through its own table (CONFIRMED by decompile): mostly
/// id+1, but Query's 2149 maps to 2795. That switch sets only the status line; the popup name drawn
/// over these widgets follows the owner's report that DK2 shows one (2026-10-19), and its native
/// producer is unread.
/// </summary>
[DK2.Core.Dk2Behavior(DK2.Core.Confidence.StronglyInferred, "docs/research/minimap-controls.md")]
public static class HudWidgetText
{
    /// <summary>Upper-left Query corner, widget <c>0x00690E88</c>.</summary>
    public const int QueryCorner = 2149;
    /// <summary>Upper-right Sell corner, widget <c>0x00690ED8</c>.</summary>
    public const int SellCorner = 2151;
    /// <summary>Lower-right magnifier ("Resize map"), widget <c>0x00690F28</c>.</summary>
    public const int ZoomCorner = 2145;
    /// <summary>Lower-left Options corner, widget <c>0x00690F78</c>.</summary>
    public const int OptionsCorner = 2147;
    /// <summary>The panel collapse button ("Resize Panel"), widget <c>0x00690D98</c>.</summary>
    public const int ResizePanel = 2161;

    /// <summary>Category tab <paramref name="category"/>, widgets <c>0x00690C58</c> + 80·i.</summary>
    public static int Tab(BottomPanelCategory category) => 2153 + 2 * (int)category;

    /// <summary><c>0x0042DB60</c>'s status string for a widget text id; 0 when it has no case.</summary>
    public static int StatusTextId(int textId) => textId switch
    {
        QueryCorner => 2795,
        SellCorner or ZoomCorner or OptionsCorner or ResizePanel or 2153 or 2155 or 2157 or 2159 => textId + 1,
        // The creature panel's worker rows (2308..2314), mode selectors (2316..2322) and the rest of
        // the even run to 0x928.
        >= 0x904 and <= 0x928 when textId % 2 == 0 => textId + 1,
        _ => 0,
    };

    public static int? ForAction(ClassicHudAction action) => action switch
    {
        ClassicHudAction.SelectQuery => QueryCorner,
        ClassicHudAction.SelectSell => SellCorner,
        ClassicHudAction.ZoomIn or ClassicHudAction.ZoomOut => ZoomCorner,
        ClassicHudAction.ToggleOptions => OptionsCorner,
        _ => null,
    };

    public static HoverTextSnapshot Hover(int textId, Func<uint, string> resolve)
    {
        if (resolve is null) throw new ArgumentNullException(nameof(resolve));
        int status = StatusTextId(textId);
        return new HoverTextSnapshot(status == 0 ? "" : resolve((uint)status), resolve((uint)textId),
            new WorldHoverHit(WorldHoverKind.Widget, default, textId));
    }
}
