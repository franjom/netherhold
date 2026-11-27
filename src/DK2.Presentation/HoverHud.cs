// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Presentation;

/// <summary>The engine-free hover HUD (ADR-0805): the wooden status bar across the top with its iron end cap
/// (0x0042E140), the hovered thing's status line scrolling inside it, and the delayed tooltip popup by the
/// cursor (0x0042E590). It builds two frames: the bar, which draws under the resource panels, and the text
/// and popup, which draw over every panel but the full map. The panel's own lengths are in the adapter's
/// local pixels (<see cref="HudScreen"/>), as DK2 laid them out in screen pixels, and converted to virtual.</summary>
public sealed class HoverHud
{
    public static readonly HudImage Bar = "GUI\\Windows\\Bar-00";
    public static readonly HudImage Cap = "GUI\\Windows\\Bar-01";
    private static readonly HudImage[] Pieces =
        [.. new[] { "tl", "t", "tr", "l", "m", "r", "bl", "b", "br" }.Select(p => (HudImage)("GUI\\Windows\\text_box-" + p))];

    /// <summary>The status line scrolls back and forth at 60 local pixels a second: our time-based form of
    /// DK2's one pixel per callback (0x0042E4A2).</summary>
    private const float ScrollPerSecond = 60;

    private readonly Func<string, BitmapFont> _font;
    private readonly (int Width, int Height) _barSize;
    private readonly HoverTooltipDelay _delay = new();
    private BitmapFont? _statusFont, _tooltipFont;
    private int _fontHeight;
    private HudText? _status, _tooltip;
    /// <summary>The texts' measured sizes, kept so a frame does not re-wrap them.</summary>
    private (int Width, int Height) _statusSize, _tooltipSize;
    private int _tooltipWrap;
    private float _scroll;
    private bool _scrollForward = true, _visible = true, _allowInput = true;
    private long _lastMs = -1;
    private HudScreen _screen;

    /// <param name="font">A bitmap font by file name (<see cref="InterfaceFonts"/>).</param>
    /// <param name="barSize">The pixel size of <see cref="Bar"/>: its tiles keep its aspect at the bar's height.
    /// The old adapter's choice, kept here; 0x0042E140 tiles it at a virtual 512x88, open with the owner.</param>
    public HoverHud(Func<string, BitmapFont> font, (int Width, int Height) barSize)
    {
        _font = font ?? throw new ArgumentNullException(nameof(font));
        _barSize = barSize;
    }

    /// <summary>Whether a creature's name pops up (the options' creature tooltip setting).</summary>
    public bool ShowCreatureTooltip { get; set; } = true;

    /// <summary>Every catalogue image the panel draws, to preload.</summary>
    public static IEnumerable<HudImage> Images()
    {
        yield return Bar; yield return Cap;
        foreach (HudImage piece in Pieces)
        {
            yield return piece;
        }
    }

    /// <summary>Hiding the HUD also resets the tooltip's wait, so it waits again when shown.</summary>
    public void SetVisibility(HudVisibility visibility, long ms)
    {
        _visible = visibility.ShowNormalHud; _allowInput = visibility.AllowOrdinaryInput;
        if (!_visible)
        {
            _delay.Visible(HoverTextSnapshot.Empty, ms);
        }
    }

    /// <summary>The wooden bar: the whole width, behind the Heart, mana and gold panels, as in DK2.</summary>
    public HudRect BarBounds(HudScreen screen)
    {
        return new(0, StatusTop, screen.VirtualWidth, StatusHeight(screen));
    }

    /// <summary>The status line's strip, right of the resource panels (1020 units, floored to pixels).</summary>
    public HudRect StatusBounds(HudScreen screen)
    {
        float left = Math.Min(screen.VirtualWidth, screen.Virtual(MathF.Floor(1020 * screen.Scale)));
        return new HudRect(left, StatusTop, Math.Max(0, screen.VirtualWidth - left), StatusHeight(screen));
    }

