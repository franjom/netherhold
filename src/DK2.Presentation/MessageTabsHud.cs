// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Globalization;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>Menu39's message tabs and Review Objective button, the message panel (menu40 in message mode),
/// the level indicators and the objective panel (menu33), engine-free (ADR-0805); docs/research/message-tabs.md.
/// Sounds 0x320..0x322, the Ally button (mode 3) and name substitution for types 82..87 are not built.
/// Built back to front from quads for text, clipping and the tiled frame.</summary>
public sealed class MessageTabsHud
{
    private const int MenuX = 1112, MenuY = 1460, ButtonWidth = 88, ButtonHeight = 128;
    private const int Slots = MessageTabsView.RecordsExamined;
    private const int ObjectiveAction = 100, ExitAction = 101, TickAction = 102, GotoAction = 103, PanelAction = 104,
        ObjectivePanelAction = 105, TrackAction = 106, ThumbAction = 107, UpAction = 108, DownAction = 109;
    private static readonly string[] Families = ["mt-info", "mt-info", "mt-creature", "mt-fight", "mt-objective", "mt-allymsg", "mt-playerexit"];
    private static readonly string[,] FrameNames = Names();
    private static readonly string[] FramePieces = ["tl", "t", "tr", "l", "m", "r", "bl", "b", "br"];
    private static readonly HudImage[] FrameImages = [.. FramePieces.Select(p => (HudImage)("GUI\\Windows\\text_box-" + p))];
    private static readonly HudIndicator[] Drawn =
    [
        HudIndicator.TimeRemaining, HudIndicator.Score, HudIndicator.CurrentHolder,
        HudIndicator.HeroesKilled, HudIndicator.SlapsRemaining, HudIndicator.NextRoom,
    ];
    private static readonly string[] IndicatorArt =
    [
        "GUI\\Windows\\timer-bg", "GUI\\Windows\\score-bg", "GUI\\Windows\\kingofhill-bg",
        "GUI\\Windows\\kills-bg", "GUI\\Windows\\slaps-bg", "GUI\\Icons\\next_room",
    ];
    private static readonly string[] HighDigits = DigitNames("GUI\\Icons\\i-"), LowDigits = DigitNames("GUI\\Icons\\lo-");
    // Menu33's own background is unread: a translucent dark pane, as before.
    private static readonly HudColor ObjectivePane = new(20, 20, 20, 153);

    private readonly IBottomPanelArt _art;
    private readonly MessageTabsView _view;
    private readonly Func<int, BitmapFont> _font;
    private readonly Func<int, string?> _resolve;
    private readonly Func<int, string?>? _briefing;
    private readonly Func<PlayerMessage, (float X, float Y)?>? _locate;
    private readonly Action<float, float>? _goTo;
    private readonly Func<byte, string>? _playerName;
    private readonly TextScrollBar _bar = new();
    private readonly List<(HudImage Image, HudRect Rect)> _indicatorArt = new(), _digits = new();
    private readonly List<(int Action, HudRect Rect)> _hits = new();

    private HudScreen _screen;
    private bool _objectiveHovered, _objectiveOpen, _optionsOpen, _visible = true, _allowInput = true;
    private int _hovered = -1;
    private (float X, float Y) _pointer;
    private bool _pointerKnown;
    private int _barHeld; // 1 up arrow, 2 down arrow, 3 thumb (0x00528B20's states)
    private HudRect _panel, _thumb, _barUp, _barDown;
    private bool _panelShown;
    private HudText? _panelText, _objectiveText, _holderText;
    private (int Width, int Height) _panelSize, _objectiveSize, _holderSize;
    private uint _shownId;
    private int _shownHeight;
    private HudIndicatorSnapshot _shownIndicators;
    private int _indicatorTop = int.MinValue, _indicatorWidth, _indicatorHeight;
    private (byte Player, int Height) _holderShown;
    private HudRect _holderWindow;
    private bool _holderVisible;
    private int _holderScroll;
    private bool _holderForward = true;

