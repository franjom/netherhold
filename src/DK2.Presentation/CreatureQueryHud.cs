// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Presentation;

/// <summary>The engine-free creature query details (ADR-0805): menu 40's text widget over the bottom panel,
/// showing the queried creature's text, with its scroll bar when the text is taller than the widget.</summary>
public sealed class CreatureQueryHud
{
    /// <summary>The text widget: 1040,1000, 1096 x 288 virtual.</summary>
    private static readonly HudRect Text = new(1040, 1000, 1096, 288);
    /// <summary>The area that keeps the pointer from the world while the details show.</summary>
    private static readonly HudRect Hit = new(1020, 980, 1200, 440);

    // Menu40's text widget owns a bar (0x0052A0D0) at menu-local (1116,20) 64x288: up arrow, track, thumb,
    // down arrow (0x00414400).
    private static readonly HudImage Up = "GUI\\Scroll Bars\\v-up_arrow", Track = "GUI\\Scroll Bars\\v-scroll_bar_bg",
        Thumb = "GUI\\Scroll Bars\\v-scroll_bar", Down = "GUI\\Scroll Bars\\v-down_arrow";

    private readonly Func<int, BitmapFont> _font;
    private string? _source;
    private int _height;
    private HudText? _text;
    private (int Width, int Height) _size;
    private bool _overflow, _visible;

    /// <param name="font">The legible font for a screen height.</param>
    public CreatureQueryHud(Func<int, BitmapFont> font) => _font = font ?? throw new ArgumentNullException(nameof(font));

    public static IEnumerable<HudImage> Images() => [Up, Track, Thumb, Down];

    /// <param name="otherPanelOpen">A modal panel is open: the details hide.</param>
    public void Build(HudScreen screen, CreatureQuerySnapshot snapshot, HudVisibility visibility, bool otherPanelOpen, HudFrame frame)
    {
        _visible = visibility.ShowNormalHud && !otherPanelOpen && !string.IsNullOrEmpty(snapshot.Text);
        if (!_visible) return;
        if (_source != snapshot.Text || _height != screen.Height) Set(screen, snapshot.Text);
        HudRect text = new(Text.X, Text.Y, screen.Virtual(_size.Width), screen.Virtual(_size.Height));
        HudQuads.Clipped(frame, HudImage.Of(_text!), text, Text, HudColor.White);
        if (!_overflow) return;
        const float x = 2136, y = 1000, w = TextScrollBar.Width, h = 288, arrow = TextScrollBar.Arrow;
        Quad(frame, Up, new HudRect(x, y, w, arrow));
        Quad(frame, Track, new HudRect(x, y + arrow, w, h - 2 * arrow));
        // Query mode resets the position to the top every draw (0x004242F0), so the thumb stays there.
        Quad(frame, Thumb, new HudRect(x + 20, y + arrow + 4, 28, TextScrollBar.MinimumThumb - 8));
        Quad(frame, Down, new HudRect(x, y + h - arrow, w, arrow));
    }

    /// <summary>0x0052A330 measures 64 narrower than the widget: text taller than it shows the bar, which in
    /// Query mode never scrolls (0x004242F0 draws the text at offset 0). Shorter text takes the full width.</summary>
    private void Set(HudScreen screen, string source)
    {
        _source = source; _height = screen.Height;
        BitmapFont font = _font(screen.Height);
        float pixels = screen.Scale;
        _text = new HudText(font, source, Math.Max(1, (int)((Text.Width - TextScrollBar.Width) * pixels)));
        _size = HudTextPixels.Measure(_text);
        _overflow = _size.Height > Text.Height * pixels;
        if (_overflow) return;
        _text = new HudText(font, source, Math.Max(1, (int)(Text.Width * pixels)));
        _size = HudTextPixels.Measure(_text);
    }

    private static void Quad(HudFrame frame, HudImage image, HudRect rect) =>
        frame.Add(new HudDraw(HudDrawKind.Quad, image, rect, HudColor.White));

    /// <summary>Whether the last built frame showed the details and the pointer is over them.</summary>
    public bool ContainsPointer(float x, float y, HudVisibility visibility) =>
        _visible && visibility.ShowNormalHud && visibility.AllowOrdinaryInput && Hit.Contains(x, y);
}
