// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Data.Settings;

namespace DK2.Presentation;

/// <summary>The engine-free in-game settings pages (ADR-0805): native pages 25..32 from
/// <see cref="ClassicGameOptionsLayout"/> over the paused level, with their sliders, value toggles and the
/// scrolling key list. Laid out in the 1024x768 Options reference; one unit is 2.5 virtual. Pointer input is
/// in virtual units; a press on a slider, the key list's arrows or its thumb holds the pointer until release.</summary>
public sealed class GameOptionsHud
{
    private const float Unit = 2.5f;
    /// <summary>The key list's scroll bar is 25.6 units wide, its arrows square.</summary>
    private const float BarSide = 25.6f;
    private const byte Normal = 200, Dim = 89;
    private static readonly HudImage SliderTrack = "GUI\\Scroll Bars\\h-scroll_bar_bg", SliderLeft = "GUI\\Scroll Bars\\h-left_arrow",
        SliderRight = "GUI\\Scroll Bars\\h-right_arrow", SliderThumb = "GUI\\Scroll Bars\\h-scroll_bar",
        Up = "GUI\\Scroll Bars\\v-up_arrow", Down = "GUI\\Scroll Bars\\v-down_arrow",
        Track = "GUI\\Scroll Bars\\v-scroll_bar_bg", Thumb = "GUI\\Scroll Bars\\v-scroll_bar";
    /// <summary>The key list's columns: action name, modifiers, key.</summary>
    private static readonly float[] CellLeft = [0, 320, 480];

    private enum Hit : byte { Label, Track, Thumb, Left, Right, KeyArea, KeyRow, KeyUp, KeyDown, KeyThumb }
    private enum Hold : byte { None, Slider, KeyArrow, KeyThumb }

    private sealed class Part
    {
        public InGameOptionsWidget Widget = null!;
        public bool Enabled;
        public HudText? Text;
        public FrontEndRect Rect;
        public byte Tint;
        public Action? Press;
    }

    private readonly Func<uint, string> _text;
    private readonly Func<int, BitmapFont> _font;
    private readonly Func<KeyScanCode, string> _keyName;
    private readonly List<Part> _parts = new List<Part>();
    private readonly List<((HudText Text, int Width, int Height)?[] Cells, bool Enabled, GameAction Action)> _rows = new List<((HudText Text, int Width, int Height)?[] Cells, bool Enabled, GameAction Action)>();
    private readonly List<(HudRect Rect, Hit Kind, int Index)> _hits = new List<(HudRect Rect, Hit Kind, int Index)>();
    private readonly TextScrollBar _keyScroll = new TextScrollBar();
    private InGameOptionsPage _shown;
    private GameSettings? _shownSettings;
    private GameAction? _shownCapture;
    private FrontEndRect _keyBounds;
    private bool _hasKeys, _visible, _dragging;
    private float _keyLine;
    private int _height, _barHeld;
    private Hold _hold;
    private int _holdIndex;