    /// <param name="font">The legible bitmap font for a screen height.</param>
    /// <param name="resolve">A message string reference to its text (Speech.str, level table, Text.str).</param>
    /// <param name="briefing">A line of the level's briefing table, or null.</param>
    /// <param name="locate">A message's goto location in tiles, or null to hide the button.</param>
    /// <param name="goTo">Moves the view to a location in tiles.</param>
    public MessageTabsHud(IBottomPanelArt art, MessageTabsView view, Func<int, BitmapFont> font, Func<int, string?> resolve,
        Func<int, string?>? briefing, Func<PlayerMessage, (float X, float Y)?>? locate = null, Action<float, float>? goTo = null,
        Func<byte, string>? playerName = null)
    {
        _art = art ?? throw new ArgumentNullException(nameof(art));
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _font = font ?? throw new ArgumentNullException(nameof(font));
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        _briefing = briefing; _locate = locate; _goTo = goTo; _playerName = playerName;
    }

    /// <summary>Every catalogue image the panel can draw except room icons, for decoding ahead of use.</summary>
    public static IEnumerable<HudImage> Images()
    {
        foreach (string name in FrameNames)
        {
            yield return name;
        }

        yield return "GUI\\Tabs\\Messages\\mt-objective-00";
        yield return "GUI\\Tabs\\Messages\\mt-objective-01";
        foreach (string piece in FramePieces)
        {
            yield return "GUI\\Windows\\text_box-" + piece;
        }

        foreach (string bar in new[] { "v-scroll_bar_bg", "v-scroll_bar", "v-up_arrow", "v-down_arrow" })
        {
            yield return "GUI\\Scroll Bars\\" + bar;
        }

        foreach (string button in new[] { "mt-exit-00", "mt-tick-00", "mt-goto-00" })
        {
            yield return "GUI\\Tabs\\Messages\\" + button;
        }

        foreach (string art in IndicatorArt)
        {
            yield return art;
        }

        yield return "GUI\\Windows\\gold-bg-01";
        foreach (string digit in HighDigits)
        {
            yield return digit;
        }

        foreach (string digit in LowDigits)
        {
            yield return digit;
        }

        yield return "GUI\\Icons\\I--";
    }

    /// <summary>The hovered tab's message text, for the status line (0x0042D820 case 5); the
    /// Review Objective button's own texts 2163/2164.</summary>
    public HoverTextSnapshot HoverText
    {
        get
        {
            if (_objectiveHovered)
            {
                return new HoverTextSnapshot(_art.Text(2164), _art.Text(2163), default);
            }

            int slot = _view.Hovered;
            if (slot < 0 || slot >= _view.Tabs.Count)
            {
                return HoverTextSnapshot.Empty;
            }

            return new HoverTextSnapshot(MessageText(_view.Tabs[slot].MessageId) ?? "", "", default);
        }
    }

    /// <summary>Whether a pointer at (x, y), virtual units, is the panel's.</summary>
    public bool ContainsPointer(float x, float y)
    {
        if (!_visible || !_allowInput)
        {
            return false;
        }

        if (_objectiveOpen)
        {
            return true;
        }

        foreach ((int action, HudRect rect) in _hits)
        {
            if ((action == PanelAction || action == ObjectiveAction || action < Slots) && rect.Contains(x, y))
            {
                return true;
            }
        }

        return false;
    }

