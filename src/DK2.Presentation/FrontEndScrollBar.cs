// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation;

/// <summary>The part of a front-end scroll bar under the pointer, as 0x005507E0 (the bar's <c>+0x24</c>
/// callback, installed by 0x0054FCF0) stores it through slot <c>+0x3C</c> in byte <c>+0xAE</c>.</summary>
public enum ScrollBarPart : byte { None = 0, Up = 6, Down = 7, Thumb = 8, Track = 9 }

/// <summary>A front-end list's scroll bar: the type-2 child a type-7 widget owns (0x0052A0D0), driven by
/// <see cref="TextScrollBar"/>'s rules, hovered by 0x005507E0 and drawn by 0x00551BA0. The rectangle is in
/// reference units (2560 × 1920); hit tests and art are in 640 × 480 units, where DK2 draws the front end.</summary>
public sealed class FrontEndScrollBar
{
    /// <summary>The art 0x00534210 loads into the slots 0x00551BA0 draws: <c>+0x4E24</c>, <c>+0x4E4C</c> and
    /// <c>+0x55CC</c> (each expanded into five frames by 0x0054FD20), and <c>+0x3B2</c>.</summary>
    public const string UpArrowArt = "1-V-Up_Arrow", DownArrowArt = "1-V-Down_Arrow",
        TrackArt = "5-V-Scroll_Bar_BG", ThumbArt = "0-V-Scroll_Bar";
    /// <summary>0x0054FD20 pads each image by 6 pixels a side for its glow frames.</summary>
    public const int GlowPadding = 6;
    /// <summary>0x00551BA0 blits the thumb from row 8 of its art, as tall as the thumb.</summary>
    public const int ThumbArtTop = 8;
    /// <summary>A hovered part's glow counter; 0x00551BA0 counts it down once per drawn frame.</summary>
    private const sbyte GlowStart = 8;

    private readonly sbyte[] _glow = { -1, -1, -1 };
    private ScrollBarPart _held;
    private bool _wasDown;

    public FrontEndScrollBar(FrontEndRect rect) => Rect = rect;

    /// <summary>0x0052A0D0: right of the list by its record byte <c>+5</c>, the list's height,
    /// <see cref="TextScrollBar.Width"/> wide.</summary>
    public static FrontEndScrollBar Beside(FrontEndRect list, int gap) =>
        new(new FrontEndRect(list.X + list.Width + gap, list.Y, TextScrollBar.Width, list.Height));

    public FrontEndRect Rect { get; }
    public TextScrollBar Bar { get; } = new();
    public FrontEndRect Screen => ToScreen(Rect);

    /// <summary>The thumb (0x00529160), in 640 × 480 units.</summary>
    public FrontEndRect ThumbRect()
    {
        (int top, int length) = Bar.Thumb((int)Rect.Height);
        return ToScreen(new FrontEndRect(Rect.X, Rect.Y + top, Rect.Width, length));
    }

    /// <summary>0x0052C630 at 640 × 480: each corner divided by 4, truncated.</summary>
    private static FrontEndRect ToScreen(FrontEndRect r)
    {
        int left = (int)r.X / 4, top = (int)r.Y / 4;
        return new FrontEndRect(left, top, (int)(r.X + r.Width) / 4 - left, (int)(r.Y + r.Height) / 4 - top);
    }

    /// <summary>The column between the arrow squares, where 0x00528B20 lets the thumb and track act.</summary>
    private bool InTrackColumn(FrontEndPoint pointer)
    {
        FrontEndRect box = Screen;
        float side = box.Width;
        return pointer.X >= box.X && pointer.X < box.X + box.Width
            && pointer.Y >= box.Y + side && pointer.Y < box.Y + box.Height - side;
    }

    /// <summary>0x005507E0: the top and bottom squares are the arrows; between them, the thumb or the track.</summary>
    public ScrollBarPart HitTest(FrontEndPoint pointer)
    {
        FrontEndRect box = Screen;
        float side = box.Width, bottom = box.Y + box.Height;
        if (pointer.X < box.X || pointer.X >= box.X + box.Width) return ScrollBarPart.None;
        if (pointer.Y >= box.Y && pointer.Y < box.Y + side) return ScrollBarPart.Up;
        if (pointer.Y >= bottom - side && pointer.Y < bottom) return ScrollBarPart.Down;
        if (pointer.Y < box.Y + side || pointer.Y >= bottom - side) return ScrollBarPart.None;
        return ThumbRect().Contains(pointer) ? ScrollBarPart.Thumb : ScrollBarPart.Track;
    }

    /// <summary>One GUI update of the class's input (0x00528B20): an arrow steps every update it stays
    /// pressed on, a press on the track steps once toward the pointer, a held thumb follows it while the
    /// pointer stays in the track column (elsewhere the position holds). The caller sets the rate: DK2's
    /// front-end update rate is unread, and ours is one call per Unity frame.</summary>
    public void Update(FrontEndPoint pointer, bool down)
    {
        if (!Bar.Enabled) { _held = ScrollBarPart.None; _wasDown = down; return; }
        ScrollBarPart part = HitTest(pointer);
        if (!down) _held = ScrollBarPart.None;
        else if (!_wasDown)
        {
            _held = part;
            if (part == ScrollBarPart.Track) Bar.Step(pointer.Y < ThumbRect().Y ? -1 : 1);
        }
        if (_held == ScrollBarPart.Up && part == ScrollBarPart.Up) Bar.Step(-1);
        else if (_held == ScrollBarPart.Down && part == ScrollBarPart.Down) Bar.Step(1);
        else if (_held == ScrollBarPart.Thumb && InTrackColumn(pointer)) Bar.DragTo((int)(pointer.Y * 4 - Rect.Y), (int)Rect.Height);
        _wasDown = down;
    }

    /// <summary>One drawn frame of 0x00551BA0's glow: the hovered part's counter (the thumb and the track
    /// share one) restarts at 8, then every counter counts down. Ours ticks once per Unity frame.</summary>
    public void Frame(ScrollBarPart hovered)
    {
        int restart = hovered switch
        {
            ScrollBarPart.Up => 0,
            ScrollBarPart.Down => 1,
            ScrollBarPart.Thumb or ScrollBarPart.Track => 2,
            _ => -1,
        };
        if (restart >= 0) _glow[restart] = GlowStart;
        for (int i = 0; i < _glow.Length; i++)
            if (--_glow[i] < 0) _glow[i] = -1;
    }

    /// <summary>The glow frame drawn under part 0 (up), 1 (down) or 2 (track) this frame: none (−1), or
    /// 0–3 for ¼ to full; a counter of 5 or more draws the full glow.</summary>
    public int GlowFrame(int part)
    {
        sbyte counter = _glow[part];
        return counter <= 0 ? -1 : counter < 5 ? counter - 1 : 3;
    }

    /// <summary>Where 0x00551BA0 blits part 0, 1 or 2's padded frames: the bar's screen box moved 8 left and
    /// 6 up, the up arrow at its top, the track 10 lower, the down arrow 8 above its bottom.</summary>
    public FrontEndPoint ArtOrigin(int part)
    {
        FrontEndRect box = Screen;
        float x = box.X - 8, top = box.Y - 6;
        return new FrontEndPoint(x, part switch { 0 => top, 1 => box.Y + box.Height - 6 - 8, _ => top + 10 });
    }
}
