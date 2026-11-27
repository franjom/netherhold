// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation;

/// <summary>What a press on the overview asks for: pick up or focus a creature of a type and row.</summary>
public readonly record struct CreatureOverviewRequest(CreatureOverviewType Type, CreatureOverviewSelection Selection,
    CreatureOverviewFilter Filter = CreatureOverviewFilter.All);

/// <summary>The engine-free creature overview (ADR-0805): the Creatures tab's panel, menu 5 and its worker
/// sidebar menu 6. Columns of portraits with counts by mode, the worker column, the page strips and the mode
/// selector. Positions are DK2's virtual units at menu 5's lone place, x 472; they move with the panel when a
/// pinned tab shares the strip (0x00417310).</summary>
public sealed class CreatureOverviewHud
{
    private const int MaximumColumns = 32;
    private static readonly HudColor Highlight = new(201, 255, 0);
    private static readonly HudImage Lit = "GUI\\Tabs\\t-cp-hilight", WorkerStrip = "GUI\\Tabs\\t-cp-imp",
        Back = "GUI\\Pages\\page_back", Forward = "GUI\\Pages\\page_forward", ModeStrip = "GUI\\Tabs\\t-creature_panel";
    private static readonly HudImage[] ModeHovers = [.. Enumerable.Range(0, 4).Select(i => (HudImage)("GUI\\Tabs\\t-creature_panel-" + i))];
    private static readonly HudImage[] ModeFrames =
        [.. new[] { "total", "jobs", "fights", "moods" }.Select(f => (HudImage)("GUI\\Tabs\\t-cp-" + f))];
    private static readonly HudImage[,] Numbers = NumberImages();
    private static readonly int[] WorkerIconTop = [4, 68, 132, 196], WorkerIconHeight = [72, 68, 72, 72];

    private readonly Func<CreatureOverviewType, string> _portrait;
    private readonly CreatureOverview _view;
    private int _page, _columns, _left = BottomPanelLayout.Left, _areaWidth = BottomPanelLayout.Span, _screenWidth = 640;
    private bool _visible;

    /// <param name="portrait">A creature type's portrait catalogue image.</param>
    public CreatureOverviewHud(Func<CreatureOverviewType, string> portrait, CreatureOverview view)
    {
        _portrait = portrait ?? throw new ArgumentNullException(nameof(portrait));
        _view = view ?? throw new ArgumentNullException(nameof(view));
    }

    public CreatureOverviewMode Mode { get; private set; }

    /// <summary>The fixed catalogue images, to preload; portraits load as their types appear.</summary>
    public static IEnumerable<HudImage> Images()
    {
        yield return Lit; yield return WorkerStrip; yield return Back; yield return Forward; yield return ModeStrip;
        foreach (HudImage image in ModeHovers)
        {
            yield return image;
        }

        foreach (HudImage image in ModeFrames)
        {
            yield return image;
        }

        foreach (HudImage image in Numbers)
        {
            yield return image;
        }
    }

    private static HudImage[,] NumberImages()
    {
        var images = new HudImage[2, 10];
        for (int digit = 0; digit < 10; digit++)
        { images[0, digit] = "GUI\\Icons\\i-" + digit; images[1, digit] = "GUI\\Icons\\lo-" + digit; }
        return images;
    }

    private float X(float value)
    {
        return value - BottomPanelLayout.Left + _left;
    }