    /// <param name="screen">The panel in local pixels; its height is the screen's.</param>
    /// <param name="screenPixelWidth">The screen's own width, which picks the digit family.</param>
    /// <param name="pointer">The pointer in virtual units, and whether the left button is held (the bar polls it).</param>
    public void Build(HudScreen screen, int screenPixelWidth, HudVisibility visibility, bool collapsed,
        in HudIndicatorSnapshot indicators, (float X, float Y, bool LeftHeld) pointer, HudFrame frame,
        bool optionsOpen = false, bool optionsObjective = false)
    {
        _screen = screen; _optionsOpen = optionsOpen;
        _visible = visibility.ShowNormalHud; _allowInput = visibility.AllowOrdinaryInput;
        _hits.Clear();
        if (!_visible || screen.Height <= 0) { _view.Hovered = -1; _objectiveHovered = false; _hovered = -1; return; }
        int top = MenuY + (collapsed ? ClassicCamera.CollapsedPanelTop - ClassicCamera.ExpandedPanelTop : 0);

        var objective = new HudRect(MenuX, top, ButtonWidth, ButtonHeight);
        Quad(frame, _objectiveHovered ? "GUI\\Tabs\\Messages\\mt-objective-01" : "GUI\\Tabs\\Messages\\mt-objective-00", objective);
        _hits.Add((ObjectiveAction, objective));
        for (int i = 0; i < Slots && i < _view.Tabs.Count; i++)
        {
            MessageTab tab = _view.Tabs[i];
            var slot = new HudRect(MenuX + tab.LocalX, top, ButtonWidth, ButtonHeight);
            Quad(frame, FrameNames[tab.Graphic, tab.Frame], slot);
            _hits.Add((i, slot));
        }
        LayoutIndicators(indicators, top, screenPixelWidth);
        ScrollHolder();
        BuildPanel(frame, top, indicators.Any, pointer);
        foreach ((HudImage image, HudRect rect) in _indicatorArt)
        {
            Quad(frame, image, rect);
        }

        if (_holderVisible && _holderText != null)
        {
            HudQuads.Clipped(frame, HudImage.Of(_holderText),
                new HudRect(_holderWindow.X - Px(_holderScroll), _holderWindow.Y, Px(_holderSize.Width), Px(_holderSize.Height)),
                _holderWindow, HudColor.White);
        }

        BuildObjective(frame, optionsObjective || (!optionsOpen && _objectiveOpen));
        foreach ((HudImage image, HudRect rect) in _digits)
        {
            Quad(frame, image, rect);
        }
    }

    // ---- Pointer input, in virtual units. A method returns true when the panel consumed the event.

    public bool PointerDown(float x, float y, int button)
    {
        int action = HitAction(x, y);
        switch (action)
        {
            case < 0: return false;
            case ObjectiveAction:
                if (!_allowInput || button != 0)
                {
                    return false;
                }
                // 0x004288C0: toggles menu33, hiding menu40 while it shows.
                _objectiveOpen = !_objectiveOpen; _objectiveText = null;
                return true;
            case < Slots:
                if (!_allowInput || _objectiveOpen)
                {
                    return false;
                }

                if (button == 0)
                {
                    _view.OpenTab(action);        // 0x00423F50
                }
                else if (button == 1)
                {
                    _view.DismissTab(action); // 0x00424220
                }

                return true;
            case ObjectivePanelAction:
                if (!_optionsOpen)
                {
                    _objectiveOpen = false;
                }

                return true;
            case PanelAction: return false;
        }
        if (button != 0)
        {
            return false;
        }

        switch (action)
        {
            case UpAction: _barHeld = 1; break;
            case DownAction: _barHeld = 2; break;
            case ThumbAction: _barHeld = 3; break;
            // Above or below the thumb: one pixel once per press (state 4).
            case TrackAction: _bar.Step(y < _thumb.Y ? -1 : 1); break;
            case ExitAction: _view.DismissOpen(); break;
            case TickAction: _view.CloseOpen(); break;
            // 0x00424720: the view to the message's location; menu40 stays open.
            case GotoAction:
                if (_view.Open is PlayerMessage open && _locate?.Invoke(open) is (float gx, float gy))
                {
                    _goTo?.Invoke(gx, gy);
                }

                break;
        }
        return true;
    }

    /// <summary>The pointer moved over the panel: the tab or button under it is hovered.</summary>
    public void PointerMove(float x, float y)
    {
        _pointer = (x, y); _pointerKnown = true;
        int action = HitAction(x, y);
        if (action == _hovered)
        {
            return;
        }

        Leave();
        _hovered = action;
        if (action == ObjectiveAction)
        {
            _objectiveHovered = true;
        }
        else if (action >= 0 && action < Slots)
        {
            _view.Hovered = action;
        }
    }

    public void PointerLeave() { Leave(); _hovered = -1; _pointerKnown = false; }

    /// <summary>Hover again at the last pointer position after the layout moved under a still pointer
    /// (a tab dismissed, slid or gone), as retained UI re-picks after a layout change.</summary>
    public void Repick()
    {
        if (_pointerKnown && _visible)
        {
            PointerMove(_pointer.X, _pointer.Y);
        }
    }

    /// <summary>Whether something of the panel's is under the point, so the adapter routes the pointer here.</summary>
    public bool Hits(float x, float y)
    {
        return HitAction(x, y) >= 0;
    }