    /// <summary>The bar, as last built, takes the pointer away from the world.</summary>
    public bool ContainsPointer(float x, float y)
    {
        return _visible && _allowInput && _screen.Height > 0 && BarBounds(_screen).Contains(x, y);
    }

    /// <summary>8/768 of the screen down: 20 virtual units.</summary>
    private static float StatusTop
    {
        get
        {
            return 20;
        }
    }

    /// <summary>22/480 of the screen (88 units), or the status font's line plus 12 pixels if taller; 12 for
    /// the line before the font is loaded.</summary>
    private float StatusHeight(HudScreen screen)
    {
        return Math.Max(88, screen.Virtual((_statusFont?.LineHeight ?? 12) + 12));
    }

    /// <param name="screen">The HUD surface; its <see cref="HudScreen.Height"/> also picks the fonts.</param>
    /// <param name="bottom">The panel's bottom edge in virtual units (less than 1920 when the UI is scaled up).</param>
    /// <param name="pointer">The pointer in virtual units.</param>
    /// <param name="cursorReach">How far the drawn cursor reaches right of the pointer, in DK2's 640x480 units;
    /// the popup sits 18 beyond it.</param>
    /// <param name="ms">Unscaled time: the scroll and the tooltip's wait run on it.</param>
    public void Build(HudScreen screen, float bottom, HoverTextSnapshot snapshot, (float X, float Y) pointer,
        float cursorReach, long ms, HudFrame bar, HudFrame top)
    {
        float seconds = _lastMs < 0 ? 0 : Math.Max(0, ms - _lastMs) / 1000f;
        _lastMs = ms;
        if (!_visible)
        {
            return;
        }

        bool fontChanged = Fonts(screen.Height);
        _screen = screen;
        BitmapFont statusFont = _statusFont!, tooltipFont = _tooltipFont!;
        string status = snapshot.TopStatus ?? "", tooltip = snapshot.TooltipName ?? "";
        if (fontChanged || _status == null || _status.Text != status)
        {
            _status = new HudText(statusFont, status, int.MaxValue / 2);
            _statusSize = HudTextPixels.Measure(_status);
            _scroll = 0; _scrollForward = true;
        }
        int wrap = Math.Max(1, screen.Width / 2);
        if (fontChanged || _tooltip == null || _tooltip.Text != tooltip || _tooltipWrap != wrap)
        {
            _tooltip = new HudText(tooltipFont, tooltip, wrap, Centred: true);
            _tooltipSize = HudTextPixels.Measure(_tooltip);
            _tooltipWrap = wrap;
        }
        BuildBar(screen, bar);
        BuildStatus(screen, seconds, top);
        if (_delay.Visible(snapshot, ms, ShowCreatureTooltip))
        {
            BuildTooltip(screen, bottom, pointer, cursorReach, top);
        }
    }

    /// <summary>The fonts for the screen height; true when they changed.</summary>
    private bool Fonts(int height)
    {
        if (_statusFont != null && _fontHeight == height)
        {
            return false;
        }

        BitmapFont status = _font(InterfaceFonts.HudTextFileName(height)), tooltip = _font(InterfaceFonts.HudTooltipFileName(height));
        bool changed = !ReferenceEquals(status, _statusFont) || !ReferenceEquals(tooltip, _tooltipFont);
        _statusFont = status; _tooltipFont = tooltip; _fontHeight = height;
        return changed;
    }

    private void BuildBar(HudScreen screen, HudFrame frame)
    {
        HudRect bar = BarBounds(screen);
        float tileWidth = _barSize.Width * bar.Height / Math.Max(1, _barSize.Height);
        HudQuads.Tiled(frame, Bar, bar, tileWidth, bar.Height, HudColor.White);
        // 0x0042E140: the cap is a 128-unit square at (W - 80, 0), virtual 2560x1920, over a bar of 88 at
        // y20, so it stands above and below the bar and 48 units off the right edge. It is not clipped.
        frame.Add(new HudDraw(HudDrawKind.Quad, Cap, new HudRect(screen.VirtualWidth - 80, 0, 128, 128), HudColor.White));
    }