    /// <param name="screenPixelWidth">The screen's width in pixels: under 640 the counts use the low-res digits.</param>
    /// <param name="area">The Creatures panel's place (BottomPanelHud's panel area), null while it is closed.</param>
    /// <param name="pointer">The pointer in virtual units.</param>
    public void Build(HudScreen screen, int screenPixelWidth, HudVisibility visibility, BottomPanelRect? area,
        (float X, float Y) pointer, HudFrame frame)
    {
        _visible = visibility.ShowNormalHud && area.HasValue;
        if (!_visible)
        {
            return;
        }

        _screenWidth = screenPixelWidth;
        _left = area!.Value.X; _areaWidth = area.Value.Width;
        // 0x00417470: (W - 432) / 116 columns of menu 5's width. Ours, not DK2's: alone, the overview keeps the
        // wider screen's extra width (the strip runs to the Reaper at width - 288); sharing, it gets its 1800 / n.
        if (_areaWidth >= BottomPanelLayout.Span)
        {
            _areaWidth = Math.Max(_areaWidth, (int)screen.VirtualWidth - 760);
        }

        _columns = Math.Min(MaximumColumns - 1, Math.Max(1, BottomPanelLayout.OverviewColumns(_areaWidth)));
        int pages = Pages;
        _page = Math.Min(_page, pages - 1);

        for (int i = 0; i <= _columns; i++)
        {
            int entryIndex = Entry(i);
            if (entryIndex >= _view.Entries.Count)
            {
                continue;
            }

            Quad(frame, _portrait(_view.Entries[entryIndex].Type),
                new HudRect(X(i == 0 ? 544 : 912 + (i - 1) * 136), i == 0 ? 1608 : 1612, 128, 264));
            if (i == 0 || !HitRect(i).Contains(pointer.X, pointer.Y))
            {
                continue;
            }

            int step = RowStep, hoveredRow = Mode == CreatureOverviewMode.Total ? 0 : Row(pointer.Y);
            frame.Add(new HudDraw(HudDrawKind.Quad, Lit, new HudRect(X(924 + (i - 1) * 136), 1616 + hoveredRow * step, 108,
                Mode == CreatureOverviewMode.Total ? 244 : step), Highlight));
        }

        Quad(frame, WorkerStrip, new HudRect(X(476), 1608, 64, 264));
        int workerHovered = -1, modeHovered = -1;
        for (int i = 0; i < 4; i++)
        {
            if (ModeRect(i).Contains(pointer.X, pointer.Y))
            {
                modeHovered = i;
            }

            if (_view.Entries.Count > 0 && WorkerCountRect(i).Contains(pointer.X, pointer.Y))
            {
                workerHovered = i;
            }
        }
        // Worker count widgets 0x006900C8 + 160·r draw highlight 162 tinted 0x00FFC9, red in the low byte:
        // (201,255,0), under their count while hovered (0x00417850).
        if (workerHovered >= 0)
        {
            frame.Add(new HudDraw(HudDrawKind.Quad, Lit, WorkerCountRect(workerHovered), Highlight));
        }

        CreatureOverviewWorkerCounts workers = _view.WorkerCounts;
        for (int i = 0; i < 4; i++)
        {
            Digits(frame, i == 0 ? workers.Total : i == 1 ? workers.Idle : i == 2 ? workers.Busy : workers.Fighting, X(604), 1624 + i * 64);
        }

        for (int i = 1; i <= _columns; i++)
        {
            int entryIndex = Entry(i);
            if (entryIndex >= _view.Entries.Count)
            {
                continue;
            }

            CreatureOverviewType type = _view.Entries[entryIndex].Type;
            float centre = X(924 + (i - 1) * 136 + 54);
            for (int row = 0; row < CreatureOverview.Rows(Mode); row++)
            {
                Digits(frame, _view.Count(type, CreatureOverview.RowFilter(Mode, row)), centre,
                    Mode == CreatureOverviewMode.Total ? 1709 : 1628 + row * RowStep);
            }
        }

        // Both page strips are always drawn, forward (catalogue 10) above back (11): widgets 0x0068FFD8 and
        // 0x00690028. Which way each pages (0x00417730) is unread. 0x00410A60 sets visibility state 3 (HYPOTHESIS:
        // while the roster fits); 0x004129E0 draws state 3 multiplied by (100,100,100), the strips' disabled
        // look (owner, 2026-10-20).
        HudColor pageTint = pages <= 1 ? HudColor.Grey(100) : HudColor.White;
        frame.Add(new HudDraw(HudDrawKind.Quad, Back, new HudRect(X(760), 1744, 64, 132), pageTint));
        frame.Add(new HudDraw(HudDrawKind.Quad, Forward, new HudRect(X(760), 1612, 64, 132), pageTint));
        Quad(frame, ModeStrip, new HudRect(X(680), 1608, 64, 264));
        // 0x00417B40 draws catalogue 238..241, the strip with button i lit, while button i is hovered.
        if (modeHovered >= 0)
        {
            Quad(frame, ModeHovers[modeHovered], new HudRect(X(680), 1608, 64, 264));
        }

        HudColor modeTint = Mode switch
        {
            CreatureOverviewMode.Total => HudColor.White,
            CreatureOverviewMode.Jobs => new HudColor(20, 255, 10),
            CreatureOverviewMode.Fighting => new HudColor(255, 30, 0),
            _ => new HudColor(240, 165, 0),
        };
        frame.Add(new HudDraw(HudDrawKind.Quad, Lit, new HudRect(X(692), 1616 + (int)Mode * 64, 44, 52), modeTint));
        Quad(frame, ModeFrames[(int)Mode], new HudRect(X(840), 1612, 64, 264));
    }

    private int Pages
    {
        get
        {
            return Math.Max(1, (Math.Max(0, _view.Entries.Count - 1) + _columns - 1) / _columns);
        }
    }

    /// <summary>Column 0 is the first entry; the others page through the rest.</summary>
    private int Entry(int column)
    {
        return column == 0 ? 0 : 1 + _page * _columns + column - 1;
    }

    /// <summary>An ordinary column's widgets stack rows of 244 / 64 / 84 / 84 units by mode (0x00418BC0).</summary>
    private int RowStep
    {
        get
        {
            return Mode == CreatureOverviewMode.Jobs ? 64 : 84;
        }
    }

    private int Row(float y)
    {
        return Math.Min(CreatureOverview.Rows(Mode) - 1, Math.Max(0, (int)((y - 1616) / RowStep)));
    }

    private void Digits(HudFrame frame, int value, float centre, float top)
    {
        if (value < 0)
        {
            return;
        }

        int length = 1, divisor = 1;
        for (int rest = value / 10; rest > 0; rest /= 10) { length++; divisor *= 10; }
        // At most five glyphs, centred on the whole number, as the old adapter drew it.
        float left = centre - length * 40 / 2f;
        int family = _screenWidth >= 640 ? 0 : 1;
        for (int i = 0; i < Math.Min(5, length); i++, divisor /= 10)
        {
            Quad(frame, Numbers[family, value / divisor % 10], new HudRect(left + i * 40, top, 48, 48));
        }
    }