    private void Leave()
    {
        if (_hovered == ObjectiveAction)
        {
            _objectiveHovered = false;
        }
        else if (_hovered >= 0 && _hovered < Slots && _view.Hovered == _hovered)
        {
            _view.Hovered = -1;
        }
    }

    private int HitAction(float x, float y)
    {
        if (!_visible)
        {
            return -1;
        }

        for (int i = _hits.Count - 1; i >= 0; i--)
        {
            if (_hits[i].Rect.Contains(x, y))
            {
                return _hits[i].Action;
            }
        }

        return -1;
    }

    // ---- Indicators

    /// <summary>Each indicator (0x00427010): its art 256x128 at the menu's corner, gold-bg-01 188x128
    /// right of it (not for Slaps), then white 0x00415650 digits 40 down, right-aligned to slots
    /// of 40: Time Remaining (0x00427230), Score (0x004273D0, 10 slots), Heroes Killed (0x00427770,
    /// 10 slots), Slaps Remaining (0x00427870, 6 slots). Current Holder (0x004274C0) draws the
    /// player's name instead; Next Room (0x00427980) draws no background, only next_room and the
    /// room's GUI icon as 128-unit squares side by side.</summary>
    private void LayoutIndicators(in HudIndicatorSnapshot shown, int panelTop, int screenPixelWidth)
    {
        // Rebuilt only when what is shown, the panel top or the screen changes; the time limit
        // changes once a turn, nothing else per frame.
        if (shown == _shownIndicators && panelTop == _indicatorTop && _screen.Width == _indicatorWidth
            && _screen.Height == _indicatorHeight)
        {
            return;
        }

        _shownIndicators = shown; _indicatorTop = panelTop; _indicatorWidth = _screen.Width; _indicatorHeight = _screen.Height;
        _indicatorArt.Clear(); _digits.Clear(); _holderVisible = false;
        IReadOnlyList<(HudIndicator Indicator, int X, int Y)> placed = shown.Any
            ? HudIndicatorLayout.Place(shown, panelTop) : Array.Empty<(HudIndicator, int, int)>();
        string[] family = screenPixelWidth >= 640 ? HighDigits : LowDigits;
        for (int i = 0; i < Drawn.Length; i++)
        {
            int at = -1;
            for (int p = 0; p < placed.Count; p++)
            {
                if (placed[p].Indicator == Drawn[i])
                {
                    at = p;
                }
            }

            if (at < 0)
            {
                continue;
            }

            int x = placed[at].X, y = placed[at].Y;
            HudImage backing = Drawn[i] == HudIndicator.NextRoom ? RoomIcon(shown.NextRoomTypeId)
                : Drawn[i] == HudIndicator.SlapsRemaining ? default : "GUI\\Windows\\gold-bg-01";
            if (Drawn[i] == HudIndicator.NextRoom)
            {
                _indicatorArt.Add((IndicatorArt[i], new HudRect(x, y, 128, 128)));
                if (!backing.IsNone)
                {
                    _indicatorArt.Add((backing, new HudRect(x + 128, y, 128, 128)));
                }

                continue;
            }
            _indicatorArt.Add((IndicatorArt[i], new HudRect(x, y, 256, 128)));
            if (!backing.IsNone)
            {
                _indicatorArt.Add((backing, new HudRect(x + 256, y, 188, 128)));
            }
            // DK2 divides by the LEVEL's turns a second, 0x006F2590 (0x0051E708).
            if (Drawn[i] == HudIndicator.TimeRemaining)
            {
                foreach ((string digits, int slots) in HudIndicatorLayout.TimeRemainingDigits(
                    shown.TimeLimitTurns, SimulationTiming.OriginalTicksPerGameSecond))
                {
                    Digits(family, digits, slots, x, y + 40);
                }
            }
            else if (Drawn[i] == HudIndicator.Score)
            {
                Digits(family, Invariant(shown.ScoreValue), 10, x, y + 40);
            }
            else if (Drawn[i] == HudIndicator.HeroesKilled)
            {
                Digits(family, Invariant(shown.HeroesDestroyed), 10, x, y + 40);
            }
            else if (Drawn[i] == HudIndicator.SlapsRemaining)
            {
                Digits(family, Invariant(shown.SlapsLeft), 6, x, y + 40);
            }
            else if (Drawn[i] == HudIndicator.CurrentHolder)
            {
                PlaceHolder(shown.HolderPlayerId, x, y);
            }
        }
    }