    private void BuildStatus(HudScreen screen, float seconds, HudFrame frame)
    {
        HudRect strip = StatusBounds(screen);
        // Our choice, not DK2's: the text starts 12 pixels into the strip and stops 12 short of the iron end
        // cap. DK2's text widget is 1440 wide from x1020, leaving 100 units for the cap (world-hover-text.md).
        float clipLeft = strip.X + screen.Virtual(12);
        float clipWidth = Math.Max(screen.Virtual(1), screen.VirtualWidth - 80 - strip.X - screen.Virtual(24));
        (int width, int height) = _statusSize;
        float overflow = Math.Max(0, width - clipWidth * screen.Scale);
        if (overflow > 0)
        {
            _scroll += (_scrollForward ? 1 : -1) * seconds * ScrollPerSecond;
            if (_scroll >= overflow) { _scroll = overflow; _scrollForward = false; }
            if (_scroll <= 0) { _scroll = 0; _scrollForward = true; }
        }
        else
        {
            _scroll = 0;
        }

        if (_status!.Text.Length == 0)
        {
            return;
        }

        var clip = new HudRect(clipLeft, strip.Y, Math.Min(clipWidth, strip.X + strip.Width - clipLeft), strip.Height);
        var text = new HudRect(clipLeft - screen.Virtual(_scroll), strip.Y + (strip.Height - screen.Virtual(height)) / 2,
            screen.Virtual(width), screen.Virtual(height));
        HudQuads.Clipped(frame, HudImage.Of(_status), text, clip, HudColor.White);
    }

    /// <summary>0x0042E590's popup in a nine-piece text box, laid out in local pixels. This is its world arm
    /// only (interface +0x12B2 clear); over the GUI DK2 offsets it 60 units and centres it on the pointer,
    /// and a flipped popup is centred too, which this does not do yet. In the world arm DK2 offsets it 98 units
    /// right of the pointer at width 640 and scales the Hand by that width; ours scales the Hand by height/480,
    /// so the offsets follow it. It flips left of the pointer near the right edge.</summary>
    private void BuildTooltip(HudScreen screen, float bottom, (float X, float Y) pointer, float cursorReach, HudFrame frame)
    {
        if (_tooltip!.Text.Length == 0)
        {
            return;
        }

        float scale = screen.Scale, screenWidth = screen.Width, statusHeight = StatusHeight(screen) * scale;
        (int textWidth, int textHeight) = _tooltipSize;
        float padding = Math.Max(4, InterfaceFonts.HudTooltipPixels(screen.Height));
        float width = Math.Min(screenWidth, textWidth + padding * 2), height = textHeight + padding * 2;
        float unit = screen.Height / 480f, cursorX = pointer.X * scale, cursorY = pointer.Y * scale;
        float x = cursorX + (cursorReach + 18) * unit;
        if (x + width > screenWidth - 40 * unit)
        {
            x = cursorX - width - 30 * unit;
        }

        x = Math.Clamp(x, 0, Math.Max(0, screenWidth - width));
        float y = Math.Clamp(cursorY - _tooltipFont!.LineHeight, statusHeight, Math.Max(statusHeight, bottom * scale - height - 10));
        float border = Math.Min(padding, Math.Min(width, height) / 2);
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
        {
            float left = column == 0 ? 0 : column == 1 ? border : width - border;
            float up = row == 0 ? 0 : row == 1 ? border : height - border;
            float w = column == 1 ? width - border * 2 : border, h = row == 1 ? height - border * 2 : border;
            frame.Add(new HudDraw(HudDrawKind.Quad, Pieces[row * 3 + column],
                new HudRect((x + left) / scale, (y + up) / scale, w / scale, h / scale), HudColor.White));
        }
        }

        frame.Add(new HudDraw(HudDrawKind.Quad, HudImage.Of(_tooltip),
            new HudRect((x + (width - textWidth) / 2) / scale, (y + padding) / scale, textWidth / scale, textHeight / scale), HudColor.White));
    }
}