    private static void Quad(HudFrame frame, HudImage image, HudRect rect)
    {
        frame.Add(new HudDraw(HudDrawKind.Quad, image, rect, HudColor.White));
    }

    // Menu 6 sits at (472,1600); its widget rectangles are local (records 0x00690078..0x00690348).
    private HudRect ModeRect(int mode)
    {
        return new(X(472 + 220), 1600 + 16 + 64 * mode, 44, 52);
    }

    private HudRect WorkerCountRect(int row)
    {
        return new(X(472 + 72), 1600 + 12 + 64 * row, 120, 64);
    }

    private HudRect HitRect(int column)
    {
        return column == 0 ? new HudRect(X(544), 1612, 120, 256)
            : new HudRect(X(924 + (column - 1) * 136), 1616, 108, 244);
    }

    /// <summary>The hovered widget's text id (record +0x30): the worker row icons 2308..2314 and the mode
    /// selectors 2316..2322; 0 for none.</summary>
    public int HoveredTextId(float x, float y)
    {
        if (!_visible)
        {
            return 0;
        }

        for (int i = 0; i < 4; i++)
        {
            if (ModeRect(i).Contains(x, y))
            {
                return 2316 + 2 * i;
            }

            if (_view.Entries.Count > 0 && new HudRect(X(472), 1600 + WorkerIconTop[i], 80, WorkerIconHeight[i]).Contains(x, y))
            {
                return 2308 + 2 * i;
            }
        }
        return 0;
    }

    /// <summary>The creature type whose column (portrait and count widgets) is hovered: 0x00418DD0 puts its
    /// description in the status strip. The worker column is the four worker count widgets.</summary>
    public CreatureOverviewType? HoveredColumnType(float x, float y)
    {
        if (!_visible)
        {
            return null;
        }

        for (int i = 0; i <= _columns; i++)
        {
            int entry = Entry(i);
            if (entry >= _view.Entries.Count)
            {
                continue;
            }

            HudRect r = HitRect(i);
            if (i > 0 && Mode != CreatureOverviewMode.Total)
            {
                r = r with { Height = CreatureOverview.Rows(Mode) * RowStep };
            }

            if (r.Contains(x, y))
            {
                return _view.Entries[entry].Type;
            }
        }
        return null;
    }

    public bool ContainsPointer(float x, float y)
    {
        return _visible && new HudRect(X(472), 1600, _areaWidth, 320).Contains(x, y);
    }

    /// <summary>A press: a mode button or a page strip changes the panel and returns false; a worker row or a
    /// column returns the pickup or focus it asks for. Of the modifier keys held, Shift alone picks the lowest level and
    /// Ctrl alone the highest; any other combination is the plain pickup.</summary>
    public bool TryPress(float x, float y, bool primary, bool secondary, bool shift, bool control, bool alt, out CreatureOverviewRequest request)
    {
        bool lowestLevel = shift && !control && !alt, highestLevel = control && !shift && !alt;
        request = default;
        if (!_visible || !primary && !secondary)
        {
            return false;
        }

        if (primary)
        {
            for (int i = 0; i < 4; i++)
            {
                if (new HudRect(X(692), 1616 + i * 64, 44, 52).Contains(x, y)) { Mode = (CreatureOverviewMode)i; return false; }
            }
        }

        if (primary && new HudRect(X(760), 1612, 64, 264).Contains(x, y))
        { _page = Math.Max(0, _page + (y < 1744 ? 1 : -1)); return false; }
        CreatureOverviewSelection selection = secondary ? CreatureOverviewSelection.FocusNext
            : lowestLevel ? CreatureOverviewSelection.PickupLowestLevel
            : highestLevel ? CreatureOverviewSelection.PickupHighestLevel : CreatureOverviewSelection.PickupNext;
        if (_view.Entries.Count > 0 && new HudRect(X(472), 1600, 192, 256).Contains(x, y))
        {
            int row = Math.Min(3, Math.Max(0, (int)((y - 1600) / 64)));
            CreatureOverviewFilter filter = row == 0 ? CreatureOverviewFilter.All : row == 1 ? CreatureOverviewFilter.Idle
                : row == 2 ? CreatureOverviewFilter.Busy : CreatureOverviewFilter.Fighting;
            request = new CreatureOverviewRequest(_view.Entries[0].Type, selection, filter);
            return true;
        }
        for (int i = 0; i <= _columns; i++)
        {
            int entry = Entry(i);
            if (entry >= _view.Entries.Count || !HitRect(i).Contains(x, y))
            {
                continue;
            }

            int row = Mode == CreatureOverviewMode.Total ? 0 : Row(y);
            request = new CreatureOverviewRequest(_view.Entries[entry].Type, selection,
                i == 0 ? CreatureOverviewFilter.All : CreatureOverview.RowFilter(Mode, row));
            return true;
        }
        return false;
    }
}