    /// <summary>0x00415650 mode 0: 48-unit squares 40 apart, i- digits from 640 wide (lo- below),
    /// catalogue 106 (I--) for a minus; white here. The whole string is drawn, as DK2 does.</summary>
    private void Digits(string[] family, string text, int slots, int x, int y)
    {
        int start = HudIndicatorLayout.AlignedStart(text.Length, slots);
        for (int c = 0; c < text.Length; c++)
        {
            _digits.Add((text[c] == '-' ? "GUI\\Icons\\I--" : family[text[c] - '0'], new HudRect(x + (start + c) * 40, y, 48, 48)));
        }
    }

    /// <summary>0x00427980's second square: the room definition's art 0, the bottom panel's icon.</summary>
    private HudImage RoomIcon(byte room)
    {
        return _art.Icon(new BottomPanelChoiceKey(BottomPanelChoiceKind.Room, room));
    }

    /// <summary>0x004274C0: the holder's name (player <c>+0xEEA</c>) in the widget moved 160 in and
    /// 16 down (2 when the screen is 300 high), seen through a window 260 wide (<c>0x104</c>).</summary>
    private void PlaceHolder(byte player, int x, int y)
    {
        if (_holderShown != (player, _screen.Height) || _holderText == null)
        {
            _holderShown = (player, _screen.Height);
            // The offset (+0x4112) carries over to a new holder, as in 0x004274C0.
            _holderText = new HudText(_font(_screen.Height), _playerName?.Invoke(player) ?? "", int.MaxValue / 2);
            _holderSize = HudTextPixels.Measure(_holderText);
        }
        int down = _screen.Height == 300 ? 2 : 16;
        _holderWindow = new HudRect(x + 160, y + down, 260, 128 - down);
        _holderVisible = true;
    }

    /// <summary>0x004274C0's scroll, once a drawn frame: a name wider than the window moves one
    /// pixel (interface <c>+0x4112</c>) toward its end, then back (direction <c>+0x43B1</c>).</summary>
    private void ScrollHolder()
    {
        if (!_holderVisible || _holderText == null)
        {
            return;
        }

        int overflow = _holderSize.Width - (int)V(260);
        if (overflow <= 0) { _holderScroll = 0; _holderForward = true; return; }
        _holderScroll += _holderForward ? 1 : -1;
        if (_holderScroll > overflow)
        {
            _holderForward = false;
        }
        else if (_holderScroll < 0)
        {
            _holderForward = true;
        }
    }

    // ---- Menu40, the message panel