    /// <param name="text">A string table entry by id.</param>
    /// <param name="font">The legible font for a screen height.</param>
    /// <param name="keyName">A key's name for the key list.</param>
    public GameOptionsHud(InGameOptionsController model, Func<uint, string> text, Func<int, BitmapFont> font, Func<KeyScanCode, string> keyName)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        _text = text ?? throw new ArgumentNullException(nameof(text));
        _font = font ?? throw new ArgumentNullException(nameof(font));
        _keyName = keyName ?? throw new ArgumentNullException(nameof(keyName));
    }

    public InGameOptionsController Model { get; }
    public bool Visible
    {
        get
        {
            return _visible;
        }
    }

    /// <summary>A press is held (a slider, the key list's arrow or thumb): the adapter keeps the pointer.</summary>
    public bool Holding
    {
        get
        {
            return _hold != Hold.None;
        }
    }

    /// <summary>The key list's scroll state, for automation.</summary>
    public TextScrollBar KeyScroll
    {
        get
        {
            return _keyScroll;
        }
    }

    /// <summary>The key list's row height in 1024x768 units.</summary>
    public float KeyLine
    {
        get
        {
            return _keyLine;
        }
    }

    /// <summary>A press on a key row began capturing a key: the adapter gives the panel keyboard focus.</summary>
    public event Action? KeyCaptureStarted;

    /// <summary>Every catalogue image the pages draw, to preload.</summary>
    public static IEnumerable<HudImage> Images()
    {
        return [SliderTrack, SliderLeft, SliderRight, SliderThumb, Up, Down, Track, Thumb];
    }

    public void Show(bool visible)
    {
        if (visible && !_visible)
        {
            Model.Open();
        }

        if (!visible && _visible)
        {
            Model.Hide();
        }

        if (!visible) { _dragging = false; Release(); }
        _visible = visible;
    }

    /// <param name="pointer">The pointer in virtual units: a pressable label under it is lit.</param>
    public void Build(HudScreen screen, (float X, float Y) pointer, HudFrame frame)
    {
        _hits.Clear();
        if (!_visible || screen.Height <= 0)
        {
            return;
        }

        OptionsPagesController settings = Model.Settings;
        if (Model.Page != _shown || _height != screen.Height || !_dragging &&
            (!ReferenceEquals(_shownSettings, settings.Pending) || settings.CapturingAction != _shownCapture))
        {
            Set(screen);
        }

        if (_barHeld != 0)
        {
            _keyScroll.Step(_barHeld);
        }

        float px = pointer.X / Unit, py = pointer.Y / Unit;
        for (int i = 0; i < _parts.Count; i++)
        {
            Part part = _parts[i];
            switch (part.Widget.Kind)
            {
                case InGameOptionsWidgetKind.Slider: Slider(part, i, frame); break;
                case InGameOptionsWidgetKind.KeyboardList: Keys(screen, frame); break;
                default:
                    if (part.Text == null)
                    {
                        break;
                    }
                    // A held press keeps other labels unlit, as pointer capture kept their enter events away.
                    bool lit = part.Press != null && _hold == Hold.None && Contains(part.Rect, px, py);
                    Quad(frame, HudImage.Of(part.Text), part.Rect, HudColor.Grey(lit ? (byte)255 : part.Tint));
                    if (part.Press != null)
                    {
                        _hits.Add((Virtual(part.Rect), Hit.Label, i));
                    }

                    break;
            }
        }
    }

    /// <summary>Lays the page out again: its texts, which settings are enabled, the key list's rows.</summary>
    private void Set(HudScreen screen)
    {
        OptionsPagesController settings = Model.Settings;
        bool resetKeys = _shown != InGameOptionsPage.Keyboard || _height != screen.Height;
        _parts.Clear(); _rows.Clear(); _dragging = false; _hasKeys = false; _barHeld = 0; _hold = Hold.None;
        _shown = Model.Page; _shownSettings = settings.Pending; _shownCapture = settings.CapturingAction; _height = screen.Height;
        if (_shown == InGameOptionsPage.None)
        {
            return;
        }

        BitmapFont font = _font(screen.Height);
        foreach (InGameOptionsWidget widget in ClassicGameOptionsLayout.ForPage(_shown))
        {
            bool setting = widget.Kind is InGameOptionsWidgetKind.Slider or InGameOptionsWidgetKind.Value;
            bool enabled = setting ? settings.Supports(widget.Setting.Value)
                : widget.Kind != InGameOptionsWidgetKind.Navigate || Model.CanNavigate(widget.Destination);
            var part = new Part { Widget = widget, Enabled = enabled };
            _parts.Add(part);
            if (widget.Kind == InGameOptionsWidgetKind.Slider)
            {
                continue;
            }

            if (widget.Kind == InGameOptionsWidgetKind.KeyboardList) { KeyRows(screen, font, widget.Bounds, resetKeys); continue; }
            string value = widget.TextId != 0 ? _text(widget.TextId) : setting ? SettingText(widget.Setting.Value) : "Unavailable";
            if (enabled)
            {
                if (widget.Kind == InGameOptionsWidgetKind.Navigate)
                {
                    part.Press = () => Model.Navigate(widget.Destination);
                }

                if (widget.Kind == InGameOptionsWidgetKind.Back)
                {
                    part.Press = Model.Back;
                }

                if (widget.Kind == InGameOptionsWidgetKind.Continue)
                {
                    part.Press = Model.Continue;
                }

                if (widget.Kind == InGameOptionsWidgetKind.Value)
                {
                    part.Press = () =>
                {
                    ushort id = widget.Setting.Value; int old = Model.Settings.Value(id);
                    Model.Settings.SetWidget(id, id == 133 ? old % 3 + 1 : old == 0 ? 1 : 0);
                };
                }
            }
            part.Text = new HudText(font, value, 0);
            (int width, int height) = HudTextPixels.Measure(part.Text);
            part.Rect = new FrontEndRect(widget.Bounds.X, widget.Bounds.Y, U(screen, width), U(screen, height));
            part.Tint = enabled && widget.Kind != InGameOptionsWidgetKind.Unavailable ? (widget.IsTitle ? (byte)255 : Normal) : Dim;
        }
    }

    /// <summary>Sound quality cycles 1443 / 281 / 1442; headphones 1465 / 111; the rest on 109 / off 110.</summary>
    private string SettingText(ushort id)
    {
        return id == 133
        ? _text(new uint[] { 0, 1443, 281, 1442 }[Model.Settings.Pending.SoundQuality])
        : id == 135 ? _text(Model.Settings.Pending.Headphones ? 1465u : 111u)
        : _text(Model.Settings.Value(id) == 0 ? 110u : 109u);
    }

    private void KeyRows(HudScreen screen, BitmapFont font, FrontEndRect bounds, bool reset)
    {
        _hasKeys = true; _keyBounds = bounds;
        _keyLine = font.LineHeight * 768f / screen.Height;
        if (reset)
        {
            _keyScroll.Reset((int)Math.Ceiling(KeyboardSetup.Actions.Count * _keyLine), (int)bounds.Height);
        }

        foreach (KeyboardAction entry in KeyboardSetup.Actions)
        {
            GameKeyBinding binding = Model.Settings.Pending.Binding(entry.Action);
            string key = Model.Settings.CapturingAction == entry.Action ? _text(1475)
                : binding.ScanCode == 0 ? "-" : _keyName(binding.ScanCode);
            string modifiers = binding.Modifiers == KeyModifiers.None ? "" : binding.Modifiers.ToString();
            _rows.Add((new[] { Cell(font, _text(entry.TextId)), Cell(font, modifiers), Cell(font, key) }, KeyboardSetup.IsBuilt(entry.Action), entry.Action));
        }
    }

    /// <summary>A key list cell's text and its measured size; none for an empty cell.</summary>
    private static (HudText Text, int Width, int Height)? Cell(BitmapFont font, string value)
    {
        if (value.Length == 0)
        {
            return null;
        }

        var text = new HudText(font, value, 0);
        (int width, int height) = HudTextPixels.Measure(text);
        return (text, width, height);
    }

    private void Slider(Part part, int index, HudFrame frame)
    {
        ushort id = part.Widget.Setting.Value;
        FrontEndRect box = part.Widget.Bounds;
        float side = box.Height;
        (int lo, int hi) = Model.Settings.Range(id);
        float t = (Model.Settings.Value(id) - lo) / (float)(hi - lo);
        HudColor tint = part.Enabled ? HudColor.White : new HudColor(255, 255, 255, Dim);
        var left = new FrontEndRect(box.X - side, box.Y, side, side);
        var right = new FrontEndRect(box.X + box.Width, box.Y, side, side);
        var thumb = new FrontEndRect(box.X + t * (box.Width - side), box.Y, side, side);
        Quad(frame, SliderTrack, box, tint); Quad(frame, SliderLeft, left, tint);
        Quad(frame, SliderRight, right, tint); Quad(frame, SliderThumb, thumb, tint);
        if (!part.Enabled)
        {
            return;
        }

        _hits.Add((Virtual(box), Hit.Track, index)); _hits.Add((Virtual(left), Hit.Left, index));
        _hits.Add((Virtual(right), Hit.Right, index)); _hits.Add((Virtual(thumb), Hit.Thumb, index));
    }

    private void Keys(HudScreen screen, HudFrame frame)
    {
        FrontEndRect bounds = _keyBounds;
        HudRect clip = Virtual(bounds);
        // The whole list takes the pointer, greyed rows and gaps included, as its clipping box did.
        _hits.Add((clip, Hit.KeyArea, 0));
        for (int i = 0; i < _rows.Count; i++)
        {
            float top = bounds.Y + i * _keyLine - _keyScroll.Position;
            if (top + _keyLine <= bounds.Y || top >= bounds.Y + bounds.Height)
            {
                continue;
            }

            ((HudText Text, int Width, int Height)?[] cells, bool enabled, _) = _rows[i];
            var tint = HudColor.Grey(enabled ? Normal : Dim);
            for (int c = 0; c < 3; c++)
            {
                float cellRight = c < 2 ? CellLeft[c + 1] : bounds.Width;
                var cell = new FrontEndRect(bounds.X + CellLeft[c], top, cellRight - CellLeft[c], _keyLine);
                if (cells[c] is not var (text, width, height))
                {
                    continue;
                }

                if (!HudQuads.Cut(Virtual(cell), new HudRect(0, 0, 1, 1), clip, out HudRect window, out _))
                {
                    continue;
                }

                HudQuads.Clipped(frame, HudImage.Of(text), new HudRect(cell.X * Unit, cell.Y * Unit, screen.Virtual(width), screen.Virtual(height)), window, tint);
            }
            if (enabled && HudQuads.Cut(Virtual(new FrontEndRect(bounds.X, top, bounds.Width, _keyLine)), new HudRect(0, 0, 1, 1), clip, out HudRect row, out _))
            {
                _hits.Add((row, Hit.KeyRow, i));
            }
        }
        float x = bounds.X + bounds.Width;
        var up = new FrontEndRect(x, bounds.Y, BarSide, BarSide);
        var down = new FrontEndRect(x, bounds.Y + bounds.Height - BarSide, BarSide, BarSide);
        (int thumbTop, int thumbHeight) = _keyScroll.Thumb((int)(bounds.Height * Unit));
        var thumb = new FrontEndRect(x, bounds.Y + thumbTop / Unit, BarSide, thumbHeight / Unit);
        Quad(frame, Up, up, HudColor.White); Quad(frame, Down, down, HudColor.White);
        Quad(frame, Track, new FrontEndRect(x, bounds.Y + BarSide, BarSide, bounds.Height - BarSide * 2), HudColor.White);
        Quad(frame, Thumb, thumb, HudColor.White);
        _hits.Add((Virtual(up), Hit.KeyUp, 0)); _hits.Add((Virtual(down), Hit.KeyDown, 0)); _hits.Add((Virtual(thumb), Hit.KeyThumb, 0));
    }

    /// <summary>Whether a press there would land on something, or the pointer is held by one.</summary>
    public bool Hits(float x, float y)
    {
        return _visible && (_hold != Hold.None || HitAt(x, y) >= 0);
    }

    /// <summary>A primary press; true when the panel took it.</summary>
    public bool PointerDown(float x, float y, int button)
    {
        if (!_visible || button != 0)
        {
            return false;
        }

        int at = HitAt(x, y);
        if (at < 0)
        {
            return false;
        }

        (_, Hit kind, int index) = _hits[at];
        switch (kind)
        {
            case Hit.Label: _parts[index].Press!(); break;
            case Hit.KeyArea: break;
            case Hit.Track:
            case Hit.Thumb:
                _dragging = true; _hold = Hold.Slider; _holdIndex = index;
                if (kind == Hit.Thumb)
                {
                    Model.Settings.BeginSliderDrag(_parts[index].Widget.Setting.Value);
                }

                SetSlider(index, x);
                break;
            case Hit.Left: Nudge(index, -1); break;
            case Hit.Right: Nudge(index, 1); break;
            case Hit.KeyRow: Model.Settings.BeginKeyCapture(_rows[index].Action); KeyCaptureStarted?.Invoke(); break;
            case Hit.KeyUp: _hold = Hold.KeyArrow; _barHeld = -1; break;
            case Hit.KeyDown: _hold = Hold.KeyArrow; _barHeld = 1; break;
            case Hit.KeyThumb: _hold = Hold.KeyThumb; break;
        }
        return true;
    }

    /// <summary>While the pages show, binds this frame's key to the row being captured
    /// (<see cref="OptionsPagesController.CapturePressedKey"/>). Returns whether one was bound.</summary>
    public bool CaptureKey(GameBindingInput keys)
    {
        return _visible && Model.Settings.CapturePressedKey(keys);
    }

    /// <summary>A held slider follows the pointer; the key list's held thumb drags the list.</summary>
    public void PointerMove(float x, float y)
    {
        if (_hold == Hold.Slider)
        {
            SetSlider(_holdIndex, x);
        }
        else if (_hold == Hold.KeyThumb && _hasKeys)
        {
            _keyScroll.DragTo((int)((y / Unit - _keyBounds.Y) * Unit), (int)(_keyBounds.Height * Unit));
        }
    }

    public void PointerUp()
    {
        Release();
    }

    private void Release()
    {
        if (_hold == Hold.Slider) { _dragging = false; Model.Settings.EndSliderDrag(); }
        _hold = Hold.None; _barHeld = 0;
    }

    private void SetSlider(int index, float x)
    {
        ushort id = _parts[index].Widget.Setting.Value;
        FrontEndRect box = _parts[index].Widget.Bounds;
        (int lo, int hi) = Model.Settings.Range(id);
        float t = Math.Clamp((x / Unit - box.X) / box.Width, 0, 1);
        Model.Settings.SetWidget(id, (int)Math.Round(lo + t * (hi - lo)));
    }

    private void Nudge(int index, int step)
    {
        ushort id = _parts[index].Widget.Setting.Value;
        Model.Settings.SetWidget(id, Model.Settings.Value(id) + step);
    }

    /// <summary>For automation: where a page widget takes a press (a label's text, a slider's track), in
    /// virtual units; null when the page has no such widget.</summary>
    public HudRect? WidgetRect(int nativeIndex)
    {
        foreach (Part part in _parts)
        {
            if (part.Widget.NativeIndex == nativeIndex)
            {
                return Virtual(part.Text != null ? part.Rect : part.Widget.Bounds);
            }
        }

        return null;
    }

    /// <summary>For automation: a slider's track by its setting id, in virtual units.</summary>
    public HudRect? SliderRect(ushort setting)
    {
        foreach (Part part in _parts)
        {
            if (part.Widget.Kind == InGameOptionsWidgetKind.Slider && part.Widget.Setting.Value == setting)
            {
                return Virtual(part.Widget.Bounds);
            }
        }

        return null;
    }

    /// <summary>For automation: a key row where the list is scrolled now, in virtual units.</summary>
    public HudRect? KeyRowRect(GameAction action)
    {
        if (!_hasKeys)
        {
            return null;
        }

        for (int i = 0; i < _rows.Count; i++)
        {
            if (_rows[i].Action == action)
            {
                return Virtual(new FrontEndRect(_keyBounds.X, _keyBounds.Y + i * _keyLine - _keyScroll.Position, _keyBounds.Width, _keyLine));
            }
        }

        return null;
    }

    /// <summary>For automation: the key list's thumb and its track, in virtual units.</summary>
    public (HudRect Thumb, HudRect Track)? KeyBar()
    {
        if (!_hasKeys)
        {
            return null;
        }

        float x = _keyBounds.X + _keyBounds.Width;
        (int top, int height) = _keyScroll.Thumb((int)(_keyBounds.Height * Unit));
        return (Virtual(new FrontEndRect(x, _keyBounds.Y + top / Unit, BarSide, height / Unit)),
            Virtual(new FrontEndRect(x, _keyBounds.Y + BarSide, BarSide, _keyBounds.Height - BarSide * 2)));
    }

    /// <summary>The topmost hit region at a point, or -1.</summary>
    private int HitAt(float x, float y)
    {
        for (int i = _hits.Count - 1; i >= 0; i--)
        {
            if (_hits[i].Rect.Contains(x, y))
            {
                return i;
            }
        }

        return -1;
    }

    private static float U(HudScreen screen, int pixels)
    {
        return screen.Virtual(pixels) / Unit;
    }

    private static bool Contains(FrontEndRect r, float x, float y)
    {
        return x >= r.X && x < r.X + r.Width && y >= r.Y && y < r.Y + r.Height;
    }

    private static HudRect Virtual(FrontEndRect r)
    {
        return new HudRect(r.X * Unit, r.Y * Unit, r.Width * Unit, r.Height * Unit);
    }

    private static void Quad(HudFrame frame, HudImage image, FrontEndRect rect, HudColor tint)
    {
        frame.Add(new HudDraw(HudDrawKind.Quad, image, Virtual(rect), tint));
    }
}
