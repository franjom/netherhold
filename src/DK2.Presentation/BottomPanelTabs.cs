// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation;

/// <summary>The category panels showing: each category menu's visibility <c>+0x48</c> (the flash's
/// "panel open" test, <c>0x00412442..0x004124B1</c>). Empty while the panel is collapsed.</summary>
public readonly record struct BottomPanelOpenSet
{
    private readonly byte _mask;
    private BottomPanelOpenSet(byte mask) => _mask = mask;

    public static BottomPanelOpenSet None => default;
    public static BottomPanelOpenSet Of(BottomPanelCategory category) => new((byte)(1 << (int)category));
    public bool Contains(BottomPanelCategory category) => (_mask & 1 << (int)category) != 0;
    public BottomPanelOpenSet With(BottomPanelCategory category) => new((byte)(_mask | 1 << (int)category));
    public BottomPanelOpenSet Without(BottomPanelCategory category) => new((byte)(_mask & ~(1 << (int)category)));
    public int Count => (_mask & 1) + (_mask >> 1 & 1) + (_mask >> 2 & 1) + (_mask >> 3 & 1);
}

/// <summary>Tab clicks and pinning, <c>0x00416C20</c> (tab-alerts.md section 3). Interface <c>+0x5F</c> is
/// the pin count and <c>+0x5F+c</c> the pin rank; the count is only ever set to 1, so at most one tab is
/// pinned and its rank is 1. <c>+0x65+4c</c> remembers the open menus across a collapse
/// (<c>0x004167D0</c>). Nothing else writes the pins, so nothing here clears one.</summary>
public sealed class BottomPanelTabs
{
    private BottomPanelOpenSet _remembered;

    public BottomPanelCategory? Pinned { get; private set; }

    /// <summary>HUD <c>+0x109</c> == 0: every category menu hidden, the remembered set kept.</summary>
    public bool Collapsed { get; set; }

    /// <summary>The menus showing: the remembered set while expanded, none while collapsed.</summary>
    public BottomPanelOpenSet Open => Collapsed ? BottomPanelOpenSet.None : _remembered;

    /// <summary>A click on tab <paramref name="category"/>. <paramref name="shiftOnly"/> is the modifier mask
    /// <c>0x005B2A50</c> equal to 1: Shift without Ctrl or Alt. Returns true when the click expanded a
    /// collapsed panel (DK2 then plays sound <c>0x138</c>, not wired).</summary>
    public bool Click(BottomPanelCategory category, bool shiftOnly)
    {
        bool expanded = Collapsed;
        Collapsed = false;
        if (!shiftOnly)
        {
            // Close every category that is not pinned, then open the clicked one.
            _remembered = Pinned is BottomPanelCategory pinned && _remembered.Contains(pinned)
                ? BottomPanelOpenSet.Of(pinned) : BottomPanelOpenSet.None;
            _remembered = _remembered.With(category);
        }
        else if (Pinned == category)
        {
            // Unpin: the panel closes only when another category menu is open.
            if (_remembered.Count > 1) _remembered = _remembered.Without(category);
            Pinned = null;
        }
        else if (Pinned is null)
        {
            // Pin and open; the panels already open stay open beside it.
            Pinned = category;
            _remembered = _remembered.With(category);
        }
        // Shift on an unpinned tab while another is pinned: only the relayout.
        return expanded;
    }
}

/// <summary>One category panel's grid from <c>0x00417470</c>'s expanded branch: square cells of
/// <see cref="Cell"/> units, filled column by column.</summary>
public readonly record struct BottomPanelGrid(int Cell, int Columns, int Rows)
{
    public const int LargeCell = 264, SmallCell = 128;

    public int PageSize => Columns * Rows;

    /// <summary>Slot widget <c>+0x3C</c>: <c>(cell != 128) + 5</c>, so 6 for 264-unit cells and 5 for
    /// 128-unit cells. The flash draws 88-unit sparkles on a 6 (<c>0x00412FC0</c>).</summary>
    public int WidgetBank => Cell == SmallCell ? 5 : 6;

    public int Pages(int count) => PageSize <= 0 ? 1 : Math.Max(1, (count + PageSize - 1) / PageSize);

    /// <summary>Page slot <paramref name="index"/> (column-major: down the rows first) at
    /// <c>x = 76 + (cell+8)·col</c>, <c>y = 12 + (cell+8)·row</c> inside <paramref name="panel"/>.</summary>
    public BottomPanelRect Slot(BottomPanelRect panel, int index)
    {
        int column = index / Rows, row = index % Rows;
        return new BottomPanelRect(panel.X + 76 + (Cell + 8) * column, panel.Y + 12 + (Cell + 8) * row, Cell, Cell);
    }
}