    /// <summary>Menu40 in message mode (0x00423F50 sizes it, HudIndicatorLayout.MessagePanel): static
    /// (1020, y) 1200 wide, the text in a 1096-wide box, the exit, tick and goto buttons 88x88 at
    /// x 88, 264 and 440, bottom-anchored 20 up (widget anchor bit 4, 0x00528570).</summary>
    private void BuildPanel(HudFrame frame, int stripTop, bool indicators, (float X, float Y, bool LeftHeld) pointer)
    {
        PlayerMessage? open = _objectiveOpen ? null : _view.Open;
        _panelShown = open.HasValue && !_optionsOpen;
        if (open is not PlayerMessage message) { _barHeld = 0; return; }
        if (_shownId != message.Id || _shownHeight != _screen.Height || _panelText == null)
        {
            _shownId = message.Id; _shownHeight = _screen.Height;
            // 0x0052A330 measures 64 narrower than the widget, the bar's width; text that
            // overflows keeps that wrap and gets the bar, text that fits wraps at the full width.
            string text = MessageText(message.Id) ?? "";
            BitmapFont popup = _font(_screen.Height * 85 / 100);
            _panelText = new HudText(popup, text, Math.Max(1, (int)V(TextScrollBar.MeasuredWidth)));
            _panelSize = HudTextPixels.Measure(_panelText);
            _bar.Reset(_panelSize.Height, (int)V(TextScrollBar.Height));
            if (!_bar.Enabled)
            {
                _panelText = new HudText(popup, text, Math.Max(1, (int)V(TextScrollBar.TextWidget)));
                _panelSize = HudTextPixels.Measure(_panelText);
            }
            _barHeld = 0;
        }
        // This height uses the remake's virtual units; native reference-point scaling is recorded in 0x0052C5A0.
        (int x, int y, int boxWidth, int height) = HudIndicatorLayout.MessagePanel(stripTop, (int)(_panelSize.Height * 1920f / _screen.Height), indicators);
        _panel = new HudRect(x, y, boxWidth, height);
        if (_panelShown)
        {
            _hits.Add((PanelAction, _panel));
            BuildFrame(frame, boxWidth, height);
            // The text widget is (20,20) 1096x288; 0x004242F0's own inset is unread, so the text keeps
            // the place fitted to the owner's DK2 shot (2026-10-22): x 1054, 32 below the top.
            // Message mode re-renders the text at y offset -position (0x004242F0).
            var window = new HudRect(x + 20, y + 20, TextScrollBar.TextWidget, TextScrollBar.Height);
            HudQuads.Clipped(frame, HudImage.Of(_panelText),
                new HudRect(window.X + 14, window.Y + 12 - Px(_bar.Position), Px(_panelSize.Width), Px(_panelSize.Height)),
                window, HudColor.White);
        }
        BuildBar(frame, pointer);
        if (!_panelShown)
        {
            return;
        }

        Button(frame, "GUI\\Tabs\\Messages\\mt-exit-00", ExitAction, new HudRect(x + 88, y + height - 108, 88, 88));
        Button(frame, "GUI\\Tabs\\Messages\\mt-tick-00", TickAction, new HudRect(x + 264, y + height - 108, 88, 88));
        if (_locate?.Invoke(message) is not null)
        {
            Button(frame, "GUI\\Tabs\\Messages\\mt-goto-00", GotoAction, new HudRect(x + 440, y + height - 108, 88, 88));
        }
    }

    /// <summary>0x00414400 while enabled: arrows 64 square at the ends, the track stretched
    /// between them, the thumb 28 wide 20 in, inset 4 at each end. 0x00528B20 each GUI update:
    /// a held arrow moves one pixel while the pointer stays on it; a held thumb follows the pointer.</summary>
    private void BuildBar(HudFrame frame, (float X, float Y, bool LeftHeld) pointer)
    {
        if (!_bar.Enabled) { _barHeld = 0; return; }
        if (!pointer.LeftHeld)
        {
            _barHeld = 0;
        }

        if (_barHeld == 1 && _barUp.Contains(pointer.X, pointer.Y))
        {
            _bar.Step(-1);
        }
        else if (_barHeld == 2 && _barDown.Contains(pointer.X, pointer.Y))
        {
            _bar.Step(1);
        }
        else if (_barHeld == 3)
        {
            _bar.DragTo((int)(pointer.Y - _panel.Y) - TextScrollBar.MenuY, TextScrollBar.Height);
        }

        const int bx = TextScrollBar.MenuX, by = TextScrollBar.MenuY, h = TextScrollBar.Height;
        float px = _panel.X, py = _panel.Y;
        _barUp = new HudRect(px + bx, py + by, TextScrollBar.Width, TextScrollBar.Arrow);
        _barDown = new HudRect(px + bx, py + by + h - TextScrollBar.Arrow, TextScrollBar.Width, TextScrollBar.Arrow);
        var track = new HudRect(px + bx, py + by + TextScrollBar.Arrow, TextScrollBar.Width, TextScrollBar.Track(h));
        (int top, int length) = _bar.Thumb(h);
        _thumb = new HudRect(px + bx + 20, py + by + top + 4, 28, Math.Max(0, length - 8));
        if (!_panelShown)
        {
            return;
        }

        Button(frame, "GUI\\Scroll Bars\\v-scroll_bar_bg", TrackAction, track);
        Button(frame, "GUI\\Scroll Bars\\v-scroll_bar", ThumbAction, _thumb);
        Button(frame, "GUI\\Scroll Bars\\v-up_arrow", UpAction, _barUp);
        Button(frame, "GUI\\Scroll Bars\\v-down_arrow", DownAction, _barDown);
    }

    /// <summary>0x00411D00 on menu40's rectangle inset by 10: corners 16 square outside the inset, the
    /// edges tiled in 16s and cropped at the far end, the middle tiled in 32s. DK2 draws these in
    /// device pixels; ours scales them by height / 1200, the owner's 1202-line reference.</summary>
    private void BuildFrame(HudFrame frame, int width, int height)
    {
        float pixel = _screen.Height / 1200f, border = HudIndicatorLayout.FrameBorder * pixel;
        (float ix, float iy, float iw, float ih) = HudIndicatorLayout.FrameInner(0, 0, V(width), V(height), pixel);
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
        {
            // In local pixels from the panel's corner, as the frame is pixel art.
            float left = column == 0 ? ix - border : column == 1 ? ix : ix + iw;
            float top = row == 0 ? iy - border : row == 1 ? iy : iy + ih;
            float w = column == 1 ? iw : border, h = row == 1 ? ih : border;
            float tile = (row == 1 && column == 1 ? HudIndicatorLayout.FrameMiddleTile : HudIndicatorLayout.FrameBorder) * pixel;
            HudQuads.Tiled(frame, FrameImages[row * 3 + column],
                new HudRect(_panel.X + Px(left), _panel.Y + Px(top), Px(w), Px(h)), Px(tile), HudColor.White);
        }
        }
    }

    // ---- Menu33, the objective panel

    /// <summary>Menu33 (0x004207E0): "Main Objective" (Text.str 1408), briefing line 2, "Sub
    /// Objective" (1409), briefing lines 3 to 5, over the play area (0,180) 2560x1280.</summary>
    private void BuildObjective(HudFrame frame, bool visible)
    {
        if (!visible)
        {
            return;
        }

        if (_objectiveText == null)
        {
            string text = _art.Text(1408) + "\n" + (_briefing?.Invoke(2) ?? "") + "\n\n" + _art.Text(1409);
            for (int line = 3; line <= 5; line++)
            {
                if (!string.IsNullOrEmpty(_briefing?.Invoke(line)))
                {
                    text += "\n" + _briefing!(line);
                }
            }

            _objectiveText = new HudText(_font(_screen.Height), text, Math.Max(1, (int)V(1600)));
            _objectiveSize = HudTextPixels.Measure(_objectiveText);
        }
        var pane = new HudRect(0, 180, _screen.VirtualWidth, 1280);
        frame.Add(new HudDraw(HudDrawKind.Fill, default, pane, ObjectivePane));
        _hits.Add((ObjectivePanelAction, pane));
        float left = Px((_screen.Width - _objectiveSize.Width) / 2f);
        Quad(frame, HudImage.Of(_objectiveText), new HudRect(left, 180 + 250, Px(_objectiveSize.Width), Px(_objectiveSize.Height)));
    }

    private string? MessageText(uint messageId)
    {
        PlayerMessage? found = _view.Open?.Id == messageId ? _view.Open : _view.MessageById(messageId);
        return found is PlayerMessage message ? _resolve(_view.Reference(message)) : null;
    }

    private void Button(HudFrame frame, HudImage image, int action, HudRect rect)
    {
        Quad(frame, image, rect);
        _hits.Add((action, rect));
    }

    private static void Quad(HudFrame frame, HudImage image, HudRect rect)
    {
        frame.Add(new HudDraw(HudDrawKind.Quad, image, rect, HudColor.White));
    }

    /// <summary>A native virtual length in local pixels, unrounded, as the adapter measured text wraps.</summary>
    private float V(int value)
    {
        return value * _screen.Height / 1920f;
    }

    private float Px(float pixels)
    {
        return _screen.Virtual(pixels);
    }

    private static string Invariant(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string[,] Names()
    {
        string[,] names = new string[Families.Length, 3];
        for (int family = 0; family < Families.Length; family++)
        {
            for (int frame = 0; frame < 3; frame++)
            {
                names[family, frame] = $"GUI\\Tabs\\Messages\\{Families[family]}-0{frame}";
            }
        }

        return names;
    }

    private static string[] DigitNames(string family)
    {
        string[] names = new string[10];
        for (int d = 0; d < 10; d++)
        {
            names[d] = family + (char)('0' + d);
        }

        return names;
    }
}
